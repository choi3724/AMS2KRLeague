using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.ActivityCapture
{
    public sealed class ActivityCaptureEngine
    {
        private readonly string _installationId;
        private readonly string _clientVersion;
        private readonly Dictionary<string, int> _attemptsByChain = new Dictionary<string, int>(StringComparer.Ordinal);
        private ActiveRace? _race;
        private ActiveTimeAttack? _timeAttack;
        private ScheduledLeagueEvent? _scheduledEvent;
        private string? _raceCaptureChainKey;

        public ActivityCaptureEngine(
            string installationId,
            string clientVersion)
        {
            _installationId = string.IsNullOrWhiteSpace(installationId) ? "local-installation" : installationId.Trim();
            _clientVersion = clientVersion ?? string.Empty;
        }

        public void SetScheduledEvent(ScheduledLeagueEvent? scheduledEvent)
        {
            _scheduledEvent = scheduledEvent;
        }

        public ActivityCaptureUpdate Observe(TelemetrySnapshot snapshot, ParticipantSnapshot? localParticipant)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var update = new ActivityCaptureUpdate();

            if (snapshot.KnownGameState == GameState.InGameRestarting)
            {
                FinalizeActive(snapshot.CapturedAt, CompletionFromLastState(), "GAME_RESTARTING", update);
                return update;
            }

            bool playing = snapshot.KnownGameState == GameState.InGamePlaying;
            if (_race != null && snapshot.KnownSessionState == SessionState.Race)
            {
                ObserveTerminalRaceResult(snapshot, update);
            }
            if (_race != null && IsRaceTransitionSnapshot(snapshot, playing, localParticipant))
            {
                ObserveRaceTransition(snapshot, update);
                return update;
            }

            if (!playing || localParticipant == null || !localParticipant.IsActive)
            {
                FinalizeActive(snapshot.CapturedAt, CompletionFromLastState(), "GAMEPLAY_ENDED", update);
                ResetRaceCaptureChain();
                return update;
            }

            SessionState? session = snapshot.KnownSessionState;
            if (session == SessionState.TimeAttack)
            {
                if (_race != null)
                {
                    FinalizeRace(snapshot.CapturedAt, CompletionFromLastState(), "SESSION_CHANGED_TO_TIME_ATTACK", update);
                }
                ResetRaceCaptureChain();
                ObserveTimeAttack(snapshot, localParticipant, update);
            }
            else if (session == SessionState.Race)
            {
                if (_timeAttack != null)
                {
                    FinalizeTimeAttack("SESSION_CHANGED_TO_RACE", update);
                }
                ObserveRace(snapshot, localParticipant, update);
            }
            else
            {
                FinalizeActive(snapshot.CapturedAt, CompletionFromLastState(), "SESSION_STATE_CHANGED", update);
                ResetRaceCaptureChain();
            }

            return update;
        }

        public ActivityCaptureUpdate Close(DateTimeOffset atUtc, string reason)
        {
            var update = new ActivityCaptureUpdate();
            FinalizeActive(atUtc, CompletionFromLastState(), string.IsNullOrWhiteSpace(reason) ? "CAPTURE_CLOSED" : reason, update);
            ResetRaceCaptureChain();
            return update;
        }

        private void ObserveRace(TelemetrySnapshot snapshot, ParticipantSnapshot local, ActivityCaptureUpdate update)
        {
            // Once AMS2 has supplied a terminal result, keep that result sticky
            // until a real lifecycle boundary. Some post-race frames briefly
            // look playable again and must not re-arm or split the activity.
            if (_race?.TerminalLocal != null) return;

            if (_race?.TransitionHeld == true)
            {
                _race.TransitionHeld = false;
                update.Events.Add("ACTIVITY_TRANSITION_RESUMED type=RACE");
            }

            if (_race != null && IsRestart(_race.LastSnapshot, snapshot, _race.Local, useSessionClock: true))
            {
                FinalizeRace(snapshot.CapturedAt, ActivityCompletionStatus.Aborted, "RACE_RESTART_SIGNAL", update);
            }

            if (_race == null)
            {
                string chainKey = _raceCaptureChainKey ??= CreateCaptureChainKey(snapshot, local);
                int attempt = NextAttempt(chainKey);
                string fingerprint = CreateFingerprint(
                    ActivityType.Race,
                    snapshot.CapturedAt,
                    snapshot.TrackLocation,
                    snapshot.TrackVariation,
                    Vehicle(snapshot, local),
                    chainKey,
                    attempt);
                _race = new ActiveRace
                {
                    StartedAtUtc = snapshot.CapturedAt.ToUniversalTime(),
                    Fingerprint = fingerprint,
                    AttemptNumber = attempt,
                    Scope = ActivityRecordScope.Unclassified,
                    ScheduledEventHint = string.IsNullOrWhiteSpace(_scheduledEvent?.EventId) ? null : _scheduledEvent!.EventId,
                    Metadata = new SessionMetadataAccumulator(snapshot.CapturedAt, "RACE"),
                    Local = local,
                    LastSnapshot = snapshot
                };
                ObserveRaceField(_race, snapshot);
                update.Events.Add("ACTIVITY_STARTED type=RACE scope=UNCLASSIFIED attempt=" + attempt);
            }

            _race.Metadata.Observe(snapshot);
            ObserveRaceField(_race, snapshot);
            _race.Local = local;
            _race.LastSnapshot = snapshot;
        }

        private static bool IsRaceTransitionSnapshot(
            TelemetrySnapshot snapshot,
            bool playing,
            ParticipantSnapshot? localParticipant)
        {
            if (snapshot.KnownSessionState != SessionState.Race) return false;

            // AMS2 temporarily leaves GAME_INGAME_PLAYING for the grid/menu and
            // post-race result/cooldown screens while the same Race session is
            // still alive.  A missing active local participant can occur in the
            // same transition.  Neither condition is proof that the race ended.
            if (playing) return localParticipant == null || !localParticipant.IsActive;
            return snapshot.KnownGameState == GameState.InGameMenuTimeTicking
                || snapshot.KnownGameState == GameState.InGamePaused
                || snapshot.KnownGameState == GameState.InGameReplay;
        }

        private void ObserveRaceTransition(TelemetrySnapshot snapshot, ActivityCaptureUpdate update)
        {
            ActiveRace race = _race!;
            if (!race.TransitionHeld)
            {
                race.TransitionHeld = true;
                update.Events.Add(
                    "ACTIVITY_TRANSITION_HELD type=RACE game="
                    + (snapshot.KnownGameState?.ToString().ToUpperInvariant() ?? "UNKNOWN"));
            }

        }

        private void ObserveTerminalRaceResult(TelemetrySnapshot snapshot, ActivityCaptureUpdate update)
        {
            // The normal local resolver intentionally rejects menu/pause/replay
            // frames so they can never start an activity.  Once a race is
            // active, however, AMS2 may publish the player's terminal result in
            // exactly those frames.  Refresh only the already identified player
            // and only when the game supplies a terminal race state.
            ActiveRace race = _race!;
            ParticipantSnapshot? observedLocal = FindExistingRaceParticipant(snapshot, race.Local);
            if (observedLocal == null || !IsTerminalRaceState(observedLocal.RaceStateRaw)) return;
            uint? previousTerminalState = race.TerminalLocal?.RaceStateRaw;
            race.TerminalLocal = observedLocal;
            race.TerminalSnapshot = snapshot;
            if (previousTerminalState != observedLocal.RaceStateRaw)
            {
                update.Events.Add(
                    "ACTIVITY_TERMINAL_RESULT_OBSERVED type=RACE state="
                    + StateName(observedLocal.RaceStateRaw));
            }
        }

        private static ParticipantSnapshot? FindExistingRaceParticipant(
            TelemetrySnapshot snapshot,
            ParticipantSnapshot previousLocal)
        {
            if (previousLocal.Index >= 0 && previousLocal.Index < snapshot.Participants.Count)
            {
                ParticipantSnapshot indexed = snapshot.Participants[previousLocal.Index];
                if (string.Equals(indexed.Name, previousLocal.Name, StringComparison.Ordinal))
                {
                    return indexed;
                }
            }

            ParticipantSnapshot? uniqueNameMatch = null;
            foreach (ParticipantSnapshot candidate in snapshot.Participants)
            {
                if (!string.Equals(candidate.Name, previousLocal.Name, StringComparison.Ordinal)) continue;
                if (uniqueNameMatch != null) return null;
                uniqueNameMatch = candidate;
            }
            return uniqueNameMatch;
        }

        private static bool IsTerminalRaceState(uint raw)
            => raw == (uint)RaceState.Finished
                || raw == (uint)RaceState.Disqualified
                || raw == (uint)RaceState.Retired
                || raw == (uint)RaceState.Dnf;

        private void FinalizeRace(
            DateTimeOffset endedAtUtc,
            ActivityCompletionStatus completion,
            string reason,
            ActivityCaptureUpdate update)
        {
            if (_race == null) return;
            ActiveRace race = _race;
            _race = null;
            ParticipantSnapshot local = race.TerminalLocal ?? race.Local;
            TelemetrySnapshot snapshot = race.TerminalSnapshot ?? race.LastSnapshot;
            int rawFieldSize = race.RawParticipantKeys.Count;
            uint? finishPosition = local.RacePosition > 0 ? local.RacePosition : (uint?)null;
            var record = new ActivityRecord
            {
                ActivityId = ActivityIds.Create("race", race.Fingerprint, race.AttemptNumber.ToString(CultureInfo.InvariantCulture)),
                ActivityType = ActivityType.Race,
                RecordScopeHint = race.Scope,
                Authority = ActivityAuthority.PlayerPersonal,
                CompletionStatus = completion,
                SessionFingerprint = race.Fingerprint,
                ScheduledEventHint = race.ScheduledEventHint,
                AttemptNumber = race.AttemptNumber,
                StartedAtUtc = race.StartedAtUtc,
                EndedAtUtc = endedAtUtc.ToUniversalTime(),
                SessionType = "RACE",
                Track = snapshot.TrackLocation,
                Layout = snapshot.TrackVariation,
                Vehicle = Vehicle(snapshot, local),
                VehicleClass = VehicleClass(snapshot, local),
                Identity = Identity(local),
                ConfiguredSettings = race.Metadata.ConfiguredSettings,
                ObservedConditions = race.Metadata.BuildObserved(endedAtUtc),
                PersonalRaceSummary = new PersonalRaceSummary
                {
                    FinishPosition = finishPosition,
                    FieldSize = rawFieldSize,
                    CompletedLaps = local.LapsCompleted,
                    BestLapMilliseconds = SecondsToMilliseconds(local.BestLapTime),
                    ResultState = StateName(local.RaceStateRaw)
                },
                Evidence = Evidence(snapshot),
                ClientVersion = _clientVersion
            };
            update.CompletedRecords.Add(record);
            update.Events.Add("ACTIVITY_FINALIZED type=RACE id=" + record.ActivityId + " status=" + completion + " reason=" + reason);
        }

        private void ObserveTimeAttack(TelemetrySnapshot snapshot, ParticipantSnapshot local, ActivityCaptureUpdate update)
        {
            if (_timeAttack != null && IsRestart(_timeAttack.LastSnapshot, snapshot, _timeAttack.Local, useSessionClock: false))
            {
                FinalizeTimeAttack("TIME_ATTACK_RESTART_SIGNAL", update);
            }

            if (_timeAttack == null)
            {
                string fingerprint = CreateFingerprint(
                    ActivityType.TimeAttack,
                    snapshot.CapturedAt,
                    snapshot.TrackLocation,
                    snapshot.TrackVariation,
                    Vehicle(snapshot, local),
                    "time-attack",
                    1);
                _timeAttack = new ActiveTimeAttack
                {
                    StartedAtUtc = snapshot.CapturedAt.ToUniversalTime(),
                    Fingerprint = fingerprint,
                    Metadata = new SessionMetadataAccumulator(snapshot.CapturedAt, "TIME_ATTACK"),
                    Local = local,
                    LastSnapshot = snapshot,
                    PreviousLapsCompleted = local.LapsCompleted,
                    PreviousCurrentLap = local.CurrentLap,
                    CurrentLapInvalid = local.LapInvalidated || snapshot.LapInvalidated,
                    LastLapTime = local.LastLapTime
                };
                CacheSectors(_timeAttack, local);
                update.Events.Add("ACTIVITY_STARTED type=TIME_ATTACK automatic=true");
            }

            ActiveTimeAttack state = _timeAttack;
            state.Metadata.Observe(snapshot);

            bool boundary = local.LapsCompleted > state.PreviousLapsCompleted
                || local.CurrentLap > state.PreviousCurrentLap;
            if (boundary)
            {
                uint completedDelta = local.LapsCompleted >= state.PreviousLapsCompleted
                    ? local.LapsCompleted - state.PreviousLapsCompleted
                    : 0;
                TimeAttackLapRecord lap = BuildTimeAttackLap(state, snapshot, local, completedDelta);
                ActivityRecord record = BuildTimeAttackActivity(state, snapshot, local, lap);
                update.CompletedRecords.Add(record);
                update.Events.Add("TIME_ATTACK_LAP_CAPTURED id=" + lap.LapUid + " valid=" + lap.IsValid);
                state.CurrentLapInvalid = local.LapInvalidated || snapshot.LapInvalidated;
                state.Sector1 = null;
                state.Sector2 = null;
                state.Sector3 = null;
            }
            else
            {
                state.CurrentLapInvalid |= local.LapInvalidated || snapshot.LapInvalidated;
            }

            CacheSectors(state, local);
            state.PreviousLapsCompleted = local.LapsCompleted;
            state.PreviousCurrentLap = local.CurrentLap;
            state.LastLapTime = local.LastLapTime;
            state.Local = local;
            state.LastSnapshot = snapshot;
        }

        private TimeAttackLapRecord BuildTimeAttackLap(
            ActiveTimeAttack state,
            TelemetrySnapshot snapshot,
            ParticipantSnapshot local,
            uint completedDelta)
        {
            int ordinal = checked((int)Math.Max(local.LapsCompleted, local.CurrentLap > 0 ? local.CurrentLap - 1 : 0));
            int? lapMs = SecondsToMilliseconds(local.LastLapTime);
            bool counterGap = completedDelta > 1;
            bool valid = !state.CurrentLapInvalid && !counterGap && lapMs.HasValue;
            var issues = new List<string>();
            if (counterGap) issues.Add("INCOMPLETE_LAP_COUNTER_GAP");
            if (!lapMs.HasValue) issues.Add("LAP_TIME_UNAVAILABLE");
            ValidateSectorSum(lapMs, state.Sector1, state.Sector2, state.Sector3, issues);
            string lapUid = ActivityIds.Create(
                "lap",
                state.Fingerprint,
                ordinal.ToString(CultureInfo.InvariantCulture));
            return new TimeAttackLapRecord
            {
                LapUid = lapUid,
                LapOrdinal = ordinal,
                CompletedAtUtc = snapshot.CapturedAt.ToUniversalTime(),
                LapTimeMilliseconds = lapMs,
                Sector1Milliseconds = SecondsToMilliseconds(state.Sector1),
                Sector2Milliseconds = SecondsToMilliseconds(state.Sector2),
                Sector3Milliseconds = SecondsToMilliseconds(state.Sector3),
                IsValid = valid,
                InvalidReason = state.CurrentLapInvalid
                    ? "AMS2_LAP_INVALIDATED"
                    : counterGap ? "INCOMPLETE_LAP_COUNTER_GAP" : !lapMs.HasValue ? "LAP_TIME_UNAVAILABLE" : null,
                Issues = issues
            };
        }

        private ActivityRecord BuildTimeAttackActivity(
            ActiveTimeAttack state,
            TelemetrySnapshot snapshot,
            ParticipantSnapshot local,
            TimeAttackLapRecord lap)
        {
            var record = new ActivityRecord
            {
                ActivityId = ActivityIds.Create("ta", state.Fingerprint, lap.LapUid),
                ActivityType = ActivityType.TimeAttack,
                RecordScopeHint = ActivityRecordScope.Unclassified,
                Authority = ActivityAuthority.PlayerPersonal,
                CompletionStatus = ActivityCompletionStatus.Finished,
                SessionFingerprint = state.Fingerprint,
                AttemptNumber = 1,
                StartedAtUtc = state.StartedAtUtc,
                EndedAtUtc = lap.CompletedAtUtc,
                SessionType = "TIME_ATTACK",
                Track = snapshot.TrackLocation,
                Layout = snapshot.TrackVariation,
                Vehicle = Vehicle(snapshot, local),
                VehicleClass = VehicleClass(snapshot, local),
                Identity = Identity(local),
                ConfiguredSettings = state.Metadata.ConfiguredSettings,
                ObservedConditions = state.Metadata.BuildObserved(lap.CompletedAtUtc),
                TimeAttackLap = lap,
                Evidence = Evidence(snapshot),
                ClientVersion = _clientVersion
            };
            return record;
        }

        private void FinalizeTimeAttack(string reason, ActivityCaptureUpdate update)
        {
            if (_timeAttack == null) return;
            _timeAttack = null;
            update.Events.Add("ACTIVITY_SESSION_CLOSED type=TIME_ATTACK reason=" + reason);
        }

        private void FinalizeActive(
            DateTimeOffset atUtc,
            ActivityCompletionStatus completion,
            string reason,
            ActivityCaptureUpdate update)
        {
            FinalizeRace(atUtc, completion, reason, update);
            FinalizeTimeAttack(reason, update);
        }

        private ActivityCompletionStatus CompletionFromLastState()
        {
            if (_race == null) return ActivityCompletionStatus.Incomplete;
            uint raw = (_race.TerminalLocal ?? _race.Local).RaceStateRaw;
            return IsTerminalRaceState(raw)
                ? ActivityCompletionStatus.Finished
                : ActivityCompletionStatus.Aborted;
        }

        private int NextAttempt(string chainKey)
        {
            if (!_attemptsByChain.TryGetValue(chainKey, out int current)) current = 0;
            int next = current + 1;
            _attemptsByChain[chainKey] = next;
            return next;
        }

        private string CreateCaptureChainKey(TelemetrySnapshot snapshot, ParticipantSnapshot local)
        {
            string observed = _installationId + "|race-chain|"
                + snapshot.CapturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "|"
                + snapshot.TrackLocation + "|" + snapshot.TrackVariation + "|" + Vehicle(snapshot, local);
            return "capture-chain-" + ActivityIds.Hash(observed).Substring(0, 24);
        }

        private void ResetRaceCaptureChain()
        {
            _raceCaptureChainKey = null;
        }

        private bool IsRestart(
            TelemetrySnapshot previous,
            TelemetrySnapshot current,
            ParticipantSnapshot previousLocal,
            bool useSessionClock)
        {
            if (current.KnownGameState == GameState.InGameRestarting) return true;
            if (!string.Equals(previous.TrackLocation, current.TrackLocation, StringComparison.Ordinal)
                || !string.Equals(previous.TrackVariation, current.TrackVariation, StringComparison.Ordinal)) return true;
            int viewed = current.ViewedParticipantIndex;
            if (viewed < 0 || viewed >= current.Participants.Count) return false;
            ParticipantSnapshot currentLocal = current.Participants[viewed];
            if (currentLocal.LapsCompleted < previousLocal.LapsCompleted) return true;
            if (currentLocal.CurrentLap + 1 < previousLocal.CurrentLap) return true;
            // In Time Attack mCurrentTime is the current lap timer and therefore
            // legitimately rolls back at every completed lap.  It is only a
            // restart signal for race-session capture.
            if (useSessionClock && current.CurrentTime + 30 < previous.CurrentTime) return true;
            return previous.EventTimeRemaining >= 0
                && current.EventTimeRemaining > previous.EventTimeRemaining + 60;
        }

        private string CreateFingerprint(
            ActivityType type,
            DateTimeOffset startedAt,
            string track,
            string layout,
            string vehicle,
            string chainKey,
            int attempt)
        {
            string value = _installationId + "|" + type + "|"
                + startedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "|"
                + track + "|" + layout + "|" + vehicle + "|" + chainKey + "|" + attempt.ToString(CultureInfo.InvariantCulture);
            return ActivityIds.Hash(value);
        }

        private static ActivityIdentitySnapshot Identity(ParticipantSnapshot local)
            => new ActivityIdentitySnapshot
            {
                ObservedAms2Name = local.Name,
                IdentitySource = "OBSERVED_AMS2_NAME"
            };

        private static ActivitySourceEvidence Evidence(TelemetrySnapshot snapshot)
            => new ActivitySourceEvidence
            {
                SharedMemoryVersion = snapshot.Version,
                Ams2Build = snapshot.BuildVersion,
                SessionStateRaw = snapshot.SessionStateRaw
            };

        private static string Vehicle(TelemetrySnapshot snapshot, ParticipantSnapshot local)
            => !string.IsNullOrWhiteSpace(local.VehicleName) ? local.VehicleName : snapshot.RootCarName;

        private static string VehicleClass(TelemetrySnapshot snapshot, ParticipantSnapshot local)
            => !string.IsNullOrWhiteSpace(local.VehicleClass) ? local.VehicleClass : snapshot.RootCarClassName;

        private static string StateName(uint raw)
            => Enum.IsDefined(typeof(RaceState), raw) ? ((RaceState)raw).ToString().ToUpperInvariant() : "UNKNOWN_" + raw;

        private static int? SecondsToMilliseconds(float? seconds)
        {
            if (!seconds.HasValue || seconds.Value <= 0 || float.IsNaN(seconds.Value) || float.IsInfinity(seconds.Value)) return null;
            double milliseconds = Math.Round(seconds.Value * 1000.0, MidpointRounding.AwayFromZero);
            return milliseconds <= int.MaxValue ? (int)milliseconds : (int?)null;
        }

        private static void CacheSectors(ActiveTimeAttack state, ParticipantSnapshot local)
        {
            if (IsFinitePositive(local.CurrentSector1Time)) state.Sector1 = local.CurrentSector1Time;
            if (IsFinitePositive(local.CurrentSector2Time)) state.Sector2 = local.CurrentSector2Time;
            if (IsFinitePositive(local.CurrentSector3Time)) state.Sector3 = local.CurrentSector3Time;
        }

        private static bool IsFinitePositive(float value)
            => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);

        private void ObserveRaceField(ActiveRace race, TelemetrySnapshot snapshot)
        {
            foreach (ParticipantSnapshot participant in snapshot.Participants.Where(participant => participant.IsActive))
            {
                string key = participant.Index.ToString(CultureInfo.InvariantCulture) + ":" + participant.Name;
                race.RawParticipantKeys.Add(key);
            }
        }

        private static void ValidateSectorSum(
            int? lapMilliseconds,
            float? sector1,
            float? sector2,
            float? sector3,
            List<string> issues)
        {
            int? s1 = SecondsToMilliseconds(sector1);
            int? s2 = SecondsToMilliseconds(sector2);
            int? s3 = SecondsToMilliseconds(sector3);
            if (!lapMilliseconds.HasValue || !s1.HasValue || !s2.HasValue || !s3.HasValue) return;
            int delta = Math.Abs(lapMilliseconds.Value - (s1.Value + s2.Value + s3.Value));
            if (delta > 250) issues.Add("SECTOR_SUM_MISMATCH");
        }

        private sealed class ActiveRace
        {
            public DateTimeOffset StartedAtUtc { get; set; }
            public string Fingerprint { get; set; } = string.Empty;
            public int AttemptNumber { get; set; }
            public ActivityRecordScope Scope { get; set; }
            public string? ScheduledEventHint { get; set; }
            public SessionMetadataAccumulator Metadata { get; set; } = null!;
            public ParticipantSnapshot Local { get; set; } = null!;
            public TelemetrySnapshot LastSnapshot { get; set; } = null!;
            public HashSet<string> RawParticipantKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
            public bool TransitionHeld { get; set; }
            public ParticipantSnapshot? TerminalLocal { get; set; }
            public TelemetrySnapshot? TerminalSnapshot { get; set; }
        }

        private sealed class ActiveTimeAttack
        {
            public DateTimeOffset StartedAtUtc { get; set; }
            public string Fingerprint { get; set; } = string.Empty;
            public SessionMetadataAccumulator Metadata { get; set; } = null!;
            public ParticipantSnapshot Local { get; set; } = null!;
            public TelemetrySnapshot LastSnapshot { get; set; } = null!;
            public uint PreviousLapsCompleted { get; set; }
            public uint PreviousCurrentLap { get; set; }
            public bool CurrentLapInvalid { get; set; }
            public float LastLapTime { get; set; }
            public float? Sector1 { get; set; }
            public float? Sector2 { get; set; }
            public float? Sector3 { get; set; }
        }
    }

    public static class ActivityIds
    {
        public static string Create(string prefix, params string[] parts)
        {
            string hash = Hash(string.Join("|", parts ?? Array.Empty<string>()));
            return prefix + "-" + hash.Substring(0, 32);
        }

        public static string Hash(string value)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
        }
    }
}
