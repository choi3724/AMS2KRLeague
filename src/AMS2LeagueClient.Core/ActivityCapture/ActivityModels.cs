using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.ActivityCapture
{
    public enum ActivityType
    {
        Race,
        TimeAttack
    }

    public enum ActivityRecordScope
    {
        Unclassified,
        General,
        League
    }

    public enum ActivityAuthority
    {
        PlayerPersonal,
        HostRecorder
    }

    public enum ActivityCompletionStatus
    {
        Finished,
        Aborted,
        Incomplete
    }

    public enum CaptureCapabilityStatus
    {
        ConfirmedLive,
        ConfirmedFixtureOnly,
        ObservedOnly,
        NotExposed,
        NotSupported,
        Unknown
    }

    public sealed class ActivityIdentitySnapshot
    {
        public string? ServerUserId { get; set; }
        public string? LeagueDriverId { get; set; }
        public string? SteamId64 { get; set; }
        public string? ServerDisplayName { get; set; }
        public string ObservedAms2Name { get; set; } = string.Empty;
        public string IdentitySource { get; set; } = "OBSERVED_AMS2_NAME";
    }

    public sealed class ConfiguredSessionSettings
    {
        public bool? Enabled { get; set; }
        public CaptureCapabilityStatus EnabledStatus { get; set; } = CaptureCapabilityStatus.Unknown;
        public double? DurationMinutes { get; set; }
        public CaptureCapabilityStatus DurationStatus { get; set; } = CaptureCapabilityStatus.Unknown;
        public int? ConfiguredLaps { get; set; }
        public CaptureCapabilityStatus ConfiguredLapsStatus { get; set; } = CaptureCapabilityStatus.Unknown;
        public string? InGameDate { get; set; }
        public CaptureCapabilityStatus InGameDateStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
        public string? StartTime { get; set; }
        public CaptureCapabilityStatus StartTimeStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
        public List<string>? WeatherSlots { get; set; }
        public CaptureCapabilityStatus WeatherSlotsStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
        public double? WeatherProgression { get; set; }
        public CaptureCapabilityStatus WeatherProgressionStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
        public double? TimeProgression { get; set; }
        public CaptureCapabilityStatus TimeProgressionStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
        public int? MandatoryPitLap { get; set; }
        public CaptureCapabilityStatus MandatoryPitStatus { get; set; } = CaptureCapabilityStatus.Unknown;
        public bool? FormationLapEnabled { get; set; }
        public CaptureCapabilityStatus FormationLapStatus { get; set; } = CaptureCapabilityStatus.Unknown;
        public double? FuelUsageMultiplier { get; set; }
        public CaptureCapabilityStatus FuelUsageStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
        public double? TyreWearMultiplier { get; set; }
        public CaptureCapabilityStatus TyreWearStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
        public string? DamageSetting { get; set; }
        public CaptureCapabilityStatus DamageStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
        public string? AssistsAndRules { get; set; }
        public CaptureCapabilityStatus AssistsAndRulesStatus { get; set; } = CaptureCapabilityStatus.NotExposed;
    }

    public sealed class ObservedWeatherPoint
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public double SessionElapsedSeconds { get; set; }
        public float AmbientTemperatureCelsius { get; set; }
        public float TrackTemperatureCelsius { get; set; }
        public float RainDensity { get; set; }
        public float WindSpeed { get; set; }
        public float WindDirectionX { get; set; }
        public float WindDirectionY { get; set; }
        public float CloudBrightness { get; set; }
        public float SnowDensity { get; set; }
        public string Source { get; set; } = "SHARED_MEMORY_V14_OBSERVED";
    }

    public sealed class ObservedSessionConditions
    {
        public bool Observed { get; set; }
        public string SessionType { get; set; } = string.Empty;
        public DateTimeOffset? ActualStartTimestampUtc { get; set; }
        public DateTimeOffset? ActualEndTimestampUtc { get; set; }
        public bool? SessionIsPrivate { get; set; }
        public string RaceMode { get; set; } = "UNKNOWN";
        public List<ObservedWeatherPoint> WeatherTimeline { get; set; } = new List<ObservedWeatherPoint>();
        public CaptureCapabilityStatus WeatherStatus { get; set; } = CaptureCapabilityStatus.ObservedOnly;
    }

    public sealed class TimeAttackLapRecord
    {
        public string LapUid { get; set; } = string.Empty;
        public int LapOrdinal { get; set; }
        public DateTimeOffset CompletedAtUtc { get; set; }
        public int? LapTimeMilliseconds { get; set; }
        public int? Sector1Milliseconds { get; set; }
        public int? Sector2Milliseconds { get; set; }
        public int? Sector3Milliseconds { get; set; }
        public bool IsValid { get; set; }
        public string ValiditySource { get; set; } = "AMS2_LAP_INVALIDATED_LATCH";
        public string? InvalidReason { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }

    public sealed class PersonalRaceSummary
    {
        public uint? FinishPosition { get; set; }
        public int? FieldSize { get; set; }
        public uint CompletedLaps { get; set; }
        public int? BestLapMilliseconds { get; set; }
        public string ResultState { get; set; } = "UNKNOWN";
    }

    public sealed class ActivitySourceEvidence
    {
        public string Source { get; set; } = "GAME_PROVIDED_SHARED_MEMORY_V14";
        public string EvidenceSha256 { get; set; } = string.Empty;
        public uint SharedMemoryVersion { get; set; }
        public uint Ams2Build { get; set; }
        public uint SessionStateRaw { get; set; }
        public string CaptureVersion { get; set; } = "phase1d3-activity-v1";
    }

    public sealed class ActivityRecord
    {
        public string Schema { get; set; } = "ams2-league-activity-v1";
        public string ActivityId { get; set; } = string.Empty;
        public ActivityType ActivityType { get; set; }
        public ActivityRecordScope RecordScopeHint { get; set; } = ActivityRecordScope.Unclassified;
        public ActivityAuthority Authority { get; set; } = ActivityAuthority.PlayerPersonal;
        public ActivityCompletionStatus CompletionStatus { get; set; } = ActivityCompletionStatus.Incomplete;
        public string SessionFingerprint { get; set; } = string.Empty;
        public string? ScheduledEventHint { get; set; }
        public int AttemptNumber { get; set; } = 1;
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset EndedAtUtc { get; set; }
        public string SessionType { get; set; } = string.Empty;
        public string Track { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public string Vehicle { get; set; } = string.Empty;
        public string VehicleClass { get; set; } = string.Empty;
        public ActivityIdentitySnapshot Identity { get; set; } = new ActivityIdentitySnapshot();
        public ConfiguredSessionSettings ConfiguredSettings { get; set; } = new ConfiguredSessionSettings();
        public ObservedSessionConditions ObservedConditions { get; set; } = new ObservedSessionConditions();
        public PersonalRaceSummary? PersonalRaceSummary { get; set; }
        public TimeAttackLapRecord? TimeAttackLap { get; set; }
        public ActivitySourceEvidence Evidence { get; set; } = new ActivitySourceEvidence();
        public string ClientVersion { get; set; } = string.Empty;
    }

    public sealed class ActivityCaptureUpdate
    {
        public List<ActivityRecord> CompletedRecords { get; set; } = new List<ActivityRecord>();
        public List<string> Events { get; set; } = new List<string>();
    }

    public sealed class HostSessionActivityMetadata
    {
        public string ActivityId { get; set; } = string.Empty;
        public ActivityType ActivityType { get; set; } = ActivityType.Race;
        public ActivityRecordScope RecordScopeHint { get; set; } = ActivityRecordScope.Unclassified;
        public string SessionFingerprint { get; set; } = string.Empty;
        public string? CaptureChainId { get; set; }
        public string? ScheduledEventHint { get; set; }
        public int AttemptNumber { get; set; } = 1;
        public string AttemptStatus { get; set; } = "INCOMPLETE";
        public string RaceMode { get; set; } = "UNKNOWN";
        public Dictionary<string, ConfiguredSessionSettings> ConfiguredSettings { get; set; }
            = new Dictionary<string, ConfiguredSessionSettings>(StringComparer.Ordinal);
        public Dictionary<string, ObservedSessionConditions> ObservedConditions { get; set; }
            = new Dictionary<string, ObservedSessionConditions>(StringComparer.Ordinal);
    }
}
