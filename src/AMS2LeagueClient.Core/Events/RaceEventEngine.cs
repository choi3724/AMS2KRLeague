using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.Localization;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Events
{
    public sealed class RaceEventEngine
    {
        public static readonly TimeSpan PositionStableDuration = TimeSpan.FromMilliseconds(750);
        public static readonly TimeSpan PositionAggregationWindow = TimeSpan.FromMilliseconds(1500);
        public static readonly TimeSpan BattleCooldown = TimeSpan.FromSeconds(25);
        public const float BattleThresholdSeconds = 1.0f;

        private readonly OverlayTextCatalog _text;
        private readonly OverlayEventQueue _queue = new OverlayEventQueue();
        private readonly Dictionary<string, DateTimeOffset> _cooldowns = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        private bool _initialized;
        private int _generation = int.MinValue;
        private DateTimeOffset _lastSnapshotAt;
        private uint _lastSessionState;
        private int _localIndex = -1;
        private uint _lastCurrentLap;
        private uint _lastLapsCompleted;
        private uint _stablePosition;
        private uint _positionCandidate;
        private DateTimeOffset _positionCandidateSince;
        private uint _aggregateOrigin;
        private uint _aggregateDestination;
        private DateTimeOffset _aggregateLastChanged;
        private bool _hasPositionAggregate;
        private float? _personalBest;
        private float? _raceFastest;
        private PitMode? _lastPitMode;
        private RaceState? _lastRaceState;
        private bool _lastLapInvalid;
        private bool _finalLapEmitted;
        private bool _openingStartEmitted;
        private int _stableLeaderIndex = -1;
        private string _stableLeaderName = string.Empty;
        private int _leaderCandidateIndex = -1;
        private string _leaderCandidateName = string.Empty;
        private DateTimeOffset _leaderCandidateSince;

        public RaceEventEngine(OverlayTextCatalog? text = null)
        {
            _text = text ?? OverlayTextCatalog.Korean;
        }

        public OverlayEventQueue Queue => _queue;

        public RaceEventUpdate Observe(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            int generation,
            DateTimeOffset now,
            BroadcastOverlayState overlayState = BroadcastOverlayState.NormalRacing)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (league == null) throw new ArgumentNullException(nameof(league));

            var detected = new List<OverlayEvent>();
            ParticipantSnapshot? local = league.Local?.Source;
            if (local == null)
            {
                Reset();
                return new RaceEventUpdate(detected, _queue.Tick(now), _queue.WaitingCount, true);
            }

            bool restart = _initialized
                && local.Index == _localIndex
                && (local.CurrentLap < _lastCurrentLap || local.LapsCompleted < _lastLapsCompleted);
            bool stateReset = !_initialized
                || generation != _generation
                || snapshot.SessionStateRaw != _lastSessionState
                || local.Index != _localIndex
                || restart;

            if (stateReset)
            {
                Initialize(snapshot, league, local, generation);
                return new RaceEventUpdate(detected, _queue.Tick(now), _queue.WaitingCount, true);
            }

            // A UI tick may see the same immutable snapshot more than once.  Event
            // stability is based only on newly accepted Shared Memory snapshots.
            if (snapshot.CapturedAt <= _lastSnapshotAt)
            {
                return new RaceEventUpdate(detected, _queue.Tick(now), _queue.WaitingCount, false);
            }

            DetectPit(snapshot, league, local, detected, now);
            DetectTerminal(snapshot, league, local, detected, now);
            DetectPosition(snapshot, league, local, detected, now, overlayState);
            DetectLeader(snapshot, league, local, detected, now);
            if (!EventSuppressionPolicy.ShouldSuppress(overlayState, OverlayEventType.PersonalBest)) DetectPersonalBest(local, detected, now);
            DetectRaceFastest(league, detected, now);
            DetectFinalLap(snapshot, league, local, detected, now);
            DetectInvalidLap(snapshot, local, detected, now);
            if (!EventSuppressionPolicy.ShouldSuppress(overlayState, OverlayEventType.Battle)) DetectBattle(snapshot, league, local, detected, now);

            _lastSnapshotAt = snapshot.CapturedAt;
            _lastCurrentLap = local.CurrentLap;
            _lastLapsCompleted = local.LapsCompleted;
            _lastSessionState = snapshot.SessionStateRaw;
            return new RaceEventUpdate(detected, _queue.Tick(now), _queue.WaitingCount, false);
        }

        public RaceEventUpdate Tick(DateTimeOffset now)
            => new RaceEventUpdate(Array.Empty<OverlayEvent>(), _queue.Tick(now), _queue.WaitingCount, false);

        public void Reset()
        {
            _queue.Clear();
            _cooldowns.Clear();
            _initialized = false;
            _generation = int.MinValue;
            _lastSnapshotAt = default;
            _localIndex = -1;
            _hasPositionAggregate = false;
            _personalBest = null;
            _raceFastest = null;
            _lastPitMode = null;
            _lastRaceState = null;
            _lastLapInvalid = false;
            _finalLapEmitted = false;
            _openingStartEmitted = false;
            _stableLeaderIndex = -1;
            _stableLeaderName = string.Empty;
            _leaderCandidateIndex = -1;
            _leaderCandidateName = string.Empty;
        }

        private void Initialize(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            ParticipantSnapshot local,
            int generation)
        {
            _queue.Clear();
            _cooldowns.Clear();
            _initialized = true;
            _generation = generation;
            _lastSnapshotAt = snapshot.CapturedAt;
            _lastSessionState = snapshot.SessionStateRaw;
            _localIndex = local.Index;
            _lastCurrentLap = local.CurrentLap;
            _lastLapsCompleted = local.LapsCompleted;
            _stablePosition = league.Local?.LeaguePosition ?? 0;
            _positionCandidate = _stablePosition;
            _positionCandidateSince = snapshot.CapturedAt;
            _hasPositionAggregate = false;
            _personalBest = Positive(local.BestLapTime);
            LeagueParticipant? fastest = league.FastestLapParticipant;
            _raceFastest = fastest == null ? null : Positive(fastest.Source.BestLapTime);
            _lastPitMode = local.KnownPitMode;
            _lastRaceState = local.KnownRaceState;
            _lastLapInvalid = local.LapInvalidated || snapshot.LapInvalidated;
            _finalLapEmitted = snapshot.LapsInEvent > 0 && local.CurrentLap >= snapshot.LapsInEvent;
            _openingStartEmitted = false;
            LeagueParticipant? leader = league.Participants.FirstOrDefault(item => item.LeaguePosition == 1);
            _stableLeaderIndex = leader?.Source.Index ?? -1;
            _stableLeaderName = leader?.Source.Name ?? string.Empty;
            _leaderCandidateIndex = _stableLeaderIndex;
            _leaderCandidateName = _stableLeaderName;
            _leaderCandidateSince = snapshot.CapturedAt;
            _cooldowns["BATTLE_INIT"] = snapshot.CapturedAt.AddSeconds(5);
        }

        private void DetectPosition(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            ParticipantSnapshot local,
            List<OverlayEvent> detected,
            DateTimeOffset now,
            BroadcastOverlayState overlayState)
        {
            uint position = league.Local?.LeaguePosition ?? 0;
            bool suppress = IsPitActive(local.KnownPitMode)
                || IsTerminal(local.KnownRaceState)
                || EventSuppressionPolicy.ShouldSuppress(overlayState, OverlayEventType.PositionGained);
            if (position == 0)
            {
                _hasPositionAggregate = false;
                return;
            }

            if (position != _stablePosition)
            {
                if (_positionCandidate != position)
                {
                    _positionCandidate = position;
                    _positionCandidateSince = snapshot.CapturedAt;
                }
                else if (snapshot.CapturedAt - _positionCandidateSince >= PositionStableDuration)
                {
                    uint previous = _stablePosition;
                    _stablePosition = position;
                    _positionCandidate = position;
                    if (suppress)
                    {
                        _hasPositionAggregate = false;
                    }
                    else if (!_hasPositionAggregate)
                    {
                        _aggregateOrigin = previous;
                        _aggregateDestination = position;
                        _aggregateLastChanged = snapshot.CapturedAt;
                        _hasPositionAggregate = true;
                    }
                    else
                    {
                        _aggregateDestination = position;
                        _aggregateLastChanged = snapshot.CapturedAt;
                    }
                }
            }
            else
            {
                _positionCandidate = position;
                _positionCandidateSince = snapshot.CapturedAt;
            }

            if (_hasPositionAggregate
                && (suppress || IsTerminal(local.KnownRaceState)))
            {
                _hasPositionAggregate = false;
            }
            else if (_hasPositionAggregate
                && snapshot.CapturedAt - _aggregateLastChanged >= PositionAggregationWindow)
            {
                EmitPosition(snapshot, local, _aggregateOrigin, _aggregateDestination, detected, now);
                _hasPositionAggregate = false;
            }
        }

        private void EmitPosition(TelemetrySnapshot snapshot, ParticipantSnapshot local, uint oldPosition, uint newPosition, List<OverlayEvent> detected, DateTimeOffset now)
        {
            if (oldPosition == 0 || newPosition == 0 || oldPosition == newPosition) return;
            bool gained = newPosition < oldPosition;
            uint change = gained ? oldPosition - newPosition : newPosition - oldPosition;
            bool openingStart = local.LapsCompleted == 0 && local.CurrentLap <= 1;
            if (openingStart && _openingStartEmitted) return;
            bool podiumEntry = oldPosition > 3 && newPosition <= 3;
            bool podiumExit = oldPosition <= 3 && newPosition > 3;
            OverlayEventType type = openingStart
                ? OverlayEventType.OpeningStart
                : podiumEntry
                    ? OverlayEventType.PodiumEntry
                    : podiumExit
                        ? OverlayEventType.PodiumExit
                        : gained ? OverlayEventType.PositionGained : OverlayEventType.PositionLost;
            string title = openingStart
                ? "좋은 스타트"
                : podiumEntry
                    ? "포디움 진입"
                    : podiumExit
                        ? "포디움 이탈"
                        : _text.Get(gained ? OverlayTextKey.PositionGained : OverlayTextKey.PositionLost);
            Emit(new OverlayEvent(
                type,
                podiumEntry || podiumExit ? OverlayEventPriority.Critical : OverlayEventPriority.High,
                now,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(12),
                title,
                "P" + oldPosition + " → P" + newPosition,
                (gained ? "▲ " : "▼ ") + change.ToString(CultureInfo.InvariantCulture),
                "LEAGUE_CLASSIFICATION",
                oldPosition: oldPosition,
                newPosition: newPosition), detected, now);
            if (openingStart) _openingStartEmitted = true;
        }

        private void DetectLeader(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            ParticipantSnapshot local,
            List<OverlayEvent> detected,
            DateTimeOffset now)
        {
            if (local.LapsCompleted == 0 && local.CurrentLapDistance < 100.0f) return;
            LeagueParticipant? leader = league.Participants.FirstOrDefault(item => item.LeaguePosition == 1);
            if (leader == null) return;
            if (leader.Source.Index == _stableLeaderIndex)
            {
                _leaderCandidateIndex = leader.Source.Index;
                _leaderCandidateName = leader.Source.Name;
                _leaderCandidateSince = snapshot.CapturedAt;
                return;
            }

            if (leader.Source.Index != _leaderCandidateIndex)
            {
                _leaderCandidateIndex = leader.Source.Index;
                _leaderCandidateName = leader.Source.Name;
                _leaderCandidateSince = snapshot.CapturedAt;
                return;
            }

            if (snapshot.CapturedAt - _leaderCandidateSince < PositionStableDuration) return;
            string previous = _stableLeaderName;
            _stableLeaderIndex = leader.Source.Index;
            _stableLeaderName = leader.Source.Name;
            string title = leader.Source.Index == local.Index ? "새로운 선두" : "선두 변경";
            string primary = leader.Source.Index == local.Index ? leader.Source.Name : previous + " → " + leader.Source.Name;
            Emit(new OverlayEvent(
                OverlayEventType.LeaderChange,
                OverlayEventPriority.Critical,
                now,
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(15),
                title,
                primary,
                "P1",
                "LEAGUE_CLASSIFICATION_STABLE",
                driver: leader.Source.Name,
                oldPosition: 1,
                newPosition: 1), detected, now);
        }

        private void DetectPersonalBest(ParticipantSnapshot local, List<OverlayEvent> detected, DateTimeOffset now)
        {
            float? current = Positive(local.BestLapTime);
            if (!current.HasValue) return;
            if (!_personalBest.HasValue)
            {
                _personalBest = current;
                return;
            }

            if (current.Value < _personalBest.Value - 0.0005f)
            {
                float previous = _personalBest.Value;
                _personalBest = current;
                float delta = current.Value - previous;
                Emit(new OverlayEvent(
                    OverlayEventType.PersonalBest,
                    OverlayEventPriority.Low,
                    now,
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(12),
                    _text.Get(OverlayTextKey.PersonalBest),
                    OverlayViewModel.FormatLapTime(current.Value),
                    delta.ToString("0.000", CultureInfo.InvariantCulture),
                    "LOCAL_FASTEST_LAP_ARRAY",
                    lapTime: current.Value,
                    delta: delta), detected, now);
            }
        }

        private void DetectRaceFastest(LeagueClassification league, List<OverlayEvent> detected, DateTimeOffset now)
        {
            LeagueParticipant? fastest = league.FastestLapParticipant;
            if (fastest == null) return;
            float current = fastest.Source.BestLapTime;
            if (!_raceFastest.HasValue)
            {
                _raceFastest = current;
                EmitRaceFastest(fastest, detected, now);
                return;
            }

            if (current < _raceFastest.Value - 0.0005f)
            {
                _raceFastest = current;
                EmitRaceFastest(fastest, detected, now);
            }
        }

        private void EmitRaceFastest(LeagueParticipant fastest, List<OverlayEvent> detected, DateTimeOffset now)
        {
            Emit(new OverlayEvent(
                OverlayEventType.RaceFastestLap,
                OverlayEventPriority.Normal,
                now,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(12),
                _text.Get(OverlayTextKey.RaceFastestLap),
                fastest.Source.Name,
                OverlayViewModel.FormatLapTime(fastest.Source.BestLapTime),
                "LEAGUE_FASTEST_LAP_ARRAY",
                driver: fastest.Source.Name,
                lapTime: fastest.Source.BestLapTime), detected, now);
        }

        private void DetectFinalLap(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            ParticipantSnapshot local,
            List<OverlayEvent> detected,
            DateTimeOffset now)
        {
            bool timed = snapshot.SessionDuration > 0;
            bool trustedTimedFinal = snapshot.KnownHighestFlagColour == FlagColour.WhiteFinalLap
                || local.KnownHighestFlagColour == FlagColour.WhiteFinalLap;
            bool trustedLapFinal = !timed && snapshot.LapsInEvent > 0 && local.CurrentLap >= snapshot.LapsInEvent;
            if (_finalLapEmitted || (!trustedTimedFinal && !trustedLapFinal)) return;
            _finalLapEmitted = true;
            uint position = league.Local?.LeaguePosition ?? 0;
            Emit(new OverlayEvent(
                OverlayEventType.FinalLap,
                OverlayEventPriority.Critical,
                now,
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(20),
                _text.Get(OverlayTextKey.FinalLap),
                _text.Get(OverlayTextKey.CurrentPosition) + " P" + position,
                string.Empty,
                trustedTimedFinal ? "WHITE_FINAL_LAP_FLAG" : "LAP_COUNT"), detected, now);
        }

        private void DetectPit(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            ParticipantSnapshot local,
            List<OverlayEvent> detected,
            DateTimeOffset now)
        {
            PitMode? current = local.KnownPitMode;
            PitMode? previous = _lastPitMode;
            _lastPitMode = current;
            if (!previous.HasValue || !current.HasValue || previous == current) return;

            bool entered = !IsPitActive(previous) && IsPitActive(current);
            bool exited = (IsPitActive(previous) && (current == PitMode.DrivingOutOfPits || current == PitMode.None))
                || (previous == PitMode.DrivingOutOfPits && current == PitMode.None);
            if (entered)
            {
                Emit(new OverlayEvent(
                    OverlayEventType.PitEntry,
                    OverlayEventPriority.Normal,
                    now,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10),
                    _text.Get(OverlayTextKey.PitEntry),
                    "LAP " + local.CurrentLap,
                    string.Empty,
                    "PIT_MODE_TRANSITION"), detected, now);
            }
            else if (exited)
            {
                uint position = league.Local?.LeaguePosition ?? 0;
                Emit(new OverlayEvent(
                    OverlayEventType.PitExit,
                    OverlayEventPriority.Normal,
                    now,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10),
                    _text.Get(OverlayTextKey.PitExit),
                    _text.Get(OverlayTextKey.CurrentPosition) + " P" + position,
                    string.Empty,
                    "PIT_MODE_TRANSITION"), detected, now);
            }
        }

        private void DetectTerminal(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            ParticipantSnapshot local,
            List<OverlayEvent> detected,
            DateTimeOffset now)
        {
            RaceState? current = local.KnownRaceState;
            RaceState? previous = _lastRaceState;
            _lastRaceState = current;
            if (!current.HasValue || current == previous || !IsTerminal(current)) return;

            uint position = league.Local?.LeaguePosition ?? 0;
            OverlayEventType type;
            string title;
            string primary;
            if (current == RaceState.Finished)
            {
                type = OverlayEventType.Finish;
                title = _text.Get(OverlayTextKey.Finish);
                primary = _text.Get(OverlayTextKey.FinalPosition) + " P" + position;
            }
            else if (current == RaceState.Disqualified)
            {
                type = OverlayEventType.Disqualified;
                title = _text.Get(OverlayTextKey.Disqualified);
                primary = string.Empty;
            }
            else
            {
                type = OverlayEventType.Retired;
                title = _text.Get(OverlayTextKey.Retired);
                primary = _text.Get(OverlayTextKey.CompletedLap) + " " + local.LapsCompleted
                    + (snapshot.LapsInEvent > 0 ? " / " + snapshot.LapsInEvent : string.Empty);
            }

            Emit(new OverlayEvent(
                type,
                OverlayEventPriority.Critical,
                now,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(30),
                title,
                primary,
                string.Empty,
                "LOCAL_RACE_STATE"), detected, now);
            _hasPositionAggregate = false;
        }

        private void DetectInvalidLap(
            TelemetrySnapshot snapshot,
            ParticipantSnapshot local,
            List<OverlayEvent> detected,
            DateTimeOffset now)
        {
            bool invalid = local.LapInvalidated || snapshot.LapInvalidated;
            if (invalid && !_lastLapInvalid)
            {
                Emit(new OverlayEvent(
                    OverlayEventType.InvalidLap,
                    OverlayEventPriority.Low,
                    now,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(8),
                    _text.Get(OverlayTextKey.InvalidLap),
                    "LAP " + local.CurrentLap,
                    string.Empty,
                    "LOCAL_LAP_INVALIDATED"), detected, now);
            }

            _lastLapInvalid = invalid;
        }

        private void DetectBattle(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            ParticipantSnapshot local,
            List<OverlayEvent> detected,
            DateTimeOffset now)
        {
            if (local.LapsCompleted == 0 && local.CurrentLapDistance < 100.0f) return;
            TrackProximity proximity = new TrackProximityResolver().Resolve(snapshot.TrackLength, local, league.Participants.Select(item => item.Source));
            ParticipantSnapshot? physicalAhead = proximity.Ahead;
            LeagueParticipant? ahead = physicalAhead == null ? null : league.Participants.FirstOrDefault(item => item.Source.Index == physicalAhead.Index);
            if (ahead == null || league.Ahead?.Source.Index != ahead.Source.Index || !league.CanUseAheadGameSplit) return;
            float gap = snapshot.SplitTimeAhead;
            if (float.IsNaN(gap) || float.IsInfinity(gap) || gap < 0 || gap > BattleThresholdSeconds) return;
            if (!proximity.AheadDistance.IsAvailable || proximity.AheadDistance.SignedMeters > 100) return;
            if (_cooldowns.TryGetValue("BATTLE_INIT", out DateTimeOffset init) && now < init) return;

            string key = "BATTLE:" + ahead.Source.Index.ToString(CultureInfo.InvariantCulture);
            if (_cooldowns.TryGetValue(key, out DateTimeOffset next) && now < next) return;
            _cooldowns[key] = now + BattleCooldown;
            Emit(new OverlayEvent(
                OverlayEventType.Battle,
                OverlayEventPriority.Low,
                now,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(10),
                _text.Get(OverlayTextKey.BattleAhead),
                "P" + ahead.LeaguePosition + " " + ahead.Source.Name,
                "+" + gap.ToString("0.000", CultureInfo.InvariantCulture) + " · " + proximity.AheadDistance.Text,
                "PHYSICAL_AHEAD_DIRECT_GAME_SPLIT_MATCHED",
                cooldownKey: key,
                driver: ahead.Source.Name), detected, now);
        }

        private void Emit(OverlayEvent item, List<OverlayEvent> detected, DateTimeOffset now)
        {
            detected.Add(item);
            _queue.Enqueue(item, now);
        }

        private static bool IsPitActive(PitMode? mode)
            => mode == PitMode.DrivingIntoPits || mode == PitMode.InPit;

        private static bool IsTerminal(RaceState? state)
            => state == RaceState.Finished
                || state == RaceState.Disqualified
                || state == RaceState.Retired
                || state == RaceState.Dnf;

        private static float? Positive(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0 ? (float?)value : null;
    }
}
