using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public enum TelemetryStreamType
    {
        SESSION_METADATA,
        RACE_STORY,
        PARTICIPANT_REPLAY,
        DRIVER_TELEMETRY,
        INCIDENT_TRACE
    }

    public enum TelemetryVisibility
    {
        PUBLIC_REPLAY,
        PRIVATE_DRIVER_ANALYTICS
    }

    public enum TelemetryCapabilityState
    {
        CAPTURED,
        OBSERVED_ONLY,
        NOT_EXPOSED,
        NOT_SUPPORTED,
        UNKNOWN
    }

    public enum TelemetryUploadStatus
    {
        PENDING,
        LOCAL_PENDING_OWNER,
        SENT,
        FAILED_RETRYABLE,
        CONFLICT,
        QUARANTINED
    }

    /// <summary>
    /// An explicit authority boundary for private telemetry delivery. The
    /// shipping queue has no implementation of this authority and therefore
    /// denies private upload by default. Implementations must be backed by an
    /// independently verified owner binding; viewed participant, nickname and
    /// input activity are not ownership evidence.
    /// </summary>
    public interface IPrivateTelemetryUploadAuthority
    {
        bool IsUploadAuthorized(TelemetryPendingUploadMetadata metadata);
    }

    public sealed class TelemetryArchiveIdentity
    {
        public string SessionId { get; set; } = string.Empty;
        public string SessionFingerprint { get; set; } = string.Empty;
        public string WitnessId { get; set; } = string.Empty;
        public string AttemptId { get; set; } = string.Empty;
        public int AttemptNumber { get; set; }
        public string? ScheduledEventHint { get; set; }

        internal TelemetryArchiveIdentity ValidatedCopy()
        {
            RequireIdentity(SessionId, nameof(SessionId));
            RequireIdentity(SessionFingerprint, nameof(SessionFingerprint));
            RequireIdentity(WitnessId, nameof(WitnessId));
            RequireIdentity(AttemptId, nameof(AttemptId));
            if (AttemptNumber < 1) throw new ArgumentOutOfRangeException(nameof(AttemptNumber));
            if (ScheduledEventHint != null && ScheduledEventHint.Length > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(ScheduledEventHint));
            }

            return new TelemetryArchiveIdentity
            {
                SessionId = SessionId,
                SessionFingerprint = SessionFingerprint,
                WitnessId = WitnessId,
                AttemptId = AttemptId,
                AttemptNumber = AttemptNumber,
                ScheduledEventHint = ScheduledEventHint
            };
        }

        private static void RequireIdentity(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            {
                throw new ArgumentException("Telemetry archive identity is missing or too long.", name);
            }
        }
    }

    public static class TelemetryArchiveIdentityFactory
    {
        public static TelemetryArchiveIdentity StartSession(
            string sessionFingerprint,
            string? witnessId = null,
            int attemptNumber = 1)
        {
            if (attemptNumber < 1) throw new ArgumentOutOfRangeException(nameof(attemptNumber));
            string sharedWitnessId = string.IsNullOrWhiteSpace(witnessId)
                ? "witness-" + Guid.NewGuid().ToString("N")
                : witnessId;
            return new TelemetryArchiveIdentity
            {
                // sessionId is the common captureSessionId used by every stream.
                SessionId = "capture-" + Guid.NewGuid().ToString("N"),
                SessionFingerprint = sessionFingerprint,
                WitnessId = sharedWitnessId,
                AttemptId = "attempt-" + Guid.NewGuid().ToString("N"),
                AttemptNumber = attemptNumber
            }.ValidatedCopy();
        }

        public static TelemetryArchiveIdentity NextAttempt(TelemetryArchiveIdentity sessionIdentity)
        {
            TelemetryArchiveIdentity current =
                (sessionIdentity ?? throw new ArgumentNullException(nameof(sessionIdentity))).ValidatedCopy();
            return new TelemetryArchiveIdentity
            {
                SessionId = current.SessionId,
                SessionFingerprint = current.SessionFingerprint,
                WitnessId = current.WitnessId,
                AttemptId = "attempt-" + Guid.NewGuid().ToString("N"),
                AttemptNumber = checked(current.AttemptNumber + 1),
                ScheduledEventHint = current.ScheduledEventHint
            };
        }
    }

    public sealed class TelemetryCapabilityValue
    {
        public TelemetryCapabilityState State { get; set; } = TelemetryCapabilityState.UNKNOWN;
        public string? Unit { get; set; }
        public double? NumericValue { get; set; }
        public string? TextValue { get; set; }
        public bool? BooleanValue { get; set; }
        public int? RawEnumValue { get; set; }
    }

    public sealed class TelemetryParticipantDictionaryEntry
    {
        public int ParticipantRef { get; set; }
        public int Slot { get; set; }
        public int Generation { get; set; }
        public string? NameSnapshot { get; set; }
        public string? VehicleRef { get; set; }
        public string? VehicleClassRef { get; set; }
        public bool IsActive { get; set; }
        public int NationalityRaw { get; set; }
    }

    public sealed class SessionMetadataSample
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public long SessionElapsedMs { get; set; }
        public int GameBuild { get; set; }
        public uint SharedMemoryVersion { get; set; }
        public string ClientVersion { get; set; } = string.Empty;
        public string ParserVersion { get; set; } = string.Empty;
        public string? Track { get; set; }
        public string? Layout { get; set; }
        public string? RawTrack { get; set; }
        public string? RawLayout { get; set; }
        public string? TranslatedTrack { get; set; }
        public string? TranslatedLayout { get; set; }
        public double? TrackLengthMeters { get; set; }
        public string? SessionType { get; set; }
        public string ClockSource { get; set; } = "MONOTONIC_CAPTURE_CLOCK";
        public long? TimedSessionDurationMs { get; set; }
        public long? EventTimeRemainingMs { get; set; }
        public bool JoinedMidSession { get; set; }
        public long? SessionStartOffsetMs { get; set; }
        public TelemetryCapabilityState SessionStartOffsetStatus { get; set; } = TelemetryCapabilityState.UNKNOWN;
        public double? SessionDurationMinutes { get; set; }
        public int? ConfiguredLaps { get; set; }
        public int? ObservedParticipants { get; set; }
        public string? VehicleClass { get; set; }
        public string? SessionPrivacyRaw { get; set; }
        public bool CaptureStarted { get; set; }
        public bool CaptureEnded { get; set; }
        public string CaptureCompleteness { get; set; } = "UNKNOWN";
        public Dictionary<string, TelemetryCapabilityValue> Fields { get; set; } =
            new Dictionary<string, TelemetryCapabilityValue>(StringComparer.Ordinal);
        public List<TelemetryParticipantDictionaryEntry> Participants { get; set; } =
            new List<TelemetryParticipantDictionaryEntry>();
    }

    public sealed class RaceStoryEventSample
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string? FactCode { get; set; }
        public DateTimeOffset CapturedAtUtc { get; set; }
        public long SessionElapsedMs { get; set; }
        public int? ParticipantRef { get; set; }
        public int? Lap { get; set; }
        public int? Sector { get; set; }
        public double? LapDistanceMeters { get; set; }
        public double? WorldX { get; set; }
        public double? WorldY { get; set; }
        public double? WorldZ { get; set; }
        public int? PositionBefore { get; set; }
        public int? PositionAfter { get; set; }
        public int? LapTimeMs { get; set; }
        public int? RaceStateRaw { get; set; }
        public int? PitStateRaw { get; set; }
        public int? FlagColourRaw { get; set; }
        public int? FlagReasonRaw { get; set; }
        public int? PenaltyTypeRaw { get; set; }
        public int? ResultStateRaw { get; set; }
        public int? YellowFlagStateRaw { get; set; }
        public bool? ParticipantIsActiveRaw { get; set; }
    }

    public sealed class ReplayParticipantSample
    {
        public int ParticipantRef { get; set; }
        public int Slot { get; set; }
        public int Generation { get; set; }
        public string? NameSnapshot { get; set; }
        public string? VehicleRef { get; set; }
        public string? VehicleClassRef { get; set; }
        public int? Lap { get; set; }
        public double? LapDistanceMeters { get; set; }
        public int? RacePosition { get; set; }
        public double? WorldX { get; set; }
        public double? WorldY { get; set; }
        public double? WorldZ { get; set; }
        public int RaceStateRaw { get; set; }
        public int PitStateRaw { get; set; }
        public double? HeadingRadians { get; set; }
        public double? SpeedMetersPerSecond { get; set; }
        public int? LapsCompleted { get; set; }
        public int? SectorRaw { get; set; }
        public double? CurrentSector1TimeSeconds { get; set; }
        public double? CurrentSector2TimeSeconds { get; set; }
        public double? CurrentSector3TimeSeconds { get; set; }
        public bool LapInvalidated { get; set; }
        public double? OrientationRawX { get; set; }
        public double? OrientationRawY { get; set; }
        public double? OrientationRawZ { get; set; }
        public int NationalityRaw { get; set; }
        public int PitScheduleRaw { get; set; }
        public int HighestFlagColourRaw { get; set; }
        public int HighestFlagReasonRaw { get; set; }
        public double? BestLapTimeSeconds { get; set; }
        public double? LastLapTimeSeconds { get; set; }
        public double? FastestSector1TimeSeconds { get; set; }
        public double? FastestSector2TimeSeconds { get; set; }
        public double? FastestSector3TimeSeconds { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class DriverTelemetrySample
    {
        // Means the active viewed participant and root telemetry were reconciled.
        // It does not prove local-player ownership in spectator/remote-follow
        // states because SHM v14 has no authoritative owner signal.
        public bool LocalParticipantResolved { get; set; }
        public int? SourceParticipantRef { get; set; }
        public int DriverRef { get; set; }
        public int? Lap { get; set; }
        public int? Sector { get; set; }
        public double? LapDistanceMeters { get; set; }
        public double? WorldX { get; set; }
        public double? WorldY { get; set; }
        public double? WorldZ { get; set; }
        public double? SpeedMetersPerSecond { get; set; }
        public double? Rpm { get; set; }
        public int? GearRaw { get; set; }
        public double? Throttle { get; set; }
        public double? Brake { get; set; }
        public double? Steering { get; set; }
        public double? Clutch { get; set; }
        public double? UnfilteredThrottle { get; set; }
        public double? UnfilteredBrake { get; set; }
        public double? UnfilteredSteering { get; set; }
        public double? UnfilteredClutch { get; set; }
        public double? LongitudinalAccelerationMetersPerSecondSquared { get; set; }
        public double? LateralAccelerationMetersPerSecondSquared { get; set; }
        public double? VerticalAccelerationMetersPerSecondSquared { get; set; }
        public double? HeadingRadians { get; set; }
        public double? VelocityX { get; set; }
        public double? VelocityY { get; set; }
        public double? VelocityZ { get; set; }
        public double? FuelLevelRatio { get; set; }
        public double? FuelCapacityLiters { get; set; }
        public double? FuelLiters { get; set; }
        public double? BrakeBias { get; set; }
        public double? EngineDamage { get; set; }
        public double? AeroDamage { get; set; }
        public double? SuspensionDamage { get; set; }
        public double?[] TyreTemperaturesCelsius { get; set; } = new double?[4];
        public double?[] TyrePressuresKpa { get; set; } = new double?[4];
        public double?[] TyreWear { get; set; } = new double?[4];
        public double? TrackTemperatureCelsius { get; set; }
        public double? AmbientTemperatureCelsius { get; set; }
        public double? RainDensity { get; set; }
        public int? PitStateRaw { get; set; }
        public bool? LapValid { get; set; }
        public int? CurrentLapTimeMs { get; set; }
        public Dictionary<string, double?> AdditionalRawValues { get; set; } =
            new Dictionary<string, double?>(StringComparer.Ordinal);
        public Dictionary<string, string?> AdditionalTextValues { get; set; } =
            new Dictionary<string, string?>(StringComparer.Ordinal);
    }

    public sealed class IncidentCandidateSample
    {
        public string CandidateId { get; set; } = string.Empty;
        public string TriggerCode { get; set; } = string.Empty;
        public int[] RelatedParticipantRefs { get; set; } = Array.Empty<int>();
    }

    public sealed class TelemetryFrameSample
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public long SessionElapsedMs { get; set; }
        public IReadOnlyList<ReplayParticipantSample> Participants { get; set; } =
            Array.Empty<ReplayParticipantSample>();
        public DriverTelemetrySample? LocalDriver { get; set; }
        public int RaceStateRaw { get; set; }
        public int FlagColourRaw { get; set; }
        public int FlagReasonRaw { get; set; }
        public bool ParticipantDisappeared { get; set; }
        public int PositionChangeMagnitude { get; set; }
        public IncidentCandidateSample? IncidentCandidate { get; set; }
        public int YellowFlagStateRaw { get; set; }
        public int? ViewedParticipantRef { get; set; }
        public int? CollisionOpponentSlotRaw { get; set; }
        public int? CollisionOpponentRef { get; set; }
        public double? CollisionMagnitude { get; set; }
        public int? CrashStateRaw { get; set; }
    }
}
