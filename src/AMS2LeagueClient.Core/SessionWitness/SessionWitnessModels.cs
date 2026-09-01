using System;
using System.Collections.Generic;
using AMS2LeagueClient.Core.HostRecording;

namespace AMS2LeagueClient.Core.SessionWitness
{
    public enum SessionWitnessSourceRole
    {
        Player,
        Host
    }

    public enum SessionWitnessCompleteness
    {
        FullSession,
        MidSession,
        EndOnly,
        Unknown
    }

    public sealed class SessionWitnessEvent
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public string Kind { get; set; } = string.Empty;
        public int? Slot { get; set; }
        public string NameSnapshot { get; set; } = string.Empty;
        public uint? Lap { get; set; }
        public uint? PreviousStateRaw { get; set; }
        public uint? StateRaw { get; set; }
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class SessionWitnessWeatherPoint
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
    }

    public sealed class SessionWitnessRecord
    {
        public string Schema { get; set; } = "ams2-session-witness-v1";
        public string WitnessId { get; set; } = string.Empty;
        public string SessionFingerprint { get; set; } = string.Empty;
        public string EventFingerprint { get; set; } = string.Empty;
        public string RosterSignature { get; set; } = string.Empty;
        public List<string> RosterNames { get; set; } = new List<string>();
        public string SourceClientId { get; set; } = string.Empty;
        public SessionWitnessSourceRole SourceRole { get; set; } = SessionWitnessSourceRole.Player;
        public DateTimeOffset CaptureStartedAtUtc { get; set; }
        public DateTimeOffset CaptureEndedAtUtc { get; set; }
        public DateTimeOffset? EstimatedSessionStartedAtUtc { get; set; }
        public SessionWitnessCompleteness CaptureCompleteness { get; set; } = SessionWitnessCompleteness.Unknown;
        public int QualityScore { get; set; }
        public string? ScheduledEventHint { get; set; }
        public string VehicleClass { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public HostSessionResult Session { get; set; } = new HostSessionResult();
        public List<SessionWitnessEvent> Events { get; set; } = new List<SessionWitnessEvent>();
        public List<SessionWitnessWeatherPoint> Weather { get; set; } = new List<SessionWitnessWeatherPoint>();
    }

    public sealed class SessionWitnessUpdate
    {
        public List<string> Events { get; set; } = new List<string>();
        public SessionWitnessRecord? FinalizedWitness { get; set; }
    }

    public enum SessionWitnessStoreDisposition
    {
        Stored,
        Duplicate,
        ConflictQuarantined
    }

    public sealed class SessionWitnessStoreOutcome
    {
        public SessionWitnessStoreDisposition Disposition { get; set; }
        public string WitnessPath { get; set; } = string.Empty;
        public string PayloadSha256 { get; set; } = string.Empty;
    }
}
