using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2CompactProof
{
    internal sealed class ReferenceFixture
    {
        public const double SyntheticTrackLengthMeters = 5_793.0;

        public int DurationMs { get; private set; }
        public int ParticipantCount { get; private set; }
        public long BaselineRawBytes { get; private set; }
        public long BaselineGzipBytes { get; private set; }
        public List<CompactParticipantSource> Participants { get; } = new List<CompactParticipantSource>();
        public List<StorySourceSample> Story { get; } = new List<StorySourceSample>();
        public List<ReplaySourceSample> ReplayReference5Hz { get; } = new List<ReplaySourceSample>();
        public List<ReplaySourceSample> ReplayAdaptive { get; } = new List<ReplaySourceSample>();
        public List<DriverSourceSample> DriverFast20Hz { get; } = new List<DriverSourceSample>();
        public List<DriverSourceSample> DriverMotion5Hz { get; } = new List<DriverSourceSample>();
        public List<DriverSourceSample> DriverSlow1Hz { get; } = new List<DriverSourceSample>();
        public List<DriverSourceSample> DriverCatalogPoint2Hz { get; } = new List<DriverSourceSample>();
        public List<IncidentSourceSample> Incident20Hz { get; } = new List<IncidentSourceSample>();

        public static ReferenceFixture Load(string archiveRoot)
        {
            if (!Directory.Exists(archiveRoot)) throw new DirectoryNotFoundException(archiveRoot);
            var fixture = new ReferenceFixture();
            string[] paths = Directory.EnumerateFiles(archiveRoot, "*.json.gz", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (paths.Length == 0) throw new InvalidDataException("The P023 archive contains no .json.gz chunks.");

            foreach (string path in paths)
            {
                var info = new FileInfo(path);
                fixture.BaselineGzipBytes = checked(fixture.BaselineGzipBytes + info.Length);
                TelemetryChunkEnvelope chunk = Read(path, out int rawBytes);
                fixture.BaselineRawBytes = checked(fixture.BaselineRawBytes + rawBytes);
                switch (chunk.StreamType)
                {
                    case TelemetryStreamType.SESSION_METADATA:
                        fixture.ReadMetadata(chunk);
                        break;
                    case TelemetryStreamType.RACE_STORY:
                        fixture.ReadStory(chunk);
                        break;
                    case TelemetryStreamType.PARTICIPANT_REPLAY:
                        fixture.ReadReplay(chunk);
                        break;
                    case TelemetryStreamType.DRIVER_TELEMETRY:
                        fixture.ReadDriver(chunk);
                        break;
                    case TelemetryStreamType.INCIDENT_TRACE:
                        fixture.ReadIncident(chunk);
                        break;
                }
            }

            if (fixture.DurationMs <= 0)
            {
                fixture.DurationMs = checked((int)Math.Max(
                    fixture.DriverFast20Hz.LastOrDefault()?.ElapsedMs ?? 0,
                    fixture.ReplayReference5Hz.LastOrDefault()?.ElapsedMs ?? 0));
            }
            if (fixture.ParticipantCount <= 0)
            {
                fixture.ParticipantCount = fixture.ReplayReference5Hz.Select(row => row.ParticipantRef).Distinct().Count();
            }
            fixture.SelectAdaptiveReplay();
            fixture.Validate();
            return fixture;
        }

        private static TelemetryChunkEnvelope Read(string path, out int rawBytes)
        {
            byte[] raw;
            using (FileStream stream = File.OpenRead(path)) raw = TelemetryChunkSerializer.Gunzip(stream);
            rawBytes = raw.Length;
            return TelemetryChunkSerializer.Deserialize(raw);
        }

        private void ReadMetadata(TelemetryChunkEnvelope chunk)
        {
            SessionMetadataSample? metadata = chunk.Data.Records?.FirstOrDefault();
            if (metadata == null) return;
            if (metadata.TimedSessionDurationMs.HasValue)
            {
                DurationMs = checked((int)metadata.TimedSessionDurationMs.Value);
            }
            ParticipantCount = metadata.ObservedParticipants ?? ParticipantCount;
            foreach (TelemetryParticipantDictionaryEntry participant in metadata.Participants.OrderBy(value => value.ParticipantRef))
            {
                if (Participants.Any(value => value.ParticipantRef == participant.ParticipantRef)) continue;
                Participants.Add(new CompactParticipantSource(
                    checked((ushort)participant.ParticipantRef),
                    participant.NameSnapshot ?? "UNKNOWN-" + participant.ParticipantRef.ToString(CultureInfo.InvariantCulture),
                    participant.VehicleRef ?? "UNKNOWN",
                    participant.VehicleClassRef ?? "UNKNOWN"));
            }
        }

        private void ReadStory(TelemetryChunkEnvelope chunk)
        {
            Dictionary<string, int> fields = FieldMap(chunk.Data.Fields);
            foreach (double?[] row in chunk.Data.Rows)
            {
                long elapsed = Long(row, fields, "sessionElapsedMs");
                int eventRef = Int(row, fields, "eventTypeRef");
                string eventType = DictionaryValue(chunk, "eventTypes", eventRef);
                string eventId = DictionaryValue(chunk, "eventIds", Int(row, fields, "eventIdRef"));
                int? factRef = NullableInt(row, fields, "factCodeRef");
                string? factCode = factRef.HasValue ? DictionaryValue(chunk, "factCodes", factRef.Value) : null;
                Story.Add(new StorySourceSample(elapsed, eventType, eventId, factCode, row));
            }
        }

        private void ReadReplay(TelemetryChunkEnvelope chunk)
        {
            Dictionary<string, int> fields = FieldMap(chunk.Data.Fields);
            foreach (double?[] row in chunk.Data.Rows)
            {
                ReplayReference5Hz.Add(new ReplaySourceSample(
                    Long(row, fields, "sessionElapsedMs"),
                    Int(row, fields, "participantRef"),
                    Int(row, fields, "racePosition"),
                    Int(row, fields, "lap"),
                    Number(row, fields, "lapDistanceMeters"),
                    Number(row, fields, "worldX"),
                    Number(row, fields, "worldY"),
                    Number(row, fields, "worldZ"),
                    NullableInt(row, fields, "pitStateRaw"),
                    row));
            }
        }

        private void ReadDriver(TelemetryChunkEnvelope chunk)
        {
            Dictionary<string, int> fields = FieldMap(chunk.Data.Fields);
            foreach (double?[] row in chunk.Data.Rows)
            {
                long elapsed = Long(row, fields, "sessionElapsedMs");
                var sample = new DriverSourceSample(elapsed, row);
                DriverFast20Hz.Add(sample);
                if (elapsed % 200 == 0) DriverMotion5Hz.Add(sample);
                if (elapsed % 1_000 == 0) DriverSlow1Hz.Add(sample);
                if (elapsed % 10_000 == 0) DriverCatalogPoint2Hz.Add(sample);
            }
        }

        private void ReadIncident(TelemetryChunkEnvelope chunk)
        {
            Dictionary<string, int> fields = FieldMap(chunk.Data.Fields);
            foreach (double?[] row in chunk.Data.Rows)
            {
                Incident20Hz.Add(new IncidentSourceSample(
                    Long(row, fields, "sessionElapsedMs"),
                    Long(row, fields, "relativeTimeMs"),
                    Int(row, fields, "participantRef"),
                    DictionaryValue(chunk, "candidates", Int(row, fields, "candidateRef")),
                    DictionaryValue(chunk, "triggerCodes", Int(row, fields, "triggerCodeRef")),
                    row));
            }
        }

        private void SelectAdaptiveReplay()
        {
            var previousPosition = new Dictionary<int, int>();
            var previousPit = new Dictionary<int, int?>();
            int incidentAt = DurationMs / 2;
            foreach (IGrouping<long, ReplaySourceSample> group in ReplayReference5Hz.GroupBy(value => value.ElapsedMs))
            {
                ReplaySourceSample[] rows = group.OrderBy(value => value.ParticipantRef).ToArray();
                long elapsed = group.Key;
                bool baseSample = elapsed % 2_000 == 0;
                bool raceStartBurst = elapsed < 10_000;
                bool incidentBurst = Math.Abs(elapsed - incidentAt) <= 3_000;
                bool sessionEndBurst = elapsed >= DurationMs - 1_000;
                var transitionRefs = new HashSet<int>();
                foreach (ReplaySourceSample row in rows)
                {
                    if (previousPosition.TryGetValue(row.ParticipantRef, out int position) && position != row.Position)
                    {
                        transitionRefs.Add(row.ParticipantRef);
                    }
                    if (previousPit.TryGetValue(row.ParticipantRef, out int? pit) && pit != row.PitStateRaw)
                    {
                        transitionRefs.Add(row.ParticipantRef);
                    }
                    previousPosition[row.ParticipantRef] = row.Position;
                    previousPit[row.ParticipantRef] = row.PitStateRaw;
                }

                var closeBattleRefs = new HashSet<int>();
                if (elapsed % 500 == 0)
                {
                    for (int left = 0; left < rows.Length; left++)
                    {
                        for (int right = left + 1; right < rows.Length; right++)
                        {
                            double dx = rows[left].WorldX - rows[right].WorldX;
                            double dy = rows[left].WorldY - rows[right].WorldY;
                            double dz = rows[left].WorldZ - rows[right].WorldZ;
                            if (Math.Abs(rows[left].Position - rows[right].Position) == 1
                                && (dx * dx) + (dy * dy) + (dz * dz) <= 400.0)
                            {
                                closeBattleRefs.Add(rows[left].ParticipantRef);
                                closeBattleRefs.Add(rows[right].ParticipantRef);
                            }
                        }
                    }
                }

                bool groupBurst = baseSample || raceStartBurst || incidentBurst || sessionEndBurst;
                foreach (ReplaySourceSample row in rows)
                {
                    if (groupBurst || transitionRefs.Contains(row.ParticipantRef) || closeBattleRefs.Contains(row.ParticipantRef))
                    {
                        ReplayAdaptive.Add(row);
                    }
                }
            }
        }

        private void Validate()
        {
            if (DurationMs < 3_500_000 || DurationMs > 3_700_000)
            {
                throw new InvalidDataException("P024 acceptance requires the 60-minute semantic fixture.");
            }
            if (ParticipantCount != 32)
            {
                throw new InvalidDataException("P024 acceptance requires exactly 32 participants.");
            }
            if (Story.Count == 0 || ReplayReference5Hz.Count == 0 || DriverFast20Hz.Count == 0 || Incident20Hz.Count == 0)
            {
                throw new InvalidDataException("The P023 reference archive is missing a required semantic stream.");
            }
        }

        internal static Dictionary<string, int> FieldMap(IReadOnlyList<string> fields)
            => fields.Select((name, index) => new { name, index })
                .ToDictionary(value => value.name, value => value.index, StringComparer.Ordinal);

        internal static double? Optional(double?[] row, IReadOnlyDictionary<string, int> fields, string name)
            => fields.TryGetValue(name, out int index) && index < row.Length ? row[index] : null;

        internal static double Number(double?[] row, IReadOnlyDictionary<string, int> fields, string name)
        {
            double? value = Optional(row, fields, name);
            return value ?? throw new InvalidDataException("Required P023 reference field is null: " + name);
        }

        internal static long Long(double?[] row, IReadOnlyDictionary<string, int> fields, string name)
            => checked((long)Number(row, fields, name));

        internal static int Int(double?[] row, IReadOnlyDictionary<string, int> fields, string name)
            => checked((int)Number(row, fields, name));

        internal static int? NullableInt(double?[] row, IReadOnlyDictionary<string, int> fields, string name)
        {
            double? value = Optional(row, fields, name);
            return value.HasValue ? checked((int)value.Value) : (int?)null;
        }

        private static string DictionaryValue(TelemetryChunkEnvelope chunk, string name, int index)
        {
            if (!chunk.Data.Dictionaries.TryGetValue(name, out string[]? values) || index < 0 || index >= values.Length)
            {
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, "Invalid {0} dictionary reference {1}.", name, index));
            }
            return values[index];
        }
    }

    internal sealed class CompactParticipantSource
    {
        public CompactParticipantSource(ushort participantRef, string name, string vehicle, string vehicleClass)
        {
            ParticipantRef = participantRef;
            Name = name;
            Vehicle = vehicle;
            VehicleClass = vehicleClass;
        }
        public ushort ParticipantRef { get; }
        public string Name { get; }
        public string Vehicle { get; }
        public string VehicleClass { get; }
    }

    internal sealed class StorySourceSample
    {
        public StorySourceSample(long elapsedMs, string eventType, string eventId, string? factCode, double?[] source)
        {
            ElapsedMs = elapsedMs; EventType = eventType; EventId = eventId; FactCode = factCode; Source = source;
        }
        public long ElapsedMs { get; }
        public string EventType { get; }
        public string EventId { get; }
        public string? FactCode { get; }
        public double?[] Source { get; }
    }

    internal sealed class ReplaySourceSample
    {
        public ReplaySourceSample(long elapsedMs, int participantRef, int position, int lap, double lapDistanceMeters, double worldX, double worldY, double worldZ, int? pitStateRaw, double?[] source)
        {
            ElapsedMs = elapsedMs; ParticipantRef = participantRef; Position = position; Lap = lap;
            LapDistanceMeters = lapDistanceMeters; WorldX = worldX; WorldY = worldY; WorldZ = worldZ;
            PitStateRaw = pitStateRaw; Source = source;
        }
        public long ElapsedMs { get; }
        public int ParticipantRef { get; }
        public int Position { get; }
        public int Lap { get; }
        public double LapDistanceMeters { get; }
        public double WorldX { get; }
        public double WorldY { get; }
        public double WorldZ { get; }
        public int? PitStateRaw { get; }
        public double?[] Source { get; }
    }

    internal sealed class DriverSourceSample
    {
        public DriverSourceSample(long elapsedMs, double?[] source) { ElapsedMs = elapsedMs; Source = source; }
        public long ElapsedMs { get; }
        public double?[] Source { get; }
    }

    internal sealed class IncidentSourceSample
    {
        public IncidentSourceSample(
            long elapsedMs,
            long relativeTimeMs,
            int participantRef,
            string candidate,
            string triggerCode,
            double?[] source)
        {
            ElapsedMs = elapsedMs; RelativeTimeMs = relativeTimeMs; ParticipantRef = participantRef;
            Candidate = candidate; TriggerCode = triggerCode; Source = source;
        }
        public long ElapsedMs { get; }
        public long RelativeTimeMs { get; }
        public int ParticipantRef { get; }
        public string Candidate { get; }
        public string TriggerCode { get; }
        public double?[] Source { get; }
    }
}
