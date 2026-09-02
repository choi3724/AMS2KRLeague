using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public sealed class TelemetryChunkQuality
    {
        public string ClockSource { get; set; } = "MONOTONIC_CAPTURE_CLOCK";
        public double TargetSampleRateHz { get; set; }
        public int ExpectedSampleCount { get; set; }
        public int ActualSampleCount { get; set; }
        public int MissingSamples { get; set; }
        public int DroppedSamples { get; set; }
        public int DroppedInputMessages { get; set; }
        public string CaptureCompleteness { get; set; } = "UNKNOWN";
        public int SourceWitnessCount { get; set; } = 1;
    }

    public sealed class TelemetryChunkData
    {
        public string[] Fields { get; set; } = Array.Empty<string>();
        public Dictionary<string, string[]> Dictionaries { get; set; } =
            new Dictionary<string, string[]>(StringComparer.Ordinal);
        public List<double?[]> Rows { get; set; } = new List<double?[]>();
        public List<SessionMetadataSample>? Records { get; set; }
    }

    public sealed class TelemetryChunkEnvelope
    {
        public string Schema { get; set; } = "ams2-telemetry-chunk-v1";
        public string ChunkId { get; set; } = string.Empty;
        public TelemetryStreamType StreamType { get; set; }
        public TelemetryVisibility Visibility { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string SessionFingerprint { get; set; } = string.Empty;
        public string WitnessId { get; set; } = string.Empty;
        public string AttemptId { get; set; } = string.Empty;
        public int AttemptNumber { get; set; }
        public string? ScheduledEventHint { get; set; }
        public int ChunkIndex { get; set; }
        public long StartElapsedMs { get; set; }
        public long EndElapsedMs { get; set; }
        public int? StartLap { get; set; }
        public int? EndLap { get; set; }
        public DateTimeOffset FirstCapturedAtUtc { get; set; }
        public DateTimeOffset LastCapturedAtUtc { get; set; }
        public TelemetryChunkQuality Quality { get; set; } = new TelemetryChunkQuality();
        public TelemetryChunkData Data { get; set; } = new TelemetryChunkData();
    }

    public sealed class TelemetryPendingUploadMetadata
    {
        public string Schema { get; set; } = "ams2-telemetry-upload-metadata-v1";
        public string Endpoint { get; set; } = "v1/telemetry/chunks";
        public string? Protocol { get; set; }
        public ushort? CompactSchemaId { get; set; }
        public uint? SessionLocalId { get; set; }
        public uint? AttemptLocalId { get; set; }
        public string ChunkId { get; set; } = string.Empty;
        public TelemetryStreamType StreamType { get; set; }
        public TelemetryVisibility Visibility { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string SessionFingerprint { get; set; } = string.Empty;
        public string WitnessId { get; set; } = string.Empty;
        public string AttemptId { get; set; } = string.Empty;
        public int AttemptNumber { get; set; }
        public int ChunkIndex { get; set; }
        public long StartElapsedMs { get; set; }
        public long EndElapsedMs { get; set; }
        public int? StartLap { get; set; }
        public int? EndLap { get; set; }
        public DateTimeOffset? FirstCapturedAtUtc { get; set; }
        public DateTimeOffset? LastCapturedAtUtc { get; set; }
        public string RelativeChunkPath { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/json";
        public string ContentEncoding { get; set; } = "gzip";
        public string PayloadSha256 { get; set; } = string.Empty;
        public string CompressedSha256 { get; set; } = string.Empty;
        public long UncompressedBytes { get; set; }
        public long CompressedBytes { get; set; }
        public TelemetryChunkQuality Quality { get; set; } = new TelemetryChunkQuality();
        public TelemetryUploadStatus Status { get; set; } = TelemetryUploadStatus.PENDING;
        public int AttemptCount { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public DateTimeOffset? LastAttemptAtUtc { get; set; }
        public DateTimeOffset? NextAttemptAtUtc { get; set; }
        public string? LastError { get; set; }
    }

    public enum TelemetryChunkCommitDisposition
    {
        STORED,
        DUPLICATE,
        CONFLICT_QUARANTINED
    }

    public sealed class TelemetryChunkCommitOutcome
    {
        public TelemetryChunkCommitDisposition Disposition { get; set; }
        public string ChunkPath { get; set; } = string.Empty;
        public string MetadataPath { get; set; } = string.Empty;
        public string PayloadSha256 { get; set; } = string.Empty;
        public string CompressedSha256 { get; set; } = string.Empty;
        public long UncompressedBytes { get; set; }
        public long CompressedBytes { get; set; }
    }

    public sealed class TelemetryArchiveRecoveryIssue
    {
        public string Path { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class TelemetryArchiveRecoveryReport
    {
        public int ValidChunks { get; set; }
        public int RebuiltPendingMetadata { get; set; }
        public int PreservedTemporaryFiles { get; set; }
        public List<TelemetryArchiveRecoveryIssue> Issues { get; set; } =
            new List<TelemetryArchiveRecoveryIssue>();
    }

    public sealed class TelemetryArchiveRuntimeCounters
    {
        public long AcceptedMessages { get; internal set; }
        public long DroppedMessages { get; internal set; }
        public long CommittedChunks { get; internal set; }
        public long CommitFailures { get; internal set; }
    }

    public enum TelemetryAttemptCompleteness
    {
        IN_PROGRESS,
        COMPLETE,
        PARTIAL
    }

    /// <summary>
    /// Durable, per-stream accounting for one capture attempt. Counts describe
    /// known loss at distinct boundaries; they are intentionally not collapsed
    /// into one generic dropped counter.
    /// </summary>
    public sealed class TelemetryStreamLossLedger
    {
        public TelemetryStreamType StreamType { get; set; }
        public long AcceptedWorkUnits { get; set; }
        public long OuterQueueLosses { get; set; }
        public long ArchiveInputLosses { get; set; }
        public long CadenceMissedSamples { get; set; }
        public long WorkerExceptions { get; set; }
        public long SerializationFailures { get; set; }
        public long DiskWriteFailures { get; set; }
        public long CommitConflicts { get; set; }
        public long FinalizeFailures { get; set; }
        public long UploadFailures { get; set; }
        public long DurableCommitAcks { get; set; }
        public bool DurableProcessingAcknowledged { get; set; }

        public long KnownLossCount => checked(
            OuterQueueLosses +
            ArchiveInputLosses +
            CadenceMissedSamples +
            WorkerExceptions +
            SerializationFailures +
            DiskWriteFailures +
            CommitConflicts +
            FinalizeFailures +
            UploadFailures);
    }

    public sealed class TelemetryAttemptLossLedger
    {
        public string Schema { get; set; } = "ams2-telemetry-attempt-loss-v1";
        public string SessionId { get; set; } = string.Empty;
        public string SessionFingerprint { get; set; } = string.Empty;
        public string WitnessId { get; set; } = string.Empty;
        public string AttemptId { get; set; } = string.Empty;
        public int AttemptNumber { get; set; }
        public bool CloseRequested { get; set; }
        public bool FinalizeAcknowledged { get; set; }
        public bool DurableAck { get; set; }
        public TelemetryAttemptCompleteness Completeness { get; set; } = TelemetryAttemptCompleteness.IN_PROGRESS;
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public List<TelemetryStreamLossLedger> Streams { get; set; } =
            new List<TelemetryStreamLossLedger>();

        public long KnownLossCount
        {
            get
            {
                long total = 0;
                foreach (TelemetryStreamLossLedger stream in Streams)
                {
                    total = checked(total + stream.KnownLossCount);
                }
                return total;
            }
        }
    }

    public sealed class TelemetryArchiveStreamCompletion
    {
        public TelemetryStreamType StreamType { get; set; }
        public long ArchiveInputLosses { get; set; }
        public long CadenceMissedSamples { get; set; }
        public long WorkerExceptions { get; set; }
        public long SerializationFailures { get; set; }
        public long DiskWriteFailures { get; set; }
        public long CommitConflicts { get; set; }
        public long FinalizeFailures { get; set; }
        public long DurableCommitAcks { get; set; }
    }

    public sealed class TelemetryArchiveCompletionReport
    {
        public bool FinalizeAcknowledged { get; set; }
        public List<TelemetryArchiveStreamCompletion> Streams { get; set; } =
            new List<TelemetryArchiveStreamCompletion>();
    }
}
