using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.HostRecording
{
    public sealed class HostRecorderEngine
    {
        private static readonly TimeSpan EvidenceInterval = TimeSpan.FromSeconds(30);
        private const int MaximumEvidenceSnapshots = 720;
        private static readonly TimeSpan QualifyingStable = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan GridStable = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan ResultStable = TimeSpan.FromSeconds(1.5);

        private readonly string _hostInstallationId;
        private readonly List<HostRecorderIssue> _issues = new List<HostRecorderIssue>();
        private readonly Queue<HostEvidenceSnapshot> _evidence = new Queue<HostEvidenceSnapshot>();
        private readonly Dictionary<int, ParticipantLifecycle> _lifecycle = new Dictionary<int, ParticipantLifecycle>();
        private readonly HashSet<string> _sessionTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly StableClassificationGate _qualifyingGate = new StableClassificationGate();
        private readonly StableClassificationGate _gridGate = new StableClassificationGate();
        private readonly StableClassificationGate _resultGate = new StableClassificationGate();
        private readonly Dictionary<string, SessionMetadataAccumulator> _sessionMetadata = new Dictionary<string, SessionMetadataAccumulator>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _attemptsByChain = new Dictionary<string, int>(StringComparer.Ordinal);
        private DateTimeOffset? _sessionStarted;
        private ScheduledLeagueEvent? _scheduledEvent;
        private string? _captureChainId;
        private int _currentAttemptNumber = 1;
        private DateTimeOffset _lastEvidenceAt = DateTimeOffset.MinValue;
        private DateTimeOffset _lastObservedAt = DateTimeOffset.MinValue;
        private uint _previousSessionState;
        private uint _previousRaceState;
        private string _initialParticipantKey = string.Empty;
        private HostClassification? _latestQualifying;
        private HostClassification? _qualifying;
        private HostClassification? _latestGrid;
        private HostClassification? _startingGrid;
        private HostClassification? _latestRace;
        private HostRaceResult? _raceResult;
        private TelemetrySnapshot? _latestSnapshot;
        private uint _sessionBuild;
        private uint _sessionSharedMemoryVersion;
        private string _sessionTrack = string.Empty;
        private string _sessionLayout = string.Empty;
        private HostRecorderPhase _phase = HostRecorderPhase.Waiting;

        public HostRecorderEngine(string hostInstallationId)
        {
            _hostInstallationId = string.IsNullOrWhiteSpace(hostInstallationId) ? "local-host" : hostInstallationId.Trim();
        }

        public HostRecorderPhase Phase => _phase;

        public void SetScheduledEvent(ScheduledLeagueEvent? scheduledEvent)
        {
            _scheduledEvent = scheduledEvent;
        }

        public HostRecorderUpdate Observe(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var update = new HostRecorderUpdate { Phase = _phase };
            TelemetrySnapshot? previousSnapshot = _latestSnapshot;
            if (_sessionStarted.HasValue && snapshot.CapturedAt < _lastObservedAt)
            {
                AddIssue(HostIssueSeverity.Error, "STALE_SNAPSHOT", "A Shared Memory snapshot timestamp moved backwards and was rejected.");
                update.Events.Add("STALE_SNAPSHOT_REJECTED");
                return update;
            }

            if (_sessionStarted.HasValue && IsRestartBoundary(previousSnapshot, snapshot))
            {
                update.FinalizedSession = CloseSession(snapshot.CapturedAt, "RACE_RESTART", update);
                update.Events.Add("RESTART_ATTEMPT_PRESERVED");
                update.Phase = _phase;
                return update;
            }
            _lastObservedAt = snapshot.CapturedAt;
            _latestSnapshot = snapshot;

            // AMS2 can briefly retain RACE plus participant data after returning to
            // the front end.  That snapshot is useful to close the active session,
            // but it must never arm a new recorder session from stale menu data.
            bool relevant = IsRelevant(snapshot.SessionStateRaw)
                && snapshot.KnownGameState != GameState.FrontEnd;
            if (relevant && !_sessionStarted.HasValue)
            {
                StartSession(snapshot, update);
            }

            if (_sessionStarted.HasValue)
            {
                PreserveSessionMetadata(snapshot);
                PreserveSessionType(snapshot.SessionStateRaw);
                ObserveSessionMetadata(snapshot);
                bool lifecycleChanged = ObserveLifecycle(snapshot);
                AddEvidenceIfDue(snapshot, lifecycleChanged);
            }

            if (_previousSessionState == (uint)SessionState.Qualify && snapshot.SessionStateRaw != (uint)SessionState.Qualify)
            {
                FinalizeQualifying(snapshot.CapturedAt, update);
            }

            if (_sessionStarted.HasValue)
            {
                switch ((SessionState?)snapshot.KnownSessionState)
                {
                    case SessionState.Practice:
                    case SessionState.Test:
                        SetPhase(HostRecorderPhase.Practice, update);
                        break;
                    case SessionState.Qualify:
                        ObserveQualifying(snapshot, update);
                        break;
                    case SessionState.FormationLap:
                        ObserveGrid(snapshot, update);
                        break;
                    case SessionState.Race:
                        ObserveRace(snapshot, update);
                        break;
                }
            }

            bool reset = _sessionStarted.HasValue
                && ((!relevant && (_previousSessionState != 0 || snapshot.KnownGameState == GameState.FrontEnd))
                    || (_previousSessionState == (uint)SessionState.Race
                        && snapshot.SessionStateRaw != (uint)SessionState.Race
                        && snapshot.SessionStateRaw != (uint)SessionState.FormationLap));
            if (reset)
            {
                update.FinalizedSession = CloseSession(snapshot.CapturedAt, "SESSION_RESET", update);
            }

            _previousSessionState = snapshot.SessionStateRaw;
            _previousRaceState = snapshot.RaceStateRaw;
            update.Phase = _phase;
            return update;
        }

        public HostRecorderUpdate Close(DateTimeOffset at, string reason)
        {
            var update = new HostRecorderUpdate { Phase = _phase };
            if (_sessionStarted.HasValue)
            {
                update.FinalizedSession = CloseSession(at, reason, update);
            }

            update.Phase = _phase;
            return update;
        }

        private void StartSession(TelemetrySnapshot snapshot, HostRecorderUpdate update)
        {
            _sessionStarted = snapshot.CapturedAt;
            StartCaptureAttempt(snapshot, update);
            PreserveSessionMetadata(snapshot);
            PreserveSessionType(snapshot.SessionStateRaw);
            _initialParticipantKey = string.Join(",", snapshot.Participants
                .Where(participant => participant.IsActive)
                .OrderBy(participant => participant.Index)
                .Select(participant => participant.Index + ":" + participant.Name));
            update.Events.Add("HOST_SESSION_STARTED");
        }

        private void StartCaptureAttempt(TelemetrySnapshot snapshot, HostRecorderUpdate update)
        {
            if (_captureChainId == null)
            {
                string raw = _hostInstallationId + "|host-capture-chain|"
                    + snapshot.CapturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "|"
                    + snapshot.TrackLocation + "|" + snapshot.TrackVariation;
                _captureChainId = "capture-chain-" + ActivityIds.Hash(raw).Substring(0, 24);
            }

            if (!_attemptsByChain.TryGetValue(_captureChainId, out int current)) current = 0;
            _currentAttemptNumber = current + 1;
            _attemptsByChain[_captureChainId] = _currentAttemptNumber;
            update.Events.Add("HOST_ACTIVITY_STARTED scope=UNCLASSIFIED chain=" + _captureChainId + " attempt=" + _currentAttemptNumber);
        }

        private void ObserveQualifying(TelemetrySnapshot snapshot, HostRecorderUpdate update)
        {
            SetPhase(HostRecorderPhase.QualifyingActive, update);
            HostClassification? candidate = BuildClassification(snapshot, "QUALIFYING_FINAL");
            if (candidate == null) return;
            _latestQualifying = candidate;
            TimeSpan stableFor = _qualifyingGate.Observe(Signature(candidate), snapshot.CapturedAt);
            if (stableFor >= QualifyingStable)
            {
                candidate.Stable = true;
                candidate.StableMilliseconds = (int)stableFor.TotalMilliseconds;
            }
        }

        private void FinalizeQualifying(DateTimeOffset at, HostRecorderUpdate update)
        {
            if (_qualifying != null || _latestQualifying == null) return;
            SetPhase(HostRecorderPhase.QualifyingFinalizing, update);
            _qualifying = CloneClassification(_latestQualifying);
            if (!_qualifying.Stable)
            {
                AddIssue(HostIssueSeverity.Warning, "QUALIFYING_NOT_STABLE", "Qualifying ended before a one-second stable final classification was observed.");
            }

            update.Events.Add("QUALIFYING_CAPTURED");
        }

        private void ObserveGrid(TelemetrySnapshot snapshot, HostRecorderUpdate update)
        {
            if (_startingGrid != null)
            {
                SetPhase(HostRecorderPhase.RaceGridCaptured, update);
                return;
            }

            SetPhase(HostRecorderPhase.RaceGridArmed, update);
            HostClassification? candidate = BuildClassification(snapshot, "STARTING_GRID_DERIVED");
            if (candidate == null) return;
            candidate.Source = "DERIVED_STABLE_RACE_START_POSITION_SNAPSHOT";
            _latestGrid = candidate;
            TimeSpan stableFor = _gridGate.Observe(Signature(candidate), snapshot.CapturedAt);
            if (stableFor >= GridStable)
            {
                candidate.Stable = true;
                candidate.StableMilliseconds = (int)stableFor.TotalMilliseconds;
                _startingGrid = CloneClassification(candidate);
                SetPhase(HostRecorderPhase.RaceGridCaptured, update);
                update.Events.Add("GRID_CAPTURED");
            }
        }

        private void ObserveRace(TelemetrySnapshot snapshot, HostRecorderUpdate update)
        {
            bool started = snapshot.RaceStateRaw == (uint)RaceState.Racing
                || snapshot.Participants.Any(participant => participant.IsActive && participant.RaceStateRaw == (uint)RaceState.Racing);
            if (_startingGrid == null && !started)
            {
                ObserveGrid(snapshot, update);
            }

            HostClassification? candidate = BuildClassification(snapshot, "RACE_FINAL");
            if (candidate != null)
            {
                _latestRace = candidate;
                TimeSpan stableFor = _resultGate.Observe(Signature(candidate), snapshot.CapturedAt);
                bool finishing = snapshot.RaceStateRaw == (uint)RaceState.Finished
                    || candidate.Participants.Any(participant => IsTerminal(participant.ResultStateRaw));
                if (finishing)
                {
                    SetPhase(HostRecorderPhase.RaceFinishing, update);
                    if (stableFor >= ResultStable && candidate.Participants.All(participant => IsTerminal(participant.ResultStateRaw)))
                    {
                        FinalizeRace(candidate, stableFor, update);
                    }
                }
                else if (started)
                {
                    SetPhase(HostRecorderPhase.RaceActive, update);
                }
            }
        }

        private void FinalizeRace(HostClassification candidate, TimeSpan stableFor, HostRecorderUpdate update)
        {
            if (_raceResult != null) return;
            SetPhase(HostRecorderPhase.PostRaceStabilizing, update);
            _raceResult = new HostRaceResult
            {
                CapturedAtUtc = candidate.CapturedAtUtc,
                Stable = stableFor >= ResultStable,
                StableMilliseconds = (int)stableFor.TotalMilliseconds,
                Participants = candidate.Participants
                    .Select(CloneParticipant)
                    .ToList()
            };
            SetPhase(HostRecorderPhase.ResultCaptured, update);
            update.Events.Add("RACE_RESULT_CAPTURED");
        }

        private HostSessionResult CloseSession(DateTimeOffset at, string reason, HostRecorderUpdate update)
        {
            FinalizeQualifying(at, update);
            if (_startingGrid == null && _latestGrid != null)
            {
                _startingGrid = CloneClassification(_latestGrid);
                AddIssue(HostIssueSeverity.Warning, "GRID_NOT_STABLE", "Race-start positions reset before the one-second grid stability gate completed.");
            }

            if (_raceResult == null && _latestRace != null)
            {
                AddIssue(HostIssueSeverity.Warning, "RESULT_FINALIZED_ON_RESET", "The last complete race classification was preserved when Shared Memory reset.");
                FinalizeRace(_latestRace, TimeSpan.Zero, update);
            }

            if (_raceResult == null)
            {
                AddIssue(HostIssueSeverity.Warning, "RACE_RESULT_NOT_CAPTURED", "Session closed without a race result classification.");
            }

            if (_latestSnapshot == null) throw new InvalidOperationException("No snapshot is available for session close.");
            string sessionId = HostSessionFingerprint.Create(
                _sessionBuild,
                _sessionTrack,
                _sessionLayout,
                string.Join(",", _sessionTypes.OrderBy(value => value, StringComparer.Ordinal)),
                _sessionStarted!.Value,
                _initialParticipantKey,
                _hostInstallationId);
            var session = new HostSessionResult
            {
                SessionId = sessionId,
                HostInstallationId = _hostInstallationId,
                StartedAtUtc = _sessionStarted.Value,
                EndedAtUtc = at,
                Ams2Build = _sessionBuild,
                SharedMemoryVersion = _sessionSharedMemoryVersion,
                Track = _sessionTrack,
                Layout = _sessionLayout,
                SessionTypesObserved = _sessionTypes.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                ClosingReason = reason,
                AttemptStatus = DetermineAttemptStatus(reason),
                Qualifying = _qualifying,
                StartingGrid = _startingGrid,
                RaceResult = _raceResult,
                Issues = _issues.Select(CloneIssue).ToList(),
                Evidence = _evidence.ToList()
            };
            session.Activity = new HostSessionActivityMetadata
            {
                ActivityId = ActivityIds.Create("host-race", session.SessionId),
                ActivityType = ActivityType.Race,
                RecordScopeHint = ActivityRecordScope.Unclassified,
                SessionFingerprint = session.SessionId,
                CaptureChainId = _captureChainId,
                ScheduledEventHint = string.IsNullOrWhiteSpace(_scheduledEvent?.EventId) ? null : _scheduledEvent!.EventId,
                AttemptNumber = _currentAttemptNumber,
                AttemptStatus = session.AttemptStatus,
                RaceMode = "UNKNOWN",
                ConfiguredSettings = _sessionMetadata.ToDictionary(
                    item => item.Key,
                    item => item.Value.ConfiguredSettings,
                    StringComparer.Ordinal),
                ObservedConditions = _sessionMetadata.ToDictionary(
                    item => item.Key,
                    item => item.Value.BuildObserved(at),
                    StringComparer.Ordinal)
            };
            session.Reliability = session.Issues.Any(issue => issue.Severity == HostIssueSeverity.Error)
                ? HostResultReliability.Quarantined
                : IsVerified(session) ? HostResultReliability.Verified : HostResultReliability.Provisional;
            update.Events.Add("SESSION_CLOSED reason=" + reason);
            SetPhase(HostRecorderPhase.SessionClosed, update);
            ResetForNextSession(string.Equals(reason, "RACE_RESTART", StringComparison.Ordinal));
            return session;
        }

        private void PreserveSessionMetadata(TelemetrySnapshot snapshot)
        {
            if (snapshot.BuildVersion != 0) _sessionBuild = snapshot.BuildVersion;
            if (snapshot.Version != 0) _sessionSharedMemoryVersion = snapshot.Version;
            if (!string.IsNullOrWhiteSpace(snapshot.TrackLocation)) _sessionTrack = snapshot.TrackLocation;
            if (!string.IsNullOrWhiteSpace(snapshot.TrackVariation)) _sessionLayout = snapshot.TrackVariation;
        }

        private void PreserveSessionType(uint raw)
        {
            string? name = (SessionState?)raw switch
            {
                SessionState.Practice => "PRACTICE",
                SessionState.Test => "TEST",
                SessionState.Qualify => "QUALIFYING",
                SessionState.FormationLap => "FORMATION_LAP",
                SessionState.Race => "RACE",
                _ => raw == 0 ? null : "UNKNOWN_" + raw.ToString(CultureInfo.InvariantCulture)
            };
            if (name != null)
            {
                _sessionTypes.Add(name);
            }
        }

        private void ObserveSessionMetadata(TelemetrySnapshot snapshot)
        {
            string? name = SessionName(snapshot.SessionStateRaw);
            if (name == null) return;
            if (!_sessionMetadata.TryGetValue(name, out SessionMetadataAccumulator? accumulator))
            {
                accumulator = new SessionMetadataAccumulator(snapshot.CapturedAt, name);
                _sessionMetadata[name] = accumulator;
            }
            accumulator.Observe(snapshot);
        }

        private static string? SessionName(uint raw)
            => (SessionState?)raw switch
            {
                SessionState.Practice => "PRACTICE",
                SessionState.Test => "TEST",
                SessionState.Qualify => "QUALIFYING",
                SessionState.FormationLap => "FORMATION_LAP",
                SessionState.Race => "RACE",
                SessionState.TimeAttack => "TIME_ATTACK",
                _ => null
            };

        private HostClassification? BuildClassification(TelemetrySnapshot snapshot, string kind)
        {
            List<ParticipantSnapshot> rawActive = snapshot.Participants.Where(participant => participant.IsActive).ToList();
            if (rawActive.Count == 0)
            {
                AddIssue(HostIssueSeverity.Error, "MISSING_PARTICIPANT", kind + " contains no active participants.");
                return null;
            }

            // Final snapshots are raw facts. Safety Car/non-driver rows remain in
            // the immutable evidence and are excluded later by server policy.
            List<ParticipantSnapshot> active = rawActive;

            var positions = new HashSet<uint>();
            bool invalid = false;
            foreach (ParticipantSnapshot participant in active)
            {
                if (participant.RacePosition == 0 || !positions.Add(participant.RacePosition))
                {
                    AddIssue(HostIssueSeverity.Error, "DUPLICATE_OR_INVALID_POSITION", kind + " has duplicate/zero position " + participant.RacePosition + ".");
                    invalid = true;
                }
                if (participant.LapsCompleted > 10000 || participant.CurrentLap > 10001)
                {
                    AddIssue(HostIssueSeverity.Error, "INVALID_LAP", "Slot " + participant.Index + " has an impossible lap value.");
                    invalid = true;
                }
            }

            foreach (IGrouping<string, ParticipantSnapshot> group in active
                .Where(participant => !string.IsNullOrWhiteSpace(participant.Name))
                .GroupBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() > 1)
                {
                    AddIssue(HostIssueSeverity.Error, "IDENTITY_AMBIGUOUS", "Display name '" + group.Key + "' occurs in multiple active slots and was not merged.");
                    invalid = true;
                }
            }

            if (invalid) return null;
            return new HostClassification
            {
                Kind = kind,
                CapturedAtUtc = snapshot.CapturedAt,
                Participants = active
                    .OrderBy(participant => participant.RacePosition)
                    .Select(participant => ToEvidence(participant, snapshot.CapturedAt))
                    .ToList()
            };
        }

        private bool ObserveLifecycle(TelemetrySnapshot snapshot)
        {
            bool changed = false;
            var observedSlots = new HashSet<int>();
            foreach (ParticipantSnapshot participant in snapshot.Participants)
            {
                observedSlots.Add(participant.Index);
                if (!_lifecycle.TryGetValue(participant.Index, out ParticipantLifecycle? life))
                {
                    life = new ParticipantLifecycle();
                    _lifecycle[participant.Index] = life;
                    changed = true;
                }

                if (participant.IsActive)
                {
                    if (life.Active && life.Name.Length > 0 && !string.Equals(life.Name, participant.Name, StringComparison.Ordinal))
                    {
                        life.Generation++;
                        changed = true;
                        AddIssue(HostIssueSeverity.Error, "SLOT_REUSED", "Slot " + participant.Index + " changed from '" + life.Name + "' to '" + participant.Name + "'.");
                    }
                    else if (!life.Active && life.Seen && string.Equals(life.Name, participant.Name, StringComparison.Ordinal))
                    {
                        life.Generation++;
                        changed = true;
                        AddIssue(HostIssueSeverity.Warning, "POSSIBLE_REJOIN", "Name '" + participant.Name + "' reappeared in slot " + participant.Index + "; identity is not asserted.");
                    }

                    if (!life.Seen) life.FirstSeen = snapshot.CapturedAt;
                    life.Seen = true;
                    life.Active = true;
                    life.Name = participant.Name;
                    life.LastSeen = snapshot.CapturedAt;
                    life.LastRaceState = participant.RaceStateRaw;
                }
                else if (life.Active)
                {
                    life.Active = false;
                    changed = true;
                    if (!IsTerminal(life.LastRaceState))
                    {
                        life.Disappeared = true;
                        AddIssue(HostIssueSeverity.Error, "PARTICIPANT_DISAPPEARED", "Slot " + participant.Index + " became inactive before a terminal classification.");
                    }
                }
            }

            foreach (KeyValuePair<int, ParticipantLifecycle> item in _lifecycle)
            {
                if (item.Value.Active && !observedSlots.Contains(item.Key))
                {
                    MarkDisappeared(item.Key, item.Value);
                    changed = true;
                }
            }
            return changed;
        }

        private void MarkDisappeared(int slot, ParticipantLifecycle life)
        {
            life.Active = false;
            if (!IsTerminal(life.LastRaceState))
            {
                life.Disappeared = true;
                AddIssue(HostIssueSeverity.Error, "PARTICIPANT_DISAPPEARED", "Slot " + slot + " became inactive before a terminal classification.");
            }
        }

        private void AddEvidenceIfDue(TelemetrySnapshot snapshot, bool force)
        {
            bool transition = snapshot.SessionStateRaw != _previousSessionState || snapshot.RaceStateRaw != _previousRaceState;
            if (!force && !transition && snapshot.CapturedAt - _lastEvidenceAt < EvidenceInterval) return;
            _lastEvidenceAt = snapshot.CapturedAt;
            _evidence.Enqueue(new HostEvidenceSnapshot
            {
                CapturedAtUtc = snapshot.CapturedAt,
                SequenceNumber = snapshot.SequenceNumber,
                GameStateRaw = snapshot.GameStateRaw,
                SessionStateRaw = snapshot.SessionStateRaw,
                RaceStateRaw = snapshot.RaceStateRaw,
                CurrentTimeSeconds = snapshot.CurrentTime,
                EventTimeRemainingSeconds = snapshot.EventTimeRemaining,
                Track = snapshot.TrackLocation,
                Layout = snapshot.TrackVariation,
                SessionDurationMinutes = snapshot.SessionDuration,
                ConfiguredLaps = snapshot.LapsInEvent,
                SessionAdditionalLaps = snapshot.SessionAdditionalLaps,
                EnforcedPitStopLap = snapshot.EnforcedPitStopLap,
                SessionIsPrivate = snapshot.SessionIsPrivate,
                AmbientTemperatureCelsius = snapshot.AmbientTemperature,
                TrackTemperatureCelsius = snapshot.TrackTemperature,
                RainDensity = snapshot.RainDensity,
                WindSpeed = snapshot.WindSpeed,
                WindDirectionX = snapshot.WindDirectionX,
                WindDirectionY = snapshot.WindDirectionY,
                CloudBrightness = snapshot.CloudBrightness,
                SnowDensity = snapshot.SnowDensity,
                Participants = snapshot.Participants.Where(participant => participant.IsActive)
                    .Select(participant => ToEvidence(participant, snapshot.CapturedAt)).ToList()
            });
            while (_evidence.Count > MaximumEvidenceSnapshots)
            {
                _evidence.Dequeue();
            }
        }

        private HostParticipantEvidence ToEvidence(ParticipantSnapshot participant, DateTimeOffset at)
        {
            _lifecycle.TryGetValue(participant.Index, out ParticipantLifecycle? life);
            return new HostParticipantEvidence
            {
                Slot = participant.Index,
                Generation = life?.Generation ?? 0,
                Active = participant.IsActive,
                NameSnapshot = participant.Name,
                Position = participant.RacePosition,
                LapsCompleted = participant.LapsCompleted,
                CurrentLap = participant.CurrentLap,
                CurrentSector = participant.CurrentSector,
                LastLapSeconds = participant.LastLapTime,
                BestLapSeconds = participant.BestLapTime,
                ResultStateRaw = participant.RaceStateRaw,
                ResultState = StateName(participant.RaceStateRaw),
                PitStateRaw = participant.PitModeRaw,
                PitState = PitName(participant.PitModeRaw),
                Vehicle = participant.VehicleName,
                VehicleClass = participant.VehicleClass,
                FirstSeenUtc = life?.FirstSeen ?? at,
                LastSeenUtc = life?.LastSeen ?? at,
                Disappeared = life?.Disappeared ?? false
            };
        }

        private void SetPhase(HostRecorderPhase phase, HostRecorderUpdate update)
        {
            if (_phase == phase) return;
            _phase = phase;
            update.Events.Add("HOST_PHASE " + phase);
        }

        private void AddIssue(HostIssueSeverity severity, string code, string message)
        {
            if (_issues.Any(issue => issue.Code == code && issue.Message == message)) return;
            _issues.Add(new HostRecorderIssue { Severity = severity, Code = code, Message = message });
        }

        private void ResetForNextSession(bool preserveCaptureChain)
        {
            _sessionStarted = null;
            _currentAttemptNumber = 1;
            if (!preserveCaptureChain) _captureChainId = null;
            _lastEvidenceAt = DateTimeOffset.MinValue;
            _lastObservedAt = DateTimeOffset.MinValue;
            _previousSessionState = 0;
            _previousRaceState = 0;
            _initialParticipantKey = string.Empty;
            _latestQualifying = null;
            _qualifying = null;
            _latestGrid = null;
            _startingGrid = null;
            _latestRace = null;
            _raceResult = null;
            _latestSnapshot = null;
            _sessionBuild = 0;
            _sessionSharedMemoryVersion = 0;
            _sessionTrack = string.Empty;
            _sessionLayout = string.Empty;
            _sessionTypes.Clear();
            _sessionMetadata.Clear();
            _issues.Clear();
            _evidence.Clear();
            _lifecycle.Clear();
            _qualifyingGate.Reset();
            _gridGate.Reset();
            _resultGate.Reset();
            _phase = HostRecorderPhase.Waiting;
        }

        private static bool IsRelevant(uint sessionState)
            => sessionState == (uint)SessionState.Practice
                || sessionState == (uint)SessionState.Test
                || sessionState == (uint)SessionState.Qualify
                || sessionState == (uint)SessionState.FormationLap
                || sessionState == (uint)SessionState.Race;

        private static bool IsTerminal(uint state)
            => state == (uint)RaceState.Finished
                || state == (uint)RaceState.Disqualified
                || state == (uint)RaceState.Retired
                || state == (uint)RaceState.Dnf;

        private static bool IsRestartBoundary(TelemetrySnapshot? previous, TelemetrySnapshot current)
        {
            if (current.KnownGameState == GameState.InGameRestarting) return true;
            if (previous == null
                || previous.SessionStateRaw != (uint)SessionState.Race
                || current.SessionStateRaw != (uint)SessionState.Race)
            {
                return false;
            }

            bool stateReset = current.RaceStateRaw == (uint)RaceState.NotStarted
                && (previous.RaceStateRaw == (uint)RaceState.Racing || previous.RaceStateRaw == (uint)RaceState.Finished);
            if (stateReset) return true;

            int index = current.ViewedParticipantIndex;
            if (index < 0 || index >= current.Participants.Count || index >= previous.Participants.Count) return false;
            ParticipantSnapshot before = previous.Participants[index];
            ParticipantSnapshot after = current.Participants[index];
            return after.LapsCompleted < before.LapsCompleted
                || after.CurrentLap + 1 < before.CurrentLap;
        }

        private string DetermineAttemptStatus(string reason)
        {
            if (reason.IndexOf("RESTART", StringComparison.OrdinalIgnoreCase) >= 0) return "RESTARTED";
            if (_raceResult != null && _raceResult.Participants.Count > 0
                && _raceResult.Participants.All(participant => IsTerminal(participant.ResultStateRaw)))
            {
                return "FINISHED";
            }
            return _latestRace != null ? "ABORTED" : "INCOMPLETE";
        }

        private static string Signature(HostClassification classification)
            => string.Join("|", classification.Participants.Select(participant => participant.Position + ":" + participant.Slot + ":" + participant.Generation + ":" + participant.NameSnapshot + ":" + participant.LapsCompleted + ":" + participant.ResultStateRaw));

        private static string StateName(uint raw)
            => Enum.IsDefined(typeof(RaceState), raw) ? ((RaceState)raw).ToString().ToUpperInvariant() : "UNKNOWN_" + raw;

        private static string PitName(uint raw)
            => Enum.IsDefined(typeof(PitMode), raw) ? ((PitMode)raw).ToString().ToUpperInvariant() : "UNKNOWN_" + raw;

        private static bool IsVerified(HostSessionResult result)
            => result.Qualifying != null && result.Qualifying.Stable
                && result.StartingGrid != null && result.StartingGrid.Stable
                && result.RaceResult != null && result.RaceResult.Stable
                && result.Issues.Count == 0;

        private static HostClassification CloneClassification(HostClassification value)
            => new HostClassification
            {
                Kind = value.Kind,
                Source = value.Source,
                CapturedAtUtc = value.CapturedAtUtc,
                Stable = value.Stable,
                StableMilliseconds = value.StableMilliseconds,
                Participants = value.Participants.Select(CloneParticipant).ToList()
            };

        private static HostParticipantEvidence CloneParticipant(HostParticipantEvidence value)
            => new HostParticipantEvidence
            {
                Slot = value.Slot, Generation = value.Generation, Active = value.Active,
                NameSnapshot = value.NameSnapshot, Position = value.Position,
                LapsCompleted = value.LapsCompleted, CurrentLap = value.CurrentLap,
                CurrentSector = value.CurrentSector, LastLapSeconds = value.LastLapSeconds,
                BestLapSeconds = value.BestLapSeconds, ResultState = value.ResultState,
                ResultStateRaw = value.ResultStateRaw, PitState = value.PitState,
                PitStateRaw = value.PitStateRaw, Vehicle = value.Vehicle,
                VehicleClass = value.VehicleClass, FirstSeenUtc = value.FirstSeenUtc,
                LastSeenUtc = value.LastSeenUtc, Disappeared = value.Disappeared
            };

        private static HostRecorderIssue CloneIssue(HostRecorderIssue value)
            => new HostRecorderIssue { Severity = value.Severity, Code = value.Code, Message = value.Message };

        private sealed class StableClassificationGate
        {
            private string _signature = string.Empty;
            private DateTimeOffset _since;

            public TimeSpan Observe(string signature, DateTimeOffset at)
            {
                if (!string.Equals(signature, _signature, StringComparison.Ordinal))
                {
                    _signature = signature;
                    _since = at;
                    return TimeSpan.Zero;
                }
                return at - _since;
            }

            public void Reset()
            {
                _signature = string.Empty;
                _since = default;
            }
        }

        private sealed class ParticipantLifecycle
        {
            public bool Seen { get; set; }
            public bool Active { get; set; }
            public bool Disappeared { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Generation { get; set; }
            public uint LastRaceState { get; set; }
            public DateTimeOffset FirstSeen { get; set; }
            public DateTimeOffset LastSeen { get; set; }
        }
    }

    public static class HostSessionFingerprint
    {
        public static string Create(uint build, string track, string layout, string sessionTypes, DateTimeOffset startedAt, string participants, string hostInstallationId)
        {
            string input = build.ToString(CultureInfo.InvariantCulture) + "|" + track + "|" + layout + "|" + sessionTypes + "|"
                + startedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "|" + participants + "|" + hostInstallationId;
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        }
    }

    public static class HostEvidenceHasher
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static string Compute(IReadOnlyCollection<HostEvidenceSnapshot> evidence)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, JsonOptions);
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }
    }
}
