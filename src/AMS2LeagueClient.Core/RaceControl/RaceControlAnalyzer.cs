using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.RaceControl
{
    public sealed class RaceControlAnalyzer
    {
        private readonly EvidenceKind _evidenceKind;
        private readonly RaceControlHistory _history = new RaceControlHistory();
        private readonly Dictionary<int, ParticipantTracker> _participants = new Dictionary<int, ParticipantTracker>();
        private readonly HashSet<int> _timeMilestones = new HashSet<int>();
        private bool _initialized;
        private int _sessionGeneration = int.MinValue;
        private uint _sessionStateRaw;
        private DateTimeOffset _lastSnapshotAt;
        private uint _lastRootFlag;
        private float _lastRemaining = -1;
        private BroadcastOverlayState _lastOverlayState;
        private string _lastDriverStateKey = string.Empty;
        private int _version;

        public RaceControlAnalyzer(EvidenceKind evidenceKind = EvidenceKind.Live)
        {
            _evidenceKind = evidenceKind;
        }

        public RaceControlHistory History => _history;

        public RaceControlUpdate Observe(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            int sessionGeneration,
            DateTimeOffset now)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (league == null) throw new ArgumentNullException(nameof(league));

            bool reset = !_initialized
                || sessionGeneration != _sessionGeneration
                || snapshot.SessionStateRaw != _sessionStateRaw;
            if (reset)
            {
                Initialize(snapshot, league, sessionGeneration);
                return BuildUpdate(Array.Empty<RaceControlEvent>(), snapshot, league, now, true);
            }

            if (snapshot.CapturedAt <= _lastSnapshotAt)
            {
                return BuildUpdate(Array.Empty<RaceControlEvent>(), snapshot, league, now, false);
            }

            var detected = new List<RaceControlEvent>();
            ObserveParticipants(snapshot, league, detected, now);
            ObserveRootFlag(snapshot, detected, now);
            ObserveTimeMilestone(snapshot, detected, now);

            foreach (RaceControlEvent item in detected)
            {
                _history.Add(item);
            }

            _lastSnapshotAt = snapshot.CapturedAt;
            _lastRootFlag = snapshot.HighestFlagColourRaw;
            _lastRemaining = snapshot.EventTimeRemaining;
            return BuildUpdate(detected, snapshot, league, now, false);
        }

        public void Reset()
        {
            _initialized = false;
            _sessionGeneration = int.MinValue;
            _sessionStateRaw = 0;
            _lastSnapshotAt = default;
            _lastRootFlag = 0;
            _lastRemaining = -1;
            _lastOverlayState = BroadcastOverlayState.NormalRacing;
            _lastDriverStateKey = string.Empty;
            _participants.Clear();
            _timeMilestones.Clear();
            _history.Clear();
            _version++;
        }

        private void Initialize(TelemetrySnapshot snapshot, LeagueClassification league, int generation)
        {
            _initialized = true;
            _sessionGeneration = generation;
            _sessionStateRaw = snapshot.SessionStateRaw;
            _lastSnapshotAt = snapshot.CapturedAt;
            _lastRootFlag = snapshot.HighestFlagColourRaw;
            _lastRemaining = snapshot.EventTimeRemaining;
            _participants.Clear();
            _timeMilestones.Clear();
            _history.Clear();
            foreach (LeagueParticipant item in league.Participants)
            {
                _participants[item.Source.Index] = ParticipantTracker.Baseline(item.Source);
            }
            _lastDriverStateKey = string.Empty;
            _lastOverlayState = BroadcastOverlayState.SessionTransition;
            _version++;
        }

        private void ObserveParticipants(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            List<RaceControlEvent> detected,
            DateTimeOffset now)
        {
            var seen = new HashSet<int>();
            foreach (LeagueParticipant leagueParticipant in league.Participants)
            {
                ParticipantSnapshot participant = leagueParticipant.Source;
                seen.Add(participant.Index);
                string signature = IdentitySignature(participant);
                if (!_participants.TryGetValue(participant.Index, out ParticipantTracker? tracker))
                {
                    _participants[participant.Index] = ParticipantTracker.Baseline(participant);
                    continue;
                }

                if (!tracker.IsActive || !string.Equals(tracker.IdentitySignature, signature, StringComparison.Ordinal))
                {
                    tracker.ResetForIdentity(participant);
                    continue;
                }

                bool terminalEmitted = false;
                RaceState? raceState = participant.KnownRaceState;
                if (participant.RaceStateRaw != tracker.RaceStateRaw)
                {
                    if (raceState == RaceState.Disqualified)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.Disqualified, RaceControlPriority.Disqualification, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "실격", "mRaceStates[]", participant.RaceStateRaw, ParticipantPenaltyState.Disqualified));
                        terminalEmitted = true;
                    }
                    else if (raceState == RaceState.Retired)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.Retired, RaceControlPriority.DriverState, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "리타이어", "mRaceStates[]", participant.RaceStateRaw, ParticipantPenaltyState.Retired));
                        terminalEmitted = true;
                    }
                    else if (raceState == RaceState.Dnf)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.Dnf, RaceControlPriority.DriverState, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "DNF", "mRaceStates[]", participant.RaceStateRaw, ParticipantPenaltyState.Dnf));
                        terminalEmitted = true;
                    }
                }

                PitSchedule? schedule = participant.KnownPitSchedule;
                if (participant.PitScheduleRaw != tracker.PitScheduleRaw)
                {
                    if (schedule == PitSchedule.DriveThrough)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.DriveThrough, RaceControlPriority.Penalty, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "드라이브스루 페널티", "mPitSchedules[]", participant.PitScheduleRaw, ParticipantPenaltyState.DriveThrough));
                    }
                    else if (schedule == PitSchedule.StopGo)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.StopGo, RaceControlPriority.Penalty, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "스톱 앤 고 페널티", "mPitSchedules[]", participant.PitScheduleRaw, ParticipantPenaltyState.StopGo));
                    }
                    else if ((tracker.PitScheduleRaw == (uint)PitSchedule.DriveThrough || tracker.PitScheduleRaw == (uint)PitSchedule.StopGo)
                        && schedule == PitSchedule.None)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.PenaltyCleared, RaceControlPriority.Informational, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "페널티 상태 해제", "mPitSchedules[]", participant.PitScheduleRaw, ParticipantPenaltyState.None, EvidenceConfidence.Conditional));
                    }
                }

                FlagColour? flag = participant.KnownHighestFlagColour;
                if (participant.HighestFlagColourRaw != tracker.FlagColourRaw)
                {
                    if (participant.Index == snapshot.ViewedParticipantIndex && flag == FlagColour.Blue)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.BluePlayer, RaceControlPriority.Flag, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "청색기", "mHighestFlagColours[]", participant.HighestFlagColourRaw, DerivePenalty(participant)));
                    }
                    else if (flag == FlagColour.BlackAndWhite)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.BlackAndWhite, RaceControlPriority.Flag, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "경고", "mHighestFlagColours[]", participant.HighestFlagColourRaw, DerivePenalty(participant)));
                    }
                    else if (flag == FlagColour.BlackOrangeCircle)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.BlackOrange, RaceControlPriority.Flag, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "차량 이상 경고", "mHighestFlagColours[]", participant.HighestFlagColourRaw, DerivePenalty(participant)));
                    }
                    else if (flag == FlagColour.Black && !terminalEmitted)
                    {
                        detected.Add(ParticipantEvent(RaceControlEventType.Disqualified, RaceControlPriority.Disqualification, now, participant, tracker.Generation, leagueParticipant.LeaguePosition, "실격", "mHighestFlagColours[]", participant.HighestFlagColourRaw, ParticipantPenaltyState.Disqualified));
                    }
                }

                tracker.Update(participant);
            }

            foreach (KeyValuePair<int, ParticipantTracker> item in _participants)
            {
                if (!seen.Contains(item.Key)) item.Value.IsActive = false;
            }
        }

        private void ObserveRootFlag(TelemetrySnapshot snapshot, List<RaceControlEvent> detected, DateTimeOffset now)
        {
            if (snapshot.HighestFlagColourRaw == _lastRootFlag) return;
            FlagColour? current = snapshot.KnownHighestFlagColour;
            FlagColour? previous = Enum.IsDefined(typeof(FlagColour), _lastRootFlag) ? (FlagColour?)_lastRootFlag : null;
            RaceControlEvent? item = null;
            if (current == FlagColour.Green && (previous == FlagColour.Yellow || previous == FlagColour.DoubleYellow || previous == FlagColour.Red))
            {
                item = GlobalEvent(RaceControlEventType.Green, RaceControlPriority.Flag, now, "그린 플래그", "레이스 재개", snapshot, current.Value);
            }
            else if (current == FlagColour.Yellow)
            {
                item = GlobalEvent(RaceControlEventType.Yellow, RaceControlPriority.Flag, now, "! 황색기", "위험 구간", snapshot, current.Value);
            }
            else if (current == FlagColour.DoubleYellow)
            {
                item = GlobalEvent(RaceControlEventType.DoubleYellow, RaceControlPriority.Flag, now, "!! 이중 황색기", "위험 구간 · 강한 감속", snapshot, current.Value);
            }
            else if (current == FlagColour.Red)
            {
                item = GlobalEvent(RaceControlEventType.Red, RaceControlPriority.SessionInterruption, now, "적색기", "세션 중단", snapshot, current.Value);
            }
            else if (current == FlagColour.WhiteFinalLap)
            {
                item = GlobalEvent(RaceControlEventType.FinalLap, RaceControlPriority.DriverState, now, "마지막 랩", string.Empty, snapshot, current.Value);
            }
            else if (current == FlagColour.Chequered)
            {
                item = GlobalEvent(RaceControlEventType.Chequered, RaceControlPriority.DriverState, now, "체커드 플래그", string.Empty, snapshot, current.Value);
            }

            if (item != null) detected.Add(item);
        }

        private void ObserveTimeMilestone(TelemetrySnapshot snapshot, List<RaceControlEvent> detected, DateTimeOffset now)
        {
            if (snapshot.SessionDuration <= 0 || !IsFiniteNonNegative(snapshot.EventTimeRemaining) || !IsFiniteNonNegative(_lastRemaining)) return;
            int threshold = 0;
            if (_lastRemaining > 60 && snapshot.EventTimeRemaining <= 60 && !_timeMilestones.Contains(60)) threshold = 60;
            else if (_lastRemaining > 300 && snapshot.EventTimeRemaining <= 300 && !_timeMilestones.Contains(300)) threshold = 300;
            else if (_lastRemaining > 600 && snapshot.EventTimeRemaining <= 600 && !_timeMilestones.Contains(600)) threshold = 600;
            if (threshold == 0) return;
            _timeMilestones.Add(threshold);
            RaceControlEventType type = threshold == 60 ? RaceControlEventType.TimeOneMinute : threshold == 300 ? RaceControlEventType.TimeFiveMinutes : RaceControlEventType.TimeTenMinutes;
            string title = threshold == 60 ? "마지막 1분" : "레이스 종료까지 " + (threshold / 60).ToString(CultureInfo.InvariantCulture) + "분";
            detected.Add(new RaceControlEvent(type, RaceControlPriority.Timing, now, TimeSpan.FromSeconds(4), title, string.Empty, "mEventTimeRemaining", threshold.ToString(CultureInfo.InvariantCulture), "TIME_MILESTONE", _evidenceKind, Confidence()));
        }

        private RaceControlUpdate BuildUpdate(
            IReadOnlyList<RaceControlEvent> detected,
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            DateTimeOffset now,
            bool stateReset)
        {
            IReadOnlyDictionary<int, ParticipantBroadcastState> states = DeriveParticipantStates(league);
            BroadcastOverlayState overlayState = DeriveOverlayState(snapshot, league, states);
            string driverKey = string.Join(";", states.OrderBy(item => item.Key).Select(item => item.Key + ":" + item.Value.ParticipantGeneration + ":" + item.Value.CompactCode));
            if (detected.Count > 0 || overlayState != _lastOverlayState || driverKey != _lastDriverStateKey)
            {
                _version++;
                _lastOverlayState = overlayState;
                _lastDriverStateKey = driverKey;
            }

            RaceControlEvent? active = _history.Items
                .Where(item => now - item.DetectedAt < item.DisplayDuration)
                .OrderByDescending(item => item.Priority)
                .ThenByDescending(item => item.DetectedAt)
                .FirstOrDefault();
            return new RaceControlUpdate(detected, active, _history.Items.ToArray(), states, overlayState, _version, stateReset);
        }

        private IReadOnlyDictionary<int, ParticipantBroadcastState> DeriveParticipantStates(LeagueClassification league)
        {
            var result = new Dictionary<int, ParticipantBroadcastState>();
            foreach (LeagueParticipant item in league.Participants)
            {
                ParticipantSnapshot source = item.Source;
                int generation = _participants.TryGetValue(source.Index, out ParticipantTracker? tracker) ? tracker.Generation : 0;
                bool pit = IsPitActive(source.KnownPitMode);
                result[source.Index] = new ParticipantBroadcastState(
                    source.Index,
                    generation,
                    item.LeaguePosition,
                    source.Name,
                    DerivePenalty(source),
                    pit,
                    source.PitScheduleRaw,
                    source.PitModeRaw,
                    source.RaceStateRaw,
                    source.HighestFlagColourRaw,
                    source.HighestFlagReasonRaw);
            }
            return result;
        }

        private static BroadcastOverlayState DeriveOverlayState(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            IReadOnlyDictionary<int, ParticipantBroadcastState> states)
        {
            BroadcastOverlayState result = BroadcastOverlayState.NormalRacing;
            FlagColour? root = snapshot.KnownHighestFlagColour;
            if (root == FlagColour.Yellow) result |= BroadcastOverlayState.Yellow;
            if (root == FlagColour.DoubleYellow) result |= BroadcastOverlayState.DoubleYellow;
            if (root == FlagColour.Red) result |= BroadcastOverlayState.RedFlag;
            if (root == FlagColour.WhiteFinalLap) result |= BroadcastOverlayState.FinalLap;
            if (root == FlagColour.Chequered) result |= BroadcastOverlayState.Chequered;

            ParticipantSnapshot? local = league.Local?.Source;
            if (local == null) return result;
            if (local.KnownHighestFlagColour == FlagColour.Blue) result |= BroadcastOverlayState.BlueFlagPlayer;
            if (local.KnownHighestFlagColour == FlagColour.WhiteFinalLap) result |= BroadcastOverlayState.FinalLap;
            if (local.KnownHighestFlagColour == FlagColour.Chequered || local.KnownRaceState == RaceState.Finished) result |= BroadcastOverlayState.Chequered;
            if (IsPitActive(local.KnownPitMode)) result |= BroadcastOverlayState.PlayerPit;
            if (states.TryGetValue(local.Index, out ParticipantBroadcastState? localState))
            {
                if (localState.PenaltyState == ParticipantPenaltyState.DriveThrough || localState.PenaltyState == ParticipantPenaltyState.StopGo) result |= BroadcastOverlayState.PlayerPenalty;
                if (localState.PenaltyState == ParticipantPenaltyState.Disqualified) result |= BroadcastOverlayState.PlayerDsq;
            }
            return result;
        }

        private RaceControlEvent ParticipantEvent(
            RaceControlEventType type,
            RaceControlPriority priority,
            DateTimeOffset now,
            ParticipantSnapshot participant,
            int participantGeneration,
            uint position,
            string message,
            string source,
            uint raw,
            ParticipantPenaltyState derived,
            EvidenceConfidence? confidence = null)
        {
            return new RaceControlEvent(
                type,
                priority,
                now,
                TimeSpan.FromSeconds(priority >= RaceControlPriority.Disqualification ? 6 : 5),
                "레이스 컨트롤",
                message,
                source,
                raw.ToString(CultureInfo.InvariantCulture),
                derived.ToString(),
                _evidenceKind,
                confidence ?? Confidence(),
                participant.Index,
                participantGeneration,
                position,
                participant.Name);
        }

        private RaceControlEvent GlobalEvent(
            RaceControlEventType type,
            RaceControlPriority priority,
            DateTimeOffset now,
            string title,
            string message,
            TelemetrySnapshot snapshot,
            FlagColour colour)
        {
            return new RaceControlEvent(
                type,
                priority,
                now,
                TimeSpan.FromSeconds(priority == RaceControlPriority.SessionInterruption ? 8 : 5),
                title,
                message,
                "mHighestFlagColour",
                ((uint)colour).ToString(CultureInfo.InvariantCulture) + "/reason=" + snapshot.HighestFlagReasonRaw.ToString(CultureInfo.InvariantCulture),
                colour.ToString(),
                _evidenceKind,
                Confidence());
        }

        private EvidenceConfidence Confidence()
            => _evidenceKind == EvidenceKind.Live ? EvidenceConfidence.ConfirmedLive : EvidenceConfidence.ConfirmedFixtureOnly;

        private static ParticipantPenaltyState DerivePenalty(ParticipantSnapshot participant)
        {
            switch (participant.KnownRaceState)
            {
                case RaceState.Disqualified: return ParticipantPenaltyState.Disqualified;
                case RaceState.Retired: return ParticipantPenaltyState.Retired;
                case RaceState.Dnf: return ParticipantPenaltyState.Dnf;
            }

            switch (participant.KnownPitSchedule)
            {
                case PitSchedule.DriveThrough: return ParticipantPenaltyState.DriveThrough;
                case PitSchedule.StopGo: return ParticipantPenaltyState.StopGo;
                case PitSchedule.Mandatory: return ParticipantPenaltyState.MandatoryPit;
                case PitSchedule.DamageRequested: return ParticipantPenaltyState.DamagePit;
            }

            if (IsPitActive(participant.KnownPitMode)) return ParticipantPenaltyState.Pit;
            if (!participant.KnownPitSchedule.HasValue || !participant.KnownRaceState.HasValue || !participant.KnownPitMode.HasValue) return ParticipantPenaltyState.Unknown;
            return ParticipantPenaltyState.None;
        }

        private static bool IsPitActive(PitMode? mode)
            => mode == PitMode.DrivingIntoPits || mode == PitMode.InPit || mode == PitMode.DrivingOutOfPits;

        private static bool IsFiniteNonNegative(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;

        private static string IdentitySignature(ParticipantSnapshot participant)
            => participant.Name + "\u001f" + participant.VehicleName + "\u001f" + participant.VehicleClass;

        private sealed class ParticipantTracker
        {
            private ParticipantTracker() { }

            public string IdentitySignature { get; private set; } = string.Empty;
            public int Generation { get; private set; }
            public bool IsActive { get; set; }
            public uint PitScheduleRaw { get; private set; }
            public uint RaceStateRaw { get; private set; }
            public uint FlagColourRaw { get; private set; }

            public static ParticipantTracker Baseline(ParticipantSnapshot participant)
            {
                var result = new ParticipantTracker();
                result.ResetForIdentity(participant, false);
                return result;
            }

            public void ResetForIdentity(ParticipantSnapshot participant)
                => ResetForIdentity(participant, true);

            public void Update(ParticipantSnapshot participant)
            {
                IsActive = true;
                PitScheduleRaw = participant.PitScheduleRaw;
                RaceStateRaw = participant.RaceStateRaw;
                FlagColourRaw = participant.HighestFlagColourRaw;
            }

            private void ResetForIdentity(ParticipantSnapshot participant, bool increment)
            {
                if (increment) Generation++;
                IdentitySignature = RaceControlAnalyzer.IdentitySignature(participant);
                Update(participant);
            }
        }
    }
}
