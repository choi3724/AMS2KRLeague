using System;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public sealed class TelemetryArchiveOptions
    {
        public const int DefaultChunkDurationMs = 30_000;
        public const int DefaultReplayIntervalMs = 200;
        public const int DefaultDriverTelemetryIntervalMs = 50;
        public const int DefaultIncidentIntervalMs = 50;

        public int ChunkDurationMs { get; set; } = DefaultChunkDurationMs;
        public int ReplayIntervalMs { get; set; } = DefaultReplayIntervalMs;
        public int DriverTelemetryIntervalMs { get; set; } = DefaultDriverTelemetryIntervalMs;
        public int IncidentIntervalMs { get; set; } = DefaultIncidentIntervalMs;
        public int IncidentPreRollMs { get; set; } = 3_000;
        public int IncidentPostRollMs { get; set; } = 3_000;
        public int IncidentRingDurationMs { get; set; } = 10_000;
        public int InputChannelCapacity { get; set; } = 512;
        public int MaximumParticipantsPerFrame { get; set; } = 64;
        public int MaximumIncidentParticipants { get; set; } = 8;
        public int MaximumConcurrentIncidentBursts { get; set; } = 4;
        public int MaximumMetadataRecordsPerChunk { get; set; } = 64;
        public int MaximumMetadataFieldsPerRecord { get; set; } = 256;
        public int MaximumStoryEventsPerChunk { get; set; } = 4_096;
        public int MaximumTextLength { get; set; } = 512;

        public double ReplayRateHz => 1000.0 / ReplayIntervalMs;
        public double DriverTelemetryRateHz => 1000.0 / DriverTelemetryIntervalMs;
        public double IncidentRateHz => 1000.0 / IncidentIntervalMs;

        internal TelemetryArchiveOptions ValidatedCopy()
        {
            ValidateRange(ChunkDurationMs, 1_000, 300_000, nameof(ChunkDurationMs));
            ValidateRange(ReplayIntervalMs, 20, ChunkDurationMs, nameof(ReplayIntervalMs));
            ValidateRange(DriverTelemetryIntervalMs, 20, ChunkDurationMs, nameof(DriverTelemetryIntervalMs));
            ValidateRange(IncidentIntervalMs, 20, ChunkDurationMs, nameof(IncidentIntervalMs));
            ValidateRange(IncidentPreRollMs, 0, 30_000, nameof(IncidentPreRollMs));
            ValidateRange(IncidentPostRollMs, 0, 30_000, nameof(IncidentPostRollMs));
            ValidateRange(IncidentRingDurationMs, IncidentPreRollMs, 60_000, nameof(IncidentRingDurationMs));
            ValidateRange(InputChannelCapacity, 8, 65_536, nameof(InputChannelCapacity));
            ValidateRange(MaximumParticipantsPerFrame, 1, 128, nameof(MaximumParticipantsPerFrame));
            ValidateRange(MaximumIncidentParticipants, 1, MaximumParticipantsPerFrame, nameof(MaximumIncidentParticipants));
            ValidateRange(MaximumConcurrentIncidentBursts, 1, 32, nameof(MaximumConcurrentIncidentBursts));
            ValidateRange(MaximumMetadataRecordsPerChunk, 1, 4_096, nameof(MaximumMetadataRecordsPerChunk));
            ValidateRange(MaximumMetadataFieldsPerRecord, 1, 4_096, nameof(MaximumMetadataFieldsPerRecord));
            ValidateRange(MaximumStoryEventsPerChunk, 1, 65_536, nameof(MaximumStoryEventsPerChunk));
            ValidateRange(MaximumTextLength, 32, 4_096, nameof(MaximumTextLength));

            return new TelemetryArchiveOptions
            {
                ChunkDurationMs = ChunkDurationMs,
                ReplayIntervalMs = ReplayIntervalMs,
                DriverTelemetryIntervalMs = DriverTelemetryIntervalMs,
                IncidentIntervalMs = IncidentIntervalMs,
                IncidentPreRollMs = IncidentPreRollMs,
                IncidentPostRollMs = IncidentPostRollMs,
                IncidentRingDurationMs = IncidentRingDurationMs,
                InputChannelCapacity = InputChannelCapacity,
                MaximumParticipantsPerFrame = MaximumParticipantsPerFrame,
                MaximumIncidentParticipants = MaximumIncidentParticipants,
                MaximumConcurrentIncidentBursts = MaximumConcurrentIncidentBursts,
                MaximumMetadataRecordsPerChunk = MaximumMetadataRecordsPerChunk,
                MaximumMetadataFieldsPerRecord = MaximumMetadataFieldsPerRecord,
                MaximumStoryEventsPerChunk = MaximumStoryEventsPerChunk,
                MaximumTextLength = MaximumTextLength
            };
        }

        private static void ValidateRange(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
        }
    }
}
