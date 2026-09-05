using System;
using System.Collections.Generic;
using System.Linq;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    // UI-only observation clock, not an official result or a persisted telemetry fact.
    // No distance/speed extrapolation: a lap begins at an observed forward line crossing.
    public sealed class ParticipantLapClock
    {
        private readonly Dictionary<int, Lap> _laps = new Dictionary<int, Lap>();
        private TelemetrySnapshot? _previous;
        private double _elapsed;

        public IReadOnlyDictionary<int, float> Observe(TelemetrySnapshot? snapshot)
        {
            var times = new Dictionary<int, float>();
            if (snapshot == null || snapshot.KnownGameState == GameState.FrontEnd || snapshot.KnownGameState == GameState.Exited
                || snapshot.KnownGameState == GameState.InGameRestarting || snapshot.KnownGameState == GameState.InGameReplay
                || snapshot.KnownGameState == GameState.FrontEndReplay)
            {
                _laps.Clear();
                _previous = null;
                _elapsed = 0;
                return times;
            }

            TelemetrySnapshot? previous = _previous;
            if (previous != null && previous.SequenceNumber == snapshot.SequenceNumber
                && previous.GameStateRaw == snapshot.GameStateRaw && previous.SessionStateRaw == snapshot.SessionStateRaw
                && previous.TrackLocation == snapshot.TrackLocation && previous.TrackVariation == snapshot.TrackVariation)
            {
                foreach (var item in _laps)
                    if (item.Value.Start.HasValue && _elapsed > item.Value.Start.Value)
                        times[item.Key] = (float)(_elapsed - item.Value.Start.Value);
                return times;
            }
            _previous = snapshot;
            double wallDelta = previous == null ? 0 : (snapshot.CapturedAt - previous.CapturedAt).TotalSeconds;
            bool running = IsRunning(snapshot) && previous != null && IsRunning(previous);
            bool reset = previous == null || snapshot.SessionStateRaw != previous.SessionStateRaw
                || snapshot.TrackLocation != previous.TrackLocation || snapshot.TrackVariation != previous.TrackVariation
                || snapshot.TrackLength != previous.TrackLength || snapshot.LapsInEvent != previous.LapsInEvent
                || wallDelta < 0 || (running && wallDelta > 1);
            double delta = running ? wallDelta : 0;
            if (previous != null && snapshot.SessionDuration > 0
                && float.IsFinite(snapshot.EventTimeRemaining) && snapshot.EventTimeRemaining >= 0
                && float.IsFinite(previous.EventTimeRemaining) && previous.EventTimeRemaining >= 0
                && (snapshot.EventTimeRemaining > 0 || previous.EventTimeRemaining > 0))
            {
                // The observed game clock also handles a live multiplayer menu
                // versus an offline pause, without adding wall time to stopped play.
                delta = previous.EventTimeRemaining - snapshot.EventTimeRemaining;
                reset |= delta < -0.01 || delta > 1;
            }
            // A timed race reaching zero is not an individual driver's finish.
            // Fresh playing snapshots keep that driver's observed clock running.
            if (reset) { _laps.Clear(); _elapsed = 0; delta = 0; }
            _elapsed += Math.Max(0, delta);

            var present = snapshot.Participants.Select(driver => driver.Index).ToHashSet();
            foreach (int missing in _laps.Keys.Where(index => !present.Contains(index)).ToArray()) _laps.Remove(missing);
            float length = snapshot.TrackLength;
            foreach (ParticipantSnapshot driver in snapshot.Participants)
            {
                float distance = driver.CurrentLapDistance;
                if (!driver.IsActive || driver.KnownRaceState != RaceState.Racing
                    || driver.KnownPitMode == PitMode.InGarage || driver.KnownPitMode == PitMode.DrivingOutOfGarage
                    || !float.IsFinite(length) || length <= 0 || !float.IsFinite(distance) || distance < 0 || distance > length)
                {
                    _laps.Remove(driver.Index);
                    continue;
                }
                string identity = driver.Name + "\u001f" + driver.VehicleName + "\u001f" + driver.VehicleClass;
                if (!_laps.TryGetValue(driver.Index, out Lap? lap) || lap.Identity != identity)
                    _laps[driver.Index] = lap = new Lap { Identity = identity, Distance = distance, Completed = driver.LapsCompleted };
                else
                {
                    bool wrap = lap.Distance > length * 0.75 && distance < length * 0.25;
                    double movement = wrap ? length - lap.Distance + distance : Math.Abs(distance - lap.Distance);
                    // Reject teleports/reverse wrap and skipped samples, not legitimate stationary time.
                    bool continuous = movement <= Math.Max(0, wallDelta) * 200 + 10
                        && driver.LapsCompleted >= lap.Completed && driver.LapsCompleted <= lap.Completed + 1;
                    if (!continuous) lap.Start = null;
                    else if (wrap && delta > 0) lap.Start = _elapsed;
                }
                lap.Distance = distance;
                lap.Completed = driver.LapsCompleted;
                if (lap.Start.HasValue && _elapsed > lap.Start.Value)
                    times[driver.Index] = (float)(_elapsed - lap.Start.Value);
            }
            return times;
        }

        private static bool IsRunning(TelemetrySnapshot snapshot)
            => snapshot.KnownGameState == GameState.InGamePlaying || snapshot.KnownGameState == GameState.InGameMenuTimeTicking;

        private sealed class Lap
        {
            public string Identity = string.Empty;
            public float Distance;
            public uint Completed;
            public double? Start;
        }
    }
}
