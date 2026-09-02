using System;

namespace AMS2LeagueClient.Core.CompactTelemetry
{
    public static class CompactTelemetryProtocol
    {
        // UInt32 0x54433241 on a little-endian wire is the ASCII byte sequence "A2CT".
        public const uint MagicLittleEndian = 0x54433241U;
        public const byte Version = 1;
        public const byte HeaderSize = 88;
        public const ushort CommonFlags = 0x0003;
        public const ushort FixedCadenceFlag = 0x0004;
        public const ushort IrregularDeltaTimeFlag = 0x0008;
        public const ushort FixedCadenceFlags = CommonFlags | FixedCadenceFlag;
        public const ushort IrregularDeltaTimeFlags = CommonFlags | IrregularDeltaTimeFlag;
        public const int Sha256Length = 32;
        public const int HashOffset = 56;
        public const int MaximumSamplesPerBlock = 1_000_000;
        public const int MaximumParticipants = 4_096;
        public const int MaximumStringDictionaryEntries = 65_535;
        public const int MaximumBodyBytes = 64 * 1024 * 1024;
    }

    public enum CompactTelemetrySchemaId : ushort
    {
        SessionStaticV1 = 0x0001,
        SessionChangeV1 = 0x0002,
        RaceEventV1 = 0x0010,
        ParticipantReplayV1 = 0x0020,
        TrackGeometryV1 = 0x0021,
        DriverFastV1 = 0x0030,
        DriverMotionV1 = 0x0031,
        DriverSlowV1 = 0x0032,
        DriverChangeV1 = 0x0033,
        IncidentV1 = 0x0040,
        LossLedgerV1 = 0x0050,
        AttemptFinalizeV1 = 0x0051
    }

    /// <summary>
    /// Stable wire codes for LOSS_LEDGER_V1.lossSourceCode. These values are a
    /// protocol contract and must not be reordered after V1 is released.
    /// </summary>
    public enum CompactTelemetryLossSourceCode : byte
    {
        None = 0,
        ShmSourceGap = 1,
        OuterQueueDrop = 2,
        ArchiveInputDrop = 3,
        CadenceMissed = 4,
        SerializationFailure = 5,
        DiskWriteFailure = 6,
        WorkerException = 7,
        UploadFailure = 8,
        FinalizeFailure = 9,
        CommitConflict = 10
    }

    /// <summary>
    /// Stable wire codes for LOSS_LEDGER_V1.reasonCode. V1 uses this field to
    /// identify the affected capture stream; zero is reserved for the clean
    /// ledger marker.
    /// </summary>
    public enum CompactTelemetryLossReasonCode : ushort
    {
        None = 0,
        SessionMetadata = 1,
        RaceStory = 2,
        ParticipantReplay = 3,
        DriverTelemetry = 4,
        IncidentTrace = 5
    }

    /// <summary>
    /// Stable wire codes for ATTEMPT_FINALIZE_V1.completenessCode.
    /// </summary>
    public enum CompactTelemetryCompletenessCode : byte
    {
        InProgress = 0,
        Partial = 1,
        Complete = 2
    }

    public enum CompactFieldEncoding : byte
    {
        FixedUnsigned = 1,
        FixedSigned = 2,
        VarUInt = 3,
        ZigZag = 4,
        DeltaZigZag = 5,
        RleUnsigned = 6,
        RleZigZag = 7
    }

    public enum CompactStringDictionaryId : ushort
    {
        EventType = 1,
        EventId = 2,
        FactCode = 3,
        IncidentCandidate = 4,
        IncidentTriggerCode = 5,
        SessionText = 6,
        DriverText = 7
    }

    public sealed class CompactTelemetryFormatException : Exception
    {
        public CompactTelemetryFormatException(string message)
            : base(message)
        {
        }

        public CompactTelemetryFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
