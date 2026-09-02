using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2TelemetryProof
{
    internal static class Program
    {
        private const int ChunkMs = 30_000;
        private const double TrackLength = 5_793.0;
        private static readonly JsonSerializerOptions Json = CreateJson(false);
        private static readonly JsonSerializerOptions PrettyJson = CreateJson(true);

        private static readonly string[] ReplayFields = TelemetryFieldCatalog.ParticipantReplayFields.ToArray();
        private static readonly string[] DriverFields = TelemetryFieldCatalog.DriverTelemetryFields.ToArray();
        private static readonly string[] StoryFields = TelemetryFieldCatalog.RaceStoryFields.ToArray();
        private static readonly string[] IncidentFields = TelemetryFieldCatalog.IncidentTraceFields.ToArray();

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: AMS2TelemetryProof generate <archive-root> [minutes] [participants]");
                    Console.Error.WriteLine("   or: AMS2TelemetryProof render <archive-root> <output-directory>");
                    Console.Error.WriteLine("   or: AMS2TelemetryProof validate <archive-root> <output-json>");
                    return 2;
                }

                string command = args[0].Trim().ToLowerInvariant();
                if (command == "generate")
                {
                    int minutes = args.Length > 2 ? ParseBounded(args[2], 1, 240, "minutes") : 60;
                    int participants = args.Length > 3 ? ParseBounded(args[3], 2, 64, "participants") : 32;
                    Generate(Path.GetFullPath(args[1]), minutes, participants);
                    return 0;
                }
                if (command == "render" && args.Length >= 3)
                {
                    Render(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
                    return 0;
                }
                if (command == "validate" && args.Length >= 3)
                {
                    return LiveCaptureValidation.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])) ? 0 : 1;
                }

                Console.Error.WriteLine("Unknown or incomplete command.");
                return 2;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void Generate(string root, int minutes, int participantCount)
        {
            Directory.CreateDirectory(root);
            int durationMs = checked(minutes * 60_000);
            DateTimeOffset startedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
            TelemetryArchiveIdentity identity = TelemetryArchiveIdentityFactory.StartSession(
                "fixture-60min-32car-v1", "witness-fixture-offline-proof-v1");
            var store = new TelemetryChunkStore(root, identity);

            CommitMetadata(store, identity, startedAt, durationMs, participantCount);
            CommitStory(store, identity, startedAt, durationMs);
            CommitReplay(store, identity, startedAt, durationMs, participantCount);
            CommitDriver(store, identity, startedAt, durationMs);
            CommitIncident(store, identity, startedAt, durationMs, participantCount);

            BudgetManifest manifest = Measure(root, minutes, participantCount, identity);
            string manifestPath = Path.Combine(root, "fixture-manifest.json");
            File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, PrettyJson));
            Console.WriteLine("FIXTURE_ARCHIVE=" + root);
            Console.WriteLine("SESSION_DIRECTORY=" + store.SessionDirectory);
            Console.WriteLine("MANIFEST=" + manifestPath);
            Console.WriteLine("UNCOMPRESSED_BYTES=" + manifest.TotalUncompressedBytes.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("GZIP_BYTES=" + manifest.TotalCompressedBytes.ToString(CultureInfo.InvariantCulture));
        }

        private static void CommitMetadata(
            TelemetryChunkStore store,
            TelemetryArchiveIdentity identity,
            DateTimeOffset startedAt,
            int durationMs,
            int participantCount)
        {
            var participants = Enumerable.Range(0, participantCount).Select(index => new TelemetryParticipantDictionaryEntry
            {
                ParticipantRef = index + 1,
                Slot = index,
                Generation = 1,
                NameSnapshot = "Fixture Driver " + (index + 1).ToString("D2", CultureInfo.InvariantCulture),
                VehicleRef = "GT3-FIXTURE",
                VehicleClassRef = "GT3"
            }).ToList();
            var record = new SessionMetadataSample
            {
                CapturedAtUtc = startedAt,
                SessionElapsedMs = 0,
                GameBuild = 1550,
                SharedMemoryVersion = 14,
                ClientVersion = "0.2.2-phase-fixture",
                ParserVersion = "shm-v14",
                Track = "Synthetic Future Proof Circuit",
                Layout = "Grand Prix",
                TrackLengthMeters = TrackLength,
                SessionType = "RACE",
                ClockSource = "MONOTONIC_CAPTURE_CLOCK",
                TimedSessionDurationMs = durationMs,
                EventTimeRemainingMs = durationMs,
                JoinedMidSession = false,
                SessionStartOffsetMs = 0,
                SessionStartOffsetStatus = TelemetryCapabilityState.CAPTURED,
                SessionDurationMinutes = durationMs / 60_000.0,
                ConfiguredLaps = null,
                ObservedParticipants = participantCount,
                VehicleClass = "GT3",
                SessionPrivacyRaw = "PUBLIC_FIXTURE",
                CaptureStarted = true,
                CaptureEnded = true,
                CaptureCompleteness = "COMPLETE_FIXTURE",
                Participants = participants
            };
            record.Fields["raceStory"] = CapturedBoolean(true);
            record.Fields["replay"] = CapturedBoolean(true);
            record.Fields["driverTelemetry"] = CapturedBoolean(true);
            record.Fields["incidentHighRate"] = CapturedBoolean(true);
            var envelope = BaseEnvelope(identity, TelemetryStreamType.SESSION_METADATA, 0, 0, 0, startedAt, startedAt);
            envelope.Data.Records = new List<SessionMetadataSample> { record };
            envelope.Quality.ExpectedSampleCount = 1;
            envelope.Quality.ActualSampleCount = 1;
            store.Commit(envelope);
        }

        private static TelemetryCapabilityValue CapturedBoolean(bool value)
            => new TelemetryCapabilityValue
            {
                State = TelemetryCapabilityState.CAPTURED,
                BooleanValue = value
            };

        private static void CommitStory(
            TelemetryChunkStore store,
            TelemetryArchiveIdentity identity,
            DateTimeOffset startedAt,
            int durationMs)
        {
            string[] eventTypes = { "SESSION_START", "RACE_START", "LAP_COMPLETE", "POSITION_CHANGE", "INCIDENT_CANDIDATE", "FINISH", "SESSION_END" };
            int chunks = (durationMs + ChunkMs - 1) / ChunkMs;
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                int start = chunk * ChunkMs;
                int end = Math.Min(durationMs, start + ChunkMs);
                var rows = new List<double?[]>();
                var eventIds = new List<string>();
                Action<int, string, int, int?, int?, int?> add = (elapsed, type, participant, lap, before, after) =>
                {
                    int typeRef = Array.IndexOf(eventTypes, type);
                    eventIds.Add("fixture-event-" + eventIds.Count.ToString("D3", CultureInfo.InvariantCulture) + "-" + elapsed.ToString(CultureInfo.InvariantCulture));
                    double progress = ((elapsed + participant * 137) % 90_000) / 90_000.0;
                    TrackPoint point = PointAt(progress);
                    rows.Add(ExpandFixtureRow(new double?[]
                    {
                        elapsed, startedAt.AddMilliseconds(elapsed).ToUnixTimeMilliseconds(), typeRef, eventIds.Count - 1, null,
                        participant, lap, Sector(progress), progress * TrackLength, point.X, point.Y, point.Z,
                        before, after, type == "LAP_COMPLETE" ? 89_500 + participant * 37 : (int?)null,
                        2, 0, 0, 0, null, type == "FINISH" ? 2 : (int?)null
                    }, StoryFields, elapsed, participant));
                };

                if (start == 0)
                {
                    add(0, "SESSION_START", 1, 1, null, null);
                    add(1_000, "RACE_START", 1, 1, null, null);
                }
                for (int elapsed = Math.Max(90_000, ((start + 89_999) / 90_000) * 90_000); elapsed < end; elapsed += 90_000)
                {
                    add(elapsed, "LAP_COMPLETE", 1, elapsed / 90_000, null, null);
                }
                if (start <= durationMs / 3 && durationMs / 3 < end)
                {
                    add(durationMs / 3, "POSITION_CHANGE", 1, 3, 4, 3);
                }
                if (start <= durationMs / 2 && durationMs / 2 < end)
                {
                    add(durationMs / 2, "INCIDENT_CANDIDATE", 8, 5, null, null);
                }
                if (end == durationMs)
                {
                    add(Math.Max(start, durationMs - 1_000), "FINISH", 1, Math.Max(1, durationMs / 90_000), null, null);
                    add(durationMs, "SESSION_END", 1, Math.Max(1, durationMs / 90_000), null, null);
                }
                if (rows.Count == 0) continue;
                TelemetryChunkEnvelope envelope = BaseEnvelope(
                    identity,
                    TelemetryStreamType.RACE_STORY,
                    chunk,
                    (long)rows[0][0]!,
                    (long)rows[rows.Count - 1][0]!,
                    startedAt.AddMilliseconds((long)rows[0][0]!),
                    startedAt.AddMilliseconds((long)rows[rows.Count - 1][0]!));
                envelope.Data.Fields = StoryFields;
                envelope.Data.Dictionaries["eventTypes"] = eventTypes;
                envelope.Data.Dictionaries["eventIds"] = eventIds.ToArray();
                envelope.Data.Rows = rows;
                envelope.Quality.ExpectedSampleCount = rows.Count;
                envelope.Quality.ActualSampleCount = rows.Count;
                store.Commit(envelope);
            }
        }

        private static void CommitReplay(
            TelemetryChunkStore store,
            TelemetryArchiveIdentity identity,
            DateTimeOffset startedAt,
            int durationMs,
            int participants)
        {
            string[] names = Enumerable.Range(1, participants).Select(value => "Fixture Driver " + value.ToString("D2", CultureInfo.InvariantCulture)).ToArray();
            string[] vehicles = { "GT3-FIXTURE" };
            string[] classes = { "GT3" };
            int chunks = (durationMs + ChunkMs - 1) / ChunkMs;
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                int start = chunk * ChunkMs;
                int end = Math.Min(durationMs, start + ChunkMs);
                var rows = new List<double?[]>(((end - start) / 200 + 1) * participants);
                for (int elapsed = start; elapsed < end; elapsed += 200)
                {
                    for (int participant = 0; participant < participants; participant++)
                    {
                        double totalLaps = (elapsed + participant * 1_100.0) / (89_000.0 + participant * 45.0);
                        double progress = totalLaps - Math.Floor(totalLaps);
                        TrackPoint point = PointAt(progress, participant * 0.55);
                        double heading = HeadingAt(progress);
                        int position = 1 + ((participant + (elapsed / 180_000)) % participants);
                        rows.Add(ExpandFixtureRow(new double?[]
                        {
                            elapsed, participant + 1, participant, 1, (int)Math.Floor(totalLaps) + 1,
                            progress * TrackLength, position, point.X, point.Y, point.Z, 2, 0,
                            participant, 0, 0, heading, 64 + 8 * Math.Sin(progress * Math.PI * 2)
                        }, ReplayFields, elapsed, participant));
                    }
                }
                TelemetryChunkEnvelope envelope = BaseEnvelope(
                    identity, TelemetryStreamType.PARTICIPANT_REPLAY, chunk, start, Math.Max(start, end - 200),
                    startedAt.AddMilliseconds(start), startedAt.AddMilliseconds(Math.Max(start, end - 200)));
                envelope.Data.Fields = ReplayFields;
                envelope.Data.Dictionaries["names"] = names;
                envelope.Data.Dictionaries["vehicles"] = vehicles;
                envelope.Data.Dictionaries["vehicleClasses"] = classes;
                envelope.Data.Rows = rows;
                envelope.Quality.TargetSampleRateHz = 5;
                envelope.Quality.ExpectedSampleCount = rows.Count;
                envelope.Quality.ActualSampleCount = rows.Count;
                store.Commit(envelope);
            }
        }

        private static void CommitDriver(
            TelemetryChunkStore store,
            TelemetryArchiveIdentity identity,
            DateTimeOffset startedAt,
            int durationMs)
        {
            int chunks = (durationMs + ChunkMs - 1) / ChunkMs;
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                int start = chunk * ChunkMs;
                int end = Math.Min(durationMs, start + ChunkMs);
                var rows = new List<double?[]>((end - start) / 50 + 1);
                for (int elapsed = start; elapsed < end; elapsed += 50)
                {
                    double totalLaps = elapsed / 89_000.0;
                    double progress = totalLaps - Math.Floor(totalLaps);
                    double angle = progress * Math.PI * 2;
                    TrackPoint point = PointAt(progress);
                    double brake = Gaussian(progress, 0.08, 0.018) + Gaussian(progress, 0.42, 0.025) + Gaussian(progress, 0.73, 0.022);
                    brake = Math.Min(1, brake);
                    double throttle = Math.Max(0, Math.Min(1, 0.82 + 0.18 * Math.Sin(angle * 3) - brake * 1.35));
                    double steering = Math.Max(-1, Math.Min(1, 0.58 * Math.Sin(angle) + 0.22 * Math.Sin(angle * 3)));
                    double speed = 78 - brake * 38 + 9 * Math.Cos(angle * 2);
                    double heading = HeadingAt(progress);
                    double velocityX = Math.Cos(heading) * speed;
                    double velocityZ = Math.Sin(heading) * speed;
                    int lap = (int)Math.Floor(totalLaps) + 1;
                    int currentLapMs = elapsed - (lap - 1) * 89_000;
                    rows.Add(ExpandFixtureRow(new double?[]
                    {
                        elapsed, startedAt.AddMilliseconds(elapsed).ToUnixTimeMilliseconds(), 1, lap, Sector(progress), progress * TrackLength,
                        point.X, point.Y, point.Z, speed, 3_000 + throttle * 5_200, 3 + (int)(speed / 25), throttle,
                        brake, steering, 0, throttle, brake, steering, 0,
                        -brake * 10.5 + throttle * 3.4, steering * speed * 0.12, 0.08 * Math.Sin(angle * 4),
                        heading, velocityX, 0, velocityZ,
                        (100 - elapsed / 75_000.0) / 100.0, 100, 100 - elapsed / 75_000.0, 0.54,
                        0.01, 0.015, 0.01, 83 + brake * 7, 84 + brake * 7, 81 + throttle * 5, 82 + throttle * 5,
                        172, 173, 169, 170, 0.96, 0.96, 0.97, 0.97, 34, 24, 0.05,
                        0, 1, currentLapMs
                    }, DriverFields, elapsed, 0));
                }
                TelemetryChunkEnvelope envelope = BaseEnvelope(
                    identity, TelemetryStreamType.DRIVER_TELEMETRY, chunk, start, Math.Max(start, end - 50),
                    startedAt.AddMilliseconds(start), startedAt.AddMilliseconds(Math.Max(start, end - 50)));
                envelope.Visibility = TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS;
                envelope.Data.Fields = DriverFields;
                envelope.Data.Dictionaries["tyreCompounds"] = new[] { "SLICK", "WET" };
                envelope.Data.Rows = rows;
                envelope.StartLap = (start / 89_000) + 1;
                envelope.EndLap = (Math.Max(start, end - 1) / 89_000) + 1;
                envelope.Quality.TargetSampleRateHz = 20;
                envelope.Quality.ExpectedSampleCount = rows.Count;
                envelope.Quality.ActualSampleCount = rows.Count;
                store.Commit(envelope);
            }
        }

        private static void CommitIncident(
            TelemetryChunkStore store,
            TelemetryArchiveIdentity identity,
            DateTimeOffset startedAt,
            int durationMs,
            int participants)
        {
            int trigger = durationMs / 2;
            int start = Math.Max(0, trigger - 3_000);
            int end = Math.Min(durationMs, trigger + 3_000);
            int involved = Math.Min(4, participants);
            var rows = new List<double?[]>();
            for (int elapsed = start; elapsed <= end; elapsed += 50)
            {
                for (int participant = 0; participant < involved; participant++)
                {
                    double totalLaps = (elapsed + participant * 900.0) / 89_000.0;
                    double progress = totalLaps - Math.Floor(totalLaps);
                    double lateral = participant * 0.8 + (elapsed >= trigger ? (participant - 1.5) * (elapsed - trigger) / 500.0 : 0);
                    TrackPoint point = PointAt(progress, lateral);
                    rows.Add(ExpandFixtureRow(new double?[]
                    {
                        elapsed - trigger, elapsed, startedAt.AddMilliseconds(elapsed).ToUnixTimeMilliseconds(), 0, 0,
                        participant + 1, participant, 1, (int)Math.Floor(totalLaps) + 1, progress * TrackLength,
                        participant + 1, point.X, point.Y, point.Z, elapsed >= trigger ? 3 : 2, 0,
                        elapsed >= trigger ? 2 : 0, elapsed >= trigger ? 1 : 0, 0, elapsed == trigger ? 5 : 0,
                        HeadingAt(progress), 55 - participant * 2
                    }, IncidentFields, elapsed, participant));
                }
            }
            TelemetryChunkEnvelope envelope = BaseEnvelope(
                identity, TelemetryStreamType.INCIDENT_TRACE, trigger / ChunkMs, start, end,
                startedAt.AddMilliseconds(start), startedAt.AddMilliseconds(end));
            envelope.Data.Fields = IncidentFields;
            envelope.Data.Dictionaries["candidates"] = new[] { "fixture-incident-1" };
            envelope.Data.Dictionaries["triggerCodes"] = new[] { "WORLD_PROXIMITY_AND_STATE_CHANGE" };
            envelope.Data.Rows = rows;
            envelope.Quality.TargetSampleRateHz = 20;
            envelope.Quality.ExpectedSampleCount = rows.Count;
            envelope.Quality.ActualSampleCount = rows.Count;
            store.Commit(envelope);
        }

        private static double?[] ExpandFixtureRow(
            double?[] prefix,
            IReadOnlyList<string> fields,
            int elapsedMs,
            int ordinal)
        {
            if (prefix.Length > fields.Count)
            {
                throw new InvalidOperationException("Fixture prefix exceeds compact field catalog.");
            }
            var row = new double?[fields.Count];
            Array.Copy(prefix, row, prefix.Length);
            for (int index = prefix.Length; index < row.Length; index++)
            {
                row[index] = ExtendedFixtureValue(fields[index], elapsedMs, ordinal, index);
            }
            return row;
        }

        private static double ExtendedFixtureValue(string field, int elapsedMs, int ordinal, int index)
        {
            string lower = field.ToLowerInvariant();
            double wave = Math.Sin(elapsedMs * 0.00037 + ordinal * 0.41 + index * 0.17);
            if (lower.EndsWith("ref", StringComparison.Ordinal)
                || lower.EndsWith("raw", StringComparison.Ordinal)
                || lower.Contains("state")
                || lower.Contains("flags")
                || lower.Contains("setting")
                || lower.Contains("stage")
                || lower.Contains("sector"))
            {
                return Math.Abs((elapsedMs / 1000 + ordinal + index) % 7);
            }
            if (lower.Contains("active") || lower.Contains("invalid")
                || lower.Contains("enabled") || lower.Contains("overheated")
                || lower.Contains("slipping"))
            {
                return ((elapsedMs / 1000 + ordinal + index) & 1) == 0 ? 0 : 1;
            }
            if (lower.Contains("temperature")) return 70 + index * 0.13 + wave * 9;
            if (lower.Contains("pressure")) return 150 + index * 0.31 + wave * 12;
            if (lower.Contains("damage") || lower.Contains("wear"))
            {
                return Math.Max(0, Math.Min(1, 0.04 + index * 0.0007 + wave * 0.015));
            }
            return index * 0.031 + ordinal * 0.007 + elapsedMs * 0.000013 + wave;
        }

        private static TelemetryChunkEnvelope BaseEnvelope(
            TelemetryArchiveIdentity identity,
            TelemetryStreamType stream,
            int chunkIndex,
            long start,
            long end,
            DateTimeOffset first,
            DateTimeOffset last)
        {
            return new TelemetryChunkEnvelope
            {
                ChunkId = "chunk-" + TelemetryChunkSerializer.StableId(identity.AttemptId, stream.ToString(), chunkIndex.ToString(CultureInfo.InvariantCulture)).Substring(0, 40),
                StreamType = stream,
                Visibility = stream == TelemetryStreamType.DRIVER_TELEMETRY
                    ? TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS
                    : TelemetryVisibility.PUBLIC_REPLAY,
                SessionId = identity.SessionId,
                SessionFingerprint = identity.SessionFingerprint,
                WitnessId = identity.WitnessId,
                AttemptId = identity.AttemptId,
                AttemptNumber = identity.AttemptNumber,
                ChunkIndex = chunkIndex,
                StartElapsedMs = start,
                EndElapsedMs = end,
                FirstCapturedAtUtc = first,
                LastCapturedAtUtc = last,
                Quality = new TelemetryChunkQuality
                {
                    ClockSource = "MONOTONIC_CAPTURE_CLOCK",
                    CaptureCompleteness = "COMPLETE_FIXTURE",
                    SourceWitnessCount = 1
                }
            };
        }

        private static BudgetManifest Measure(
            string root,
            int minutes,
            int participants,
            TelemetryArchiveIdentity identity)
        {
            var streams = new Dictionary<string, StreamBudget>(StringComparer.Ordinal);
            foreach (string metadataPath in Directory.EnumerateFiles(root, "*.upload.json", SearchOption.AllDirectories))
            {
                TelemetryPendingUploadMetadata metadata = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(metadataPath));
                string key = metadata.StreamType.ToString();
                if (!streams.TryGetValue(key, out StreamBudget? budget))
                {
                    budget = new StreamBudget();
                    streams[key] = budget;
                }
                budget.Chunks++;
                budget.UncompressedBytes += metadata.UncompressedBytes;
                budget.CompressedBytes += metadata.CompressedBytes;
                budget.Samples += metadata.Quality.ActualSampleCount;
            }
            long totalUncompressed = streams.Values.Sum(value => value.UncompressedBytes);
            long totalCompressed = streams.Values.Sum(value => value.CompressedBytes);
            var estimates = new Dictionary<string, ClientEstimate>(StringComparer.Ordinal);
            foreach (int clients in new[] { 1, 5, 10, 20 })
            {
                estimates[clients.ToString(CultureInfo.InvariantCulture)] = new ClientEstimate
                {
                    Clients = clients,
                    UncompressedBytes = checked(totalUncompressed * clients),
                    CompressedBytes = checked(totalCompressed * clients)
                };
            }
            return new BudgetManifest
            {
                Schema = "ams2-telemetry-budget-v1",
                FixtureMinutes = minutes,
                Participants = participants,
                SessionId = identity.SessionId,
                SessionFingerprint = identity.SessionFingerprint,
                WitnessId = identity.WitnessId,
                AttemptId = identity.AttemptId,
                Streams = streams,
                TotalUncompressedBytes = totalUncompressed,
                TotalCompressedBytes = totalCompressed,
                CompressionRatio = totalUncompressed == 0 ? 0 : (double)totalCompressed / totalUncompressed,
                ClientEstimates = estimates,
                FullSharedMemory30HzStreamUsed = false
            };
        }

        private static TrackPoint PointAt(double progress, double lateralOffset = 0)
        {
            double angle = progress * Math.PI * 2;
            double x = 800 * Math.Cos(angle) + 80 * Math.Sin(angle * 3);
            double z = 550 * Math.Sin(angle) + 50 * Math.Cos(angle * 2);
            double y = 4 * Math.Sin(angle * 2);
            double heading = HeadingAt(progress);
            return new TrackPoint(x - Math.Sin(heading) * lateralOffset, y, z + Math.Cos(heading) * lateralOffset);
        }

        private static double HeadingAt(double progress)
        {
            const double epsilon = 0.00001;
            double a1 = (progress - epsilon) * Math.PI * 2;
            double a2 = (progress + epsilon) * Math.PI * 2;
            double x1 = 800 * Math.Cos(a1) + 80 * Math.Sin(a1 * 3);
            double z1 = 550 * Math.Sin(a1) + 50 * Math.Cos(a1 * 2);
            double x2 = 800 * Math.Cos(a2) + 80 * Math.Sin(a2 * 3);
            double z2 = 550 * Math.Sin(a2) + 50 * Math.Cos(a2 * 2);
            return Math.Atan2(z2 - z1, x2 - x1);
        }

        private static double Gaussian(double value, double center, double width)
        {
            double delta = value - center;
            delta -= Math.Round(delta);
            double x = delta / width;
            return Math.Exp(-0.5 * x * x);
        }

        private static int Sector(double progress)
            => progress < 1.0 / 3.0 ? 1 : progress < 2.0 / 3.0 ? 2 : 3;

        private static int ParseBounded(string value, int minimum, int maximum, string name)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < minimum || parsed > maximum)
            {
                throw new ArgumentOutOfRangeException(name, "Expected " + minimum + ".." + maximum + ".");
            }
            return parsed;
        }

        private static JsonSerializerOptions CreateJson(bool indented)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.Default
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private sealed class TrackPoint
        {
            public TrackPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
        }

        private sealed class StreamBudget
        {
            public int Chunks { get; set; }
            public long Samples { get; set; }
            public long UncompressedBytes { get; set; }
            public long CompressedBytes { get; set; }
        }

        private sealed class ClientEstimate
        {
            public int Clients { get; set; }
            public long UncompressedBytes { get; set; }
            public long CompressedBytes { get; set; }
        }

        private sealed class BudgetManifest
        {
            public string Schema { get; set; } = string.Empty;
            public int FixtureMinutes { get; set; }
            public int Participants { get; set; }
            public string SessionId { get; set; } = string.Empty;
            public string SessionFingerprint { get; set; } = string.Empty;
            public string WitnessId { get; set; } = string.Empty;
            public string AttemptId { get; set; } = string.Empty;
            public Dictionary<string, StreamBudget> Streams { get; set; } = new Dictionary<string, StreamBudget>();
            public long TotalUncompressedBytes { get; set; }
            public long TotalCompressedBytes { get; set; }
            public double CompressionRatio { get; set; }
            public Dictionary<string, ClientEstimate> ClientEstimates { get; set; } = new Dictionary<string, ClientEstimate>();
            public bool FullSharedMemory30HzStreamUsed { get; set; }
        }

        private static void Render(string archiveRoot, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            ProofData data = LoadProofData(archiveRoot);
            ProofSummary summary = BuildSummary(data);
            string summaryPath = Path.Combine(outputDirectory, "proof-summary.json");
            File.WriteAllBytes(summaryPath, JsonSerializer.SerializeToUtf8Bytes(summary, PrettyJson));
            string htmlPath = Path.Combine(outputDirectory, "telemetry-proof.html");
            File.WriteAllText(htmlPath, BuildHtml(data, summary), new UTF8Encoding(false));
            Console.WriteLine("PROOF_SUMMARY=" + summaryPath);
            Console.WriteLine("PROOF_HTML=" + htmlPath);
            Console.WriteLine("FINAL=" + (summary.AllRequiredProofsPass ? "PASS" : "FAIL"));
        }

        private static ProofData LoadProofData(string archiveRoot)
        {
            string[] paths = Directory.Exists(archiveRoot)
                ? Directory.EnumerateFiles(archiveRoot, "*.json.gz", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.Ordinal).ToArray()
                : throw new DirectoryNotFoundException(archiveRoot);
            if (paths.Length == 0) throw new InvalidDataException("No persisted telemetry chunks were found.");
            var result = new ProofData();
            var lastReplayElapsedByParticipant = new Dictionary<int, long>();
            long? lastDriverElapsed = null;
            foreach (string path in paths)
            {
                TelemetryChunkEnvelope chunk;
                using (FileStream stream = File.OpenRead(path))
                {
                    chunk = TelemetryChunkSerializer.Deserialize(TelemetryChunkSerializer.Gunzip(stream));
                }
                result.ChunkCount++;
                if (!result.StreamChunkCounts.ContainsKey(chunk.StreamType.ToString())) result.StreamChunkCounts[chunk.StreamType.ToString()] = 0;
                result.StreamChunkCounts[chunk.StreamType.ToString()]++;
                if (chunk.StreamType == TelemetryStreamType.SESSION_METADATA && chunk.Data.Records != null)
                {
                    result.Metadata.AddRange(chunk.Data.Records);
                    continue;
                }
                Dictionary<string, int> fields = chunk.Data.Fields.Select((name, index) => new { name, index }).ToDictionary(value => value.name, value => value.index, StringComparer.Ordinal);
                foreach (double?[] row in chunk.Data.Rows)
                {
                    switch (chunk.StreamType)
                    {
                        case TelemetryStreamType.RACE_STORY:
                            result.Story.Add(new StoryPoint(
                                Long(row, fields, "sessionElapsedMs"),
                                DictionaryValue(chunk, "eventTypes", Int(row, fields, "eventTypeRef")),
                                NullableInt(row, fields, "participantRef"),
                                NullableInt(row, fields, "lap"),
                                NullableInt(row, fields, "lapTimeMs")));
                            break;
                        case TelemetryStreamType.PARTICIPANT_REPLAY:
                            long replayElapsed = Long(row, fields, "sessionElapsedMs");
                            int replayParticipantRef = Int(row, fields, "participantRef");
                            if (!lastReplayElapsedByParticipant.TryGetValue(replayParticipantRef, out long lastReplayElapsed)
                                || replayElapsed - lastReplayElapsed >= 2_000)
                            {
                                result.Replay.Add(new ReplayPoint(
                                    replayElapsed,
                                    replayParticipantRef,
                                    Int(row, fields, "racePosition"),
                                    Number(row, fields, "worldX"),
                                    Number(row, fields, "worldZ"),
                                    Number(row, fields, "lapDistanceMeters")));
                                lastReplayElapsedByParticipant[replayParticipantRef] = replayElapsed;
                            }
                            break;
                        case TelemetryStreamType.DRIVER_TELEMETRY:
                            long driverElapsed = Long(row, fields, "sessionElapsedMs");
                            if (!lastDriverElapsed.HasValue || driverElapsed - lastDriverElapsed.Value >= 250)
                            {
                                result.Driver.Add(new DriverPoint(
                                    driverElapsed,
                                    Int(row, fields, "lap"),
                                    Number(row, fields, "lapDistanceMeters"),
                                    Number(row, fields, "worldX"),
                                    Number(row, fields, "worldZ"),
                                    Number(row, fields, "speedMetersPerSecond"),
                                    Number(row, fields, "throttle"),
                                    Number(row, fields, "brake"),
                                    Number(row, fields, "unfilteredSteering"),
                                    Number(row, fields, "longitudinalAccelerationMetersPerSecondSquared"),
                                    Number(row, fields, "lateralAccelerationMetersPerSecondSquared")));
                                lastDriverElapsed = driverElapsed;
                            }
                            break;
                        case TelemetryStreamType.INCIDENT_TRACE:
                            result.Incident.Add(new IncidentPoint(
                                Long(row, fields, "relativeTimeMs"),
                                Int(row, fields, "participantRef"),
                                Number(row, fields, "worldX"),
                                Number(row, fields, "worldZ")));
                            break;
                    }
                }
            }
            return result;
        }

        private static ProofSummary BuildSummary(ProofData data)
        {
            int positionDrivers = data.Replay.Select(value => value.ParticipantRef).Distinct().Count();
            int completedLaps = data.Story.Count(value => string.Equals(value.EventType, "LAP_COMPLETE", StringComparison.Ordinal));
            bool lapTable = completedLaps > 0 || data.Driver.Select(value => value.Lap).Distinct().Count() > 1;
            bool replay = data.Replay.Count > 10 && positionDrivers > 1;
            bool driver = data.Driver.Count > 10;
            bool incident = data.Incident.Count > 10 && data.Incident.Select(value => value.ParticipantRef).Distinct().Count() > 1;
            bool drivingLine = data.Driver.Select(value => value.Lap).Distinct().Count() > 1 && data.Driver.Any(value => value.WorldX != 0 || value.WorldZ != 0);
            return new ProofSummary
            {
                Schema = "ams2-offline-reprocessing-proof-v1",
                InputSource = "PERSISTED_GZIP_CHUNKS_ONLY",
                SharedMemoryRead = false,
                ChunkCount = data.ChunkCount,
                StreamChunkCounts = data.StreamChunkCounts,
                LapTimes = lapTable ? "PASS" : "FAIL",
                PositionChart = replay ? "PASS" : "FAIL",
                Replay2D = replay ? "PASS" : "FAIL",
                SpeedGraph = driver && data.Driver.Any(value => value.SpeedMetersPerSecond > 0) ? "PASS" : "FAIL",
                BrakeGraph = driver && data.Driver.Any(value => value.Brake > 0.1) ? "PASS" : "FAIL",
                ThrottleGraph = driver && data.Driver.Any(value => value.Throttle > 0.1) ? "PASS" : "FAIL",
                SteeringGraph = driver && data.Driver.Any(value => Math.Abs(value.Steering) > 0.1) ? "PASS" : "FAIL",
                GForceGraph = driver && data.Driver.Any(value => Math.Abs(value.LateralAcceleration) > 0.1) ? "PASS" : "FAIL",
                DrivingLine = drivingLine ? "PASS" : "FAIL",
                TrackCenterline = drivingLine ? "PASS" : "FAIL",
                IncidentAnimation = incident ? "PASS" : "FAIL"
            };
        }

        private static string BuildHtml(ProofData data, ProofSummary summary)
        {
            string replayJson = JsonSerializer.Serialize(data.Replay, Json);
            string driverJson = JsonSerializer.Serialize(data.Driver, Json);
            string incidentJson = JsonSerializer.Serialize(data.Incident, Json);
            string storyJson = JsonSerializer.Serialize(data.Story, Json);
            string summaryJson = JsonSerializer.Serialize(summary, Json);
            return "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">" +
                "<title>AMS2 Offline Telemetry Proof</title><style>body{margin:0;background:#071018;color:#d8e7ef;font:14px system-ui}main{max-width:1500px;margin:auto;padding:24px}h1{font-size:24px}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.card{background:#0d1c27;border:1px solid #274050;border-radius:10px;padding:14px}canvas{width:100%;height:330px;background:#061018;border-radius:6px}.ok{color:#49e69a}table{border-collapse:collapse;width:100%}td,th{padding:6px;border-bottom:1px solid #263b48;text-align:left}@media(max-width:900px){.grid{grid-template-columns:1fr}}</style></head><body><main>" +
                "<h1>AMS2 persisted-data offline proof</h1><p>Renderer input: completed <code>.json.gz</code> chunks only. No client process or SHM is read.</p>" +
                "<div class=\"card\"><pre id=\"summary\"></pre></div><div class=\"grid\">" +
                "<section class=\"card\"><h2>2D replay / track path</h2><canvas id=\"replay\"></canvas></section>" +
                "<section class=\"card\"><h2>Position chart</h2><canvas id=\"position\"></canvas></section>" +
                "<section class=\"card\"><h2>Speed</h2><canvas id=\"speed\"></canvas></section>" +
                "<section class=\"card\"><h2>Brake / throttle / steering</h2><canvas id=\"controls\"></canvas></section>" +
                "<section class=\"card\"><h2>Driving line</h2><canvas id=\"line\"></canvas></section>" +
                "<section class=\"card\"><h2>Incident candidate animation</h2><canvas id=\"incident\"></canvas></section>" +
                "</div><section class=\"card\"><h2>Lap/event table</h2><table id=\"events\"></table></section></main>" +
                "<script>const replay=" + replayJson + ",driver=" + driverJson + ",incident=" + incidentJson + ",story=" + storyJson + ",summary=" + summaryJson + ";" +
                "document.getElementById('summary').textContent=JSON.stringify(summary,null,2);" +
                "function setup(id){const c=document.getElementById(id),d=devicePixelRatio||1;c.width=c.clientWidth*d;c.height=c.clientHeight*d;const x=c.getContext('2d');x.scale(d,d);return{x,w:c.clientWidth,h:c.clientHeight}}" +
                "function bounds(a,xk,yk){let xs=a.map(v=>v[xk]),ys=a.map(v=>v[yk]);return[Math.min(...xs),Math.max(...xs),Math.min(...ys),Math.max(...ys)]}" +
                "function path(id,a,xk,yk,color,flip=false){if(!a.length)return;const {x,w,h}=setup(id),b=bounds(a,xk,yk),sx=v=>20+(v-b[0])/(b[1]-b[0]||1)*(w-40),sy=v=>flip?20+(v-b[2])/(b[3]-b[2]||1)*(h-40):h-20-(v-b[2])/(b[3]-b[2]||1)*(h-40);x.strokeStyle=color;x.lineWidth=1.5;x.beginPath();a.forEach((v,i)=>(i?x.lineTo(sx(v[xk]),sy(v[yk])):x.moveTo(sx(v[xk]),sy(v[yk]))));x.stroke()}" +
                "const colors=['#4ce0c1','#ff6b6b','#ffd166','#64b5ff','#b892ff','#ff9f5c','#63d3ff','#d6f27a'];const leader=replay.filter(v=>v.participantRef===1);" +
                "function positionChart(){const rows=replay.filter(v=>v.participantRef<=8);if(!rows.length)return;const s=setup('position'),b=bounds(rows,'elapsedMs','position'),sx=v=>20+(v-b[0])/(b[1]-b[0]||1)*(s.w-40),sy=v=>20+(v-b[2])/(b[3]-b[2]||1)*(s.h-40);[...new Set(rows.map(v=>v.participantRef))].forEach((ref,n)=>{const g=rows.filter(v=>v.participantRef===ref);s.x.strokeStyle=colors[n%colors.length];s.x.beginPath();g.forEach((v,i)=>i?s.x.lineTo(sx(v.elapsedMs),sy(v.position)):s.x.moveTo(sx(v.elapsedMs),sy(v.position)));s.x.stroke()})}positionChart();" +
                "function replayFrame(){if(!replay.length)return;const s=setup('replay'),b=bounds(leader,'worldX','worldZ'),sx=v=>20+(v-b[0])/(b[1]-b[0]||1)*(s.w-40),sy=v=>s.h-20-(v-b[2])/(b[3]-b[2]||1)*(s.h-40);s.x.strokeStyle='#315667';s.x.lineWidth=2;s.x.beginPath();leader.forEach((v,i)=>i?s.x.lineTo(sx(v.worldX),sy(v.worldZ)):s.x.moveTo(sx(v.worldX),sy(v.worldZ)));s.x.stroke();const times=[...new Set(replay.map(v=>v.elapsedMs))],t=times[Math.floor((Date.now()/120)%times.length)],cars=replay.filter(v=>v.elapsedMs===t);s.x.fillStyle='#9cb3c0';s.x.fillText((t/1000).toFixed(1)+' s',10,18);cars.forEach(v=>{s.x.fillStyle=colors[(v.participantRef-1)%colors.length];s.x.beginPath();s.x.arc(sx(v.worldX),sy(v.worldZ),v.participantRef===1?6:4,0,Math.PI*2);s.x.fill()});requestAnimationFrame(replayFrame)}replayFrame();" +
                "path('speed',driver,'elapsedMs','speedMetersPerSecond','#ffd166');path('line',driver.filter(v=>v.lap===Math.max(...driver.map(x=>x.lap))-1),'worldX','worldZ','#b892ff');" +
                "function multi(){const s=setup('controls'),series=[['throttle','#43e68b'],['brake','#ff5c6c'],['steering','#64b5ff']];series.forEach(([k,c])=>{s.x.strokeStyle=c;s.x.beginPath();driver.forEach((v,i)=>{const px=i/(driver.length-1||1)*s.w,py=s.h/2-v[k]*s.h*.42;i?s.x.lineTo(px,py):s.x.moveTo(px,py)});s.x.stroke()})}multi();" +
                "function incidentFrame(){if(!incident.length)return;const s=setup('incident'),b=bounds(incident,'worldX','worldZ'),times=[...new Set(incident.map(v=>v.relativeTimeMs))],t=times[Math.floor((Date.now()/80)%times.length)],rows=incident.filter(v=>v.relativeTimeMs===t);s.x.fillStyle='#9cb3c0';s.x.fillText((t/1000).toFixed(2)+' s',10,18);rows.forEach(v=>{const px=20+(v.worldX-b[0])/(b[1]-b[0]||1)*(s.w-40),py=s.h-20-(v.worldZ-b[2])/(b[3]-b[2]||1)*(s.h-40);s.x.fillStyle=colors[(v.participantRef-1)%colors.length];s.x.beginPath();s.x.arc(px,py,7,0,Math.PI*2);s.x.fill()});requestAnimationFrame(incidentFrame)}incidentFrame();" +
                "const rows=story.filter(v=>['LAP_COMPLETE','POSITION_CHANGE','INCIDENT_CANDIDATE','FINISH'].includes(v.eventType));document.getElementById('events').innerHTML='<tr><th>Elapsed</th><th>Event</th><th>Driver</th><th>Lap</th><th>Lap time</th></tr>'+rows.map(v=>`<tr><td>${(v.elapsedMs/1000).toFixed(1)}s</td><td>${v.eventType}</td><td>${v.participantRef??''}</td><td>${v.lap??''}</td><td>${v.lapTimeMs? (v.lapTimeMs/1000).toFixed(3):''}</td></tr>`).join('');" +
                "</script></body></html>";
        }

        private static long Long(double?[] row, Dictionary<string, int> fields, string name)
            => checked((long)Number(row, fields, name));
        private static int Int(double?[] row, Dictionary<string, int> fields, string name)
            => checked((int)Number(row, fields, name));
        private static int? NullableInt(double?[] row, Dictionary<string, int> fields, string name)
        {
            int index = fields[name];
            return row[index].HasValue ? checked((int)row[index]!.Value) : (int?)null;
        }
        private static double Number(double?[] row, Dictionary<string, int> fields, string name)
        {
            int index = fields[name];
            if (index >= row.Length || !row[index].HasValue) throw new InvalidDataException("Required proof field is null: " + name);
            return row[index]!.Value;
        }
        private static string DictionaryValue(TelemetryChunkEnvelope chunk, string name, int index)
        {
            if (!chunk.Data.Dictionaries.TryGetValue(name, out string[]? values) || index < 0 || index >= values.Length)
            {
                throw new InvalidDataException("Chunk dictionary reference is invalid: " + name);
            }
            return values[index];
        }

        private sealed class ProofData
        {
            public int ChunkCount { get; set; }
            public Dictionary<string, int> StreamChunkCounts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
            public List<SessionMetadataSample> Metadata { get; } = new List<SessionMetadataSample>();
            public List<StoryPoint> Story { get; } = new List<StoryPoint>();
            public List<ReplayPoint> Replay { get; } = new List<ReplayPoint>();
            public List<DriverPoint> Driver { get; } = new List<DriverPoint>();
            public List<IncidentPoint> Incident { get; } = new List<IncidentPoint>();
        }

        private sealed class ProofSummary
        {
            public string Schema { get; set; } = string.Empty;
            public string InputSource { get; set; } = string.Empty;
            public bool SharedMemoryRead { get; set; }
            public int ChunkCount { get; set; }
            public Dictionary<string, int> StreamChunkCounts { get; set; } = new Dictionary<string, int>();
            public string LapTimes { get; set; } = string.Empty;
            public string PositionChart { get; set; } = string.Empty;
            public string Replay2D { get; set; } = string.Empty;
            public string SpeedGraph { get; set; } = string.Empty;
            public string BrakeGraph { get; set; } = string.Empty;
            public string ThrottleGraph { get; set; } = string.Empty;
            public string SteeringGraph { get; set; } = string.Empty;
            public string GForceGraph { get; set; } = string.Empty;
            public string DrivingLine { get; set; } = string.Empty;
            public string TrackCenterline { get; set; } = string.Empty;
            public string IncidentAnimation { get; set; } = string.Empty;
            [JsonIgnore]
            public bool AllRequiredProofsPass => new[] { LapTimes, PositionChart, Replay2D, SpeedGraph, BrakeGraph, ThrottleGraph, DrivingLine, IncidentAnimation }.All(value => value == "PASS");
        }

        private sealed class StoryPoint
        {
            public StoryPoint(long elapsedMs, string eventType, int? participantRef, int? lap, int? lapTimeMs) { ElapsedMs = elapsedMs; EventType = eventType; ParticipantRef = participantRef; Lap = lap; LapTimeMs = lapTimeMs; }
            public long ElapsedMs { get; }
            public string EventType { get; }
            public int? ParticipantRef { get; }
            public int? Lap { get; }
            public int? LapTimeMs { get; }
        }
        private sealed class ReplayPoint
        {
            public ReplayPoint(long elapsedMs, int participantRef, int position, double worldX, double worldZ, double lapDistanceMeters) { ElapsedMs = elapsedMs; ParticipantRef = participantRef; Position = position; WorldX = worldX; WorldZ = worldZ; LapDistanceMeters = lapDistanceMeters; }
            public long ElapsedMs { get; }
            public int ParticipantRef { get; }
            public int Position { get; }
            public double WorldX { get; }
            public double WorldZ { get; }
            public double LapDistanceMeters { get; }
        }
        private sealed class DriverPoint
        {
            public DriverPoint(long elapsedMs, int lap, double lapDistanceMeters, double worldX, double worldZ, double speedMetersPerSecond, double throttle, double brake, double steering, double longitudinalAcceleration, double lateralAcceleration) { ElapsedMs = elapsedMs; Lap = lap; LapDistanceMeters = lapDistanceMeters; WorldX = worldX; WorldZ = worldZ; SpeedMetersPerSecond = speedMetersPerSecond; Throttle = throttle; Brake = brake; Steering = steering; LongitudinalAcceleration = longitudinalAcceleration; LateralAcceleration = lateralAcceleration; }
            public long ElapsedMs { get; }
            public int Lap { get; }
            public double LapDistanceMeters { get; }
            public double WorldX { get; }
            public double WorldZ { get; }
            public double SpeedMetersPerSecond { get; }
            public double Throttle { get; }
            public double Brake { get; }
            public double Steering { get; }
            public double LongitudinalAcceleration { get; }
            public double LateralAcceleration { get; }
        }
        private sealed class IncidentPoint
        {
            public IncidentPoint(long relativeTimeMs, int participantRef, double worldX, double worldZ) { RelativeTimeMs = relativeTimeMs; ParticipantRef = participantRef; WorldX = worldX; WorldZ = worldZ; }
            public long RelativeTimeMs { get; }
            public int ParticipantRef { get; }
            public double WorldX { get; }
            public double WorldZ { get; }
        }
    }
}
