using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.HostRecording;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.SessionWitness
{
    /// <summary>
    /// Converts the already parsed Shared Memory snapshot stream into a bounded,
    /// durable session witness. It does not open Shared Memory or perform I/O.
    /// </summary>
    public sealed class SessionWitnessCaptureEngine
    {
        private static readonly TimeSpan WeatherInterval = TimeSpan.FromSeconds(60);
        // Enough for a long race with a full grid while preventing an accidental
        // per-frame producer regression from creating an unbounded upload body.
        private const int MaximumTimelineEvents = 4096;
        private readonly HostRecorderEngine _capture;
        private readonly string _sourceClientId;
        private readonly string _clientVersion;
        private readonly SessionWitnessSourceRole _sourceRole;
        private readonly Dictionary<int, ParticipantState> _participants = new Dictionary<int, ParticipantState>();
        private readonly List<SessionWitnessEvent> _events = new List<SessionWitnessEvent>();
        private readonly List<SessionWitnessWeatherPoint> _weather = new List<SessionWitnessWeatherPoint>();
        private DateTimeOffset? _captureStartedAt;
        private DateTimeOffset _lastWeatherAt = DateTimeOffset.MinValue;
        private uint? _lastSessionState;
        private uint? _lastRaceState;
        private ScheduledLeagueEvent? _scheduledEvent;

        public SessionWitnessCaptureEngine(
            string sourceClientId,
            string clientVersion,
            SessionWitnessSourceRole sourceRole = SessionWitnessSourceRole.Player)
        {
            _sourceClientId = string.IsNullOrWhiteSpace(sourceClientId) ? "local-installation" : sourceClientId.Trim();
            _clientVersion = clientVersion ?? string.Empty;
            _sourceRole = sourceRole;
            _capture = new HostRecorderEngine(_sourceClientId);
        }

        public void SetScheduledEvent(ScheduledLeagueEvent? scheduledEvent)
        {
            _scheduledEvent = scheduledEvent;
            _capture.SetScheduledEvent(scheduledEvent);
        }

        public SessionWitnessUpdate Observe(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var result = new SessionWitnessUpdate();
            bool relevantSession = IsRelevant(snapshot.SessionStateRaw);
            bool canStart = relevantSession && snapshot.NumParticipants > 1
                && snapshot.KnownGameState != GameState.FrontEnd;

            // Official SHM v14 exposes no authoritative multiplayer boolean.
            // Multiple observed participants are therefore a capture eligibility
            // fact, not a claim that the session is online or official.
            if (!_captureStartedAt.HasValue && !canStart)
            {
                return result;
            }

            if (_captureStartedAt.HasValue && !relevantSession)
            {
                return Wrap(_capture.Close(snapshot.CapturedAt, "WITNESS_SCOPE_ENDED"), result);
            }

            if (!_captureStartedAt.HasValue)
            {
                ResetTimeline();
                _captureStartedAt = snapshot.CapturedAt.ToUniversalTime();
                AddEvent(snapshot.CapturedAt, "SESSION_START", null, string.Empty, null, null, snapshot.SessionStateRaw, "OBSERVED_PARTICIPANTS_GT_ONE");
            }

            Track(snapshot);
            return Wrap(_capture.Observe(snapshot), result);
        }

        public SessionWitnessUpdate Close(DateTimeOffset atUtc, string reason)
        {
            var result = new SessionWitnessUpdate();
            return Wrap(_capture.Close(atUtc, string.IsNullOrWhiteSpace(reason) ? "WITNESS_CLOSED" : reason), result);
        }

        private SessionWitnessUpdate Wrap(HostRecorderUpdate update, SessionWitnessUpdate result)
        {
            foreach (string value in update.Events)
            {
                result.Events.Add("WITNESS_CAPTURE " + value);
            }
            if (update.FinalizedSession == null)
            {
                return result;
            }

            HostSessionResult session = update.FinalizedSession;
            List<string> roster = SessionWitnessFingerprint.Roster(session);
            string vehicleClass = SessionWitnessFingerprint.VehicleClass(session);
            string? scheduledEventHint = string.IsNullOrWhiteSpace(_scheduledEvent?.EventId)
                ? session.Activity?.ScheduledEventHint
                : _scheduledEvent!.EventId;
            string fingerprint = SessionWitnessFingerprint.CreateSession(
                session,
                vehicleClass,
                scheduledEventHint);
            SessionWitnessCompleteness completeness = Completeness(session);
            var witness = new SessionWitnessRecord
            {
                WitnessId = "witness-" + Guid.NewGuid().ToString("N"),
                SessionFingerprint = fingerprint,
                EventFingerprint = SessionWitnessFingerprint.CreateEvent(session, vehicleClass, scheduledEventHint),
                RosterSignature = SessionWitnessFingerprint.CreateRosterSignature(roster),
                RosterNames = roster,
                SourceClientId = _sourceClientId,
                SourceRole = _sourceRole,
                CaptureStartedAtUtc = _captureStartedAt ?? session.StartedAtUtc,
                CaptureEndedAtUtc = session.EndedAtUtc.ToUniversalTime(),
                EstimatedSessionStartedAtUtc = EstimateSessionStart(session),
                CaptureCompleteness = completeness,
                ScheduledEventHint = string.IsNullOrWhiteSpace(scheduledEventHint) ? null : scheduledEventHint,
                VehicleClass = vehicleClass,
                ClientVersion = _clientVersion,
                Session = session,
                Events = _events.ToList(),
                Weather = _weather.ToList()
            };
            witness.QualityScore = QualityScore(witness);
            result.FinalizedWitness = witness;
            result.Events.Add(
                "SESSION_WITNESS_FINALIZED id=" + witness.WitnessId
                + " fingerprint=" + witness.SessionFingerprint
                + " completeness=" + witness.CaptureCompleteness.ToString().ToUpperInvariant());
            ResetTimeline();
            return result;
        }

        private void Track(TelemetrySnapshot snapshot)
        {
            if (!_lastSessionState.HasValue || _lastSessionState.Value != snapshot.SessionStateRaw)
            {
                AddEvent(snapshot.CapturedAt, "SESSION_STATE", null, string.Empty, null, _lastSessionState, snapshot.SessionStateRaw, string.Empty);
                _lastSessionState = snapshot.SessionStateRaw;
            }
            if (!_lastRaceState.HasValue || _lastRaceState.Value != snapshot.RaceStateRaw)
            {
                AddEvent(snapshot.CapturedAt, "RACE_STATE", null, string.Empty, null, _lastRaceState, snapshot.RaceStateRaw, string.Empty);
                _lastRaceState = snapshot.RaceStateRaw;
            }

            var observedSlots = new HashSet<int>();
            foreach (ParticipantSnapshot participant in snapshot.Participants.Where(value => value.IsActive))
            {
                observedSlots.Add(participant.Index);
                if (!_participants.TryGetValue(participant.Index, out ParticipantState? previous)
                    || !string.Equals(previous.Name, participant.Name, StringComparison.Ordinal))
                {
                    previous = new ParticipantState
                    {
                        Name = participant.Name,
                        LapsCompleted = participant.LapsCompleted,
                        RaceStateRaw = participant.RaceStateRaw,
                        PitModeRaw = participant.PitModeRaw
                    };
                    _participants[participant.Index] = previous;
                    AddEvent(snapshot.CapturedAt, "PARTICIPANT_SNAPSHOT", participant.Index, participant.Name, participant.LapsCompleted, null, participant.RaceStateRaw, participant.VehicleName);
                    continue;
                }

                if (participant.LapsCompleted > previous.LapsCompleted)
                {
                    string detail = participant.LapsCompleted == previous.LapsCompleted + 1
                        ? "LAP_COMPLETE"
                        : "LAP_COUNTER_GAP_" + (participant.LapsCompleted - previous.LapsCompleted).ToString(CultureInfo.InvariantCulture);
                    AddEvent(snapshot.CapturedAt, "LAP_COMPLETE", participant.Index, participant.Name, participant.LapsCompleted, null, null, detail);
                }
                if (participant.RaceStateRaw != previous.RaceStateRaw)
                {
                    AddEvent(snapshot.CapturedAt, "PARTICIPANT_STATUS", participant.Index, participant.Name, participant.LapsCompleted, previous.RaceStateRaw, participant.RaceStateRaw, string.Empty);
                }
                if (participant.PitModeRaw != previous.PitModeRaw)
                {
                    AddEvent(snapshot.CapturedAt, "PIT_TRANSITION", participant.Index, participant.Name, participant.LapsCompleted, previous.PitModeRaw, participant.PitModeRaw, string.Empty);
                }

                previous.LapsCompleted = participant.LapsCompleted;
                previous.RaceStateRaw = participant.RaceStateRaw;
                previous.PitModeRaw = participant.PitModeRaw;
            }

            foreach (KeyValuePair<int, ParticipantState> item in _participants.ToArray())
            {
                if (!observedSlots.Contains(item.Key) && !item.Value.Missing)
                {
                    item.Value.Missing = true;
                    AddEvent(snapshot.CapturedAt, "PARTICIPANT_MISSING", item.Key, item.Value.Name, item.Value.LapsCompleted, item.Value.RaceStateRaw, null, string.Empty);
                }
                else if (observedSlots.Contains(item.Key))
                {
                    item.Value.Missing = false;
                }
            }

            bool weatherChanged = _weather.Count == 0
                || Math.Abs(_weather[_weather.Count - 1].RainDensity - snapshot.RainDensity) >= 0.05f
                || Math.Abs(_weather[_weather.Count - 1].AmbientTemperatureCelsius - snapshot.AmbientTemperature) >= 1f
                || Math.Abs(_weather[_weather.Count - 1].TrackTemperatureCelsius - snapshot.TrackTemperature) >= 1f;
            if (weatherChanged || snapshot.CapturedAt - _lastWeatherAt >= WeatherInterval)
            {
                _lastWeatherAt = snapshot.CapturedAt;
                _weather.Add(new SessionWitnessWeatherPoint
                {
                    CapturedAtUtc = snapshot.CapturedAt.ToUniversalTime(),
                    SessionElapsedSeconds = IsFiniteNonNegative(snapshot.CurrentTime) ? snapshot.CurrentTime : 0,
                    AmbientTemperatureCelsius = snapshot.AmbientTemperature,
                    TrackTemperatureCelsius = snapshot.TrackTemperature,
                    RainDensity = snapshot.RainDensity,
                    WindSpeed = snapshot.WindSpeed,
                    WindDirectionX = snapshot.WindDirectionX,
                    WindDirectionY = snapshot.WindDirectionY,
                    CloudBrightness = snapshot.CloudBrightness,
                    SnowDensity = snapshot.SnowDensity
                });
            }
        }

        private void AddEvent(
            DateTimeOffset capturedAt,
            string kind,
            int? slot,
            string name,
            uint? lap,
            uint? previousState,
            uint? state,
            string detail)
        {
            if (_events.Count >= MaximumTimelineEvents) return;
            _events.Add(new SessionWitnessEvent
            {
                CapturedAtUtc = capturedAt.ToUniversalTime(),
                Kind = kind,
                Slot = slot,
                NameSnapshot = name ?? string.Empty,
                Lap = lap,
                PreviousStateRaw = previousState,
                StateRaw = state,
                Detail = detail ?? string.Empty
            });
        }

        private void ResetTimeline()
        {
            _captureStartedAt = null;
            _lastWeatherAt = DateTimeOffset.MinValue;
            _lastSessionState = null;
            _lastRaceState = null;
            _participants.Clear();
            _events.Clear();
            _weather.Clear();
        }

        private static SessionWitnessCompleteness Completeness(HostSessionResult session)
        {
            List<HostEvidenceSnapshot> race = session.Evidence
                .Where(value => value.SessionStateRaw == (uint)SessionState.Race)
                .OrderBy(value => value.CapturedAtUtc)
                .ToList();
            if (race.Count == 0) return SessionWitnessCompleteness.Unknown;

            HostEvidenceSnapshot first = race[0];
            bool terminalAtFirst = IsTerminal(first.RaceStateRaw)
                || (first.Participants.Count > 0 && first.Participants.All(value => IsTerminal(value.ResultStateRaw)));
            bool startObserved = !terminalAtFirst
                && (first.RaceStateRaw == (uint)RaceState.NotStarted
                    || (IsFiniteNonNegative(first.CurrentTimeSeconds) && first.CurrentTimeSeconds <= 15));
            bool endObserved = session.RaceResult != null
                && session.RaceResult.Participants.Count > 0
                && session.RaceResult.Participants.All(value => IsTerminal(value.ResultStateRaw));
            if (startObserved && endObserved) return SessionWitnessCompleteness.FullSession;
            if (terminalAtFirst || (race.Count <= 2 && endObserved)) return SessionWitnessCompleteness.EndOnly;
            return SessionWitnessCompleteness.MidSession;
        }

        private static DateTimeOffset? EstimateSessionStart(HostSessionResult session)
        {
            HostEvidenceSnapshot? firstRace = session.Evidence
                .Where(value => value.SessionStateRaw == (uint)SessionState.Race)
                .OrderBy(value => value.CapturedAtUtc)
                .FirstOrDefault();
            if (firstRace == null) return session.StartedAtUtc.ToUniversalTime();
            if (IsFiniteNonNegative(firstRace.CurrentTimeSeconds) && firstRace.CurrentTimeSeconds <= 86400)
            {
                return firstRace.CapturedAtUtc.ToUniversalTime() - TimeSpan.FromSeconds(firstRace.CurrentTimeSeconds);
            }
            return firstRace.CapturedAtUtc.ToUniversalTime();
        }

        private static int QualityScore(SessionWitnessRecord witness)
        {
            int value = witness.CaptureCompleteness switch
            {
                SessionWitnessCompleteness.FullSession => 400,
                SessionWitnessCompleteness.MidSession => 250,
                SessionWitnessCompleteness.EndOnly => 200,
                _ => 100
            };
            if (witness.SourceRole == SessionWitnessSourceRole.Host) value += 20;
            if (witness.Session.RaceResult?.Participants.Count > 0) value += 40;
            if (witness.Session.StartingGrid?.Participants.Count > 0) value += 20;
            if (witness.Session.Qualifying?.Participants.Count > 0) value += 20;
            return value + Math.Min(witness.RosterNames.Count, 99);
        }

        private static bool IsRelevant(uint raw)
            => raw == (uint)SessionState.Practice
                || raw == (uint)SessionState.Test
                || raw == (uint)SessionState.Qualify
                || raw == (uint)SessionState.FormationLap
                || raw == (uint)SessionState.Race;

        private static bool IsTerminal(uint raw)
            => raw == (uint)RaceState.Finished
                || raw == (uint)RaceState.Disqualified
                || raw == (uint)RaceState.Retired
                || raw == (uint)RaceState.Dnf;

        private static bool IsFiniteNonNegative(float value)
            => value >= 0 && !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class ParticipantState
        {
            public string Name { get; set; } = string.Empty;
            public uint LapsCompleted { get; set; }
            public uint RaceStateRaw { get; set; }
            public uint PitModeRaw { get; set; }
            public bool Missing { get; set; }
        }
    }

    public static class SessionWitnessFingerprint
    {
        public static string CreateSession(HostSessionResult session, string vehicleClass, string? scheduledEventHint)
        {
            (double duration, uint laps) = Configuration(session);
            string value = string.Join("|", new[]
            {
                "session-witness-v2",
                Normalize(session.Track),
                Normalize(session.Layout),
                Normalize(vehicleClass),
                duration.ToString("0.0", CultureInfo.InvariantCulture),
                laps.ToString(CultureInfo.InvariantCulture),
                Normalize(scheduledEventHint)
            });
            return Hash(value);
        }

        public static string CreateEvent(HostSessionResult session, string vehicleClass, string? scheduledEventHint)
        {
            string value = string.Join("|", new[]
            {
                "event-witness-v1",
                Normalize(session.Track),
                Normalize(session.Layout),
                Normalize(vehicleClass),
                Normalize(scheduledEventHint)
            });
            return Hash(value);
        }

        public static string CreateRosterSignature(IEnumerable<string> names)
            => Hash("roster-v1|" + string.Join("|", names.Select(Normalize).OrderBy(value => value, StringComparer.Ordinal)));

        public static List<string> Roster(HostSessionResult session)
            => session.Evidence
                .SelectMany(value => value.Participants)
                .Select(value => (value.NameSnapshot ?? string.Empty).Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static string VehicleClass(HostSessionResult session)
            => session.RaceResult?.Participants.Select(value => value.VehicleClass).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? session.StartingGrid?.Participants.Select(value => value.VehicleClass).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? session.Qualifying?.Participants.Select(value => value.VehicleClass).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? session.Evidence.SelectMany(value => value.Participants).Select(value => value.VehicleClass).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? string.Empty;

        private static (double DurationMinutes, uint Laps) Configuration(HostSessionResult session)
        {
            double duration = session.Evidence
                .Select(value => (double)value.SessionDurationMinutes)
                .Where(value => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value))
                .DefaultIfEmpty(0)
                .Max();
            uint laps = session.Evidence.Select(value => value.ConfiguredLaps).DefaultIfEmpty(0u).Max();
            return (Math.Round(duration, 1, MidpointRounding.AwayFromZero), laps);
        }

        private static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        private static string Hash(string value)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }
    }
}
