using System;
using System.Collections.Generic;
using System.Linq;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.RaceControl
{
    [Flags]
    public enum BroadcastOverlayState
    {
        NormalRacing = 0,
        Yellow = 1 << 0,
        DoubleYellow = 1 << 1,
        RedFlag = 1 << 2,
        BlueFlagPlayer = 1 << 3,
        FinalLap = 1 << 4,
        Chequered = 1 << 5,
        PlayerPit = 1 << 6,
        PlayerPenalty = 1 << 7,
        PlayerDsq = 1 << 8,
        SessionTransition = 1 << 9
    }

    public enum ParticipantPenaltyState
    {
        None,
        DriveThrough,
        StopGo,
        MandatoryPit,
        DamagePit,
        Disqualified,
        Retired,
        Dnf,
        Pit,
        Unknown
    }

    public enum RaceControlEventType
    {
        DriveThrough,
        StopGo,
        PenaltyCleared,
        Disqualified,
        Retired,
        Dnf,
        Green,
        Yellow,
        DoubleYellow,
        BluePlayer,
        Red,
        BlackAndWhite,
        BlackOrange,
        FinalLap,
        Chequered,
        TimeTenMinutes,
        TimeFiveMinutes,
        TimeOneMinute
    }

    public enum RaceControlPriority
    {
        Informational = 0,
        Timing = 1,
        DriverState = 2,
        Flag = 3,
        Penalty = 4,
        Disqualification = 5,
        SessionInterruption = 6
    }

    public enum EvidenceConfidence
    {
        ConfirmedLive,
        ConfirmedFixtureOnly,
        Conditional,
        NotSupported
    }

    public enum EvidenceKind
    {
        Live,
        Fixture
    }

    public sealed class ParticipantBroadcastState
    {
        public ParticipantBroadcastState(
            int participantIndex,
            int participantGeneration,
            uint leaguePosition,
            string name,
            ParticipantPenaltyState penaltyState,
            bool isPitActive,
            uint pitScheduleRaw,
            uint pitModeRaw,
            uint raceStateRaw,
            uint flagColourRaw,
            uint flagReasonRaw)
        {
            ParticipantIndex = participantIndex;
            ParticipantGeneration = participantGeneration;
            LeaguePosition = leaguePosition;
            Name = name ?? string.Empty;
            PenaltyState = penaltyState;
            IsPitActive = isPitActive;
            PitScheduleRaw = pitScheduleRaw;
            PitModeRaw = pitModeRaw;
            RaceStateRaw = raceStateRaw;
            FlagColourRaw = flagColourRaw;
            FlagReasonRaw = flagReasonRaw;
        }

        public int ParticipantIndex { get; }
        public int ParticipantGeneration { get; }
        public uint LeaguePosition { get; }
        public string Name { get; }
        public ParticipantPenaltyState PenaltyState { get; }
        public bool IsPitActive { get; }
        public uint PitScheduleRaw { get; }
        public uint PitModeRaw { get; }
        public uint RaceStateRaw { get; }
        public uint FlagColourRaw { get; }
        public uint FlagReasonRaw { get; }

        public string CompactCode
        {
            get
            {
                switch (PenaltyState)
                {
                    case ParticipantPenaltyState.DriveThrough: return "DT";
                    case ParticipantPenaltyState.StopGo: return "SG";
                    case ParticipantPenaltyState.MandatoryPit: return "MAND";
                    case ParticipantPenaltyState.DamagePit: return "DMG";
                    case ParticipantPenaltyState.Disqualified: return "DSQ";
                    case ParticipantPenaltyState.Retired: return "RET";
                    case ParticipantPenaltyState.Dnf: return "DNF";
                    case ParticipantPenaltyState.Pit: return "PIT";
                    case ParticipantPenaltyState.Unknown: return "?";
                    default: return IsPitActive ? "PIT" : string.Empty;
                }
            }
        }
    }

    public sealed class RaceControlEvent
    {
        public RaceControlEvent(
            RaceControlEventType type,
            RaceControlPriority priority,
            DateTimeOffset detectedAt,
            TimeSpan displayDuration,
            string title,
            string message,
            string source,
            string rawEnum,
            string derivedState,
            EvidenceKind evidenceKind,
            EvidenceConfidence confidence,
            int participantIndex = -1,
            int participantGeneration = 0,
            uint leaguePosition = 0,
            string driver = "")
        {
            Id = "RC:" + type.ToString().ToUpperInvariant() + ":" + Guid.NewGuid().ToString("N");
            Type = type;
            Priority = priority;
            DetectedAt = detectedAt;
            DisplayDuration = displayDuration;
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            Source = source ?? string.Empty;
            RawEnum = rawEnum ?? string.Empty;
            DerivedState = derivedState ?? string.Empty;
            EvidenceKind = evidenceKind;
            Confidence = confidence;
            ParticipantIndex = participantIndex;
            ParticipantGeneration = participantGeneration;
            LeaguePosition = leaguePosition;
            Driver = driver ?? string.Empty;
        }

        public string Id { get; }
        public RaceControlEventType Type { get; }
        public RaceControlPriority Priority { get; }
        public DateTimeOffset DetectedAt { get; }
        public TimeSpan DisplayDuration { get; }
        public string Title { get; }
        public string Message { get; }
        public string Source { get; }
        public string RawEnum { get; }
        public string DerivedState { get; }
        public EvidenceKind EvidenceKind { get; }
        public EvidenceConfidence Confidence { get; }
        public int ParticipantIndex { get; }
        public int ParticipantGeneration { get; }
        public uint LeaguePosition { get; }
        public string Driver { get; }
    }

    public sealed class RaceControlHistory
    {
        public const int MaximumEntries = 8;
        private readonly List<RaceControlEvent> _items = new List<RaceControlEvent>();

        public IReadOnlyList<RaceControlEvent> Items => _items;

        public void Add(RaceControlEvent item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _items.Insert(0, item);
            if (_items.Count > MaximumEntries)
            {
                _items.RemoveRange(MaximumEntries, _items.Count - MaximumEntries);
            }
        }

        public void Clear() => _items.Clear();
    }

    public sealed class RaceControlUpdate
    {
        public RaceControlUpdate(
            IReadOnlyList<RaceControlEvent> detectedEvents,
            RaceControlEvent? activeEvent,
            IReadOnlyList<RaceControlEvent> history,
            IReadOnlyDictionary<int, ParticipantBroadcastState> participantStates,
            BroadcastOverlayState overlayState,
            int version,
            bool stateReset)
        {
            DetectedEvents = detectedEvents;
            ActiveEvent = activeEvent;
            History = history;
            ParticipantStates = participantStates;
            OverlayState = overlayState;
            Version = version;
            StateReset = stateReset;
        }

        public IReadOnlyList<RaceControlEvent> DetectedEvents { get; }
        public RaceControlEvent? ActiveEvent { get; }
        public IReadOnlyList<RaceControlEvent> History { get; }
        public IReadOnlyDictionary<int, ParticipantBroadcastState> ParticipantStates { get; }
        public BroadcastOverlayState OverlayState { get; }
        public int Version { get; }
        public bool StateReset { get; }

        public bool IsSuppressed(Events.OverlayEventType type)
            => EventSuppressionPolicy.ShouldSuppress(OverlayState, type);
    }
}
