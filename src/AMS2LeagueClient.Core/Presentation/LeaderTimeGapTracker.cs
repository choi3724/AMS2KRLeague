using System;
using System.Collections.Generic;
using System.Globalization;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    // UI-only race timing. A follower's gap is the elapsed time since the leader
    // crossed the follower's current cumulative track position. This never changes
    // official lap times, results, or the game's shared-memory data.
    public sealed class LeaderTimeGapTracker
    {
        private const int MaxPointsPerCar = 32768;
        private readonly Dictionary<int, ProgressHistory> _histories = new Dictionary<int, ProgressHistory>();
        private TelemetrySnapshot? _previous;
        private int _generation = int.MinValue;
        private double _elapsed;

        public IReadOnlyDictionary<int, string> Observe(TelemetrySnapshot snapshot, LeagueClassification league, int generation)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (league == null) throw new ArgumentNullException(nameof(league));
            if (snapshot.KnownSessionState != SessionState.Race || !IsUsableGameState(snapshot.KnownGameState)
                || !float.IsFinite(snapshot.TrackLength) || snapshot.TrackLength <= 0)
            {
                Reset();
                return new Dictionary<int, string>();
            }

            TelemetrySnapshot? previous = _previous;
            if (previous != null && generation == _generation
                && previous.SequenceNumber == snapshot.SequenceNumber
                && previous.GameStateRaw == snapshot.GameStateRaw
                && previous.SessionStateRaw == snapshot.SessionStateRaw)
                return BuildGaps(snapshot, league);

            bool reset = previous == null || generation != _generation
                || previous.SessionStateRaw != snapshot.SessionStateRaw
                || previous.TrackLocation != snapshot.TrackLocation
                || previous.TrackVariation != snapshot.TrackVariation
                || previous.TrackLength != snapshot.TrackLength
                || previous.LapsInEvent != snapshot.LapsInEvent;
            double delta = 0;
            if (previous != null && !reset)
            {
                double wallDelta = (snapshot.CapturedAt - previous.CapturedAt).TotalSeconds;
                reset = wallDelta < 0 || wallDelta > 5;
                if (!reset && IsRunning(snapshot.KnownGameState))
                {
                    delta = IsRunning(previous.KnownGameState) ? wallDelta : 0;
                    if (snapshot.SessionDuration > 0 && previous.SessionDuration > 0
                        && float.IsFinite(snapshot.EventTimeRemaining) && snapshot.EventTimeRemaining >= 0
                        && float.IsFinite(previous.EventTimeRemaining) && previous.EventTimeRemaining >= 0
                        && (snapshot.EventTimeRemaining > 0 || previous.EventTimeRemaining > 0))
                    {
                        double gameDelta = previous.EventTimeRemaining - snapshot.EventTimeRemaining;
                        reset = gameDelta < -0.02 || gameDelta > 5;
                        delta = Math.Max(0, gameDelta);
                    }
                    else if (!IsRunning(previous.KnownGameState))
                    {
                        // An untimed race has no game clock to separate paused time from driving time.
                        reset = true;
                    }
                }
            }
            if (reset) { _histories.Clear(); _elapsed = 0; delta = 0; }
            _elapsed += delta;
            _previous = snapshot;
            _generation = generation;

            var present = new HashSet<int>();
            foreach (LeagueParticipant item in league.Participants)
            {
                ParticipantSnapshot driver = item.Source;
                present.Add(driver.Index);
                if (!driver.IsActive || !TryProgress(driver, snapshot.TrackLength, out double progress)
                    || driver.KnownPitMode == PitMode.InGarage || driver.KnownPitMode == PitMode.DrivingOutOfGarage)
                {
                    _histories.Remove(driver.Index);
                    continue;
                }
                string identity = driver.Name + "\u001f" + driver.VehicleName + "\u001f" + driver.VehicleClass;
                if (!_histories.TryGetValue(driver.Index, out ProgressHistory? history)
                    || history.Identity != identity
                    || progress < history.LastProgress - 1
                    || progress - history.LastProgress > delta * 200 + 10)
                {
                    history = new ProgressHistory(identity);
                    _histories[driver.Index] = history;
                }
                history.Observe(progress, _elapsed, snapshot.TrackLength);
            }
            foreach (int index in new List<int>(_histories.Keys))
                if (!present.Contains(index)) _histories.Remove(index);
            return BuildGaps(snapshot, league);
        }

        public void Reset()
        {
            _histories.Clear();
            _previous = null;
            _generation = int.MinValue;
            _elapsed = 0;
        }

        private IReadOnlyDictionary<int, string> BuildGaps(TelemetrySnapshot snapshot, LeagueClassification league)
        {
            var gaps = new Dictionary<int, string>();
            if (league.Participants.Count == 0) return gaps;
            ParticipantSnapshot leader = league.Participants[0].Source;
            gaps[leader.Index] = "0.000";
            _histories.TryGetValue(leader.Index, out ProgressHistory? leaderHistory);
            bool leaderValid = TryProgress(leader, snapshot.TrackLength, out double leaderProgress)
                && leaderHistory != null;
            foreach (LeagueParticipant item in league.Participants)
            {
                ParticipantSnapshot driver = item.Source;
                if (driver.Index == leader.Index || driver.KnownRaceState != RaceState.Racing) continue;
                string text = "—";
                if (leaderValid && TryProgress(driver, snapshot.TrackLength, out double progress))
                {
                    double distanceBehind = leaderProgress - progress;
                    if (distanceBehind >= snapshot.TrackLength)
                    {
                        int laps = Math.Max(1, (int)Math.Floor(distanceBehind / snapshot.TrackLength));
                        text = "+" + laps.ToString(CultureInfo.InvariantCulture) + "랩";
                    }
                    else if (distanceBehind >= -1 && leaderHistory!.TryCrossingTime(progress, out double crossingTime))
                    {
                        double seconds = _elapsed - crossingTime;
                        if (seconds >= 0) text = FormatGap(seconds);
                    }
                }
                gaps[driver.Index] = text;
            }
            return gaps;
        }

        private static bool TryProgress(ParticipantSnapshot driver, float length, out double progress)
        {
            progress = 0;
            if (driver.LapsCompleted > 10000 || !float.IsFinite(driver.CurrentLapDistance)
                || driver.CurrentLapDistance < 0 || driver.CurrentLapDistance > length) return false;
            progress = driver.LapsCompleted * (double)length + driver.CurrentLapDistance;
            return double.IsFinite(progress);
        }

        private static string FormatGap(double seconds)
        {
            long milliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
            if (milliseconds == 0) return "0.000";
            return milliseconds < 60000
                ? "+" + (milliseconds / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)
                : "+" + (milliseconds / 60000).ToString(CultureInfo.InvariantCulture) + ":"
                    + ((milliseconds % 60000) / 1000.0).ToString("00.000", CultureInfo.InvariantCulture);
        }

        private static bool IsUsableGameState(GameState? state)
            => state == GameState.InGamePlaying || state == GameState.InGameMenuTimeTicking || state == GameState.InGamePaused;

        private static bool IsRunning(GameState? state)
            => state == GameState.InGamePlaying || state == GameState.InGameMenuTimeTicking;

        private readonly struct ProgressPoint
        {
            public ProgressPoint(double progress, double time) { Progress = progress; Time = time; }
            public double Progress { get; }
            public double Time { get; }
        }

        private sealed class ProgressHistory
        {
            private readonly List<ProgressPoint> _points = new List<ProgressPoint>();
            private int _first;
            public ProgressHistory(string identity) { Identity = identity; }
            public string Identity { get; }
            public double LastProgress { get; private set; }

            public void Observe(double progress, double time, float trackLength)
            {
                if (_points.Count == 0 || progress > LastProgress + 0.01)
                {
                    _points.Add(new ProgressPoint(progress, time));
                    LastProgress = progress;
                    double cutoff = progress - trackLength * 1.1;
                    while (_first + 1 < _points.Count && _points[_first + 1].Progress < cutoff) _first++;
                    while (_points.Count - _first > MaxPointsPerCar) _first++;
                    if (_first > 1024 && _first > _points.Count / 2)
                    { _points.RemoveRange(0, _first); _first = 0; }
                }
            }

            public bool TryCrossingTime(double progress, out double time)
            {
                time = 0;
                if (_points.Count == _first || progress < _points[_first].Progress - 0.01
                    || progress > _points[_points.Count - 1].Progress + 0.01) return false;
                int low = _first, high = _points.Count - 1;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (_points[middle].Progress < progress) low = middle + 1;
                    else high = middle;
                }
                ProgressPoint after = _points[low];
                if (low == _first || Math.Abs(after.Progress - progress) < 0.01)
                { time = after.Time; return true; }
                ProgressPoint before = _points[low - 1];
                double fraction = Math.Clamp((progress - before.Progress) / (after.Progress - before.Progress), 0, 1);
                time = before.Time + (after.Time - before.Time) * fraction;
                return true;
            }
        }
    }
}
