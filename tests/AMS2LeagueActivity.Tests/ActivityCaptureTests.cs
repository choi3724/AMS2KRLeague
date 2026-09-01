using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueActivity.Tests
{
    internal static class ActivityCaptureTests
    {
        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Fixed Phase 1-D.3 fixture inventory", FixedFixtureInventory);
            yield return new TestCase("Tuesday 21:59 KST is general", Tuesday2159IsGeneral);
            yield return new TestCase("Tuesday 22:00 KST starts league capture", Tuesday2200StartsLeagueCapture);
            yield return new TestCase("Tuesday chain continues across KST midnight", TuesdayChainContinuesAcrossMidnight);
            yield return new TestCase("Scheduled event overrides weekly fallback", ScheduledEventOverridesWeeklyFallback);
            yield return new TestCase("Time Attack auto-detect accepts position zero", TimeAttackAutoDetects);
            yield return new TestCase("Time Attack valid lap history uses valid-only PB", TimeAttackValidHistoryAndPb);
            yield return new TestCase("Time Attack invalid lap cannot become PB", TimeAttackInvalidLapCannotBecomePb);
            yield return new TestCase("Time Attack counter gap is invalid", TimeAttackCounterGapIsInvalid);
            yield return new TestCase("Time Attack lap IDs are deterministic", TimeAttackLapIdsAreDeterministic);
            yield return new TestCase("General race is player personal-only", GeneralRaceIsPlayerPersonalOnly);
            yield return new TestCase("General race excludes Safety Car field row", GeneralRaceExcludesSafetyCar);
            yield return new TestCase("Safety Car fixture uses metadata classification", SafetyCarFixtureUsesMetadataClassification);
            yield return new TestCase("Unresolved local participant is null-safe", UnresolvedLocalParticipantIsNullSafe);
            yield return new TestCase("Configured and observed weather remain separate", ConfiguredAndObservedWeatherSeparate);
            yield return new TestCase("Unsupported configured fields stay null", UnsupportedConfiguredFieldsStayNull);
            yield return new TestCase("Restart attempts are immutable and distinct", RestartAttemptsAreImmutable);
            yield return new TestCase("Multiplayer race menu transitions keep one attempt", MultiplayerRaceMenuTransitionsKeepOneAttempt);
            yield return new TestCase("Race transition without terminal evidence stays aborted", RaceTransitionWithoutTerminalEvidenceStaysAborted);
        }

        private static void FixedFixtureInventory()
        {
            string[] files =
            {
                "LEAGUE_TIMED_RACE.json",
                "GENERAL_RACE.json",
                "TIME_ATTACK_VALID_LAPS.json",
                "TIME_ATTACK_INVALID_LAP.json",
                "TIME_ATTACK_COUNTER_GAP.json",
                "RACE_RESTART_SEQUENCE.json",
                "WEATHER_CHANGE_SEQUENCE.json",
                "UNSUPPORTED_CONFIG_FIELDS.json",
                "SAFETY_CAR_PRESENT.json",
                "UNRESOLVED_PARTICIPANTS.json"
            };
            foreach (string file in files)
            {
                ActivityFixtureDocument fixture = ActivityFixtureLoader.Load(file);
                AssertEx.True(fixture.Scenario.Length > 0, "Fixture scenario is missing: " + file);
            }
        }

        private static void Tuesday2159IsGeneral()
        {
            var policy = new LeagueCapturePolicy();
            DateTimeOffset utc = new DateTimeOffset(2026, 9, 1, 12, 59, 0, TimeSpan.Zero);
            LeagueCaptureDecision result = policy.Classify(utc);
            AssertEx.False(result.IsLeagueCandidate);
            AssertEx.Equal("OUTSIDE_LEAGUE_CAPTURE_WINDOW", result.Reason);
            AssertEx.Equal(21, policy.ToKoreaTime(utc).Hour);
        }

        private static void Tuesday2200StartsLeagueCapture()
        {
            var policy = new LeagueCapturePolicy();
            DateTimeOffset utc = new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.Zero);
            LeagueCaptureDecision result = policy.Classify(utc);
            AssertEx.True(result.IsLeagueCandidate);
            AssertEx.NotNull(result.ChainAnchorUtc);
            AssertEx.True(result.LeagueCandidateId != null && result.LeagueCandidateId.StartsWith("league-chain-", StringComparison.Ordinal));
            AssertEx.Equal("TUESDAY_2200_KST_FALLBACK", result.Reason);
            AssertEx.Equal(22, policy.ToKoreaTime(utc).Hour);
        }

        private static void TuesdayChainContinuesAcrossMidnight()
        {
            var engine = new ActivityCaptureEngine("fixture-installation", "phase1d3-test");
            ActivityCaptureUpdate update = ActivityFixtureLoader.Replay(engine, "LEAGUE_TIMED_RACE.json");
            ActivityRecord record = AssertEx.Single(update.CompletedRecords.Where(item => item.ActivityType == ActivityType.Race));
            AssertEx.Equal(ActivityRecordScope.League, record.RecordScopeHint);
            AssertEx.NotNull(record.LeagueCandidateId);
            AssertEx.Equal(ActivityCompletionStatus.Finished, record.CompletionStatus);
            AssertEx.True(update.Events.Any(value => value.Contains("LEAGUE_CHAIN_STARTED", StringComparison.Ordinal)));
        }

        private static void ScheduledEventOverridesWeeklyFallback()
        {
            var engine = new ActivityCaptureEngine("scheduled-installation", "phase1d3-test");
            engine.SetScheduledEvent(new ScheduledLeagueEvent
            {
                EventId = "EVT-MONDAY-OVERRIDE",
                CaptureOpensAtUtc = new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
                ExpectedTrack = "Bathurst",
                ExpectedVehicleClass = "GT3"
            });
            ActivityCaptureUpdate update = ActivityFixtureLoader.Replay(engine, "GENERAL_RACE.json");
            ActivityRecord record = AssertEx.Single(update.CompletedRecords);
            AssertEx.Equal(ActivityRecordScope.League, record.RecordScopeHint);
            AssertEx.Equal("EVT-MONDAY-OVERRIDE", record.ScheduledEventId);
            AssertEx.Equal("league-EVT-MONDAY-OVERRIDE", record.LeagueCandidateId);
        }

        private static void TimeAttackAutoDetects()
        {
            TelemetrySnapshot first = ActivityFixtureLoader.LoadSnapshots("TIME_ATTACK_VALID_LAPS.json")[0];
            ActivityLocalParticipantResolution local = new ActivityLocalParticipantResolver().Resolve(first);
            AssertEx.True(local.IsValid, local.Reason);
            AssertEx.Equal(0U, local.Participant!.RacePosition);
            var engine = new ActivityCaptureEngine("ta-auto", "phase1d3-test");
            ActivityCaptureUpdate update = engine.Observe(first, local.Participant);
            AssertEx.True(update.Events.Any(value => value == "ACTIVITY_STARTED type=TIME_ATTACK automatic=true"));
            AssertEx.Equal(0, update.CompletedRecords.Count);
        }

        private static void TimeAttackValidHistoryAndPb()
        {
            var engine = new ActivityCaptureEngine("ta-history", "phase1d3-test");
            ActivityCaptureUpdate update = ActivityFixtureLoader.Replay(engine, "TIME_ATTACK_VALID_LAPS.json");
            ActivityRecord[] history = update.CompletedRecords.ToArray();
            AssertEx.Equal(3, history.Length);
            AssertEx.True(history.All(item => item.TimeAttackLap != null && item.TimeAttackLap.IsValid));
            AssertEx.Equal(90000, history[0].TimeAttackLap!.LapTimeMilliseconds);
            AssertEx.Equal(92000, history[1].TimeAttackLap!.LapTimeMilliseconds);
            AssertEx.Equal(89000, history[2].TimeAttackLap!.LapTimeMilliseconds);
            AssertEx.True(history[0].TimeAttackLap!.ClientPersonalBest);
            AssertEx.False(history[1].TimeAttackLap!.ClientPersonalBest);
            AssertEx.True(history[2].TimeAttackLap!.ClientPersonalBest);
            AssertEx.Equal(3, history.Select(item => item.TimeAttackLap!.LapUid).Distinct(StringComparer.Ordinal).Count());
        }

        private static void TimeAttackInvalidLapCannotBecomePb()
        {
            var engine = new ActivityCaptureEngine("ta-invalid", "phase1d3-test");
            ActivityCaptureUpdate update = ActivityFixtureLoader.Replay(engine, "TIME_ATTACK_INVALID_LAP.json");
            ActivityRecord[] history = update.CompletedRecords.ToArray();
            AssertEx.Equal(2, history.Length);
            AssertEx.False(history[0].TimeAttackLap!.IsValid);
            AssertEx.False(history[0].TimeAttackLap!.ClientPersonalBest);
            AssertEx.Equal("AMS2_LAP_INVALIDATED", history[0].TimeAttackLap!.InvalidReason);
            AssertEx.True(history[1].TimeAttackLap!.IsValid);
            AssertEx.True(history[1].TimeAttackLap!.ClientPersonalBest, "An invalid faster lap must not poison the valid-only PB baseline.");
        }

        private static void TimeAttackCounterGapIsInvalid()
        {
            var engine = new ActivityCaptureEngine("ta-gap", "phase1d3-test");
            ActivityRecord record = AssertEx.Single(ActivityFixtureLoader.Replay(engine, "TIME_ATTACK_COUNTER_GAP.json").CompletedRecords);
            AssertEx.False(record.TimeAttackLap!.IsValid);
            AssertEx.Equal("INCOMPLETE_LAP_COUNTER_GAP", record.TimeAttackLap.InvalidReason);
            AssertEx.True(record.TimeAttackLap.Issues.Contains("INCOMPLETE_LAP_COUNTER_GAP"));
            AssertEx.False(record.TimeAttackLap.ClientPersonalBest);
        }

        private static void TimeAttackLapIdsAreDeterministic()
        {
            var first = new ActivityCaptureEngine("ta-deterministic", "phase1d3-test");
            var second = new ActivityCaptureEngine("ta-deterministic", "phase1d3-test");
            string[] firstIds = ActivityFixtureLoader.Replay(first, "TIME_ATTACK_VALID_LAPS.json").CompletedRecords.Select(value => value.ActivityId).ToArray();
            string[] secondIds = ActivityFixtureLoader.Replay(second, "TIME_ATTACK_VALID_LAPS.json").CompletedRecords.Select(value => value.ActivityId).ToArray();
            AssertEx.Equal(string.Join("|", firstIds), string.Join("|", secondIds));
        }

        private static void GeneralRaceIsPlayerPersonalOnly()
        {
            var engine = new ActivityCaptureEngine("general-personal", "phase1d3-test");
            ActivityRecord record = AssertEx.Single(ActivityFixtureLoader.Replay(engine, "GENERAL_RACE.json").CompletedRecords);
            AssertEx.Equal(ActivityType.Race, record.ActivityType);
            AssertEx.Equal(ActivityRecordScope.General, record.RecordScopeHint);
            AssertEx.Equal(ActivityAuthority.PlayerPersonal, record.Authority);
            AssertEx.NotNull(record.PersonalRaceSummary);
            string payload = Encoding.UTF8.GetString(ActivityCanonicalSerializer.Serialize(record));
            AssertEx.True(payload.Contains("Fixture Player", StringComparison.Ordinal));
            AssertEx.False(payload.Contains("Fixture Opponent", StringComparison.Ordinal));
            AssertEx.False(payload.Contains("Safety Car", StringComparison.Ordinal));
        }

        private static void GeneralRaceExcludesSafetyCar()
        {
            var engine = new ActivityCaptureEngine("general-sc", "phase1d3-test");
            ActivityRecord record = AssertEx.Single(ActivityFixtureLoader.Replay(engine, "GENERAL_RACE.json").CompletedRecords);
            AssertEx.Equal(2, record.PersonalRaceSummary!.FieldSize);
            AssertEx.True(record.PersonalRaceSummary.SafetyCarExcludedFromFieldSize);
        }

        private static void SafetyCarFixtureUsesMetadataClassification()
        {
            TelemetrySnapshot snapshot = ActivityFixtureLoader.LoadSnapshots("SAFETY_CAR_PRESENT.json")[0];
            var classifier = new ParticipantRoleClassifier();
            AssertEx.Equal(ParticipantRole.RacingDriver, classifier.Classify(snapshot.Participants[0]));
            AssertEx.Equal(ParticipantRole.SafetyCar, classifier.Classify(snapshot.Participants[1]));
        }

        private static void UnresolvedLocalParticipantIsNullSafe()
        {
            TelemetrySnapshot snapshot = ActivityFixtureLoader.LoadSnapshots("UNRESOLVED_PARTICIPANTS.json")[0];
            ActivityLocalParticipantResolution local = new ActivityLocalParticipantResolver().Resolve(snapshot);
            AssertEx.False(local.IsValid);
            AssertEx.Null(local.Participant);
            AssertEx.True(local.Reason.Contains("outside", StringComparison.OrdinalIgnoreCase));

            var engine = new ActivityCaptureEngine("unresolved-local", "phase1d3-test");
            ActivityCaptureUpdate update = engine.Observe(snapshot, local.Participant);
            AssertEx.Equal(0, update.CompletedRecords.Count);
            AssertEx.Equal(0, update.Events.Count);
        }

        private static void ConfiguredAndObservedWeatherSeparate()
        {
            IReadOnlyList<TelemetrySnapshot> snapshots = ActivityFixtureLoader.LoadSnapshots("WEATHER_CHANGE_SEQUENCE.json");
            var metadata = new SessionMetadataAccumulator(snapshots[0].CapturedAt, "RACE");
            foreach (TelemetrySnapshot snapshot in snapshots) metadata.Observe(snapshot);
            ConfiguredSessionSettings configured = metadata.ConfiguredSettings;
            ObservedSessionConditions observed = metadata.BuildObserved(snapshots[snapshots.Count - 1].CapturedAt);
            AssertEx.Equal(40.0, configured.DurationMinutes);
            AssertEx.Equal(CaptureCapabilityStatus.ObservedOnly, configured.DurationStatus);
            AssertEx.Null(configured.WeatherSlots);
            AssertEx.Equal(CaptureCapabilityStatus.NotExposed, configured.WeatherSlotsStatus);
            AssertEx.Equal(2, observed.WeatherTimeline.Count);
            AssertEx.Equal(0f, observed.WeatherTimeline[0].RainDensity);
            AssertEx.Equal(0.35f, observed.WeatherTimeline[1].RainDensity);
            AssertEx.Equal(CaptureCapabilityStatus.ObservedOnly, observed.WeatherStatus);
        }

        private static void UnsupportedConfiguredFieldsStayNull()
        {
            TelemetrySnapshot snapshot = ActivityFixtureLoader.LoadSnapshots("UNSUPPORTED_CONFIG_FIELDS.json")[0];
            var metadata = new SessionMetadataAccumulator(snapshot.CapturedAt, "RACE");
            metadata.Observe(snapshot);
            ConfiguredSessionSettings configured = metadata.ConfiguredSettings;
            AssertEx.Null(configured.Enabled);
            AssertEx.Null(configured.DurationMinutes);
            AssertEx.Null(configured.ConfiguredLaps);
            AssertEx.Null(configured.InGameDate);
            AssertEx.Null(configured.StartTime);
            AssertEx.Null(configured.WeatherSlots);
            AssertEx.Equal(CaptureCapabilityStatus.NotExposed, configured.InGameDateStatus);
            AssertEx.Equal(CaptureCapabilityStatus.NotExposed, configured.WeatherSlotsStatus);
        }

        private static void RestartAttemptsAreImmutable()
        {
            var engine = new ActivityCaptureEngine("restart-installation", "phase1d3-test");
            ActivityRecord[] attempts = ActivityFixtureLoader.Replay(engine, "RACE_RESTART_SEQUENCE.json").CompletedRecords.ToArray();
            AssertEx.Equal(2, attempts.Length);
            AssertEx.Equal(1, attempts[0].AttemptNumber);
            AssertEx.Equal(ActivityCompletionStatus.Aborted, attempts[0].CompletionStatus);
            AssertEx.Equal(2, attempts[1].AttemptNumber);
            AssertEx.Equal(ActivityCompletionStatus.Finished, attempts[1].CompletionStatus);
            AssertEx.NotEqual(attempts[0].ActivityId, attempts[1].ActivityId);
            AssertEx.NotEqual(attempts[0].SessionFingerprint, attempts[1].SessionFingerprint);

            using var scope = new TemporaryDirectory("restart-store");
            var store = new ActivityRecordStore(scope.Root);
            ActivityStoreOutcome first = store.Commit(attempts[0]);
            ActivityStoreOutcome second = store.Commit(attempts[1]);
            AssertEx.Equal(ActivityStoreDisposition.Stored, first.Disposition);
            AssertEx.Equal(ActivityStoreDisposition.Stored, second.Disposition);
            AssertEx.Equal(2, Directory.GetDirectories(Path.Combine(scope.Root, "activities")).Length);
            AssertEx.True(File.Exists(Path.Combine(first.ActivityPath, "activity.json")));
            AssertEx.True(File.Exists(Path.Combine(second.ActivityPath, "activity.json")));
        }

        private static void MultiplayerRaceMenuTransitionsKeepOneAttempt()
        {
            var engine = new ActivityCaptureEngine("multiplayer-transition", "phase1d3-test");
            DateTimeOffset start = new DateTimeOffset(2026, 9, 1, 5, 4, 3, TimeSpan.Zero);

            ActivityCaptureUpdate started = Observe(engine, RaceSnapshot(start, GameState.InGamePlaying, RaceState.Racing));
            AssertEx.True(started.Events.Any(value => value.Contains("ACTIVITY_STARTED", StringComparison.Ordinal)));

            ActivityCaptureUpdate gridMenu = Observe(engine, RaceSnapshot(start.AddSeconds(58), GameState.InGameMenuTimeTicking, RaceState.Racing));
            AssertEx.Equal(0, gridMenu.CompletedRecords.Count);
            AssertEx.True(gridMenu.Events.Any(value => value.Contains("ACTIVITY_TRANSITION_HELD", StringComparison.Ordinal)));

            ActivityCaptureUpdate resumed = Observe(engine, RaceSnapshot(start.AddSeconds(60), GameState.InGamePlaying, RaceState.Racing));
            AssertEx.Equal(0, resumed.CompletedRecords.Count);
            AssertEx.True(resumed.Events.Any(value => value.Contains("ACTIVITY_TRANSITION_RESUMED", StringComparison.Ordinal)));
            AssertEx.False(resumed.Events.Any(value => value.Contains("ACTIVITY_STARTED", StringComparison.Ordinal)));

            ActivityCaptureUpdate resultMenu = Observe(engine, RaceSnapshot(start.AddMinutes(3), GameState.InGameMenuTimeTicking, RaceState.Retired));
            AssertEx.Equal(0, resultMenu.CompletedRecords.Count);
            AssertEx.True(resultMenu.Events.Any(value => value.Contains("ACTIVITY_TERMINAL_RESULT_OBSERVED", StringComparison.Ordinal)));

            // A later live-looking frame must not erase a terminal result that
            // AMS2 already exposed during the result/cooldown transition.
            ActivityCaptureUpdate postResultFrame = Observe(engine, RaceSnapshot(start.AddMinutes(3).AddSeconds(1), GameState.InGamePlaying, RaceState.Racing));
            AssertEx.Equal(0, postResultFrame.CompletedRecords.Count);

            ActivityCaptureUpdate closed = Observe(engine, RaceSnapshot(start.AddMinutes(4), GameState.FrontEnd, RaceState.Invalid));
            ActivityRecord record = AssertEx.Single(closed.CompletedRecords);
            AssertEx.Equal(1, record.AttemptNumber);
            AssertEx.Equal(ActivityCompletionStatus.Finished, record.CompletionStatus);
            AssertEx.Equal("RETIRED", record.PersonalRaceSummary!.ResultState);
        }

        private static void RaceTransitionWithoutTerminalEvidenceStaysAborted()
        {
            var engine = new ActivityCaptureEngine("abandoned-transition", "phase1d3-test");
            DateTimeOffset start = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
            Observe(engine, RaceSnapshot(start, GameState.InGamePlaying, RaceState.Racing));

            ActivityCaptureUpdate held = Observe(engine, RaceSnapshot(start.AddMinutes(1), GameState.InGameMenuTimeTicking, RaceState.Racing));
            AssertEx.Equal(0, held.CompletedRecords.Count);

            ActivityRecord record = AssertEx.Single(
                Observe(engine, RaceSnapshot(start.AddMinutes(2), GameState.FrontEnd, RaceState.Invalid)).CompletedRecords);
            AssertEx.Equal(ActivityCompletionStatus.Aborted, record.CompletionStatus);
            AssertEx.Equal("RACING", record.PersonalRaceSummary!.ResultState);
        }

        private static ActivityCaptureUpdate Observe(ActivityCaptureEngine engine, TelemetrySnapshot snapshot)
        {
            ActivityLocalParticipantResolution local = new ActivityLocalParticipantResolver().Resolve(snapshot);
            return engine.Observe(snapshot, local.IsValid ? local.Participant : null);
        }

        private static TelemetrySnapshot RaceSnapshot(DateTimeOffset at, GameState gameState, RaceState localState)
        {
            bool frontEnd = gameState == GameState.FrontEnd;
            var fixture = new ActivityFixtureSnapshot
            {
                CapturedAtUtc = at,
                GameState = gameState.ToString(),
                SessionState = frontEnd ? SessionState.Invalid.ToString() : SessionState.Race.ToString(),
                RaceState = frontEnd ? RaceState.Invalid.ToString() : localState.ToString(),
                ViewedParticipantIndex = frontEnd ? -1 : 0,
                LapsInEvent = 1,
                CurrentTime = frontEnd ? 0 : 120,
                Track = "Monza",
                Layout = "Monza_2020"
            };
            if (!frontEnd)
            {
                fixture.Participants.Add(new ActivityFixtureParticipant
                {
                    Name = "Multiplayer Player",
                    Position = 31,
                    LapsCompleted = 0,
                    CurrentLap = 1,
                    RaceState = localState.ToString(),
                    Vehicle = "GT3 Player",
                    VehicleClass = "GT3"
                });
                fixture.Participants.Add(new ActivityFixtureParticipant
                {
                    Name = "Multiplayer Opponent",
                    Position = 1,
                    LapsCompleted = localState == RaceState.Retired ? 1U : 0U,
                    CurrentLap = localState == RaceState.Retired ? 2U : 1U,
                    RaceState = localState == RaceState.Retired ? RaceState.Finished.ToString() : RaceState.Racing.ToString(),
                    Vehicle = "GT3 Opponent",
                    VehicleClass = "GT3"
                });
            }
            return fixture.ToSnapshot((uint)at.Millisecond + 200U);
        }
    }
}
