using System;
using System.Collections.Generic;
using AMS2LeagueClient.Core.ActivityCapture;

namespace AMS2LeagueClient.Core.HostRecording
{
    public enum HostRecorderPhase
    {
        Waiting,
        Practice,
        QualifyingActive,
        QualifyingFinalizing,
        RaceGridArmed,
        RaceGridCaptured,
        RaceActive,
        RaceFinishing,
        PostRaceStabilizing,
        ResultCaptured,
        SessionClosed
    }

    public enum HostResultReliability
    {
        Verified,
        Provisional,
        Quarantined
    }

    public enum HostIssueSeverity
    {
        Warning,
        Error
    }

    public sealed class HostRecorderIssue
    {
        public HostIssueSeverity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class HostParticipantEvidence
    {
        public int Slot { get; set; }
        public int Generation { get; set; }
        public bool Active { get; set; }
        public string NameSnapshot { get; set; } = string.Empty;
        public uint Position { get; set; }
        public uint LapsCompleted { get; set; }
        public uint CurrentLap { get; set; }
        public int CurrentSector { get; set; }
        public float LastLapSeconds { get; set; }
        public float BestLapSeconds { get; set; }
        public string ResultState { get; set; } = string.Empty;
        public uint ResultStateRaw { get; set; }
        public string PitState { get; set; } = string.Empty;
        public uint PitStateRaw { get; set; }
        public string Vehicle { get; set; } = string.Empty;
        public string VehicleClass { get; set; } = string.Empty;
        public DateTimeOffset FirstSeenUtc { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; }
        public bool Disappeared { get; set; }
    }

    public sealed class HostEvidenceSnapshot
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public uint SequenceNumber { get; set; }
        public uint GameStateRaw { get; set; }
        public uint SessionStateRaw { get; set; }
        public uint RaceStateRaw { get; set; }
        public float CurrentTimeSeconds { get; set; }
        public float EventTimeRemainingSeconds { get; set; }
        public string Track { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public float SessionDurationMinutes { get; set; }
        public uint ConfiguredLaps { get; set; }
        public int SessionAdditionalLaps { get; set; }
        public int EnforcedPitStopLap { get; set; }
        public bool SessionIsPrivate { get; set; }
        public float AmbientTemperatureCelsius { get; set; }
        public float TrackTemperatureCelsius { get; set; }
        public float RainDensity { get; set; }
        public float WindSpeed { get; set; }
        public float WindDirectionX { get; set; }
        public float WindDirectionY { get; set; }
        public float CloudBrightness { get; set; }
        public float SnowDensity { get; set; }
        public List<HostParticipantEvidence> Participants { get; set; } = new List<HostParticipantEvidence>();
    }

    public sealed class HostClassification
    {
        public string Schema { get; set; } = "ams2-league-host-classification-v1";
        public string SessionId { get; set; } = string.Empty;
        public string ParserVersion { get; set; } = "host-shm-v1";
        public uint Ams2Build { get; set; }
        public uint SharedMemoryVersion { get; set; }
        public string EvidenceSha256 { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Source { get; set; } = "GAME_PROVIDED_SHARED_MEMORY_SNAPSHOT";
        public DateTimeOffset CapturedAtUtc { get; set; }
        public bool Stable { get; set; }
        public int StableMilliseconds { get; set; }
        public List<HostParticipantEvidence> Participants { get; set; } = new List<HostParticipantEvidence>();
    }

    public sealed class HostRaceResult
    {
        public string Schema { get; set; } = "ams2-league-host-race-result-v1";
        public string SessionId { get; set; } = string.Empty;
        public string ParserVersion { get; set; } = "host-shm-v1";
        public uint Ams2Build { get; set; }
        public uint SharedMemoryVersion { get; set; }
        public string EvidenceSha256 { get; set; } = string.Empty;
        public string Source { get; set; } = "GAME_PROVIDED_SHARED_MEMORY_SNAPSHOT";
        public DateTimeOffset CapturedAtUtc { get; set; }
        public bool Stable { get; set; }
        public int StableMilliseconds { get; set; }
        public double? OfficialTotalRaceTimeSeconds { get; set; }
        public string OfficialTotalRaceTimeSource { get; set; } = "NOT_SUPPORTED";
        public double? OfficialFinalGapSeconds { get; set; }
        public string OfficialFinalGapSource { get; set; } = "NOT_SUPPORTED";
        public List<HostParticipantEvidence> Participants { get; set; } = new List<HostParticipantEvidence>();
    }

    public sealed class HostSessionResult
    {
        public string Schema { get; set; } = "ams2-league-host-session-v1";
        public string ParserVersion { get; set; } = "host-shm-v1";
        public string SessionId { get; set; } = string.Empty;
        public string HostInstallationId { get; set; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset EndedAtUtc { get; set; }
        public uint Ams2Build { get; set; }
        public uint SharedMemoryVersion { get; set; }
        public string Track { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public List<string> SessionTypesObserved { get; set; } = new List<string>();
        public HostResultReliability Reliability { get; set; }
        public HostClassification? Qualifying { get; set; }
        public HostClassification? StartingGrid { get; set; }
        public HostRaceResult? RaceResult { get; set; }
        public string EvidenceSha256 { get; set; } = string.Empty;
        public string ClosingReason { get; set; } = string.Empty;
        public string AttemptStatus { get; set; } = "INCOMPLETE";
        public HostSessionActivityMetadata? Activity { get; set; }
        public List<HostRecorderIssue> Issues { get; set; } = new List<HostRecorderIssue>();
        public List<HostEvidenceSnapshot> Evidence { get; set; } = new List<HostEvidenceSnapshot>();
    }

    public sealed class HostRecorderUpdate
    {
        public HostRecorderPhase Phase { get; set; }
        public List<string> Events { get; set; } = new List<string>();
        public HostSessionResult? FinalizedSession { get; set; }
    }

    public enum HostStoreDisposition
    {
        Stored,
        Duplicate,
        Quarantined,
        ConflictQuarantined
    }

    public sealed class HostStoreOutcome
    {
        public HostStoreDisposition Disposition { get; set; }
        public string SessionPath { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
