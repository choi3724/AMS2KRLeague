using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Security;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new (string Name, Action Test)[]
            {
                ("Official v14 activity metadata offsets", ActivityMetadataLayoutOffsets),
                ("Official v14 root vehicle parses", RootVehicleTelemetryParses),
                ("Official v14 fastest timing parses", RootFastestTimingParses),
                ("Official v14 weather parses", WeatherTelemetryParses),
                ("Official v14 pit, snow and privacy metadata parses", SessionActivityMetadataParses),
                ("RaceControl history alone stays hidden", RaceControlHistoryAloneHidden),
                ("RaceControl state-only card fits", RaceControlStateOnlyCardFits),
                ("RaceControl clears AMS2 top-center alert", RaceControlLeftAuxiliaryPlacement),
                ("Multiplayer menu shows waiting overlay", MultiplayerMenuShowsWaitingOverlay),
                ("Multiplayer qualify-end transition shows waiting", MultiplayerQualifyEndShowsWaitingOverlay),
                ("Waiting overlay excludes single and replay", WaitingOverlayExcludesSingleAndReplay),
                ("Waiting overlay returns to gameplay", WaitingOverlayReturnsToGameplay),
                ("Remaining timer fallback is bounded to generation", RemainingTimerFallbackBounded),
                ("Waiting timer never fabricates countdown", WaitingTimerDoesNotFabricateCountdown),
                ("Session card uses observed terminal status", SessionCardUsesObservedTerminalStatus)
                ,("Public launch shows first-run status", PublicLaunchShowsStatus)
                ,("Background launch is explicit", BackgroundLaunchIsExplicit)
                ,("Fresh user has no pairing identity", FreshUserHasNoPairingIdentity)
                ,("Pairing credential is DPAPI protected", PairingCredentialIsProtected)
                ,("Unpair clears protected credential", UnpairClearsCredential)
            };
            int passed = 0;
            foreach ((string name, Action test) in tests)
            {
                try
                {
                    test();
                    passed++;
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("FAIL " + name);
                    Console.Error.WriteLine(exception);
                }
            }

            Console.WriteLine("RESULT: " + passed + " passed, " + (tests.Length - passed) + " failed, " + tests.Length + " total");
            return passed == tests.Length ? 0 : 1;
        }

        private static void ActivityMetadataLayoutOffsets()
        {
            AssertEqual(6444, SharedMemoryLayout.CarName);
            AssertEqual(6508, SharedMemoryLayout.CarClassName);
            AssertEqual(6744, SharedMemoryLayout.PersonalFastestLapTime);
            AssertEqual(6748, SharedMemoryLayout.WorldFastestLapTime);
            AssertEqual(6776, SharedMemoryLayout.PersonalFastestSector1Time);
            AssertEqual(6780, SharedMemoryLayout.PersonalFastestSector2Time);
            AssertEqual(6784, SharedMemoryLayout.PersonalFastestSector3Time);
            AssertEqual(6788, SharedMemoryLayout.WorldFastestSector1Time);
            AssertEqual(6792, SharedMemoryLayout.WorldFastestSector2Time);
            AssertEqual(6796, SharedMemoryLayout.WorldFastestSector3Time);
            AssertEqual(7292, SharedMemoryLayout.AmbientTemperature);
            AssertEqual(7296, SharedMemoryLayout.TrackTemperature);
            AssertEqual(7300, SharedMemoryLayout.RainDensity);
            AssertEqual(7304, SharedMemoryLayout.WindSpeed);
            AssertEqual(7308, SharedMemoryLayout.WindDirectionX);
            AssertEqual(7312, SharedMemoryLayout.WindDirectionY);
            AssertEqual(7316, SharedMemoryLayout.CloudBrightness);
            AssertEqual(19248, SharedMemoryLayout.EnforcedPitStopLap);
            AssertEqual(20572, SharedMemoryLayout.SnowDensity);
            AssertEqual(20692, SharedMemoryLayout.SessionIsPrivate);
            AssertEqual(20700, SharedMemoryLayout.RequiredBytes);
        }

        private static void RootVehicleTelemetryParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder().SetRootVehicle("McLaren 720S GT3 Evo", "GT3 Gen2"));
            AssertEqual("McLaren 720S GT3 Evo", snapshot.RootCarName);
            AssertEqual("GT3 Gen2", snapshot.RootCarClassName);
        }

        private static void RootFastestTimingParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder().SetRootFastestTiming(
                    91.234f,
                    89.876f,
                    30.111f,
                    31.222f,
                    29.901f,
                    29.501f,
                    30.402f,
                    29.973f));
            AssertEqual(91.234f, snapshot.PersonalFastestLapTime);
            AssertEqual(89.876f, snapshot.WorldFastestLapTime);
            AssertEqual(30.111f, snapshot.PersonalFastestSector1Time);
            AssertEqual(31.222f, snapshot.PersonalFastestSector2Time);
            AssertEqual(29.901f, snapshot.PersonalFastestSector3Time);
            AssertEqual(29.501f, snapshot.WorldFastestSector1Time);
            AssertEqual(30.402f, snapshot.WorldFastestSector2Time);
            AssertEqual(29.973f, snapshot.WorldFastestSector3Time);
        }

        private static void WeatherTelemetryParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder().SetWeather(22.5f, 31.75f, 0.42f, 7.25f, -0.6f, 0.8f, 0.35f, 0.12f));
            AssertEqual(22.5f, snapshot.AmbientTemperature);
            AssertEqual(31.75f, snapshot.TrackTemperature);
            AssertEqual(0.42f, snapshot.RainDensity);
            AssertEqual(7.25f, snapshot.WindSpeed);
            AssertEqual(-0.6f, snapshot.WindDirectionX);
            AssertEqual(0.8f, snapshot.WindDirectionY);
            AssertEqual(0.35f, snapshot.CloudBrightness);
            AssertEqual(0.12f, snapshot.SnowDensity);
        }

        private static void SessionActivityMetadataParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder()
                    .SetWeather(20, 26, 0, 3, 1, 0, 0.9f, 0.27f)
                    .SetSessionActivityMetadata(7, true));
            AssertEqual(7, snapshot.EnforcedPitStopLap);
            AssertEqual(0.27f, snapshot.SnowDensity);
            AssertEqual(true, snapshot.SessionIsPrivate);

            TelemetrySnapshot publicSession = Parse(
                new RawFixtureBuilder().SetSessionActivityMetadata(-1, false));
            AssertEqual(-1, publicSession.EnforcedPitStopLap);
            AssertEqual(false, publicSession.SessionIsPrivate);
        }

        private static void RaceControlHistoryAloneHidden()
        {
            var analyzer = new RaceControlAnalyzer(EvidenceKind.Fixture);
            DateTimeOffset t = FixedTime();
            ObserveControl(analyzer, new RawFixtureBuilder(), t);
            var penalty = new RawFixtureBuilder().SetParticipantControl(1, PitSchedule.DriveThrough);
            RaceControlUpdate active = ObserveControl(analyzer, penalty, t.AddSeconds(1));
            AssertTrue(RaceControlViewModel.FromUpdate(active).IsVisible);

            RaceControlUpdate expired = ObserveControl(analyzer, penalty, t.AddSeconds(7));
            AssertNull(expired.ActiveEvent);
            AssertEqual(1, expired.History.Count);
            RaceControlViewModel hidden = RaceControlViewModel.FromUpdate(expired);
            AssertFalse(hidden.IsVisible);
            AssertTrue(hidden.HistoryText.Contains("드라이브스루", StringComparison.Ordinal));
        }

        private static void RaceControlStateOnlyCardFits()
        {
            var analyzer = new RaceControlAnalyzer(EvidenceKind.Fixture);
            DateTimeOffset t = FixedTime();
            ObserveControl(analyzer, new RawFixtureBuilder(), t);
            var yellow = new RawFixtureBuilder().SetRootControl(FlagColour.Yellow);
            ObserveControl(analyzer, yellow, t.AddSeconds(1));
            RaceControlUpdate persistent = ObserveControl(analyzer, yellow, t.AddSeconds(7));
            RaceControlViewModel view = RaceControlViewModel.FromUpdate(persistent);

            AssertNull(persistent.ActiveEvent);
            AssertTrue(view.IsVisible);
            AssertFalse(view.IsExpanded);
            AssertEqual("황색기", view.StateLabel);
            AssertTrue(AuxiliaryOverlayLayoutMetrics.RaceControlCompactHeight >= 80);
            AssertTrue(AuxiliaryOverlayLayoutMetrics.RaceControlExpandedHeight > AuxiliaryOverlayLayoutMetrics.RaceControlCompactHeight);
        }

        private static void RaceControlLeftAuxiliaryPlacement()
        {
            int towerLeft = Math.Max(8, (int)Math.Round(3440 * 0.004));
            int auxiliaryLeft = towerLeft + LeftTowerLayoutMetrics.Width + LeftTowerLayoutMetrics.SessionGap;
            int towerTop = Math.Max(8, (int)Math.Round(1440 * 0.008));
            int raceTop = towerTop + AuxiliaryOverlayLayoutMetrics.RaceControlTopOffset;

            AssertEqual(AuxiliaryOverlayLayoutMetrics.SessionHeight + LeftTowerLayoutMetrics.SessionGap,
                AuxiliaryOverlayLayoutMetrics.RaceControlTopOffset);
            AssertTrue(auxiliaryLeft + AuxiliaryOverlayLayoutMetrics.RaceControlExpandedWidth < 3440 / 2);
            AssertEqual(towerTop + AuxiliaryOverlayLayoutMetrics.SessionHeight + LeftTowerLayoutMetrics.SessionGap, raceTop);
        }

        private static void MultiplayerMenuShowsWaitingOverlay()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetGameState(GameState.InGameMenuTimeTicking)
                .SetSession(SessionState.Qualify)
                .SetSessionTiming(15, 0, 210.17f)
                .SetParticipantVehicle(1, "Camaro SafetyCar", "SafetyCar");
            MultiplayerOverlayDecision decision = new MultiplayerWaitingOverlayController().Observe(Parse(fixture), 0, FixedTime());

            AssertEqual(MultiplayerOverlayMode.Waiting, decision.Mode);
            AssertNotNull(decision.Waiting);
            AssertEqual("멀티플레이어 세션 대기", decision.Waiting?.Title);
            AssertEqual("예선", decision.Waiting?.SessionLabel);
            AssertEqual("리그 3 / 원본 4", decision.Waiting?.ParticipantCountText);
            AssertEqual("3:31", decision.Waiting?.RemainingValue);
        }

        private static void MultiplayerQualifyEndShowsWaitingOverlay()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetGameState(GameState.InGamePlaying)
                .SetSession(SessionState.Qualify)
                .SetGlobalRaceState(RaceState.NotStarted)
                .SetSessionTiming(15, 0, -1);
            MultiplayerOverlayDecision decision = new MultiplayerWaitingOverlayController().Observe(Parse(fixture), 0, FixedTime());

            AssertEqual(MultiplayerOverlayMode.Waiting, decision.Mode);
            AssertEqual("MULTIPLAYER_SESSION_TRANSITION", decision.Reason);
            AssertEqual("세션 종료 대기", decision.Waiting?.RemainingValue);
        }

        private static void WaitingOverlayExcludesSingleAndReplay()
        {
            var controller = new MultiplayerWaitingOverlayController();
            TelemetrySnapshot single = Parse(new RawFixtureBuilder(1)
                .SetViewedIndex(0)
                .SetGameState(GameState.InGameMenuTimeTicking)
                .SetSessionTiming(15, 0, 210));
            TelemetrySnapshot replay = Parse(new RawFixtureBuilder(4)
                .SetGameState(GameState.InGameReplay)
                .SetSessionTiming(15, 0, 210));

            AssertEqual(MultiplayerOverlayMode.Hidden, controller.Observe(single, 0, FixedTime()).Mode);
            AssertEqual(MultiplayerOverlayMode.Hidden, controller.Observe(replay, 1, FixedTime()).Mode);
        }

        private static void WaitingOverlayReturnsToGameplay()
        {
            var controller = new MultiplayerWaitingOverlayController();
            DateTimeOffset t = FixedTime();
            TelemetrySnapshot waiting = Parse(new RawFixtureBuilder(4)
                .SetGameState(GameState.InGameMenuTimeTicking)
                .SetSessionTiming(15, 0, 210), t);
            TelemetrySnapshot playing = Parse(new RawFixtureBuilder(4)
                .SetGameState(GameState.InGamePlaying)
                .SetSessionTiming(15, 0, 209), t.AddSeconds(1));

            AssertEqual(MultiplayerOverlayMode.Waiting, controller.Observe(waiting, 0, t).Mode);
            MultiplayerOverlayDecision resumed = controller.Observe(playing, 1, t.AddSeconds(1));
            AssertEqual(MultiplayerOverlayMode.Gameplay, resumed.Mode);
            AssertTrue(resumed.EffectiveRemainingSeconds.HasValue);
            AssertEqual(209f, resumed.EffectiveRemainingSeconds.GetValueOrDefault());
        }

        private static void RemainingTimerFallbackBounded()
        {
            var controller = new MultiplayerWaitingOverlayController();
            DateTimeOffset t = FixedTime();
            TelemetrySnapshot valid = Parse(new RawFixtureBuilder(4).SetSessionTiming(15, 0, 210), t);
            TelemetrySnapshot transient = Parse(new RawFixtureBuilder(4).SetSessionTiming(15, 0, -1), t.AddSeconds(1));

            controller.Observe(valid, 4, t);
            MultiplayerOverlayDecision held = controller.Observe(transient, 4, t.AddSeconds(1));
            AssertTrue(held.EffectiveRemainingSeconds.HasValue);
            AssertEqual(210f, held.EffectiveRemainingSeconds.GetValueOrDefault());
            AssertEqual("GAMEPLAY_TIMER_TRANSIENT", held.Reason);
            ParticipantSnapshot local = ResolveLocal(transient);
            OverlayViewModel timing = OverlayViewModel.Build(
                transient,
                local,
                Classify(transient),
                30,
                20,
                false,
                "TEST",
                eventTimeRemainingOverride: held.EffectiveRemainingSeconds,
                eventTimeRemainingTextOverride: held.RemainingDisplayTextOverride);
            AssertEqual("3:30", OverlayShellViewModel.Build(transient, timing, null, false).Session.PrimaryValue);

            MultiplayerOverlayDecision expired = controller.Observe(transient, 4, t.AddSeconds(3.1));
            AssertNull(expired.EffectiveRemainingSeconds);
            AssertEqual("종료 처리 중", expired.RemainingDisplayTextOverride);
            controller.Observe(valid, 4, t.AddSeconds(4));
            MultiplayerOverlayDecision nextGeneration = controller.Observe(transient, 5, t.AddSeconds(5));
            AssertNull(nextGeneration.EffectiveRemainingSeconds);
        }

        private static void WaitingTimerDoesNotFabricateCountdown()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetSession(SessionState.Qualify)
                .SetGlobalRaceState(RaceState.NotStarted)
                .SetSessionTiming(15, 0, -1);
            var controller = new MultiplayerWaitingOverlayController();

            MultiplayerOverlayDecision unknown = controller.Observe(Parse(fixture), 0, FixedTime());
            AssertEqual("상태", unknown.Waiting?.RemainingLabel);
            AssertEqual("세션 종료 대기", unknown.Waiting?.RemainingValue);
            AssertNull(unknown.EffectiveRemainingSeconds);
        }

        private static void SessionCardUsesObservedTerminalStatus()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetSessionTiming(15, 0, -1)
                .SetGlobalRaceState(RaceState.Finished);
            TelemetrySnapshot snapshot = Parse(fixture);
            MultiplayerOverlayDecision decision = new MultiplayerWaitingOverlayController().Observe(snapshot, 0, FixedTime());
            ParticipantSnapshot local = ResolveLocal(snapshot);
            LeagueClassification league = Classify(snapshot);
            OverlayViewModel timing = OverlayViewModel.Build(
                snapshot,
                local,
                league,
                30,
                20,
                false,
                "TEST",
                eventTimeRemainingOverride: decision.EffectiveRemainingSeconds,
                eventTimeRemainingTextOverride: decision.RemainingDisplayTextOverride);
            SessionInfoViewModel session = OverlayShellViewModel.Build(snapshot, timing, null, false).Session;

            AssertEqual(MultiplayerOverlayMode.Gameplay, decision.Mode);
            AssertEqual("세션 종료", decision.RemainingDisplayTextOverride);
            AssertEqual("세션 종료", session.PrimaryValue);
        }

        private static void PublicLaunchShowsStatus()
        {
            ClientStartupPolicy policy = ClientStartupPolicy.FromArguments(Array.Empty<string>());
            AssertTrue(policy.ShowStatusWindow);
            AssertTrue(policy.ShowStatusWindowActivated);
            AssertFalse(policy.IsBackgroundStartup);
            AssertFalse(policy.Diagnostic);
        }

        private static void BackgroundLaunchIsExplicit()
        {
            ClientStartupPolicy policy = ClientStartupPolicy.FromArguments(new[] { "--background" });
            AssertFalse(policy.ShowStatusWindow);
            AssertFalse(policy.ShowStatusWindowActivated);
            AssertTrue(policy.IsBackgroundStartup);
        }

        private static void FreshUserHasNoPairingIdentity()
        {
            WithTemporaryDirectory(directory => AssertEqual(string.Empty, PairingTokenStore.Load(directory)));
        }

        private static void PairingCredentialIsProtected()
        {
            WithTemporaryDirectory(directory =>
            {
                string token = "release020_pairing_test_0123456789abcdef";
                PairingTokenStore.Save(directory, token);
                string path = PairingTokenStore.ResolvePath(directory);
                AssertTrue(File.Exists(path));
                byte[] stored = File.ReadAllBytes(path);
                AssertFalse(Encoding.UTF8.GetString(stored).Contains(token, StringComparison.Ordinal));
                AssertEqual(token, PairingTokenStore.Load(directory));
            });
        }

        private static void UnpairClearsCredential()
        {
            WithTemporaryDirectory(directory =>
            {
                PairingTokenStore.Save(directory, "release020_unpair_test_0123456789abcdef");
                PairingTokenStore.Clear(directory);
                AssertEqual(string.Empty, PairingTokenStore.Load(directory));
            });
        }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-release020-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                action(path);
            }
            finally
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        }

        private static TelemetrySnapshot Parse(RawFixtureBuilder fixture)
            => Parse(fixture, DateTimeOffset.UtcNow);

        private static TelemetrySnapshot Parse(RawFixtureBuilder fixture, DateTimeOffset capturedAt)
        {
            TelemetryReadResult result = new SharedMemoryParser().Parse(fixture.Buffer, capturedAt);
            if (result.Status != TelemetryReadStatus.Success || result.Snapshot == null)
            {
                throw new InvalidOperationException("Fixture did not parse: " + result.Status + " " + result.Message);
            }
            return result.Snapshot;
        }

        private static DateTimeOffset FixedTime()
            => new DateTimeOffset(2026, 9, 1, 4, 0, 0, TimeSpan.Zero);

        private static RaceControlUpdate ObserveControl(
            RaceControlAnalyzer analyzer,
            RawFixtureBuilder fixture,
            DateTimeOffset time,
            int generation = 0)
        {
            TelemetrySnapshot snapshot = Parse(fixture, time);
            return analyzer.Observe(snapshot, Classify(snapshot), generation, time);
        }

        private static ParticipantSnapshot ResolveLocal(TelemetrySnapshot snapshot)
        {
            LocalParticipantResolution result = new LocalParticipantResolver().Resolve(snapshot);
            if (!result.IsValid || result.Participant == null)
            {
                throw new InvalidOperationException(result.Reason);
            }
            return result.Participant;
        }

        private static LeagueClassification Classify(TelemetrySnapshot snapshot)
            => new LeagueClassificationResolver().Resolve(snapshot, ResolveLocal(snapshot));

        private static void AssertTrue(bool value)
        {
            if (!value) throw new InvalidOperationException("Expected true.");
        }

        private static void AssertFalse(bool value)
        {
            if (value) throw new InvalidOperationException("Expected false.");
        }

        private static void AssertNull(object? value)
        {
            if (value != null) throw new InvalidOperationException("Expected null, got " + value + ".");
        }

        private static void AssertNotNull(object? value)
        {
            if (value == null) throw new InvalidOperationException("Expected non-null.");
        }

        private static void AssertEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
            }
        }
    }
}
