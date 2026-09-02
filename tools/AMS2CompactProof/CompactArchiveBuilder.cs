using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using AMS2LeagueClient.Core.CompactTelemetry;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2CompactProof
{
    internal sealed class CompactArchiveBuilder
    {
        private const int TimeRangeChunkMs = 300_000;
        private readonly ReferenceFixture _reference;
        private readonly string _archiveRoot;
        private readonly Dictionary<string, int> _storyFields = ReferenceFixture.FieldMap(TelemetryFieldCatalog.RaceStoryFields);
        private readonly Dictionary<string, int> _replayFields = ReferenceFixture.FieldMap(TelemetryFieldCatalog.ParticipantReplayFields);
        private readonly Dictionary<string, int> _driverFields = ReferenceFixture.FieldMap(TelemetryFieldCatalog.DriverTelemetryFields);
        private readonly Dictionary<string, int> _incidentFields = ReferenceFixture.FieldMap(TelemetryFieldCatalog.IncidentTraceFields);
        private readonly List<CompactFrameArtifact> _frames = new List<CompactFrameArtifact>();
        private uint _sequence;

        public CompactArchiveBuilder(ReferenceFixture reference, string archiveRoot)
        {
            _reference = reference ?? throw new ArgumentNullException(nameof(reference));
            _archiveRoot = archiveRoot ?? throw new ArgumentNullException(nameof(archiveRoot));
        }

        public CompactArchiveResult Build()
        {
            Directory.CreateDirectory(_archiveRoot);
            WriteSessionStatic();
            WriteStory();
            WriteReplay();
            WriteTrackGeometry();
            WriteDriverFast();
            WriteDriverMotion();
            WriteDriverSlow();
            WriteDriverCatalog();
            WriteIncident();
            WriteLossLedger();
            WriteFinalize();
            return new CompactArchiveResult(_archiveRoot, _frames);
        }

        private void WriteSessionStatic()
        {
            double maxRpm = Value(_reference.DriverFast20Hz[0].Source, _driverFields, "maxRpm") ?? 9_000;
            var row = new double?[] { ReferenceFixture.SyntheticTrackLengthMeters, maxRpm };
            IReadOnlyList<CompactParticipantDictionaryEntry> participants = _reference.Participants
                .Select(value => new CompactParticipantDictionaryEntry(
                    checked((ushort)(value.ParticipantRef - 1)), value.Name, value.Vehicle, value.VehicleClass))
                .ToArray();
            WriteFrame("SESSION", CompactTelemetrySchemaId.SessionStaticV1, 0, 0,
                new[] { new CompactTelemetrySample(0, row) }, participants);
        }

        private void WriteStory()
        {
            foreach (IGrouping<long, StorySourceSample> chunk in _reference.Story.GroupBy(value => value.ElapsedMs / TimeRangeChunkMs))
            {
                StorySourceSample[] source = chunk.OrderBy(value => value.ElapsedMs).ToArray();
                CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.RaceEventV1);
                Dictionary<string, int> eventTypes = DictionaryRefs(source.Select(value => value.EventType));
                Dictionary<string, int> eventIds = DictionaryRefs(source.Select(value => value.EventId));
                Dictionary<string, int> factCodes = DictionaryRefs(source.Select(value => value.FactCode).OfType<string>());
                CompactTelemetrySample[] samples = source.Select(value =>
                {
                    double?[] row = Project(schema, value.Source, _storyFields);
                    Set(row, schema, "eventTypeRef", eventTypes[value.EventType]);
                    Set(row, schema, "eventIdRef", eventIds[value.EventId]);
                    Set(row, schema, "factCodeRef", value.FactCode == null
                        ? (double?)null
                        : factCodes[value.FactCode]);
                    return new CompactTelemetrySample(value.ElapsedMs, row);
                }).ToArray();
                var strings = new List<CompactStringDictionaryEntry>();
                AddDictionary(strings, CompactStringDictionaryId.EventType, eventTypes);
                AddDictionary(strings, CompactStringDictionaryId.EventId, eventIds);
                AddDictionary(strings, CompactStringDictionaryId.FactCode, factCodes);
                WriteFrame("STORY", schema.Id, samples[0].ElapsedMs, 0, samples, strings: strings);
            }
        }

        private void WriteReplay()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.ParticipantReplayV1);
            var progressFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "participantRef", "lap", "lapDistanceMeters", "racePosition", "raceStateRaw", "pitStateRaw", "isActive"
            };
            var worldFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "participantRef", "lap", "lapDistanceMeters", "worldX", "worldY", "worldZ", "headingRadians", "speedMetersPerSecond"
            };
            int incidentAt = _reference.DurationMs / 2;
            IEnumerable<ReplaySourceSample> worldSparse = _reference.ReplayReference5Hz.Where(value =>
                value.ElapsedMs % 5_000 == 0 || value.ElapsedMs < 10_000 ||
                Math.Abs(value.ElapsedMs - incidentAt) <= 3_000 || value.ElapsedMs >= _reference.DurationMs - 1_000);
            var extensionContext = new HashSet<string>(StringComparer.Ordinal) { "participantRef", "lap" };
            foreach (CompactTelemetryField field in schema.Fields)
            {
                if (!progressFields.Contains(field.Name) && !worldFields.Contains(field.Name)) extensionContext.Add(field.Name);
            }
            IEnumerable<ReplaySourceSample> sparse = _reference.ReplayReference5Hz.Where(value => value.ElapsedMs % 20_000 == 0);

            var chunks = new SortedDictionary<long, Dictionary<(long Elapsed, int Participant), double?[]>>();
            MergeReplayRows(chunks, schema, _reference.ReplayAdaptive, progressFields);
            MergeReplayRows(chunks, schema, worldSparse, worldFields);
            MergeReplayRows(chunks, schema, sparse, extensionContext);
            foreach (KeyValuePair<long, Dictionary<(long Elapsed, int Participant), double?[]>> chunk in chunks)
            {
                CompactTelemetrySample[] samples = chunk.Value
                    .OrderBy(value => value.Key.Elapsed)
                    .ThenBy(value => value.Key.Participant)
                    .Select(value => new CompactTelemetrySample(value.Key.Elapsed, value.Value))
                    .ToArray();
                WriteFrame("REPLAY", schema.Id, samples[0].ElapsedMs, 0, samples);
            }
        }

        private void MergeReplayRows(
            IDictionary<long, Dictionary<(long Elapsed, int Participant), double?[]>> chunks,
            CompactTelemetrySchema schema,
            IEnumerable<ReplaySourceSample> source,
            ISet<string> includedFields)
        {
            foreach (ReplaySourceSample sample in source)
            {
                long chunkKey = sample.ElapsedMs / TimeRangeChunkMs;
                if (!chunks.TryGetValue(chunkKey, out Dictionary<(long Elapsed, int Participant), double?[]>? rows))
                {
                    rows = new Dictionary<(long Elapsed, int Participant), double?[]>();
                    chunks.Add(chunkKey, rows);
                }
                var rowKey = (sample.ElapsedMs, sample.ParticipantRef);
                if (!rows.TryGetValue(rowKey, out double?[]? row))
                {
                    row = new double?[schema.Fields.Count];
                    rows.Add(rowKey, row);
                }
                double?[] projected = Project(schema, sample.Source, _replayFields, includedFields);
                for (int index = 0; index < projected.Length; index++)
                {
                    if (!projected[index].HasValue) continue;
                    if (row[index].HasValue && row[index]!.Value != projected[index]!.Value)
                    {
                        throw new InvalidDataException("Conflicting merged replay value for " + schema.Fields[index].Name + ".");
                    }
                    row[index] = projected[index];
                }
            }
        }

        private void WriteTrackGeometry()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.TrackGeometryV1);
            var byBin = new SortedDictionary<int, DriverSourceSample>();
            foreach (DriverSourceSample sample in _reference.DriverMotion5Hz)
            {
                double lapDistance = Required(sample.Source, _driverFields, "lapDistanceMeters");
                int lap = checked((int)Required(sample.Source, _driverFields, "lap"));
                if (lap != 1) break;
                int bin = checked((int)Math.Round(lapDistance / 20.0, MidpointRounding.AwayFromZero));
                if (!byBin.ContainsKey(bin)) byBin.Add(bin, sample);
            }
            CompactTelemetrySample[] rows = byBin.Select((pair, index) => new CompactTelemetrySample(index, new double?[]
            {
                Required(pair.Value.Source, _driverFields, "lapDistanceMeters"),
                Required(pair.Value.Source, _driverFields, "worldX"),
                Required(pair.Value.Source, _driverFields, "worldY"),
                Required(pair.Value.Source, _driverFields, "worldZ")
            })).ToArray();
            WriteFrame("TRACK_GEOMETRY", schema.Id, 0, 1, rows);
        }

        private void WriteDriverFast()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverFastV1);
            foreach (IGrouping<long, DriverSourceSample> chunk in _reference.DriverFast20Hz.GroupBy(value => value.ElapsedMs / TimeRangeChunkMs))
            {
                DriverSourceSample[] source = chunk.OrderBy(value => value.ElapsedMs).ToArray();
                CompactTelemetrySample[] rows = source.Select(value => new CompactTelemetrySample(value.ElapsedMs, new double?[]
                {
                    Value(value.Source, _driverFields, "unfilteredThrottle") ?? Value(value.Source, _driverFields, "throttle"),
                    Value(value.Source, _driverFields, "unfilteredBrake") ?? Value(value.Source, _driverFields, "brake"),
                    Value(value.Source, _driverFields, "unfilteredSteering") ?? Value(value.Source, _driverFields, "steering"),
                    Value(value.Source, _driverFields, "speedMetersPerSecond"),
                    Value(value.Source, _driverFields, "lapDistanceMeters"),
                    Value(value.Source, _driverFields, "longitudinalAccelerationMetersPerSecondSquared"),
                    Value(value.Source, _driverFields, "lateralAccelerationMetersPerSecondSquared")
                })).ToArray();
                WriteFrame("DRIVER_FAST", schema.Id, rows[0].ElapsedMs, 50, rows);
            }
        }

        private void WriteDriverMotion()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverMotionV1);
            foreach (IGrouping<long, DriverSourceSample> chunk in _reference.DriverMotion5Hz.GroupBy(value => value.ElapsedMs / TimeRangeChunkMs))
            {
                DriverSourceSample[] source = chunk.OrderBy(value => value.ElapsedMs).ToArray();
                CompactTelemetrySample[] rows = source.Select(value => new CompactTelemetrySample(value.ElapsedMs, new double?[]
                {
                    Value(value.Source, _driverFields, "worldX"),
                    Value(value.Source, _driverFields, "worldY"),
                    Value(value.Source, _driverFields, "worldZ"),
                    Value(value.Source, _driverFields, "headingRadians"),
                    Value(value.Source, _driverFields, "rpm")
                })).ToArray();
                WriteFrame("DRIVER_MOTION", schema.Id, rows[0].ElapsedMs, 200, rows);
            }
        }

        private void WriteDriverSlow()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverSlowV1);
            foreach (IGrouping<long, DriverSourceSample> chunk in _reference.DriverSlow1Hz.GroupBy(value => value.ElapsedMs / TimeRangeChunkMs))
            {
                DriverSourceSample[] source = chunk.OrderBy(value => value.ElapsedMs).ToArray();
                CompactTelemetrySample[] rows = source.Select(value => new CompactTelemetrySample(value.ElapsedMs, new double?[]
                {
                    Value(value.Source, _driverFields, "fuelLiters"),
                    Value(value.Source, _driverFields, "engineDamage"),
                    Value(value.Source, _driverFields, "aeroDamage"),
                    Value(value.Source, _driverFields, "trackTemperatureCelsius")
                })).ToArray();
                WriteFrame("DRIVER_SLOW", schema.Id, rows[0].ElapsedMs, 1_000, rows);
            }
        }

        private void WriteDriverCatalog()
        {
            var dedicated = new HashSet<string>(StringComparer.Ordinal)
            {
                "sessionElapsedMs", "capturedAtUnixMs", "throttle", "brake", "steering",
                "unfilteredThrottle", "unfilteredBrake", "unfilteredSteering",
                "speedMetersPerSecond", "lapDistanceMeters",
                "longitudinalAccelerationMetersPerSecondSquared", "lateralAccelerationMetersPerSecondSquared",
                "worldX", "worldY", "worldZ", "headingRadians", "rpm",
                "fuelLiters", "engineDamage", "aeroDamage", "trackTemperatureCelsius"
            };
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverChangeV1);
            int[] catalogOrdinals = Enumerable.Range(0, TelemetryFieldCatalog.DriverTelemetryFields.Count)
                .Where(fieldOrdinal => !dedicated.Contains(TelemetryFieldCatalog.DriverTelemetryFields[fieldOrdinal]))
                .ToArray();
            foreach (IGrouping<long, DriverSourceSample> chunk in _reference.DriverCatalogPoint2Hz
                .Where(value => value.ElapsedMs % 20_000 == 0)
                .GroupBy(value => value.ElapsedMs / TimeRangeChunkMs))
            {
                CompactTelemetrySample[] rows = chunk
                    .OrderBy(value => value.ElapsedMs)
                    .SelectMany(value => catalogOrdinals.Select(fieldOrdinal => new CompactTelemetrySample(
                        value.ElapsedMs,
                        new double?[]
                    {
                        fieldOrdinal,
                        value.Source[fieldOrdinal]
                    })))
                    .ToArray();
                WriteFrame(
                    "DRIVER_CHANGE",
                    schema.Id,
                    rows[0].ElapsedMs,
                    0,
                    rows,
                    suffix: chunk.Key.ToString("D4", CultureInfo.InvariantCulture));
            }
        }

        private void WriteIncident()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.IncidentV1);
            IncidentSourceSample[] source = _reference.Incident20Hz
                .OrderBy(value => value.ElapsedMs).ThenBy(value => value.ParticipantRef)
                .ToArray();
            Dictionary<string, int> candidates = DictionaryRefs(source.Select(value => value.Candidate));
            Dictionary<string, int> triggerCodes = DictionaryRefs(source.Select(value => value.TriggerCode));
            CompactTelemetrySample[] rows = source.Select(value =>
            {
                double?[] row = Project(schema, value.Source, _incidentFields);
                Set(row, schema, "candidateRef", candidates[value.Candidate]);
                Set(row, schema, "triggerCodeRef", triggerCodes[value.TriggerCode]);
                return new CompactTelemetrySample(value.ElapsedMs, row);
            }).ToArray();
            var strings = new List<CompactStringDictionaryEntry>();
            AddDictionary(strings, CompactStringDictionaryId.IncidentCandidate, candidates);
            AddDictionary(strings, CompactStringDictionaryId.IncidentTriggerCode, triggerCodes);
            WriteFrame("INCIDENT", schema.Id, rows[0].ElapsedMs, 0, rows, strings: strings);
        }

        private void WriteLossLedger()
        {
            var rows = new[] { new CompactTelemetrySample(_reference.DurationMs, new double?[] { 0, 0, 0 }) };
            WriteFrame("INTEGRITY", CompactTelemetrySchemaId.LossLedgerV1, _reference.DurationMs, 0, rows);
        }

        private void WriteFinalize()
        {
            int accepted = checked(_reference.Story.Count + _reference.ReplayAdaptive.Count + _reference.DriverFast20Hz.Count +
                _reference.DriverMotion5Hz.Count + _reference.DriverSlow1Hz.Count + _reference.Incident20Hz.Count);
            var rows = new[] { new CompactTelemetrySample(_reference.DurationMs, new double?[] { accepted, accepted, 0, 2 }) };
            WriteFrame("INTEGRITY", CompactTelemetrySchemaId.AttemptFinalizeV1, _reference.DurationMs, 0, rows);
        }

        private void WriteFrame(
            string family,
            CompactTelemetrySchemaId schemaId,
            long baseElapsedMs,
            uint cadenceMs,
            IEnumerable<CompactTelemetrySample> samples,
            IEnumerable<CompactParticipantDictionaryEntry>? participants = null,
            string? suffix = null,
            IEnumerable<CompactStringDictionaryEntry>? strings = null)
        {
            CompactTelemetrySample[] sampleArray = samples.ToArray();
            if (sampleArray.Length == 0) return;
            var block = new CompactTelemetryBlock(schemaId, baseElapsedMs, cadenceMs, sampleArray);
            var envelope = new CompactTelemetryEnvelope(1, 1, _sequence, block, participants, strings);
            long started = Stopwatch.GetTimestamp();
            byte[] raw = CompactTelemetryCodec.Encode(envelope);
            double encodeMilliseconds = ElapsedMilliseconds(started);
            started = Stopwatch.GetTimestamp();
            CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(raw);
            double decodeMilliseconds = ElapsedMilliseconds(started);
            if (decoded.Block.Samples.Count != sampleArray.Length) throw new InvalidDataException("Compact frame round-trip sample mismatch.");
            byte[] gzip = Gzip(raw);
            string directory = Path.Combine(_archiveRoot, family.ToLowerInvariant());
            Directory.CreateDirectory(directory);
            string safeSuffix = string.IsNullOrWhiteSpace(suffix) ? string.Empty : "-" + suffix;
            string fileName = _sequence.ToString("D6", CultureInfo.InvariantCulture) + "-" +
                ((ushort)schemaId).ToString("X4", CultureInfo.InvariantCulture) + safeSuffix + ".a2ct.gz";
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, gzip);
            _frames.Add(new CompactFrameArtifact
            {
                Sequence = _sequence,
                Family = family,
                SchemaId = (ushort)schemaId,
                SchemaName = CompactTelemetrySchemaRegistry.Get(schemaId).Name,
                StartElapsedMs = sampleArray[0].ElapsedMs,
                EndElapsedMs = sampleArray[sampleArray.Length - 1].ElapsedMs,
                Samples = sampleArray.Length,
                RawBytes = raw.Length,
                WireBytes = gzip.Length,
                RawSha256 = Sha256(raw),
                WireSha256 = Sha256(gzip),
                RelativePath = Path.GetRelativePath(_archiveRoot, path).Replace('\\', '/'),
                EncodeMilliseconds = encodeMilliseconds,
                DecodeMilliseconds = decodeMilliseconds
            });
            _sequence++;
        }

        private static double?[] Project(
            CompactTelemetrySchema schema,
            double?[] source,
            IReadOnlyDictionary<string, int> sourceFields,
            ISet<string>? includedFields = null)
        {
            var values = new double?[schema.Fields.Count];
            for (int index = 0; index < schema.Fields.Count; index++)
            {
                string name = schema.Fields[index].Name;
                if (includedFields != null && !includedFields.Contains(name))
                {
                    values[index] = null;
                    continue;
                }
                double? value = Value(source, sourceFields, name);
                if (value.HasValue && IsParticipantReference(name) && value.Value >= 1)
                {
                    value = value.Value - 1;
                }
                values[index] = value;
            }
            return values;
        }

        private static Dictionary<string, int> DictionaryRefs(IEnumerable<string> source)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string value in source)
            {
                if (!result.ContainsKey(value)) result.Add(value, result.Count);
            }
            return result;
        }

        private static void AddDictionary(
            ICollection<CompactStringDictionaryEntry> target,
            CompactStringDictionaryId dictionaryId,
            IReadOnlyDictionary<string, int> values)
        {
            foreach (KeyValuePair<string, int> pair in values.OrderBy(value => value.Value))
            {
                target.Add(new CompactStringDictionaryEntry(dictionaryId, checked((uint)pair.Value), pair.Key));
            }
        }

        private static void Set(double?[] row, CompactTelemetrySchema schema, string field, double? value)
        {
            int ordinal = schema.Fields.First(item => string.Equals(item.Name, field, StringComparison.Ordinal)).Ordinal;
            row[ordinal] = value;
        }

        private static bool IsParticipantReference(string name)
            => string.Equals(name, "participantRef", StringComparison.Ordinal)
                || string.Equals(name, "viewedParticipantRef", StringComparison.Ordinal)
                || string.Equals(name, "collisionOpponentRef", StringComparison.Ordinal);

        private static double? Value(double?[] source, IReadOnlyDictionary<string, int> fields, string name)
            => fields.TryGetValue(name, out int index) && index < source.Length ? source[index] : null;

        private static double Required(double?[] source, IReadOnlyDictionary<string, int> fields, string name)
            => Value(source, fields, name) ?? throw new InvalidDataException("Required compact source field is null: " + name);

        private static byte[] Gzip(byte[] raw)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true)) gzip.Write(raw, 0, raw.Length);
            return output.ToArray();
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return Convert.ToHexString(hash.ComputeHash(bytes)).ToLowerInvariant();
        }

        private static double ElapsedMilliseconds(long started)
            => (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
    }

    internal sealed class CompactArchiveResult
    {
        public CompactArchiveResult(string archiveRoot, IReadOnlyList<CompactFrameArtifact> frames)
        {
            ArchiveRoot = archiveRoot;
            Frames = frames;
        }
        public string ArchiveRoot { get; }
        public IReadOnlyList<CompactFrameArtifact> Frames { get; }
        public long RawBytes => Frames.Sum(value => value.RawBytes);
        public long WireBytes => Frames.Sum(value => value.WireBytes);
        public int Samples => Frames.Sum(value => value.Samples);
        public double EncodeMilliseconds => Frames.Sum(value => value.EncodeMilliseconds);
        public double DecodeMilliseconds => Frames.Sum(value => value.DecodeMilliseconds);
        public IReadOnlyDictionary<string, long> WireBreakdown => Frames
            .GroupBy(value => value.Family, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(value => (long)value.WireBytes), StringComparer.Ordinal);
    }

    internal sealed class CompactFrameArtifact
    {
        public uint Sequence { get; set; }
        public string Family { get; set; } = string.Empty;
        public ushort SchemaId { get; set; }
        public string SchemaName { get; set; } = string.Empty;
        public long StartElapsedMs { get; set; }
        public long EndElapsedMs { get; set; }
        public int Samples { get; set; }
        public int RawBytes { get; set; }
        public int WireBytes { get; set; }
        public string RawSha256 { get; set; } = string.Empty;
        public string WireSha256 { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public double EncodeMilliseconds { get; set; }
        public double DecodeMilliseconds { get; set; }
    }
}
