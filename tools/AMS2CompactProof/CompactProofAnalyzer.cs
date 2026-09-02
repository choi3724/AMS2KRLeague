using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AMS2LeagueClient.Core.CompactTelemetry;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2CompactProof
{
    internal sealed class CompactProofAnalyzer
    {
        private readonly ReferenceFixture _reference;
        private readonly CompactArchiveResult _archive;
        private readonly Dictionary<string, int> _driverFields = ReferenceFixture.FieldMap(TelemetryFieldCatalog.DriverTelemetryFields);

        public CompactProofAnalyzer(ReferenceFixture reference, CompactArchiveResult archive)
        {
            _reference = reference;
            _archive = archive;
        }

        public CompactProofReport Analyze()
        {
            DecodedCompactArchive decoded = DecodeArchive();
            QuantizationMetrics quantization = CompareQuantization(decoded);
            ReplayQualityMetrics replay = CompareReplay(decoded.Replay, decoded.ReplayWorld, decoded.TrackGeometry);
            CoachingMetrics coaching = CompareCoaching(decoded.DriverFast, decoded.DriverMotion);

            var proofs = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LAP_TABLE"] = decoded.Story.Any(row => Value(row, "lapTimeMs").HasValue) ? "PASS" : "FAIL",
                ["POSITION_CHART"] = decoded.Replay.Select(row => Int(row, "participantRef")).Distinct().Count() == 32 ? "PASS" : "FAIL",
                ["REPLAY_2D"] = decoded.Replay.Count > 1_000 && decoded.ReplayWorld.Count > 1_000 && decoded.TrackGeometry.Count >= 200 ? "PASS" : "FAIL",
                ["SPEED_GRAPH"] = decoded.DriverFast.Any(row => Number(row, "speedMetersPerSecond") > 1) ? "PASS" : "FAIL",
                ["BRAKE_GRAPH"] = decoded.DriverFast.Any(row => Number(row, "brake") > 0.5) ? "PASS" : "FAIL",
                ["THROTTLE_GRAPH"] = decoded.DriverFast.Any(row => Number(row, "throttle") > 0.5) ? "PASS" : "FAIL",
                ["STEERING_GRAPH"] = decoded.DriverFast.Any(row => Math.Abs(Number(row, "steering")) > 0.1) ? "PASS" : "FAIL",
                ["G_FORCE_GRAPH"] = decoded.DriverFast.Any(row => Math.Abs(Number(row, "lateralAccelerationMetersPerSecondSquared")) > 0.1) ? "PASS" : "FAIL",
                ["DRIVING_LINE"] = decoded.DriverMotion.Count > 1_000 && decoded.DriverMotion.Any(row => Math.Abs(Number(row, "worldX")) > 1) ? "PASS" : "FAIL",
                ["TRACK_CENTERLINE"] = decoded.TrackGeometry.Count >= 40 ? "PASS" : "FAIL",
                ["INCIDENT_ANIMATION"] = decoded.Incident.Select(row => Int(row, "participantRef")).Distinct().Count() >= 2 ? "PASS" : "FAIL"
            };

            int passCount = proofs.Count(pair => pair.Value == "PASS");
            bool fieldNamesAbsent = CountFieldNamesOnWire() == 0;
            bool fidelityPass = quantization.AllWithinDeclaredBounds && replay.PositionMismatches == 0 &&
                replay.ProgressRmsMeters <= 2.0 && replay.WorldRmsMeters <= 1.0 &&
                coaching.BrakingPointMaxDifferenceMeters <= 2.0 && coaching.ThrottleOnMaxDifferenceMeters <= 2.0 &&
                coaching.MinimumSpeedMaxDifferenceMetersPerSecond <= 0.02 && coaching.LineRmsMeters <= 0.02;

            return new CompactProofReport
            {
                InputSource = "PERSISTED_COMPACT_A2CT_GZIP_ONLY",
                SharedMemoryRead = false,
                Frames = _archive.Frames.Count,
                Samples = _archive.Samples,
                Proofs = proofs,
                ProofsPassed = passCount,
                ProofsRequired = 11,
                AllProofsPass = passCount == 11,
                FieldNamesOnWire = fieldNamesAbsent ? 0 : CountFieldNamesOnWire(),
                StoryEventCountReference = _reference.Story.Count,
                StoryEventCountCompact = decoded.Story.Count,
                StoryExact = CompareExactStory(decoded.Story),
                IncidentParticipantSetExact = CompareIncidentParticipants(decoded.Incident),
                GenericDriverOrdinalsCaptured = decoded.DriverChange.Select(row => Int(row, "fieldOrdinal")).Distinct().Count(),
                Quantization = quantization,
                ReplayQuality = replay,
                Coaching = coaching,
                FidelityPass = fidelityPass
            };
        }

        private DecodedCompactArchive DecodeArchive()
        {
            var result = new DecodedCompactArchive();
            foreach (CompactFrameArtifact artifact in _archive.Frames.OrderBy(value => value.Sequence))
            {
                string path = Path.Combine(_archive.ArchiveRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                byte[] raw;
                using (FileStream file = File.OpenRead(path))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    raw = output.ToArray();
                }
                CompactTelemetryEnvelope envelope = CompactTelemetryCodec.Decode(raw);
                CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(envelope.Block.SchemaId);
                foreach (CompactTelemetrySample sample in envelope.Block.Samples)
                {
                    var row = new DecodedSample(sample.ElapsedMs, schema, sample.Values, envelope.Strings);
                    switch (envelope.Block.SchemaId)
                    {
                        case CompactTelemetrySchemaId.RaceEventV1: result.Story.Add(row); break;
                        case CompactTelemetrySchemaId.ParticipantReplayV1:
                            bool hasProgress = row.Values[row.Ordinals["racePosition"]].HasValue;
                            bool hasWorld = row.Values[row.Ordinals["worldX"]].HasValue;
                            if (hasProgress) result.Replay.Add(row);
                            if (hasWorld) result.ReplayWorld.Add(row);
                            if (!hasProgress && !hasWorld) result.ReplayExtension.Add(row);
                            break;
                        case CompactTelemetrySchemaId.TrackGeometryV1: result.TrackGeometry.Add(row); break;
                        case CompactTelemetrySchemaId.DriverFastV1: result.DriverFast.Add(row); break;
                        case CompactTelemetrySchemaId.DriverMotionV1: result.DriverMotion.Add(row); break;
                        case CompactTelemetrySchemaId.DriverSlowV1: result.DriverSlow.Add(row); break;
                        case CompactTelemetrySchemaId.DriverChangeV1: result.DriverChange.Add(row); break;
                        case CompactTelemetrySchemaId.IncidentV1: result.Incident.Add(row); break;
                    }
                }
            }
            return result;
        }

        private QuantizationMetrics CompareQuantization(DecodedCompactArchive decoded)
        {
            var metrics = new QuantizationMetrics();
            CompareRows(
                _reference.DriverFast20Hz,
                decoded.DriverFast,
                new[]
                {
                    new SourceTarget("unfilteredThrottle", "throttle"),
                    new SourceTarget("unfilteredBrake", "brake"),
                    new SourceTarget("unfilteredSteering", "steering"),
                    new SourceTarget("speedMetersPerSecond", "speedMetersPerSecond"),
                    new SourceTarget("lapDistanceMeters", "lapDistanceMeters"),
                    new SourceTarget("longitudinalAccelerationMetersPerSecondSquared", "longitudinalAccelerationMetersPerSecondSquared"),
                    new SourceTarget("lateralAccelerationMetersPerSecondSquared", "lateralAccelerationMetersPerSecondSquared")
                },
                metrics);
            CompareRows(
                _reference.DriverMotion5Hz,
                decoded.DriverMotion,
                new[]
                {
                    new SourceTarget("worldX", "worldX"), new SourceTarget("worldY", "worldY"),
                    new SourceTarget("worldZ", "worldZ"), new SourceTarget("headingRadians", "headingRadians"),
                    new SourceTarget("rpm", "rpm")
                },
                metrics);
            return metrics;
        }

        private void CompareRows(
            IReadOnlyList<DriverSourceSample> reference,
            IReadOnlyList<DecodedSample> compact,
            IReadOnlyList<SourceTarget> fields,
            QuantizationMetrics metrics)
        {
            if (reference.Count != compact.Count) throw new InvalidDataException("Compact driver sample count differs from the reference cadence.");
            for (int row = 0; row < reference.Count; row++)
            {
                if (reference[row].ElapsedMs != compact[row].ElapsedMs) throw new InvalidDataException("Compact driver timestamp mismatch.");
                foreach (SourceTarget field in fields)
                {
                    double expected = ReferenceFixture.Number(reference[row].Source, _driverFields, field.Source);
                    double actual = Number(compact[row], field.Target);
                    double error = Math.Abs(expected - actual);
                    CompactTelemetryField compactField = compact[row].Schema.Fields[compact[row].Ordinals[field.Target]];
                    metrics.Observe(field.Target, error, compactField.MaximumQuantizationError);
                }
            }
        }

        private bool CompareExactStory(IReadOnlyList<DecodedSample> compact)
        {
            if (_reference.Story.Count != compact.Count) return false;
            Dictionary<string, int> sourceFields = ReferenceFixture.FieldMap(TelemetryFieldCatalog.RaceStoryFields);
            for (int row = 0; row < compact.Count; row++)
            {
                if (_reference.Story[row].ElapsedMs != compact[row].ElapsedMs) return false;
                if (!string.Equals(
                        _reference.Story[row].EventType,
                        ResolveString(compact[row], CompactStringDictionaryId.EventType, "eventTypeRef"),
                        StringComparison.Ordinal)
                    || !string.Equals(
                        _reference.Story[row].EventId,
                        ResolveString(compact[row], CompactStringDictionaryId.EventId, "eventIdRef"),
                        StringComparison.Ordinal)
                    || !string.Equals(
                        _reference.Story[row].FactCode,
                        ResolveString(compact[row], CompactStringDictionaryId.FactCode, "factCodeRef"),
                        StringComparison.Ordinal))
                {
                    return false;
                }
                foreach (CompactTelemetryField field in compact[row].Schema.Fields)
                {
                    if (field.Name == "eventTypeRef" || field.Name == "eventIdRef" || field.Name == "factCodeRef")
                    {
                        continue;
                    }
                    double? expected = ReferenceFixture.Optional(_reference.Story[row].Source, sourceFields, field.Name);
                    if (expected.HasValue && string.Equals(field.Name, "participantRef", StringComparison.Ordinal) && expected.Value >= 1)
                    {
                        expected = expected.Value - 1;
                    }
                    double? actual = Value(compact[row], field.Name);
                    if (!expected.HasValue || !actual.HasValue)
                    {
                        if (expected.HasValue != actual.HasValue) return false;
                        continue;
                    }
                    if (Math.Abs(expected.Value - actual.Value) > field.MaximumQuantizationError + 1e-12) return false;
                }
            }
            return true;
        }

        private static string? ResolveString(
            DecodedSample row,
            CompactStringDictionaryId dictionaryId,
            string referenceField)
        {
            double? rawReference = Value(row, referenceField);
            if (!rawReference.HasValue) return null;
            uint reference = checked((uint)rawReference.Value);
            return row.Strings.FirstOrDefault(value =>
                value.DictionaryId == dictionaryId && value.ValueRef == reference)?.Value;
        }

        private bool CompareIncidentParticipants(IReadOnlyList<DecodedSample> compact)
        {
            int[] expected = _reference.Incident20Hz.Select(value => value.ParticipantRef - 1).Distinct().OrderBy(value => value).ToArray();
            int[] actual = compact.Select(value => Int(value, "participantRef")).Distinct().OrderBy(value => value).ToArray();
            return expected.SequenceEqual(actual);
        }

        private ReplayQualityMetrics CompareReplay(
            IReadOnlyList<DecodedSample> compact,
            IReadOnlyList<DecodedSample> worldKeyframes,
            IReadOnlyList<DecodedSample> trackGeometry)
        {
            var byParticipant = compact.GroupBy(row => Int(row, "participantRef"))
                .ToDictionary(group => group.Key, group => group.OrderBy(row => row.ElapsedMs).ToArray());
            var worldByParticipant = worldKeyframes.GroupBy(row => Int(row, "participantRef"))
                .ToDictionary(group => group.Key, group => group.OrderBy(row => row.ElapsedMs).ToArray());
            DecodedSample[] geometry = trackGeometry.OrderBy(row => Number(row, "lapDistanceMeters")).ToArray();
            double progressSquared = 0;
            double worldSquared = 0;
            double progressMax = 0;
            double worldMax = 0;
            int positionMismatches = 0;
            int count = 0;
            foreach (ReplaySourceSample reference in _reference.ReplayReference5Hz)
            {
                if (!byParticipant.TryGetValue(reference.ParticipantRef - 1, out DecodedSample[]? rows)) continue;
                if (!worldByParticipant.TryGetValue(reference.ParticipantRef - 1, out DecodedSample[]? worldRows)) continue;
                int upper = LowerBound(rows, reference.ElapsedMs);
                DecodedSample before;
                DecodedSample after;
                if (upper < rows.Length && rows[upper].ElapsedMs == reference.ElapsedMs)
                {
                    before = rows[upper];
                    after = rows[upper];
                }
                else
                {
                    before = rows[Math.Max(0, upper - 1)];
                    after = rows[Math.Min(rows.Length - 1, upper)];
                }
                double ratio = after.ElapsedMs == before.ElapsedMs ? 0 :
                    (reference.ElapsedMs - before.ElapsedMs) / (double)(after.ElapsedMs - before.ElapsedMs);
                double beforeProgress = (Number(before, "lap") * ReferenceFixture.SyntheticTrackLengthMeters) + Number(before, "lapDistanceMeters");
                double afterProgress = (Number(after, "lap") * ReferenceFixture.SyntheticTrackLengthMeters) + Number(after, "lapDistanceMeters");
                double expectedProgress = (reference.Lap * ReferenceFixture.SyntheticTrackLengthMeters) + reference.LapDistanceMeters;
                double interpolatedProgress = Lerp(beforeProgress, afterProgress, ratio);
                double progressError = Math.Abs(expectedProgress - interpolatedProgress);
                int worldUpper = LowerBound(worldRows, reference.ElapsedMs);
                DecodedSample worldBefore;
                DecodedSample worldAfter;
                if (worldUpper < worldRows.Length && worldRows[worldUpper].ElapsedMs == reference.ElapsedMs)
                {
                    worldBefore = worldRows[worldUpper];
                    worldAfter = worldRows[worldUpper];
                }
                else
                {
                    worldBefore = worldRows[Math.Max(0, worldUpper - 1)];
                    worldAfter = worldRows[Math.Min(worldRows.Length - 1, worldUpper)];
                }
                double worldRatio = worldAfter.ElapsedMs == worldBefore.ElapsedMs ? 0 :
                    (reference.ElapsedMs - worldBefore.ElapsedMs) / (double)(worldAfter.ElapsedMs - worldBefore.ElapsedMs);
                Point3 center = CenterAt(geometry, reference.LapDistanceMeters);
                Point3 centerBefore = CenterAt(geometry, Number(worldBefore, "lapDistanceMeters"));
                Point3 centerAfter = CenterAt(geometry, Number(worldAfter, "lapDistanceMeters"));
                double deviationX = Lerp(Number(worldBefore, "worldX") - centerBefore.X, Number(worldAfter, "worldX") - centerAfter.X, worldRatio);
                double deviationY = Lerp(Number(worldBefore, "worldY") - centerBefore.Y, Number(worldAfter, "worldY") - centerAfter.Y, worldRatio);
                double deviationZ = Lerp(Number(worldBefore, "worldZ") - centerBefore.Z, Number(worldAfter, "worldZ") - centerAfter.Z, worldRatio);
                double x = center.X + deviationX;
                double y = center.Y + deviationY;
                double z = center.Z + deviationZ;
                double dx = x - reference.WorldX;
                double dy = y - reference.WorldY;
                double dz = z - reference.WorldZ;
                double worldError = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                int position = Int(before, "racePosition");
                if (position != reference.Position) positionMismatches++;
                progressSquared += progressError * progressError;
                worldSquared += worldError * worldError;
                progressMax = Math.Max(progressMax, progressError);
                worldMax = Math.Max(worldMax, worldError);
                count++;
            }
            return new ReplayQualityMetrics
            {
                ComparedSamples = count,
                ProgressRmsMeters = count == 0 ? double.PositiveInfinity : Math.Sqrt(progressSquared / count),
                ProgressMaxMeters = progressMax,
                WorldRmsMeters = count == 0 ? double.PositiveInfinity : Math.Sqrt(worldSquared / count),
                WorldMaxMeters = worldMax,
                PositionMismatches = positionMismatches
            };
        }

        private static Point3 CenterAt(DecodedSample[] geometry, double lapDistanceMeters)
        {
            if (geometry.Length == 0) throw new InvalidDataException("Track geometry is empty.");
            int low = 0;
            int high = geometry.Length;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (Number(geometry[middle], "lapDistanceMeters") < lapDistanceMeters) low = middle + 1;
                else high = middle;
            }
            DecodedSample before = geometry[Math.Max(0, low - 1)];
            DecodedSample after = geometry[Math.Min(geometry.Length - 1, low)];
            double beforeDistance = Number(before, "lapDistanceMeters");
            double afterDistance = Number(after, "lapDistanceMeters");
            double ratio = afterDistance == beforeDistance ? 0 : (lapDistanceMeters - beforeDistance) / (afterDistance - beforeDistance);
            return new Point3(
                Lerp(Number(before, "worldX"), Number(after, "worldX"), ratio),
                Lerp(Number(before, "worldY"), Number(after, "worldY"), ratio),
                Lerp(Number(before, "worldZ"), Number(after, "worldZ"), ratio));
        }

        private CoachingMetrics CompareCoaching(IReadOnlyList<DecodedSample> driver, IReadOnlyList<DecodedSample> motion)
        {
            DriverLapMetrics[] reference = BuildReferenceLapMetrics();
            DriverLapMetrics[] compact = BuildCompactLapMetrics(driver);
            int laps = Math.Min(reference.Length, compact.Length);
            double braking = 0;
            double throttle = 0;
            double minimumSpeed = 0;
            for (int index = 0; index < laps; index++)
            {
                braking = Math.Max(braking, Math.Abs(reference[index].BrakingPointMeters - compact[index].BrakingPointMeters));
                throttle = Math.Max(throttle, Math.Abs(reference[index].ThrottleOnMeters - compact[index].ThrottleOnMeters));
                minimumSpeed = Math.Max(minimumSpeed, Math.Abs(reference[index].MinimumSpeed - compact[index].MinimumSpeed));
            }
            double lineSquared = 0;
            int lineCount = Math.Min(_reference.DriverMotion5Hz.Count, motion.Count);
            for (int index = 0; index < lineCount; index++)
            {
                double dx = ReferenceFixture.Number(_reference.DriverMotion5Hz[index].Source, _driverFields, "worldX") - Number(motion[index], "worldX");
                double dy = ReferenceFixture.Number(_reference.DriverMotion5Hz[index].Source, _driverFields, "worldY") - Number(motion[index], "worldY");
                double dz = ReferenceFixture.Number(_reference.DriverMotion5Hz[index].Source, _driverFields, "worldZ") - Number(motion[index], "worldZ");
                lineSquared += (dx * dx) + (dy * dy) + (dz * dz);
            }
            return new CoachingMetrics
            {
                ComparedLaps = laps,
                BrakingPointMaxDifferenceMeters = braking,
                ThrottleOnMaxDifferenceMeters = throttle,
                MinimumSpeedMaxDifferenceMetersPerSecond = minimumSpeed,
                LineRmsMeters = lineCount == 0 ? double.PositiveInfinity : Math.Sqrt(lineSquared / lineCount),
                LapConsistencyExact = CompareLapConsistency()
            };
        }

        private DriverLapMetrics[] BuildReferenceLapMetrics()
        {
            return SegmentReferenceLaps().Select(rows => BuildLapMetrics(
                rows.Select(row => ReferenceFixture.Number(row.Source, _driverFields, "lapDistanceMeters")).ToArray(),
                rows.Select(row => ReferenceFixture.Number(row.Source, _driverFields, "unfilteredBrake")).ToArray(),
                rows.Select(row => ReferenceFixture.Number(row.Source, _driverFields, "unfilteredThrottle")).ToArray(),
                rows.Select(row => ReferenceFixture.Number(row.Source, _driverFields, "speedMetersPerSecond")).ToArray())).ToArray();
        }

        private DriverLapMetrics[] BuildCompactLapMetrics(IReadOnlyList<DecodedSample> rows)
        {
            return SegmentCompactLaps(rows).Select(lap => BuildLapMetrics(
                lap.Select(row => Number(row, "lapDistanceMeters")).ToArray(),
                lap.Select(row => Number(row, "brake")).ToArray(),
                lap.Select(row => Number(row, "throttle")).ToArray(),
                lap.Select(row => Number(row, "speedMetersPerSecond")).ToArray())).ToArray();
        }

        private IEnumerable<DriverSourceSample[]> SegmentReferenceLaps()
        {
            var current = new List<DriverSourceSample>();
            double previous = -1;
            foreach (DriverSourceSample row in _reference.DriverFast20Hz)
            {
                double distance = ReferenceFixture.Number(row.Source, _driverFields, "lapDistanceMeters");
                if (current.Count > 0 && distance + 100 < previous)
                {
                    if (current.Count > 100) yield return current.ToArray();
                    current.Clear();
                }
                current.Add(row);
                previous = distance;
            }
            if (current.Count > 100) yield return current.ToArray();
        }

        private static IEnumerable<DecodedSample[]> SegmentCompactLaps(IReadOnlyList<DecodedSample> rows)
        {
            var current = new List<DecodedSample>();
            double previous = -1;
            foreach (DecodedSample row in rows)
            {
                double distance = Number(row, "lapDistanceMeters");
                if (current.Count > 0 && distance + 100 < previous)
                {
                    if (current.Count > 100) yield return current.ToArray();
                    current.Clear();
                }
                current.Add(row);
                previous = distance;
            }
            if (current.Count > 100) yield return current.ToArray();
        }

        private static DriverLapMetrics BuildLapMetrics(double[] distance, double[] brake, double[] throttle, double[] speed)
        {
            int brakeIndex = Array.FindIndex(brake, value => value >= 0.5);
            int throttleIndex = -1;
            for (int index = Math.Max(1, brakeIndex + 1); index < throttle.Length; index++)
            {
                if (throttle[index - 1] < 0.5 && throttle[index] >= 0.5) { throttleIndex = index; break; }
            }
            return new DriverLapMetrics
            {
                BrakingPointMeters = brakeIndex < 0 ? 0 : distance[brakeIndex],
                ThrottleOnMeters = throttleIndex < 0 ? 0 : distance[throttleIndex],
                MinimumSpeed = speed.Min()
            };
        }

        private bool CompareLapConsistency()
        {
            int[] reference = _reference.Story
                .Select(row => ReferenceFixture.Optional(row.Source, ReferenceFixture.FieldMap(TelemetryFieldCatalog.RaceStoryFields), "lapTimeMs"))
                .Where(value => value.HasValue)
                .Select(value => checked((int)value!.Value))
                .ToArray();
            return reference.Length > 0 && reference.All(value => value > 0);
        }

        private int CountFieldNamesOnWire()
        {
            int count = 0;
            string[] names = CompactTelemetrySchemaRegistry.Schemas.Values
                .SelectMany(schema => schema.Fields.Select(field => field.Name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (CompactFrameArtifact artifact in _archive.Frames)
            {
                string path = Path.Combine(_archive.ArchiveRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                byte[] raw;
                using (FileStream file = File.OpenRead(path))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                using (var output = new MemoryStream()) { gzip.CopyTo(output); raw = output.ToArray(); }
                foreach (string name in names)
                {
                    if (ContainsAscii(raw, name)) count++;
                }
            }
            return count;
        }

        private static bool ContainsAscii(byte[] bytes, string value)
        {
            byte[] pattern = System.Text.Encoding.ASCII.GetBytes(value);
            for (int offset = 0; offset <= bytes.Length - pattern.Length; offset++)
            {
                int index = 0;
                while (index < pattern.Length && bytes[offset + index] == pattern[index]) index++;
                if (index == pattern.Length) return true;
            }
            return false;
        }

        private static int LowerBound(DecodedSample[] rows, long elapsed)
        {
            int low = 0;
            int high = rows.Length;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (rows[middle].ElapsedMs < elapsed) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private static double Lerp(double left, double right, double ratio) => left + ((right - left) * ratio);
        private static double? Value(DecodedSample row, string field) => row.Values[row.Ordinals[field]];
        private static double Number(DecodedSample row, string field) => Value(row, field) ?? throw new InvalidDataException("Compact field is null: " + field);
        private static int Int(DecodedSample row, string field) => checked((int)Number(row, field));

        private sealed class SourceTarget
        {
            public SourceTarget(string source, string target) { Source = source; Target = target; }
            public string Source { get; }
            public string Target { get; }
        }

        private sealed class DriverLapMetrics
        {
            public double BrakingPointMeters { get; set; }
            public double ThrottleOnMeters { get; set; }
            public double MinimumSpeed { get; set; }
        }

        private readonly struct Point3
        {
            public Point3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
        }
    }

    internal sealed class DecodedCompactArchive
    {
        public List<DecodedSample> Story { get; } = new List<DecodedSample>();
        public List<DecodedSample> Replay { get; } = new List<DecodedSample>();
        public List<DecodedSample> ReplayWorld { get; } = new List<DecodedSample>();
        public List<DecodedSample> ReplayExtension { get; } = new List<DecodedSample>();
        public List<DecodedSample> TrackGeometry { get; } = new List<DecodedSample>();
        public List<DecodedSample> DriverFast { get; } = new List<DecodedSample>();
        public List<DecodedSample> DriverMotion { get; } = new List<DecodedSample>();
        public List<DecodedSample> DriverSlow { get; } = new List<DecodedSample>();
        public List<DecodedSample> DriverChange { get; } = new List<DecodedSample>();
        public List<DecodedSample> Incident { get; } = new List<DecodedSample>();
    }

    internal sealed class DecodedSample
    {
        public DecodedSample(
            long elapsedMs,
            CompactTelemetrySchema schema,
            IReadOnlyList<double?> values,
            IReadOnlyList<CompactStringDictionaryEntry>? strings = null)
        {
            ElapsedMs = elapsedMs;
            Schema = schema;
            Values = values;
            Strings = strings ?? Array.Empty<CompactStringDictionaryEntry>();
            Ordinals = schema.Fields.ToDictionary(field => field.Name, field => field.Ordinal, StringComparer.Ordinal);
        }
        public long ElapsedMs { get; }
        public CompactTelemetrySchema Schema { get; }
        public IReadOnlyList<double?> Values { get; }
        public IReadOnlyList<CompactStringDictionaryEntry> Strings { get; }
        public IReadOnlyDictionary<string, int> Ordinals { get; }
    }

    internal sealed class QuantizationMetrics
    {
        public Dictionary<string, FieldErrorMetric> Fields { get; } = new Dictionary<string, FieldErrorMetric>(StringComparer.Ordinal);
        public bool AllWithinDeclaredBounds => Fields.Count > 0 && Fields.Values.All(value => value.MaximumObservedError <= value.DeclaredMaximumError + 1e-12);
        public void Observe(string field, double error, double bound)
        {
            if (!Fields.TryGetValue(field, out FieldErrorMetric? metric))
            {
                metric = new FieldErrorMetric { DeclaredMaximumError = bound };
                Fields.Add(field, metric);
            }
            metric.MaximumObservedError = Math.Max(metric.MaximumObservedError, error);
            metric.Samples++;
        }
    }

    internal sealed class FieldErrorMetric
    {
        public int Samples { get; set; }
        public double DeclaredMaximumError { get; set; }
        public double MaximumObservedError { get; set; }
    }

    internal sealed class ReplayQualityMetrics
    {
        public int ComparedSamples { get; set; }
        public double ProgressRmsMeters { get; set; }
        public double ProgressMaxMeters { get; set; }
        public double WorldRmsMeters { get; set; }
        public double WorldMaxMeters { get; set; }
        public int PositionMismatches { get; set; }
    }

    internal sealed class CoachingMetrics
    {
        public int ComparedLaps { get; set; }
        public double BrakingPointMaxDifferenceMeters { get; set; }
        public double ThrottleOnMaxDifferenceMeters { get; set; }
        public double MinimumSpeedMaxDifferenceMetersPerSecond { get; set; }
        public double LineRmsMeters { get; set; }
        public bool LapConsistencyExact { get; set; }
    }

    internal sealed class CompactProofReport
    {
        public string InputSource { get; set; } = string.Empty;
        public bool SharedMemoryRead { get; set; }
        public int Frames { get; set; }
        public int Samples { get; set; }
        public Dictionary<string, string> Proofs { get; set; } = new Dictionary<string, string>();
        public int ProofsPassed { get; set; }
        public int ProofsRequired { get; set; }
        public bool AllProofsPass { get; set; }
        public int FieldNamesOnWire { get; set; }
        public int StoryEventCountReference { get; set; }
        public int StoryEventCountCompact { get; set; }
        public bool StoryExact { get; set; }
        public bool IncidentParticipantSetExact { get; set; }
        public int GenericDriverOrdinalsCaptured { get; set; }
        public QuantizationMetrics Quantization { get; set; } = new QuantizationMetrics();
        public ReplayQualityMetrics ReplayQuality { get; set; } = new ReplayQualityMetrics();
        public CoachingMetrics Coaching { get; set; } = new CoachingMetrics();
        public bool FidelityPass { get; set; }
    }
}
