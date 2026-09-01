using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Telemetry
{
    public sealed class ParticipantSnapshot
    {
        public ParticipantSnapshot(
            int index,
            bool isActive,
            string name,
            uint racePosition,
            uint lapsCompleted,
            uint currentLap,
            int currentSector,
            uint raceStateRaw,
            uint pitModeRaw,
            float bestLapTime,
            float lastLapTime,
            string vehicleName = "",
            string vehicleClass = "",
            float currentLapDistance = 0,
            bool lapInvalidated = false,
            float currentSector1Time = -1,
            float currentSector2Time = -1,
            float currentSector3Time = -1,
            float fastestSector1Time = -1,
            float fastestSector2Time = -1,
            float fastestSector3Time = -1,
            uint pitScheduleRaw = 0,
            uint highestFlagColourRaw = 0,
            uint highestFlagReasonRaw = 0)
        {
            Index = index;
            IsActive = isActive;
            Name = name ?? string.Empty;
            RacePosition = racePosition;
            LapsCompleted = lapsCompleted;
            CurrentLap = currentLap;
            CurrentSector = currentSector;
            RaceStateRaw = raceStateRaw;
            PitModeRaw = pitModeRaw;
            BestLapTime = bestLapTime;
            LastLapTime = lastLapTime;
            VehicleName = vehicleName ?? string.Empty;
            VehicleClass = vehicleClass ?? string.Empty;
            CurrentLapDistance = currentLapDistance;
            LapInvalidated = lapInvalidated;
            CurrentSector1Time = currentSector1Time;
            CurrentSector2Time = currentSector2Time;
            CurrentSector3Time = currentSector3Time;
            FastestSector1Time = fastestSector1Time;
            FastestSector2Time = fastestSector2Time;
            FastestSector3Time = fastestSector3Time;
            PitScheduleRaw = pitScheduleRaw;
            HighestFlagColourRaw = highestFlagColourRaw;
            HighestFlagReasonRaw = highestFlagReasonRaw;
        }

        public int Index { get; }
        public bool IsActive { get; }
        public string Name { get; }
        public uint RacePosition { get; }
        public uint LapsCompleted { get; }
        public uint CurrentLap { get; }
        public int CurrentSector { get; }
        public uint RaceStateRaw { get; }
        public uint PitModeRaw { get; }
        public float BestLapTime { get; }
        public float LastLapTime { get; }
        public string VehicleName { get; }
        public string VehicleClass { get; }
        public float CurrentLapDistance { get; }
        public bool LapInvalidated { get; }
        public float CurrentSector1Time { get; }
        public float CurrentSector2Time { get; }
        public float CurrentSector3Time { get; }
        public float FastestSector1Time { get; }
        public float FastestSector2Time { get; }
        public float FastestSector3Time { get; }
        public uint PitScheduleRaw { get; }
        public uint HighestFlagColourRaw { get; }
        public uint HighestFlagReasonRaw { get; }

        public RaceState? KnownRaceState => Enum.IsDefined(typeof(RaceState), RaceStateRaw)
            ? (RaceState?)RaceStateRaw
            : null;

        public PitMode? KnownPitMode => Enum.IsDefined(typeof(PitMode), PitModeRaw)
            ? (PitMode?)PitModeRaw
            : null;

        public PitSchedule? KnownPitSchedule => Enum.IsDefined(typeof(PitSchedule), PitScheduleRaw)
            ? (PitSchedule?)PitScheduleRaw
            : null;

        public FlagColour? KnownHighestFlagColour => Enum.IsDefined(typeof(FlagColour), HighestFlagColourRaw)
            ? (FlagColour?)HighestFlagColourRaw
            : null;

        public FlagReason? KnownHighestFlagReason => Enum.IsDefined(typeof(FlagReason), HighestFlagReasonRaw)
            ? (FlagReason?)HighestFlagReasonRaw
            : null;
    }

    public sealed class TelemetrySnapshot
    {
        private readonly ParticipantSnapshot[] _participants;

        public TelemetrySnapshot(
            DateTimeOffset capturedAt,
            uint version,
            uint buildVersion,
            uint sequenceNumber,
            uint gameStateRaw,
            uint sessionStateRaw,
            uint raceStateRaw,
            int viewedParticipantIndex,
            int numParticipants,
            uint lapsInEvent,
            float lastLapTime,
            float bestLapTime,
            float splitTimeAhead,
            float splitTimeBehind,
            ParticipantSnapshot[] participants,
            string trackLocation = "",
            string trackVariation = "",
            int numSectors = -1,
            bool lapInvalidated = false,
            float currentTime = 0,
            float currentSector1Time = -1,
            float currentSector2Time = -1,
            float currentSector3Time = -1,
            float fastestSector1Time = -1,
            float fastestSector2Time = -1,
            float fastestSector3Time = -1,
            float trackLength = 0,
            float eventTimeRemaining = -1,
            uint highestFlagColourRaw = 0,
            uint highestFlagReasonRaw = 0,
            uint rootPitModeRaw = 0,
            uint rootPitScheduleRaw = 0,
            float sessionDuration = 0,
            int sessionAdditionalLaps = 0,
            string rootCarName = "",
            string rootCarClassName = "",
            float personalFastestLapTime = -1,
            float worldFastestLapTime = -1,
            float personalFastestSector1Time = -1,
            float personalFastestSector2Time = -1,
            float personalFastestSector3Time = -1,
            float worldFastestSector1Time = -1,
            float worldFastestSector2Time = -1,
            float worldFastestSector3Time = -1,
            float ambientTemperature = 25,
            float trackTemperature = 30,
            float rainDensity = 0,
            float windSpeed = 2,
            float windDirectionX = 0,
            float windDirectionY = 0,
            float cloudBrightness = 0,
            float snowDensity = 0,
            int enforcedPitStopLap = -1,
            bool sessionIsPrivate = false)
        {
            CapturedAt = capturedAt;
            Version = version;
            BuildVersion = buildVersion;
            SequenceNumber = sequenceNumber;
            GameStateRaw = gameStateRaw;
            SessionStateRaw = sessionStateRaw;
            RaceStateRaw = raceStateRaw;
            ViewedParticipantIndex = viewedParticipantIndex;
            NumParticipants = numParticipants;
            LapsInEvent = lapsInEvent;
            LastLapTime = lastLapTime;
            BestLapTime = bestLapTime;
            SplitTimeAhead = splitTimeAhead;
            SplitTimeBehind = splitTimeBehind;
            TrackLocation = trackLocation ?? string.Empty;
            TrackVariation = trackVariation ?? string.Empty;
            NumSectors = numSectors;
            LapInvalidated = lapInvalidated;
            CurrentTime = currentTime;
            CurrentSector1Time = currentSector1Time;
            CurrentSector2Time = currentSector2Time;
            CurrentSector3Time = currentSector3Time;
            FastestSector1Time = fastestSector1Time;
            FastestSector2Time = fastestSector2Time;
            FastestSector3Time = fastestSector3Time;
            TrackLength = trackLength;
            EventTimeRemaining = eventTimeRemaining;
            HighestFlagColourRaw = highestFlagColourRaw;
            HighestFlagReasonRaw = highestFlagReasonRaw;
            RootPitModeRaw = rootPitModeRaw;
            RootPitScheduleRaw = rootPitScheduleRaw;
            SessionDuration = sessionDuration;
            SessionAdditionalLaps = sessionAdditionalLaps;
            RootCarName = rootCarName ?? string.Empty;
            RootCarClassName = rootCarClassName ?? string.Empty;
            PersonalFastestLapTime = personalFastestLapTime;
            WorldFastestLapTime = worldFastestLapTime;
            PersonalFastestSector1Time = personalFastestSector1Time;
            PersonalFastestSector2Time = personalFastestSector2Time;
            PersonalFastestSector3Time = personalFastestSector3Time;
            WorldFastestSector1Time = worldFastestSector1Time;
            WorldFastestSector2Time = worldFastestSector2Time;
            WorldFastestSector3Time = worldFastestSector3Time;
            AmbientTemperature = ambientTemperature;
            TrackTemperature = trackTemperature;
            RainDensity = rainDensity;
            WindSpeed = windSpeed;
            WindDirectionX = windDirectionX;
            WindDirectionY = windDirectionY;
            CloudBrightness = cloudBrightness;
            SnowDensity = snowDensity;
            EnforcedPitStopLap = enforcedPitStopLap;
            SessionIsPrivate = sessionIsPrivate;
            _participants = (ParticipantSnapshot[])(participants ?? throw new ArgumentNullException(nameof(participants))).Clone();
        }

        public DateTimeOffset CapturedAt { get; }
        public uint Version { get; }
        public uint BuildVersion { get; }
        public uint SequenceNumber { get; }
        public uint GameStateRaw { get; }
        public uint SessionStateRaw { get; }
        public uint RaceStateRaw { get; }
        public int ViewedParticipantIndex { get; }
        public int NumParticipants { get; }
        public uint LapsInEvent { get; }
        public float LastLapTime { get; }
        public float BestLapTime { get; }
        public float SplitTimeAhead { get; }
        public float SplitTimeBehind { get; }
        public string TrackLocation { get; }
        public string TrackVariation { get; }
        public int NumSectors { get; }
        public bool LapInvalidated { get; }
        public float CurrentTime { get; }
        public float CurrentSector1Time { get; }
        public float CurrentSector2Time { get; }
        public float CurrentSector3Time { get; }
        public float FastestSector1Time { get; }
        public float FastestSector2Time { get; }
        public float FastestSector3Time { get; }
        public float TrackLength { get; }
        public float EventTimeRemaining { get; }
        public uint HighestFlagColourRaw { get; }
        public uint HighestFlagReasonRaw { get; }
        public uint RootPitModeRaw { get; }
        public uint RootPitScheduleRaw { get; }
        public float SessionDuration { get; }
        public int SessionAdditionalLaps { get; }
        public string RootCarName { get; }
        public string RootCarClassName { get; }
        public float PersonalFastestLapTime { get; }
        public float WorldFastestLapTime { get; }
        public float PersonalFastestSector1Time { get; }
        public float PersonalFastestSector2Time { get; }
        public float PersonalFastestSector3Time { get; }
        public float WorldFastestSector1Time { get; }
        public float WorldFastestSector2Time { get; }
        public float WorldFastestSector3Time { get; }
        public float AmbientTemperature { get; }
        public float TrackTemperature { get; }
        public float RainDensity { get; }
        public float WindSpeed { get; }
        public float WindDirectionX { get; }
        public float WindDirectionY { get; }
        public float CloudBrightness { get; }
        public float SnowDensity { get; }
        public int EnforcedPitStopLap { get; }
        public bool SessionIsPrivate { get; }
        public IReadOnlyList<ParticipantSnapshot> Participants => _participants;

        public GameState? KnownGameState => Enum.IsDefined(typeof(GameState), GameStateRaw)
            ? (GameState?)GameStateRaw
            : null;

        public SessionState? KnownSessionState => Enum.IsDefined(typeof(SessionState), SessionStateRaw)
            ? (SessionState?)SessionStateRaw
            : null;

        public FlagColour? KnownHighestFlagColour => Enum.IsDefined(typeof(FlagColour), HighestFlagColourRaw)
            ? (FlagColour?)HighestFlagColourRaw
            : null;

        public FlagReason? KnownHighestFlagReason => Enum.IsDefined(typeof(FlagReason), HighestFlagReasonRaw)
            ? (FlagReason?)HighestFlagReasonRaw
            : null;

        public PitMode? KnownRootPitMode => Enum.IsDefined(typeof(PitMode), RootPitModeRaw)
            ? (PitMode?)RootPitModeRaw
            : null;

        public PitSchedule? KnownRootPitSchedule => Enum.IsDefined(typeof(PitSchedule), RootPitScheduleRaw)
            ? (PitSchedule?)RootPitScheduleRaw
            : null;
    }

    public sealed class TelemetryReadResult
    {
        private TelemetryReadResult(TelemetryReadStatus status, TelemetrySnapshot? snapshot, string message, int retries)
        {
            Status = status;
            Snapshot = snapshot;
            Message = message;
            SequenceRetries = retries;
        }

        public TelemetryReadStatus Status { get; }
        public TelemetrySnapshot? Snapshot { get; }
        public string Message { get; }
        public int SequenceRetries { get; }

        public static TelemetryReadResult Success(TelemetrySnapshot snapshot, int retries)
            => new TelemetryReadResult(TelemetryReadStatus.Success, snapshot, string.Empty, retries);

        public static TelemetryReadResult Failure(TelemetryReadStatus status, string message, int retries = 0)
            => new TelemetryReadResult(status, null, message, retries);
    }
}
