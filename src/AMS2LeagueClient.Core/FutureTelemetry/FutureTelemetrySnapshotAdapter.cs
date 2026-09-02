using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    /// <summary>
    /// Immutable facts produced from one parsed SHM snapshot. Persistence and
    /// compression deliberately remain outside this adapter.
    /// </summary>
    public sealed class FutureTelemetryCaptureBatch
    {
        public SessionMetadataSample? Metadata { get; set; }
        public List<RaceStoryEventSample> StoryEvents { get; } = new List<RaceStoryEventSample>();
        public TelemetryFrameSample? Frame { get; set; }
    }

    /// <summary>
    /// Maps the official SHM v14 model to the five future-telemetry tiers and
    /// detects raw facts independently from overlay presentation/suppression.
    /// This class performs no file, compression, hashing, network or DB work.
    /// </summary>
    public sealed class FutureTelemetrySnapshotAdapter
    {
        private const double PsiToKpa = 6.89475729316836;
        private const double MaximumHeadingRadians = Math.PI * 2.0;
        private const double MinimumDriverAcceleration = -327.68;
        private const double MaximumDriverAcceleration = 327.67;
        private const long MetadataHeartbeatMs = 60_000;
        private const long IncidentCooldownMs = 2_000;
        private readonly string _clientVersion;
        private readonly string _parserVersion;
        private readonly ActivityLocalParticipantResolver _localResolver;
        private readonly Dictionary<int, ParticipantTransitionState> _participants =
            new Dictionary<int, ParticipantTransitionState>();
        private readonly Dictionary<string, long> _incidentCooldowns =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private bool _started;
        private uint? _lastSessionState;
        private uint? _lastRaceState;
        private uint? _lastFlagColour;
        private int? _lastYellowFlagState;
        private int? _leaderRef;
        private double? _sessionFastestSeconds;
        private long _eventSequence;
        private long _lastMetadataElapsedMs = long.MinValue;
        private string _lastMetadataKey = string.Empty;
        private WeatherState? _lastMetadataWeather;
        private int _lastCollisionIndex = -1;
        private float _lastCollisionMagnitude;
        private uint _lastCrashState;
        private bool _raceStoryObserved;
        private bool _replayObserved;
        private bool _driverTelemetryObserved;
        private bool _incidentHighRateObserved;

        public FutureTelemetrySnapshotAdapter(
            string clientVersion,
            string parserVersion = "AMS2_SHM_V14",
            ActivityLocalParticipantResolver? localResolver = null)
        {
            _clientVersion = clientVersion ?? string.Empty;
            _parserVersion = parserVersion ?? string.Empty;
            _localResolver = localResolver ?? new ActivityLocalParticipantResolver();
        }

        public FutureTelemetryCaptureBatch Observe(TelemetrySnapshot snapshot, TelemetryCaptureStamp stamp)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (stamp == null) throw new ArgumentNullException(nameof(stamp));
            var batch = new FutureTelemetryCaptureBatch();

            Dictionary<int, ParticipantIdentity> current = ResolveParticipantIdentities(snapshot);
            bool first = !_started;
            if (first)
            {
                _started = true;
                batch.StoryEvents.Add(Event("SESSION_START", snapshot, stamp, null, "CAPTURE_SCOPE_OBSERVED"));
                batch.StoryEvents.Add(Event("SESSION_STATE", snapshot, stamp, null, "INITIAL_STATE"));
                if (snapshot.RaceStateRaw == (uint)RaceState.Racing)
                {
                    batch.StoryEvents.Add(Event("RACE_START", snapshot, stamp, null, "OBSERVED_RACING"));
                }
                AddInitialFlagFact(batch.StoryEvents, snapshot, stamp);
                foreach (ParticipantIdentity identity in current.Values.OrderBy(value => value.Participant.Index))
                {
                    batch.StoryEvents.Add(ParticipantActiveStateEvent(
                        snapshot,
                        stamp,
                        identity.ParticipantRef,
                        true,
                        "INITIAL_ACTIVE_PARTICIPANT",
                        identity));
                }
            }
            else
            {
                DetectRootFacts(batch.StoryEvents, snapshot, stamp);
            }

            ParticipantTransitionSummary transition = DetectParticipantFacts(
                batch.StoryEvents,
                snapshot,
                stamp,
                current,
                first);
            IncidentCandidateSample? incident = DetectIncidentCandidate(
                snapshot,
                stamp,
                current,
                transition);

            batch.Frame = BuildFrame(snapshot, stamp, current, transition, incident);
            _raceStoryObserved |= batch.StoryEvents.Count > 0;
            _replayObserved |= batch.Frame.Participants.Count > 0;
            _driverTelemetryObserved |= batch.Frame.LocalDriver != null
                && batch.Frame.LocalDriver.LocalParticipantResolved;
            _incidentHighRateObserved |= batch.Frame.IncidentCandidate != null;
            if (ShouldCaptureMetadata(snapshot, stamp, current, first))
            {
                batch.Metadata = BuildMetadata(snapshot, stamp, current, captureStarted: first, captureEnded: false);
            }

            _lastSessionState = snapshot.SessionStateRaw;
            _lastRaceState = snapshot.RaceStateRaw;
            _lastFlagColour = snapshot.HighestFlagColourRaw;
            _lastYellowFlagState = snapshot.YellowFlagStateRaw;
            UpdateParticipantStates(snapshot, current);
            return batch;
        }

        public FutureTelemetryCaptureBatch End(
            TelemetrySnapshot? lastSnapshot,
            TelemetryCaptureStamp stamp,
            string reason)
        {
            if (stamp == null) throw new ArgumentNullException(nameof(stamp));
            var batch = new FutureTelemetryCaptureBatch();
            if (!_started || lastSnapshot == null) return batch;
            Dictionary<int, ParticipantIdentity> current = ResolveParticipantIdentities(lastSnapshot);
            batch.StoryEvents.Add(Event(
                "SESSION_END",
                lastSnapshot,
                stamp,
                null,
                string.IsNullOrWhiteSpace(reason) ? "CAPTURE_ENDED" : reason));
            _raceStoryObserved = true;
            batch.Metadata = BuildMetadata(lastSnapshot, stamp, current, captureStarted: false, captureEnded: true);
            return batch;
        }

        private Dictionary<int, ParticipantIdentity> ResolveParticipantIdentities(TelemetrySnapshot snapshot)
        {
            var result = new Dictionary<int, ParticipantIdentity>();
            int limit = Math.Min(snapshot.NumParticipants, snapshot.Participants.Count);
            for (int index = 0; index < limit; index++)
            {
                ParticipantSnapshot participant = snapshot.Participants[index];
                if (!participant.IsActive) continue;
                if (!_participants.TryGetValue(participant.Index, out ParticipantTransitionState? previous))
                {
                    previous = new ParticipantTransitionState { Generation = 1 };
                    _participants.Add(participant.Index, previous);
                }
                else if ((!previous.Active || !string.Equals(previous.Name, participant.Name, StringComparison.Ordinal))
                    && previous.Seen)
                {
                    previous.Generation = checked(previous.Generation + 1);
                }

                result[participant.Index] = new ParticipantIdentity(
                    participant,
                    previous.Generation,
                    ParticipantRef(participant.Index, previous.Generation));
            }
            return result;
        }

        private ParticipantTransitionSummary DetectParticipantFacts(
            List<RaceStoryEventSample> events,
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp,
            Dictionary<int, ParticipantIdentity> current,
            bool first)
        {
            var summary = new ParticipantTransitionSummary();
            int? currentLeaderRef = null;
            uint bestPosition = uint.MaxValue;
            double? currentSessionFastest = null;

            foreach (ParticipantIdentity identity in current.Values)
            {
                ParticipantSnapshot participant = identity.Participant;
                if (participant.RacePosition > 0 && participant.RacePosition < bestPosition)
                {
                    bestPosition = participant.RacePosition;
                    currentLeaderRef = identity.ParticipantRef;
                }
                if (IsPositiveFinite(participant.BestLapTime)
                    && (!currentSessionFastest.HasValue || participant.BestLapTime < currentSessionFastest.Value))
                {
                    currentSessionFastest = participant.BestLapTime;
                }

                if (!_participants.TryGetValue(participant.Index, out ParticipantTransitionState? previous)
                    || !previous.Seen || !previous.Active || first)
                {
                    if (!first)
                    {
                        events.Add(ParticipantActiveStateEvent(
                            snapshot,
                            stamp,
                            identity.ParticipantRef,
                            true,
                            "OBSERVED_INACTIVE_TO_ACTIVE",
                            identity));
                    }
                    continue;
                }

                int positionChange = Math.Abs(ToInt(participant.RacePosition) - ToInt(previous.RacePosition));
                summary.PositionChangeMagnitude = Math.Max(summary.PositionChangeMagnitude, positionChange);
                if (participant.RacePosition > 0 && previous.RacePosition > 0
                    && participant.RacePosition != previous.RacePosition)
                {
                    RaceStoryEventSample fact = Event("POSITION_CHANGE", snapshot, stamp, identity, "OBSERVED_CLASSIFICATION_CHANGE");
                    fact.PositionBefore = ToInt(previous.RacePosition);
                    fact.PositionAfter = ToInt(participant.RacePosition);
                    events.Add(fact);
                    bool wasPodium = previous.RacePosition >= 1 && previous.RacePosition <= 3;
                    bool isPodium = participant.RacePosition >= 1 && participant.RacePosition <= 3;
                    if (!wasPodium && isPodium)
                    {
                        events.Add(Event("PODIUM_ENTRY", snapshot, stamp, identity, "OBSERVED_TOP_THREE_ENTRY"));
                    }
                    else if (wasPodium && !isPodium)
                    {
                        events.Add(Event("PODIUM_EXIT", snapshot, stamp, identity, "OBSERVED_TOP_THREE_EXIT"));
                    }
                }

                if (participant.LapsCompleted > previous.LapsCompleted)
                {
                    RaceStoryEventSample lap = Event("LAP_COMPLETE", snapshot, stamp, identity,
                        participant.LapsCompleted == previous.LapsCompleted + 1
                            ? "OBSERVED_LAP_INCREMENT"
                            : "OBSERVED_LAP_COUNTER_GAP");
                    lap.LapTimeMs = SecondsToMilliseconds(participant.LastLapTime);
                    events.Add(lap);
                }

                if (IsPositiveFinite(participant.BestLapTime)
                    && IsPositiveFinite(previous.BestLapTime)
                    && participant.BestLapTime < previous.BestLapTime - 0.0005f)
                {
                    RaceStoryEventSample personalBest = Event(
                        "PERSONAL_BEST",
                        snapshot,
                        stamp,
                        identity,
                        "OBSERVED_PARTICIPANT_BEST_IMPROVEMENT");
                    personalBest.LapTimeMs = SecondsToMilliseconds(participant.BestLapTime);
                    events.Add(personalBest);
                }

                bool wasInPit = IsPitLaneOrBox(previous.PitModeRaw);
                bool isInPit = IsPitLaneOrBox(participant.PitModeRaw);
                if (!wasInPit && isInPit)
                {
                    events.Add(Event("PIT_ENTRY", snapshot, stamp, identity, "OBSERVED_PIT_MODE_TRANSITION"));
                }
                else if (wasInPit && !isInPit)
                {
                    events.Add(Event("PIT_EXIT", snapshot, stamp, identity, "OBSERVED_PIT_MODE_TRANSITION"));
                }

                if (participant.PitScheduleRaw != previous.PitScheduleRaw)
                {
                    if (participant.PitScheduleRaw == (uint)PitSchedule.DriveThrough)
                    {
                        RaceStoryEventSample value = Event("DRIVE_THROUGH", snapshot, stamp, identity, "OBSERVED_PIT_SCHEDULE");
                        value.PenaltyTypeRaw = ToInt(participant.PitScheduleRaw);
                        events.Add(value);
                    }
                    else if (participant.PitScheduleRaw == (uint)PitSchedule.StopGo)
                    {
                        RaceStoryEventSample value = Event("STOP_GO", snapshot, stamp, identity, "OBSERVED_PIT_SCHEDULE");
                        value.PenaltyTypeRaw = ToInt(participant.PitScheduleRaw);
                        events.Add(value);
                    }
                    else if (IsPenaltySchedule(previous.PitScheduleRaw)
                        && participant.PitScheduleRaw == (uint)PitSchedule.None)
                    {
                        RaceStoryEventSample value = Event("PENALTY_CLEARED", snapshot, stamp, identity, "OBSERVED_PIT_SCHEDULE_CLEAR");
                        value.PenaltyTypeRaw = ToInt(previous.PitScheduleRaw);
                        events.Add(value);
                    }
                }

                if (participant.RaceStateRaw != previous.RaceStateRaw)
                {
                    string? terminal = TerminalFact(participant.RaceStateRaw);
                    if (terminal != null)
                    {
                        events.Add(Event(terminal, snapshot, stamp, identity, "OBSERVED_RESULT_STATE"));
                        summary.TerminalTransitionRefs.Add(identity.ParticipantRef);
                    }
                }

                double distance = Distance(previous.WorldPosition, participant.WorldPosition);
                if (previous.WorldPositionUsable && IsUsablePosition(participant.WorldPosition)
                    && distance >= 30.0)
                {
                    summary.AbruptWorldPositionRefs.Add(identity.ParticipantRef);
                }
            }

            foreach (KeyValuePair<int, ParticipantTransitionState> pair in _participants)
            {
                if (!pair.Value.Seen || !pair.Value.Active || current.ContainsKey(pair.Key)) continue;
                summary.ParticipantDisappeared = true;
                int disappearedRef = ParticipantRef(pair.Key, pair.Value.Generation);
                summary.DisappearedRefs.Add(disappearedRef);
                events.Add(ParticipantActiveStateEvent(
                    snapshot,
                    stamp,
                    disappearedRef,
                    false,
                    "OBSERVED_ACTIVE_TO_INACTIVE"));
            }

            if (!first && currentLeaderRef.HasValue && _leaderRef.HasValue && currentLeaderRef != _leaderRef)
            {
                ParticipantIdentity? leader = current.Values.FirstOrDefault(value => value.ParticipantRef == currentLeaderRef.Value);
                events.Add(Event("LEADER_CHANGE", snapshot, stamp, leader, "OBSERVED_LEADER_REFERENCE_CHANGE"));
            }
            _leaderRef = currentLeaderRef;

            if (!first && currentSessionFastest.HasValue && _sessionFastestSeconds.HasValue
                && currentSessionFastest.Value < _sessionFastestSeconds.Value - 0.0005)
            {
                ParticipantIdentity? fastest = current.Values
                    .Where(value => IsPositiveFinite(value.Participant.BestLapTime))
                    .OrderBy(value => value.Participant.BestLapTime)
                    .FirstOrDefault();
                RaceStoryEventSample fact = Event(
                    "SESSION_FASTEST_LAP",
                    snapshot,
                    stamp,
                    fastest,
                    "OBSERVED_SESSION_BEST_IMPROVEMENT");
                fact.LapTimeMs = SecondsToMilliseconds((float)currentSessionFastest.Value);
                events.Add(fact);
            }
            _sessionFastestSeconds = currentSessionFastest;
            return summary;
        }

        private void DetectRootFacts(
            List<RaceStoryEventSample> events,
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp)
        {
            if (_lastSessionState.HasValue && _lastSessionState.Value != snapshot.SessionStateRaw)
            {
                events.Add(Event("SESSION_STATE", snapshot, stamp, null, "OBSERVED_STATE_TRANSITION"));
            }
            if (_lastRaceState.HasValue && _lastRaceState.Value != snapshot.RaceStateRaw)
            {
                if (snapshot.RaceStateRaw == (uint)RaceState.Racing)
                {
                    events.Add(Event("RACE_START", snapshot, stamp, null, "OBSERVED_RACE_STATE_TRANSITION"));
                }
                if (snapshot.RaceStateRaw == (uint)RaceState.Finished)
                {
                    events.Add(Event("FINISH", snapshot, stamp, null, "OBSERVED_RACE_STATE_TRANSITION"));
                }
            }

            if (!_lastFlagColour.HasValue || _lastFlagColour.Value != snapshot.HighestFlagColourRaw)
            {
                AddFlagFact(events, snapshot, stamp, snapshot.HighestFlagColourRaw);
            }
            if ((!_lastYellowFlagState.HasValue || _lastYellowFlagState.Value != snapshot.YellowFlagStateRaw)
                && IsFullCourseYellow(snapshot.YellowFlagStateRaw))
            {
                events.Add(Event("FULL_COURSE_YELLOW", snapshot, stamp, null, "OBSERVED_YELLOW_FLAG_STATE"));
            }
            else if (_lastYellowFlagState.HasValue
                && IsFullCourseYellow(_lastYellowFlagState.Value)
                && !IsFullCourseYellow(snapshot.YellowFlagStateRaw))
            {
                events.Add(Event("FULL_COURSE_YELLOW_END", snapshot, stamp, null, "OBSERVED_YELLOW_FLAG_STATE_EXIT"));
            }
        }

        private void AddInitialFlagFact(
            List<RaceStoryEventSample> events,
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp)
        {
            AddFlagFact(events, snapshot, stamp, snapshot.HighestFlagColourRaw);
            if (IsFullCourseYellow(snapshot.YellowFlagStateRaw))
            {
                events.Add(Event("FULL_COURSE_YELLOW", snapshot, stamp, null, "INITIAL_YELLOW_FLAG_STATE"));
            }
        }

        private void AddFlagFact(
            List<RaceStoryEventSample> events,
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp,
            uint raw)
        {
            string? type = raw switch
            {
                (uint)FlagColour.Yellow => "YELLOW",
                (uint)FlagColour.DoubleYellow => "DOUBLE_YELLOW",
                (uint)FlagColour.Red => "RED",
                _ => null
            };
            if (type != null)
            {
                events.Add(Event(type, snapshot, stamp, null, "OBSERVED_HIGHEST_FLAG_COLOUR"));
            }
        }

        private IncidentCandidateSample? DetectIncidentCandidate(
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp,
            Dictionary<int, ParticipantIdentity> current,
            ParticipantTransitionSummary transition)
        {
            ParticipantIdentity? viewed = current.TryGetValue(snapshot.ViewedParticipantIndex, out ParticipantIdentity? value)
                ? value
                : null;
            ViewedVehicleTelemetrySnapshot? vehicle = snapshot.ViewedVehicleTelemetry;

            if (viewed != null && vehicle != null)
            {
                bool collisionChanged = vehicle.LastOpponentCollisionMagnitude > 0
                    && (vehicle.LastOpponentCollisionIndex != _lastCollisionIndex
                        || vehicle.LastOpponentCollisionMagnitude > _lastCollisionMagnitude + 0.1f);
                _lastCollisionIndex = vehicle.LastOpponentCollisionIndex;
                _lastCollisionMagnitude = vehicle.LastOpponentCollisionMagnitude;
                if (collisionChanged)
                {
                    var related = new List<int> { viewed.ParticipantRef };
                    if (current.TryGetValue(vehicle.LastOpponentCollisionIndex, out ParticipantIdentity? opponent))
                    {
                        related.Add(opponent.ParticipantRef);
                    }
                    IncidentCandidateSample? candidate = Candidate("COLLISION_MAGNITUDE_CHANGE", related, stamp.SessionElapsedMs);
                    if (candidate != null) return candidate;
                }

                if (vehicle.CrashStateRaw != _lastCrashState && vehicle.CrashStateRaw != 0)
                {
                    IncidentCandidateSample? candidate = Candidate(
                        "CRASH_STATE_CHANGE",
                        new[] { viewed.ParticipantRef },
                        stamp.SessionElapsedMs);
                    _lastCrashState = vehicle.CrashStateRaw;
                    if (candidate != null) return candidate;
                }
                _lastCrashState = vehicle.CrashStateRaw;
            }

            if (transition.TerminalTransitionRefs.Count > 0)
            {
                IncidentCandidateSample? candidate = Candidate(
                    "ABRUPT_RESULT_STATE_CHANGE",
                    transition.TerminalTransitionRefs,
                    stamp.SessionElapsedMs);
                if (candidate != null) return candidate;
            }
            if (transition.DisappearedRefs.Count > 0)
            {
                IncidentCandidateSample? candidate = Candidate(
                    "PARTICIPANT_DISAPPEARANCE",
                    transition.DisappearedRefs,
                    stamp.SessionElapsedMs);
                if (candidate != null) return candidate;
            }
            if (transition.AbruptWorldPositionRefs.Count > 0)
            {
                IncidentCandidateSample? candidate = Candidate(
                    "ABRUPT_WORLD_POSITION_CHANGE",
                    transition.AbruptWorldPositionRefs,
                    stamp.SessionElapsedMs);
                if (candidate != null) return candidate;
            }

            if (viewed != null && IsUsablePosition(viewed.Participant.WorldPosition)
                && viewed.Participant.SpeedMetresPerSecond >= 10f)
            {
                ParticipantIdentity? near = current.Values
                    .Where(item => item.ParticipantRef != viewed.ParticipantRef
                        && IsUsablePosition(item.Participant.WorldPosition)
                        && Distance(viewed.Participant.WorldPosition, item.Participant.WorldPosition) <= 2.5)
                    .OrderBy(item => Distance(viewed.Participant.WorldPosition, item.Participant.WorldPosition))
                    .FirstOrDefault();
                if (near != null && Math.Abs(viewed.Participant.SpeedMetresPerSecond - near.Participant.SpeedMetresPerSecond) >= 3f)
                {
                    return Candidate(
                        "HIGH_CLOSING_PROXIMITY",
                        new[] { viewed.ParticipantRef, near.ParticipantRef },
                        stamp.SessionElapsedMs);
                }
            }
            return null;
        }

        private IncidentCandidateSample? Candidate(string trigger, IEnumerable<int> related, long elapsedMs)
        {
            int[] refs = related.Distinct().OrderBy(value => value).Take(8).ToArray();
            if (refs.Length == 0) return null;
            string key = trigger + ":" + string.Join(",", refs.Select(value => value.ToString(CultureInfo.InvariantCulture)));
            if (_incidentCooldowns.TryGetValue(key, out long last) && elapsedMs - last < IncidentCooldownMs)
            {
                return null;
            }
            _incidentCooldowns[key] = elapsedMs;
            foreach (string old in _incidentCooldowns
                .Where(pair => elapsedMs - pair.Value > IncidentCooldownMs * 4)
                .Select(pair => pair.Key)
                .ToArray())
            {
                _incidentCooldowns.Remove(old);
            }
            return new IncidentCandidateSample
            {
                CandidateId = NextEventId("INCIDENT_CANDIDATE", elapsedMs),
                TriggerCode = trigger,
                RelatedParticipantRefs = refs
            };
        }

        private TelemetryFrameSample BuildFrame(
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp,
            Dictionary<int, ParticipantIdentity> current,
            ParticipantTransitionSummary transition,
            IncidentCandidateSample? incident)
        {
            ActivityLocalParticipantResolution local = _localResolver.Resolve(snapshot);
            current.TryGetValue(snapshot.ViewedParticipantIndex, out ParticipantIdentity? viewedIdentity);
            ViewedVehicleTelemetrySnapshot? viewedVehicle = snapshot.ViewedVehicleTelemetry;
            ParticipantIdentity? collisionOpponent = viewedVehicle == null
                ? null
                : (current.TryGetValue(viewedVehicle.LastOpponentCollisionIndex, out ParticipantIdentity? opponent)
                    ? opponent
                    : null);
            DriverTelemetrySample? driver = null;
            if (local.IsValid && local.Participant != null
                && current.TryGetValue(local.Participant.Index, out ParticipantIdentity? identity)
                && IsRootVehicleMatch(snapshot, local.Participant)
                && snapshot.ViewedVehicleTelemetry != null)
            {
                driver = BuildDriver(snapshot, identity, snapshot.ViewedVehicleTelemetry);
            }

            return new TelemetryFrameSample
            {
                CapturedAtUtc = stamp.CapturedAtUtc.ToUniversalTime(),
                SessionElapsedMs = stamp.SessionElapsedMs,
                Participants = current.Values
                    .OrderBy(value => value.Participant.Index)
                    .Select(BuildReplayParticipant)
                    .ToArray(),
                LocalDriver = driver,
                RaceStateRaw = ToInt(snapshot.RaceStateRaw),
                FlagColourRaw = ToInt(snapshot.HighestFlagColourRaw),
                FlagReasonRaw = ToInt(snapshot.HighestFlagReasonRaw),
                ParticipantDisappeared = transition.ParticipantDisappeared,
                PositionChangeMagnitude = transition.PositionChangeMagnitude,
                IncidentCandidate = incident,
                YellowFlagStateRaw = snapshot.YellowFlagStateRaw,
                ViewedParticipantRef = viewedIdentity?.ParticipantRef,
                CollisionOpponentSlotRaw = viewedVehicle?.LastOpponentCollisionIndex,
                CollisionOpponentRef = collisionOpponent?.ParticipantRef,
                CollisionMagnitude = viewedVehicle == null
                    ? null
                    : FiniteOrNull(viewedVehicle.LastOpponentCollisionMagnitude),
                CrashStateRaw = viewedVehicle == null ? (int?)null : ToInt(viewedVehicle.CrashStateRaw)
            };
        }

        private static ReplayParticipantSample BuildReplayParticipant(ParticipantIdentity identity)
        {
            ParticipantSnapshot participant = identity.Participant;
            return new ReplayParticipantSample
            {
                ParticipantRef = identity.ParticipantRef,
                Slot = participant.Index,
                Generation = identity.Generation,
                NameSnapshot = participant.Name,
                VehicleRef = participant.VehicleName,
                VehicleClassRef = participant.VehicleClass,
                Lap = ToInt(participant.CurrentLap),
                LapDistanceMeters = NonNegativeFiniteOrNull(participant.CurrentLapDistance),
                RacePosition = participant.RacePosition > 0 ? ToInt(participant.RacePosition) : (int?)null,
                WorldX = FiniteOrNull(participant.WorldPosition.X),
                WorldY = FiniteOrNull(participant.WorldPosition.Y),
                WorldZ = FiniteOrNull(participant.WorldPosition.Z),
                RaceStateRaw = ToInt(participant.RaceStateRaw),
                PitStateRaw = ToInt(participant.PitModeRaw),
                HeadingRadians = FiniteInRangeOrNull(
                    participant.Orientation.Y,
                    -MaximumHeadingRadians,
                    MaximumHeadingRadians),
                SpeedMetersPerSecond = NonNegativeFiniteOrNull(participant.SpeedMetresPerSecond),
                LapsCompleted = ToInt(participant.LapsCompleted),
                SectorRaw = participant.CurrentSector,
                CurrentSector1TimeSeconds = NonNegativeFiniteOrNull(participant.CurrentSector1Time),
                CurrentSector2TimeSeconds = NonNegativeFiniteOrNull(participant.CurrentSector2Time),
                CurrentSector3TimeSeconds = NonNegativeFiniteOrNull(participant.CurrentSector3Time),
                LapInvalidated = participant.LapInvalidated,
                OrientationRawX = FiniteOrNull(participant.Orientation.X),
                OrientationRawY = FiniteOrNull(participant.Orientation.Y),
                OrientationRawZ = FiniteOrNull(participant.Orientation.Z),
                NationalityRaw = ToInt(participant.NationalityRaw),
                PitScheduleRaw = ToInt(participant.PitScheduleRaw),
                HighestFlagColourRaw = ToInt(participant.HighestFlagColourRaw),
                HighestFlagReasonRaw = ToInt(participant.HighestFlagReasonRaw),
                BestLapTimeSeconds = NonNegativeFiniteOrNull(participant.BestLapTime),
                LastLapTimeSeconds = NonNegativeFiniteOrNull(participant.LastLapTime),
                FastestSector1TimeSeconds = NonNegativeFiniteOrNull(participant.FastestSector1Time),
                FastestSector2TimeSeconds = NonNegativeFiniteOrNull(participant.FastestSector2Time),
                FastestSector3TimeSeconds = NonNegativeFiniteOrNull(participant.FastestSector3Time),
                IsActive = participant.IsActive
            };
        }

        private static DriverTelemetrySample BuildDriver(
            TelemetrySnapshot snapshot,
            ParticipantIdentity identity,
            ViewedVehicleTelemetrySnapshot vehicle)
        {
            ParticipantSnapshot participant = identity.Participant;
            TyreTelemetrySnapshot[] tyres = vehicle.Tyres.OrderBy(value => value.Index).Take(4).ToArray();
            var sample = new DriverTelemetrySample
            {
                LocalParticipantResolved = true,
                SourceParticipantRef = identity.ParticipantRef,
                DriverRef = identity.ParticipantRef,
                Lap = ToInt(participant.CurrentLap),
                Sector = participant.CurrentSector,
                LapDistanceMeters = NonNegativeFiniteOrNull(participant.CurrentLapDistance),
                WorldX = FiniteOrNull(participant.WorldPosition.X),
                WorldY = FiniteOrNull(participant.WorldPosition.Y),
                WorldZ = FiniteOrNull(participant.WorldPosition.Z),
                SpeedMetersPerSecond = NonNegativeFiniteOrNull(vehicle.SpeedMetresPerSecond),
                Rpm = NonNegativeFiniteOrNull(vehicle.Rpm),
                GearRaw = vehicle.Gear,
                Throttle = UnitIntervalOrNull(vehicle.Throttle),
                Brake = UnitIntervalOrNull(vehicle.Brake),
                Steering = SignedUnitIntervalOrNull(vehicle.Steering),
                Clutch = UnitIntervalOrNull(vehicle.Clutch),
                UnfilteredThrottle = UnitIntervalOrNull(vehicle.UnfilteredThrottle),
                UnfilteredBrake = UnitIntervalOrNull(vehicle.UnfilteredBrake),
                UnfilteredSteering = SignedUnitIntervalOrNull(vehicle.UnfilteredSteering),
                UnfilteredClutch = UnitIntervalOrNull(vehicle.UnfilteredClutch),
                LongitudinalAccelerationMetersPerSecondSquared = FiniteInRangeOrNull(
                    vehicle.LocalAcceleration.Z,
                    MinimumDriverAcceleration,
                    MaximumDriverAcceleration),
                LateralAccelerationMetersPerSecondSquared = FiniteInRangeOrNull(
                    vehicle.LocalAcceleration.X,
                    MinimumDriverAcceleration,
                    MaximumDriverAcceleration),
                VerticalAccelerationMetersPerSecondSquared = FiniteInRangeOrNull(
                    vehicle.LocalAcceleration.Y,
                    MinimumDriverAcceleration,
                    MaximumDriverAcceleration),
                HeadingRadians = FiniteInRangeOrNull(
                    vehicle.Orientation.Y,
                    -MaximumHeadingRadians,
                    MaximumHeadingRadians),
                VelocityX = FiniteOrNull(vehicle.WorldVelocity.X),
                VelocityY = FiniteOrNull(vehicle.WorldVelocity.Y),
                VelocityZ = FiniteOrNull(vehicle.WorldVelocity.Z),
                FuelLevelRatio = UnitIntervalOrNull(vehicle.FuelLevel),
                FuelCapacityLiters = NonNegativeFiniteOrNull(vehicle.FuelCapacityLitres),
                FuelLiters = ProductOrNull(vehicle.FuelLevel, vehicle.FuelCapacityLitres),
                BrakeBias = UnitIntervalOrNull(vehicle.BrakeBias),
                EngineDamage = UnitIntervalOrNull(vehicle.EngineDamage),
                AeroDamage = UnitIntervalOrNull(vehicle.AeroDamage),
                SuspensionDamage = tyres.Length == 0
                    ? (double?)null
                    : tyres.Where(value => IsFinite(value.SuspensionDamage)).Select(value => (double)value.SuspensionDamage).DefaultIfEmpty().Max(),
                TyreTemperaturesCelsius = FourTyres(tyres, value => value.TemperatureCelsius, 1.0),
                TyrePressuresKpa = FourTyres(tyres, value => value.AirPressurePsi, PsiToKpa),
                TyreWear = FourTyres(tyres, value => value.Wear, 1.0),
                TrackTemperatureCelsius = FiniteInRangeOrNull(snapshot.TrackTemperature, -50, 150),
                AmbientTemperatureCelsius = FiniteOrNull(snapshot.AmbientTemperature),
                RainDensity = UnitIntervalOrNull(snapshot.RainDensity),
                PitStateRaw = ToInt(participant.PitModeRaw),
                LapValid = !snapshot.LapInvalidated && !participant.LapInvalidated,
                CurrentLapTimeMs = SecondsToMilliseconds(snapshot.CurrentTime)
            };
            AddDriverRawValues(sample, snapshot, participant, vehicle, tyres);
            return sample;
        }

        private static void AddDriverRawValues(
            DriverTelemetrySample sample,
            TelemetrySnapshot snapshot,
            ParticipantSnapshot participant,
            ViewedVehicleTelemetrySnapshot vehicle,
            TyreTelemetrySnapshot[] tyres)
        {
            SetRaw(sample, "rootLapInvalidated", snapshot.LapInvalidated);
            SetRaw(sample, "participantLapInvalidated", participant.LapInvalidated);
            SetRaw(sample, "bestLapTimeSeconds", snapshot.BestLapTime);
            SetRaw(sample, "lastLapTimeSeconds", snapshot.LastLapTime);
            SetRaw(sample, "splitTimeAheadSeconds", snapshot.SplitTimeAhead);
            SetRaw(sample, "splitTimeBehindSeconds", snapshot.SplitTimeBehind);
            SetRaw(sample, "splitTimeSeconds", snapshot.SplitTime);
            SetRaw(sample, "personalFastestLapTimeSeconds", snapshot.PersonalFastestLapTime);
            SetRaw(sample, "worldFastestLapTimeSeconds", snapshot.WorldFastestLapTime);
            SetRaw(sample, "currentSector1TimeSeconds", snapshot.CurrentSector1Time);
            SetRaw(sample, "currentSector2TimeSeconds", snapshot.CurrentSector2Time);
            SetRaw(sample, "currentSector3TimeSeconds", snapshot.CurrentSector3Time);
            SetRaw(sample, "fastestSector1TimeSeconds", snapshot.FastestSector1Time);
            SetRaw(sample, "fastestSector2TimeSeconds", snapshot.FastestSector2Time);
            SetRaw(sample, "fastestSector3TimeSeconds", snapshot.FastestSector3Time);
            SetRaw(sample, "personalFastestSector1TimeSeconds", snapshot.PersonalFastestSector1Time);
            SetRaw(sample, "personalFastestSector2TimeSeconds", snapshot.PersonalFastestSector2Time);
            SetRaw(sample, "personalFastestSector3TimeSeconds", snapshot.PersonalFastestSector3Time);
            SetRaw(sample, "worldFastestSector1TimeSeconds", snapshot.WorldFastestSector1Time);
            SetRaw(sample, "worldFastestSector2TimeSeconds", snapshot.WorldFastestSector2Time);
            SetRaw(sample, "worldFastestSector3TimeSeconds", snapshot.WorldFastestSector3Time);
            SetRaw(sample, "rootPitModeRaw", ToInt(snapshot.RootPitModeRaw));
            SetRaw(sample, "rootPitScheduleRaw", ToInt(snapshot.RootPitScheduleRaw));
            SetRaw(sample, "participantPitScheduleRaw", ToInt(participant.PitScheduleRaw));
            SetRaw(sample, "highestFlagColourRaw", ToInt(snapshot.HighestFlagColourRaw));
            SetRaw(sample, "highestFlagReasonRaw", ToInt(snapshot.HighestFlagReasonRaw));
            SetRaw(sample, "participantHighestFlagColourRaw", ToInt(participant.HighestFlagColourRaw));
            SetRaw(sample, "participantHighestFlagReasonRaw", ToInt(participant.HighestFlagReasonRaw));
            SetRaw(sample, "carFlagsRaw", ToInt(vehicle.CarFlagsRaw));
            SetRaw(sample, "oilTemperatureCelsius", vehicle.OilTemperatureCelsius);
            SetRaw(sample, "oilPressureKPa", vehicle.OilPressureKPa);
            SetRaw(sample, "waterTemperatureCelsius", vehicle.WaterTemperatureCelsius);
            SetRaw(sample, "waterPressureKPa", vehicle.WaterPressureKPa);
            SetRaw(sample, "fuelPressureKPa", vehicle.FuelPressureKPa);
            SetRaw(sample, "maxRpm", vehicle.MaxRpm);
            SetRaw(sample, "numGears", vehicle.NumGears);
            SetRaw(sample, "odometerKilometres", vehicle.OdometerKilometres);
            SetRaw(sample, "antiLockActive", vehicle.AntiLockActive);
            SetRaw(sample, "lastOpponentCollisionIndex", vehicle.LastOpponentCollisionIndex);
            SetRaw(sample, "lastOpponentCollisionMagnitude", vehicle.LastOpponentCollisionMagnitude);
            SetRaw(sample, "boostActive", vehicle.BoostActive);
            SetRaw(sample, "boostAmount", vehicle.BoostAmount);
            SetVectorRaw(sample, "orientationRaw", vehicle.Orientation);
            SetVectorRaw(sample, "localVelocityRaw", vehicle.LocalVelocity);
            SetVectorRaw(sample, "worldVelocityRaw", vehicle.WorldVelocity);
            SetVectorRaw(sample, "angularVelocityRaw", vehicle.AngularVelocity);
            SetVectorRaw(sample, "localAccelerationRaw", vehicle.LocalAcceleration);
            SetVectorRaw(sample, "worldAccelerationRaw", vehicle.WorldAcceleration);
            SetVectorRaw(sample, "extentsCentreRaw", vehicle.ExtentsCentre);
            SetRaw(sample, "engineSpeedRadiansPerSecond", vehicle.EngineSpeedRadiansPerSecond);
            SetRaw(sample, "engineTorqueNewtonMetres", vehicle.EngineTorqueNewtonMetres);
            SetRaw(sample, "frontWingRaw", vehicle.FrontWing);
            SetRaw(sample, "rearWingRaw", vehicle.RearWing);
            SetRaw(sample, "handBrake", vehicle.HandBrake);
            SetRaw(sample, "crashStateRaw", ToInt(vehicle.CrashStateRaw));
            SetRaw(sample, "turboBoostPressure", vehicle.TurboBoostPressure);
            SetRaw(sample, "drsStateRaw", ToInt(vehicle.DrsStateRaw));
            SetRaw(sample, "antiLockSetting", vehicle.AntiLockSetting);
            SetRaw(sample, "tractionControlSetting", vehicle.TractionControlSetting);
            SetRaw(sample, "ersDeploymentModeRaw", vehicle.ErsDeploymentModeRaw);
            SetRaw(sample, "ersAutoModeEnabled", vehicle.ErsAutoModeEnabled);
            SetRaw(sample, "clutchTemperatureKelvin", vehicle.ClutchTemperatureKelvin);
            SetRaw(sample, "clutchWear", vehicle.ClutchWear);
            SetRaw(sample, "clutchOverheated", vehicle.ClutchOverheated);
            SetRaw(sample, "clutchSlipping", vehicle.ClutchSlipping);
            SetRaw(sample, "launchStageRaw", vehicle.LaunchStageRaw);
            SetRaw(sample, "currentTimeSecondsRaw", snapshot.CurrentTime);
            SetRaw(sample, "sequenceNumberRaw", snapshot.SequenceNumber);

            AddWheelRaw(sample, tyres, "tyreFlags", value => value.FlagsRaw);
            AddWheelRaw(sample, tyres, "tyreTerrain", value => value.TerrainRaw);
            AddWheelRaw(sample, tyres, "tyreLocalY", value => value.LocalY);
            AddWheelRaw(sample, tyres, "tyreRevolutionsPerSecond", value => value.RevolutionsPerSecond);
            AddWheelRaw(sample, tyres, "tyreHeightAboveGround", value => value.HeightAboveGround);
            AddWheelRaw(sample, tyres, "tyreBrakeDamage", value => value.BrakeDamage);
            AddWheelRaw(sample, tyres, "tyreSuspensionDamage", value => value.SuspensionDamage);
            AddWheelRaw(sample, tyres, "tyreBrakeTemperatureCelsius", value => value.BrakeTemperatureCelsius);
            AddWheelRaw(sample, tyres, "tyreTreadTemperatureKelvin", value => value.TreadTemperatureKelvin);
            AddWheelRaw(sample, tyres, "tyreLayerTemperatureKelvin", value => value.LayerTemperatureKelvin);
            AddWheelRaw(sample, tyres, "tyreCarcassTemperatureKelvin", value => value.CarcassTemperatureKelvin);
            AddWheelRaw(sample, tyres, "tyreRimTemperatureKelvin", value => value.RimTemperatureKelvin);
            AddWheelRaw(sample, tyres, "tyreInternalAirTemperatureKelvin", value => value.InternalAirTemperatureKelvin);
            AddWheelRaw(sample, tyres, "wheelLocalPositionY", value => value.WheelLocalPositionY);
            AddWheelRaw(sample, tyres, "tyreSuspensionTravelMetres", value => value.SuspensionTravelMetres);
            AddWheelRaw(sample, tyres, "tyreSuspensionVelocity", value => value.SuspensionVelocity);
            AddWheelRaw(sample, tyres, "tyreAirPressurePsi", value => value.AirPressurePsi);
            AddWheelText(sample, tyres, "tyreCompound", value => value.Compound);
            AddWheelRaw(sample, tyres, "tyreLeftTemperatureCelsius", value => value.LeftTemperatureCelsius);
            AddWheelRaw(sample, tyres, "tyreCenterTemperatureCelsius", value => value.CenterTemperatureCelsius);
            AddWheelRaw(sample, tyres, "tyreRightTemperatureCelsius", value => value.RightTemperatureCelsius);
            AddWheelRaw(sample, tyres, "rideHeightCentimetres", value => value.RideHeightCentimetres);
        }

        private bool ShouldCaptureMetadata(
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp,
            Dictionary<int, ParticipantIdentity> current,
            bool first)
        {
            string key = MetadataKey(snapshot, current);
            var weather = new WeatherState(snapshot);
            bool structuralChange = !string.Equals(key, _lastMetadataKey, StringComparison.Ordinal);
            bool weatherChange = !_lastMetadataWeather.HasValue || _lastMetadataWeather.Value.SignificantlyDiffers(weather);
            bool heartbeat = _lastMetadataElapsedMs == long.MinValue
                || stamp.SessionElapsedMs - _lastMetadataElapsedMs >= MetadataHeartbeatMs;
            if (!first && !structuralChange && !weatherChange && !heartbeat) return false;
            _lastMetadataKey = key;
            _lastMetadataWeather = weather;
            _lastMetadataElapsedMs = stamp.SessionElapsedMs;
            return true;
        }

        private SessionMetadataSample BuildMetadata(
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp,
            Dictionary<int, ParticipantIdentity> current,
            bool captureStarted,
            bool captureEnded)
        {
            var sample = new SessionMetadataSample
            {
                CapturedAtUtc = stamp.CapturedAtUtc.ToUniversalTime(),
                SessionElapsedMs = stamp.SessionElapsedMs,
                GameBuild = ToInt(snapshot.BuildVersion),
                SharedMemoryVersion = snapshot.Version,
                ClientVersion = _clientVersion,
                ParserVersion = _parserVersion,
                // Track/Layout remain stable machine keys. Translations are retained separately.
                Track = EmptyToNull(snapshot.TrackLocation, snapshot.TranslatedTrackLocation),
                Layout = EmptyToNull(snapshot.TrackVariation, snapshot.TranslatedTrackVariation),
                RawTrack = EmptyToNull(snapshot.TrackLocation, string.Empty),
                RawLayout = EmptyToNull(snapshot.TrackVariation, string.Empty),
                TranslatedTrack = EmptyToNull(snapshot.TranslatedTrackLocation, string.Empty),
                TranslatedLayout = EmptyToNull(snapshot.TranslatedTrackVariation, string.Empty),
                TrackLengthMeters = PositiveFiniteOrNull(snapshot.TrackLength),
                SessionType = snapshot.KnownSessionState?.ToString().ToUpperInvariant()
                    ?? ("RAW_" + snapshot.SessionStateRaw.ToString(CultureInfo.InvariantCulture)),
                ClockSource = stamp.ClockSource,
                TimedSessionDurationMs = PositiveFiniteMilliseconds(snapshot.SessionDuration * 60f),
                EventTimeRemainingMs = NonNegativeFiniteMilliseconds(snapshot.EventTimeRemaining),
                JoinedMidSession = captureStarted && snapshot.RaceStateRaw == (uint)RaceState.Racing,
                SessionStartOffsetStatus = TelemetryCapabilityState.NOT_EXPOSED,
                SessionDurationMinutes = PositiveFiniteOrNull(snapshot.SessionDuration),
                ConfiguredLaps = snapshot.LapsInEvent > 0 ? ToInt(snapshot.LapsInEvent) : (int?)null,
                ObservedParticipants = current.Count,
                VehicleClass = string.IsNullOrWhiteSpace(snapshot.RootCarClassName) ? null : snapshot.RootCarClassName,
                SessionPrivacyRaw = snapshot.SessionIsPrivate ? "PRIVATE" : "NOT_MARKED_PRIVATE",
                CaptureStarted = captureStarted,
                CaptureEnded = captureEnded,
                CaptureCompleteness = captureEnded ? "CAPTURE_SCOPE_ENDED" : "IN_PROGRESS",
                Participants = current.Values.OrderBy(value => value.Participant.Index).Select(value =>
                    new TelemetryParticipantDictionaryEntry
                    {
                        ParticipantRef = value.ParticipantRef,
                        Slot = value.Participant.Index,
                        Generation = value.Generation,
                        NameSnapshot = value.Participant.Name,
                        VehicleRef = value.Participant.VehicleName,
                        VehicleClassRef = value.Participant.VehicleClass,
                        IsActive = value.Participant.IsActive,
                        NationalityRaw = ToInt(value.Participant.NationalityRaw)
                    }).ToList()
            };
            AddNumericField(sample, "ambientTemperatureCelsius", snapshot.AmbientTemperature, "degC");
            AddNumericField(sample, "trackTemperatureCelsius", snapshot.TrackTemperature, "degC");
            AddNumericField(sample, "rainDensity", snapshot.RainDensity, "ratio");
            AddNumericField(sample, "snowDensity", snapshot.SnowDensity, "ratio");
            AddNumericField(sample, "windSpeedRaw", snapshot.WindSpeed, "SHM_RAW_UNIT_UNKNOWN");
            AddNumericField(sample, "windDirectionXRaw", snapshot.WindDirectionX, "SHM_RAW");
            AddNumericField(sample, "windDirectionYRaw", snapshot.WindDirectionY, "SHM_RAW");
            AddNumericField(sample, "cloudBrightnessRaw", snapshot.CloudBrightness, "SHM_RAW");
            AddNumericField(sample, "mandatoryPitLap", snapshot.EnforcedPitStopLap >= 0 ? snapshot.EnforcedPitStopLap : (double?)null, "lap");
            AddNumericField(sample, "eventTimeRemainingRaw", snapshot.EventTimeRemaining, "SHM_RAW");
            AddNumericField(sample, "sessionDurationMinutesRaw", snapshot.SessionDuration, "SHM_RAW");
            AddRawEnumField(sample, "gameStateRaw", snapshot.GameStateRaw);
            AddRawEnumField(sample, "sessionStateRaw", snapshot.SessionStateRaw);
            AddRawEnumField(sample, "raceStateRaw", snapshot.RaceStateRaw);
            AddRawEnumField(sample, "sequenceNumberRaw", snapshot.SequenceNumber);
            AddRawEnumField(sample, "viewedParticipantSlotRaw", snapshot.ViewedParticipantIndex);
            AddNumericField(sample, "viewedParticipantRef",
                current.TryGetValue(snapshot.ViewedParticipantIndex, out ParticipantIdentity? viewed)
                    ? viewed.ParticipantRef
                    : (double?)null,
                "participantRef");
            AddNumericField(sample, "numParticipantsRaw", snapshot.NumParticipants, "count");
            AddNumericField(sample, "numSectorsRaw", snapshot.NumSectors, "count");
            AddNumericField(sample, "lapsInEventRaw", snapshot.LapsInEvent, "lap");
            AddNumericField(sample, "sessionAdditionalLapsRaw", snapshot.SessionAdditionalLaps, "lap");
            AddRawEnumField(sample, "highestFlagColourRaw", snapshot.HighestFlagColourRaw);
            AddRawEnumField(sample, "highestFlagReasonRaw", snapshot.HighestFlagReasonRaw);
            AddRawEnumField(sample, "rootPitModeRaw", snapshot.RootPitModeRaw);
            AddRawEnumField(sample, "rootPitScheduleRaw", snapshot.RootPitScheduleRaw);
            AddRawEnumField(sample, "yellowFlagStateRaw", snapshot.YellowFlagStateRaw);
            AddBooleanField(sample, "sessionIsPrivateRaw", snapshot.SessionIsPrivate);
            AddTextField(sample, "eventTimeRemainingUnitStatus", "SEMANTICS_PENDING_HEADER_MS_RUNTIME_OBSERVED_SECONDS");
            AddTextField(sample, "rootVehicleRaw", EmptyToNull(snapshot.RootCarName, string.Empty));
            AddTextField(sample, "rootVehicleClassRaw", EmptyToNull(snapshot.RootCarClassName, string.Empty));
            AddCapability(sample, "worldPosition", TelemetryCapabilityState.CAPTURED, "m");
            AddCapability(sample, "lapDistance", TelemetryCapabilityState.CAPTURED, "m");
            AddCapability(sample, "participantControls", TelemetryCapabilityState.NOT_EXPOSED, null);
            AddCapability(sample, "authoritativeMultiplayerBoolean", TelemetryCapabilityState.NOT_EXPOSED, null);
            // These are monotonic, observed-stream facts for this capture attempt. The
            // durable server index remains authoritative if a later local write fails.
            AddBooleanField(sample, "raceStory", _raceStoryObserved);
            AddBooleanField(sample, "replay", _replayObserved);
            AddBooleanField(sample, "driverTelemetry", _driverTelemetryObserved);
            AddBooleanField(sample, "incidentHighRate", _incidentHighRateObserved);
            return sample;
        }

        private RaceStoryEventSample Event(
            string type,
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp,
            ParticipantIdentity? identity,
            string factCode)
        {
            ParticipantSnapshot? participant = identity?.Participant;
            return new RaceStoryEventSample
            {
                EventId = NextEventId(type, stamp.SessionElapsedMs),
                EventType = type,
                FactCode = factCode,
                CapturedAtUtc = stamp.CapturedAtUtc.ToUniversalTime(),
                SessionElapsedMs = stamp.SessionElapsedMs,
                ParticipantRef = identity?.ParticipantRef,
                Lap = participant == null ? (int?)null : ToInt(participant.CurrentLap),
                Sector = participant?.CurrentSector,
                LapDistanceMeters = participant == null ? null : FiniteOrNull(participant.CurrentLapDistance),
                WorldX = participant == null ? null : FiniteOrNull(participant.WorldPosition.X),
                WorldY = participant == null ? null : FiniteOrNull(participant.WorldPosition.Y),
                WorldZ = participant == null ? null : FiniteOrNull(participant.WorldPosition.Z),
                RaceStateRaw = participant == null ? ToInt(snapshot.RaceStateRaw) : ToInt(participant.RaceStateRaw),
                PitStateRaw = participant == null ? ToInt(snapshot.RootPitModeRaw) : ToInt(participant.PitModeRaw),
                FlagColourRaw = participant == null ? ToInt(snapshot.HighestFlagColourRaw) : ToInt(participant.HighestFlagColourRaw),
                FlagReasonRaw = participant == null ? ToInt(snapshot.HighestFlagReasonRaw) : ToInt(participant.HighestFlagReasonRaw),
                ResultStateRaw = participant == null ? (int?)null : ToInt(participant.RaceStateRaw),
                YellowFlagStateRaw = snapshot.YellowFlagStateRaw
            };
        }

        private RaceStoryEventSample ParticipantActiveStateEvent(
            TelemetrySnapshot snapshot,
            TelemetryCaptureStamp stamp,
            int participantRef,
            bool isActive,
            string factCode,
            ParticipantIdentity? identity = null)
        {
            RaceStoryEventSample value = Event(
                "PARTICIPANT_ACTIVE_STATE",
                snapshot,
                stamp,
                identity,
                factCode);
            value.ParticipantRef = participantRef;
            value.ParticipantIsActiveRaw = isActive;
            return value;
        }

        private string NextEventId(string type, long elapsedMs)
            => type.ToLowerInvariant() + "-" + elapsedMs.ToString(CultureInfo.InvariantCulture)
                + "-" + (++_eventSequence).ToString(CultureInfo.InvariantCulture);

        private void UpdateParticipantStates(
            TelemetrySnapshot snapshot,
            Dictionary<int, ParticipantIdentity> current)
        {
            foreach (ParticipantTransitionState state in _participants.Values) state.Active = false;
            foreach (ParticipantIdentity identity in current.Values)
            {
                ParticipantSnapshot participant = identity.Participant;
                ParticipantTransitionState state = _participants[participant.Index];
                state.Seen = true;
                state.Active = true;
                state.Name = participant.Name;
                state.RacePosition = participant.RacePosition;
                state.LapsCompleted = participant.LapsCompleted;
                state.BestLapTime = participant.BestLapTime;
                state.RaceStateRaw = participant.RaceStateRaw;
                state.PitModeRaw = participant.PitModeRaw;
                state.PitScheduleRaw = participant.PitScheduleRaw;
                state.WorldPosition = participant.WorldPosition;
                state.WorldPositionUsable = IsUsablePosition(participant.WorldPosition);
            }
        }

        private static bool IsRootVehicleMatch(TelemetrySnapshot snapshot, ParticipantSnapshot participant)
            => !string.IsNullOrWhiteSpace(snapshot.RootCarName)
                && !string.IsNullOrWhiteSpace(snapshot.RootCarClassName)
                && string.Equals(snapshot.RootCarName.Trim(), participant.VehicleName.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(snapshot.RootCarClassName.Trim(), participant.VehicleClass.Trim(), StringComparison.OrdinalIgnoreCase)
                && snapshot.ViewedParticipantIndex == participant.Index;

        private string MetadataKey(TelemetrySnapshot snapshot, Dictionary<int, ParticipantIdentity> current)
            => string.Join("|", new[]
            {
                snapshot.TrackLocation,
                snapshot.TrackVariation,
                snapshot.TranslatedTrackLocation,
                snapshot.TranslatedTrackVariation,
                snapshot.GameStateRaw.ToString(CultureInfo.InvariantCulture),
                snapshot.SessionStateRaw.ToString(CultureInfo.InvariantCulture),
                snapshot.RaceStateRaw.ToString(CultureInfo.InvariantCulture),
                snapshot.ViewedParticipantIndex.ToString(CultureInfo.InvariantCulture),
                snapshot.NumParticipants.ToString(CultureInfo.InvariantCulture),
                snapshot.LapsInEvent.ToString(CultureInfo.InvariantCulture),
                snapshot.NumSectors.ToString(CultureInfo.InvariantCulture),
                snapshot.SessionDuration.ToString("R", CultureInfo.InvariantCulture),
                snapshot.SessionAdditionalLaps.ToString(CultureInfo.InvariantCulture),
                snapshot.RootCarName,
                snapshot.RootCarClassName,
                snapshot.HighestFlagColourRaw.ToString(CultureInfo.InvariantCulture),
                snapshot.HighestFlagReasonRaw.ToString(CultureInfo.InvariantCulture),
                snapshot.RootPitModeRaw.ToString(CultureInfo.InvariantCulture),
                snapshot.RootPitScheduleRaw.ToString(CultureInfo.InvariantCulture),
                snapshot.YellowFlagStateRaw.ToString(CultureInfo.InvariantCulture),
                snapshot.EnforcedPitStopLap.ToString(CultureInfo.InvariantCulture),
                snapshot.SessionIsPrivate ? "1" : "0",
                _raceStoryObserved ? "1" : "0",
                _replayObserved ? "1" : "0",
                _driverTelemetryObserved ? "1" : "0",
                _incidentHighRateObserved ? "1" : "0",
                string.Join(",", current.Values.OrderBy(value => value.Participant.Index).Select(value =>
                    value.ParticipantRef.ToString(CultureInfo.InvariantCulture) + ":" + value.Participant.Name + ":"
                    + value.Participant.VehicleName + ":" + value.Participant.VehicleClass + ":"
                    + (value.Participant.IsActive ? "1" : "0") + ":"
                    + value.Participant.NationalityRaw.ToString(CultureInfo.InvariantCulture)))
            });

        private static void AddNumericField(SessionMetadataSample sample, string key, float value, string unit)
            => AddNumericField(sample, key, IsFinite(value) ? value : (double?)null, unit);

        private static void AddNumericField(SessionMetadataSample sample, string key, double? value, string unit)
        {
            sample.Fields[key] = new TelemetryCapabilityValue
            {
                State = value.HasValue ? TelemetryCapabilityState.CAPTURED : TelemetryCapabilityState.UNKNOWN,
                NumericValue = value,
                Unit = unit
            };
        }

        private static void AddRawEnumField(SessionMetadataSample sample, string key, uint value)
        {
            sample.Fields[key] = new TelemetryCapabilityValue
            {
                State = TelemetryCapabilityState.CAPTURED,
                RawEnumValue = ToInt(value)
            };
        }

        private static void AddRawEnumField(SessionMetadataSample sample, string key, int value)
        {
            sample.Fields[key] = new TelemetryCapabilityValue
            {
                State = TelemetryCapabilityState.CAPTURED,
                RawEnumValue = value
            };
        }

        private static void AddCapability(
            SessionMetadataSample sample,
            string key,
            TelemetryCapabilityState state,
            string? unit)
        {
            sample.Fields[key] = new TelemetryCapabilityValue { State = state, Unit = unit };
        }

        private static void AddBooleanField(SessionMetadataSample sample, string key, bool value)
        {
            sample.Fields[key] = new TelemetryCapabilityValue
            {
                State = TelemetryCapabilityState.CAPTURED,
                BooleanValue = value
            };
        }

        private static void AddTextField(SessionMetadataSample sample, string key, string? value)
        {
            sample.Fields[key] = new TelemetryCapabilityValue
            {
                State = string.IsNullOrWhiteSpace(value)
                    ? TelemetryCapabilityState.UNKNOWN
                    : TelemetryCapabilityState.CAPTURED,
                TextValue = value
            };
        }

        private static double?[] FourTyres(
            IEnumerable<TyreTelemetrySnapshot> tyres,
            Func<TyreTelemetrySnapshot, float> selector,
            double multiplier)
        {
            var result = new double?[4];
            foreach (TyreTelemetrySnapshot tyre in tyres)
            {
                if (tyre.Index < 0 || tyre.Index >= result.Length) continue;
                float value = selector(tyre);
                result[tyre.Index] = IsFinite(value) ? value * multiplier : (double?)null;
            }
            return result;
        }

        private static void SetRaw(DriverTelemetrySample sample, string field, float value)
            => sample.AdditionalRawValues[field] = FiniteOrNull(value);

        private static void SetRaw(DriverTelemetrySample sample, string field, int value)
            => sample.AdditionalRawValues[field] = value;

        private static void SetRaw(DriverTelemetrySample sample, string field, uint value)
            => sample.AdditionalRawValues[field] = ToInt(value);

        private static void SetRaw(DriverTelemetrySample sample, string field, bool value)
            => sample.AdditionalRawValues[field] = value ? 1 : 0;

        private static void SetVectorRaw(DriverTelemetrySample sample, string prefix, TelemetryVector3 value)
        {
            SetRaw(sample, prefix + "X", value.X);
            SetRaw(sample, prefix + "Y", value.Y);
            SetRaw(sample, prefix + "Z", value.Z);
        }

        private static void AddWheelRaw(
            DriverTelemetrySample sample,
            IEnumerable<TyreTelemetrySnapshot> tyres,
            string prefix,
            Func<TyreTelemetrySnapshot, float> selector)
        {
            foreach (TyreTelemetrySnapshot tyre in tyres)
            {
                string? position = WheelPosition(tyre.Index);
                if (position == null) continue;
                SetRaw(sample, prefix + position, selector(tyre));
            }
        }

        private static void AddWheelRaw(
            DriverTelemetrySample sample,
            IEnumerable<TyreTelemetrySnapshot> tyres,
            string prefix,
            Func<TyreTelemetrySnapshot, uint> selector)
        {
            foreach (TyreTelemetrySnapshot tyre in tyres)
            {
                string? position = WheelPosition(tyre.Index);
                if (position == null) continue;
                SetRaw(sample, prefix + position, selector(tyre));
            }
        }

        private static void AddWheelText(
            DriverTelemetrySample sample,
            IEnumerable<TyreTelemetrySnapshot> tyres,
            string prefix,
            Func<TyreTelemetrySnapshot, string> selector)
        {
            foreach (TyreTelemetrySnapshot tyre in tyres)
            {
                string? position = WheelPosition(tyre.Index);
                if (position == null) continue;
                sample.AdditionalTextValues[prefix + position + "Ref"] = selector(tyre);
            }
        }

        private static string? WheelPosition(int index)
            => index switch
            {
                0 => "FrontLeft",
                1 => "FrontRight",
                2 => "RearLeft",
                3 => "RearRight",
                _ => null
            };

        private static string? EmptyToNull(string preferred, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(preferred)) return preferred;
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
        }

        private static string? TerminalFact(uint raw)
            => raw switch
            {
                (uint)RaceState.Retired => "RETIREMENT",
                (uint)RaceState.Dnf => "DNF",
                (uint)RaceState.Disqualified => "DISQUALIFICATION",
                _ => null
            };

        private static bool IsPenaltySchedule(uint raw)
            => raw == (uint)PitSchedule.DriveThrough || raw == (uint)PitSchedule.StopGo;

        private static bool IsPitLaneOrBox(uint raw)
            => raw == (uint)PitMode.DrivingIntoPits || raw == (uint)PitMode.InPit;

        private static bool IsFullCourseYellow(int raw)
            => raw >= (int)YellowFlagState.Pending && raw <= (int)YellowFlagState.RaceHalt;

        private static int ParticipantRef(int slot, int generation)
            => checked(generation * SharedMemoryLayout.MaxParticipants + slot);

        private static int ToInt(uint value)
            => value > int.MaxValue ? int.MaxValue : (int)value;

        private static int? SecondsToMilliseconds(float seconds)
            => IsPositiveFinite(seconds)
                ? (int?)Math.Min(int.MaxValue, Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero))
                : null;

        private static long? PositiveFiniteMilliseconds(float seconds)
            => IsPositiveFinite(seconds)
                ? (long?)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero)
                : null;

        private static long? NonNegativeFiniteMilliseconds(float seconds)
            => IsFinite(seconds) && seconds >= 0
                ? (long?)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero)
                : null;

        private static double? PositiveFiniteOrNull(float value)
            => IsPositiveFinite(value) ? value : (double?)null;

        private static double? NonNegativeFiniteOrNull(float value)
            => IsFinite(value) && value >= 0 ? value : (double?)null;

        private static double? UnitIntervalOrNull(float value)
            => FiniteInRangeOrNull(value, 0, 1);

        private static double? SignedUnitIntervalOrNull(float value)
            => FiniteInRangeOrNull(value, -1, 1);

        private static double? FiniteInRangeOrNull(float value, double minimum, double maximum)
            => IsFinite(value) && value >= minimum && value <= maximum ? value : (double?)null;

        private static double? FiniteOrNull(float value)
            => IsFinite(value) ? value : (double?)null;

        private static double? ProductOrNull(float first, float second)
            => IsFinite(first) && IsFinite(second) && first >= 0 && first <= 1 && second > 0
                ? first * second
                : (double?)null;

        private static bool IsPositiveFinite(float value)
            => IsFinite(value) && value > 0;

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsUsablePosition(TelemetryVector3 value)
            => IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z)
                && (Math.Abs(value.X) > 0.001f || Math.Abs(value.Y) > 0.001f || Math.Abs(value.Z) > 0.001f);

        private static double Distance(TelemetryVector3 first, TelemetryVector3 second)
        {
            double x = first.X - second.X;
            double y = first.Y - second.Y;
            double z = first.Z - second.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private sealed class ParticipantIdentity
        {
            public ParticipantIdentity(ParticipantSnapshot participant, int generation, int participantRef)
            {
                Participant = participant;
                Generation = generation;
                ParticipantRef = participantRef;
            }

            public ParticipantSnapshot Participant { get; }
            public int Generation { get; }
            public int ParticipantRef { get; }
        }

        private sealed class ParticipantTransitionState
        {
            public bool Seen { get; set; }
            public bool Active { get; set; }
            public int Generation { get; set; }
            public string Name { get; set; } = string.Empty;
            public uint RacePosition { get; set; }
            public uint LapsCompleted { get; set; }
            public float BestLapTime { get; set; }
            public uint RaceStateRaw { get; set; }
            public uint PitModeRaw { get; set; }
            public uint PitScheduleRaw { get; set; }
            public TelemetryVector3 WorldPosition { get; set; }
            public bool WorldPositionUsable { get; set; }
        }

        private sealed class ParticipantTransitionSummary
        {
            public bool ParticipantDisappeared { get; set; }
            public int PositionChangeMagnitude { get; set; }
            public List<int> DisappearedRefs { get; } = new List<int>();
            public List<int> TerminalTransitionRefs { get; } = new List<int>();
            public List<int> AbruptWorldPositionRefs { get; } = new List<int>();
        }

        private readonly struct WeatherState
        {
            public WeatherState(TelemetrySnapshot snapshot)
            {
                Ambient = snapshot.AmbientTemperature;
                Track = snapshot.TrackTemperature;
                Rain = snapshot.RainDensity;
                Snow = snapshot.SnowDensity;
                WindSpeed = snapshot.WindSpeed;
                WindX = snapshot.WindDirectionX;
                WindY = snapshot.WindDirectionY;
                Cloud = snapshot.CloudBrightness;
            }

            public float Ambient { get; }
            public float Track { get; }
            public float Rain { get; }
            public float Snow { get; }
            public float WindSpeed { get; }
            public float WindX { get; }
            public float WindY { get; }
            public float Cloud { get; }

            public bool SignificantlyDiffers(WeatherState other)
                => Difference(Ambient, other.Ambient) >= 1f
                    || Difference(Track, other.Track) >= 1f
                    || Difference(Rain, other.Rain) >= 0.05f
                    || Difference(Snow, other.Snow) >= 0.05f
                    || Difference(WindSpeed, other.WindSpeed) >= 0.5f
                    || Difference(WindX, other.WindX) >= 0.05f
                    || Difference(WindY, other.WindY) >= 0.05f
                    || Difference(Cloud, other.Cloud) >= 0.05f;

            private static float Difference(float first, float second)
                => IsFinite(first) && IsFinite(second) ? Math.Abs(first - second) : float.MaxValue;
        }
    }
}
