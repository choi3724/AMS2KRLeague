using System;
using System.Collections.Generic;
using System.Linq;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    // UI-only freeze; telemetry capture keeps the unmodified game timing/invalid flags.
    public sealed class InvalidLapDisplayTracker
    {
        private readonly Dictionary<int, FrozenLap> _frozen = new Dictionary<int, FrozenLap>();
        private readonly Dictionary<int, Stint> _stints = new Dictionary<int, Stint>();
        private readonly HashSet<int> _outLapParticipants = new HashSet<int>();
        private TelemetrySnapshot? _previous;
        private int _generation = int.MinValue;

        public IReadOnlyCollection<int> OutLapParticipants => _outLapParticipants;

        internal static bool IsOutLap(SessionState? session, ParticipantSnapshot driver, IReadOnlyCollection<int>? outLapParticipants = null)
            => driver.IsActive
                && (session == SessionState.Race
                    ? driver.KnownRaceState == RaceState.Racing && driver.LapsCompleted == 0
                    : IsPractice(session) && (driver.KnownRaceState == RaceState.Racing || driver.KnownRaceState == RaceState.NotStarted)
                        && (driver.KnownPitMode == PitMode.None || driver.KnownPitMode == PitMode.DrivingOutOfGarage
                            || driver.KnownPitMode == PitMode.DrivingOutOfPits)
                        && (outLapParticipants?.Contains(driver.Index) ?? InitialOutLap(driver)));

        private static bool IsPractice(SessionState? session)
            => session == SessionState.Practice || session == SessionState.Qualify || session == SessionState.Test;

        private static bool HasFirstSectorTiming(ParticipantSnapshot driver)
            => float.IsFinite(driver.CurrentSector1Time) && driver.CurrentSector1Time >= 0;

        private static bool InitialOutLap(ParticipantSnapshot driver)
            => driver.KnownPitMode == PitMode.DrivingOutOfGarage || driver.KnownPitMode == PitMode.DrivingOutOfPits
                || (driver.KnownPitMode == PitMode.None && driver.LapsCompleted == 0 && driver.CurrentLap <= 1
                    && !HasFirstSectorTiming(driver) && !(float.IsFinite(driver.BestLapTime) && driver.BestLapTime > 0)
                    && !(float.IsFinite(driver.LastLapTime) && driver.LastLapTime > 0));

        private static bool IsLive(GameState? state)
            => state == GameState.InGamePlaying || state == GameState.InGamePaused || state == GameState.InGameMenuTimeTicking;

        // Observe while the waiting screen is visible too: a new stint is a pit exit,
        // not "completed laps == 0". This state affects display only, never raw capture.
        public void Observe(TelemetrySnapshot? snapshot, int generation)
        {
            // The coordinator's UI generation also advances on pause/menu changes.
            // Those are not new stints; retain the observed pit exit across them.
            bool liveModeChange = snapshot != null && _previous != null && snapshot.GameStateRaw != _previous.GameStateRaw
                && IsLive(snapshot.KnownGameState) && IsLive(_previous.KnownGameState);
            if (snapshot == null || (generation != _generation && !liveModeChange) || _previous == null
                || snapshot.SessionStateRaw != _previous.SessionStateRaw
                || snapshot.TrackLocation != _previous.TrackLocation || snapshot.TrackVariation != _previous.TrackVariation
                || snapshot.TrackLength != _previous.TrackLength || snapshot.CapturedAt < _previous.CapturedAt
                || snapshot.KnownGameState == GameState.FrontEnd || snapshot.KnownGameState == GameState.Exited
                || snapshot.KnownGameState == GameState.InGameRestarting || snapshot.KnownGameState == GameState.InGameReplay
                || snapshot.KnownGameState == GameState.FrontEndReplay)
            {
                _frozen.Clear(); _stints.Clear(); _outLapParticipants.Clear();
                _previous = null;
            }
            _generation = generation;
            if (snapshot == null) return;
            if (!IsLive(snapshot.KnownGameState)) return;
            if (_previous != null && snapshot.CapturedAt == _previous.CapturedAt) return;
            double delta = _previous == null ? 0 : (snapshot.CapturedAt - _previous.CapturedAt).TotalSeconds;
            _previous = snapshot;
            _outLapParticipants.Clear();
            if (!IsPractice(snapshot.KnownSessionState)) { _stints.Clear(); return; }
            var present = snapshot.Participants.Select(driver => driver.Index).ToHashSet();
            foreach (int missing in _stints.Keys.Where(index => !present.Contains(index)).ToArray()) _stints.Remove(missing);
            foreach (ParticipantSnapshot driver in snapshot.Participants)
            {
                if (!driver.IsActive || !driver.KnownPitMode.HasValue
                    || (driver.KnownRaceState != RaceState.Racing && driver.KnownRaceState != RaceState.NotStarted))
                { _stints.Remove(driver.Index); continue; }
                if (!_stints.TryGetValue(driver.Index, out Stint? stint)
                    || stint.Previous.Name != driver.Name || stint.Previous.VehicleName != driver.VehicleName
                    || stint.Previous.VehicleClass != driver.VehicleClass || driver.LapsCompleted < stint.Previous.LapsCompleted
                    || driver.CurrentLap < stint.Previous.CurrentLap)
                    _stints[driver.Index] = stint = new Stint(driver);
                ParticipantSnapshot previous = stint.Previous;
                if (driver.KnownPitMode == PitMode.InGarage || driver.KnownPitMode == PitMode.InPit
                    || driver.KnownPitMode == PitMode.DrivingOutOfGarage || driver.KnownPitMode == PitMode.DrivingIntoPits)
                    stint.OutLap = true;
                else if (stint.OutLap && driver.KnownRaceState == RaceState.Racing
                    && (driver.KnownPitMode == PitMode.None || driver.KnownPitMode == PitMode.DrivingOutOfPits)
                    && (previous.KnownPitMode == PitMode.None || previous.KnownPitMode == PitMode.DrivingOutOfPits))
                {
                    // AMS2's first timed lap can keep lap=1/completed=0 and change
                    // NotStarted -> Racing with S1 -1 -> actual timing. Root CurrentTime
                    // alone is not evidence: it can run during an untimed out lap.
                    bool timingStarted = !HasFirstSectorTiming(previous) && HasFirstSectorTiming(driver) && driver.CurrentSector == 0;
                    // Native lap/sector transitions may arrive before completed laps,
                    // geometric distance reset or the pit-exit flag clearing. Do not wait for those.
                    bool lapStarted = previous.CurrentLap > 0 && driver.CurrentLap == previous.CurrentLap + 1;
                    bool sectorStarted = snapshot.NumSectors >= 2 && snapshot.NumSectors <= 3
                        && previous.CurrentSector == snapshot.NumSectors - 1 && driver.CurrentSector == 0
                        && float.IsFinite(previous.CurrentSector1Time) && previous.CurrentSector1Time > 0
                        && float.IsFinite(driver.CurrentSector1Time) && driver.CurrentSector1Time >= 0
                        && driver.CurrentSector1Time < previous.CurrentSector1Time && delta > 0 && delta <= 1;
                    float length = snapshot.TrackLength;
                    float before = previous.CurrentLapDistance, after = driver.CurrentLapDistance;
                    bool wrap = float.IsFinite(length) && length > 0 && float.IsFinite(before) && float.IsFinite(after)
                        && before > length * .75f && before <= length && after >= 0 && after < length * .25f
                        && delta > 0 && delta <= 1 && length - before + after <= delta * 200 + 10;
                    bool completed = driver.LapsCompleted == previous.LapsCompleted + 1;
                    if (timingStarted || lapStarted || sectorStarted || completed || wrap) stint.OutLap = false;
                }
                stint.Previous = driver;
                if (stint.OutLap && (driver.KnownPitMode == PitMode.None
                    || driver.KnownPitMode == PitMode.DrivingOutOfGarage || driver.KnownPitMode == PitMode.DrivingOutOfPits))
                    _outLapParticipants.Add(driver.Index);
            }
        }

        internal static bool ShouldShowInvalidLap(TelemetrySnapshot snapshot, ParticipantSnapshot driver, bool isLocal,
            IReadOnlyCollection<int>? outLapParticipants = null)
            => driver.IsActive && driver.KnownRaceState == RaceState.Racing
                && driver.KnownPitMode == PitMode.None
                // Out laps have no invalid-lap display, including later practice/qualifying stints.
                // Keep the original flag intact for capture; never infer a track-limit offence at the start.
                && !IsOutLap(snapshot.KnownSessionState, driver, outLapParticipants)
                && (driver.LapInvalidated || (isLocal && snapshot.ViewedParticipantIndex == driver.Index && snapshot.LapInvalidated));

        public void Apply(OverlayViewModel view, TelemetrySnapshot snapshot, int generation)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Observe(snapshot, generation);
            var participants = snapshot.Participants.ToDictionary(driver => driver.Index);
            var shown = view.AllRankingRows.Select(row => row.ParticipantIndex).ToHashSet();
            foreach (int missing in _frozen.Keys.Where(index => !shown.Contains(index)).ToArray()) _frozen.Remove(missing);
            foreach (RankingRowViewModel row in view.AllRankingRows)
            {
                if (!participants.TryGetValue(row.ParticipantIndex, out ParticipantSnapshot? driver)) continue;
                bool localSource = row.IsPlayer && snapshot.ViewedParticipantIndex == driver.Index;
                bool invalid = ShouldShowInvalidLap(snapshot, driver, localSource, _outLapParticipants);
                if (!invalid) { _frozen.Remove(driver.Index); continue; }
                // The badge concerns this lap, not the valid historical best in the tower.
                row.StatusColor = "#FF7777";
                row.Status = "무효";
                if (!localSource) continue;
                string key = driver.Name + "\u001f" + driver.VehicleName + "\u001f" + driver.VehicleClass
                    + "\u001f" + driver.LapsCompleted + "/" + driver.CurrentLap + "/" + localSource;
                if (!_frozen.TryGetValue(driver.Index, out FrozenLap? frozen) || frozen.Key != key)
                {
                    _frozen[driver.Index] = frozen = new FrozenLap(key, view.CurrentLapText);
                }
                if (localSource)
                {
                    view.CurrentLapText = frozen.LocalTime;
                    view.CurrentLapColor = "#FF7777";
                    if (!view.CurrentLabel.EndsWith(" · 무효", StringComparison.Ordinal)) view.CurrentLabel += " · 무효";
                }
            }
        }

        private sealed class Stint
        {
            public Stint(ParticipantSnapshot driver)
            {
                Previous = driver;
                OutLap = InitialOutLap(driver);
            }
            public ParticipantSnapshot Previous;
            public bool OutLap;
        }

        private sealed class FrozenLap
        {
            public FrozenLap(string key, string localTime) { Key = key; LocalTime = localTime; }
            public string Key { get; }
            public string LocalTime { get; }
        }
    }
}
