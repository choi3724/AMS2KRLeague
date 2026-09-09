using System;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void AllDrivingModesRemainVisibleWithoutTimer()
        {
            foreach (SessionState session in new[] { SessionState.Practice, SessionState.Test, SessionState.Qualify,
                SessionState.FormationLap, SessionState.Race, SessionState.TimeAttack })
            foreach (SessionPlayMode mode in Enum.GetValues<SessionPlayMode>())
            foreach (RaceState state in new[] { RaceState.NotStarted, RaceState.Racing })
            {
                var fixture = new RawFixtureBuilder(1).SetViewedIndex(0).SetSession(session)
                    .SetGameState(GameState.InGamePlaying).SetLapsInEvent(0).SetSessionTiming(0, 0, -1)
                    .SetGlobalRaceState(state).SetParticipant(0, true, "LOCAL", 1, 0, 1, state, PitMode.None);
                TelemetrySnapshot snapshot = Parse(fixture);
                var controller = new MultiplayerWaitingOverlayController();
                var decision = controller.Observe(snapshot, 1, FixedTime(), mode);
                AssertEqual(MultiplayerOverlayMode.Gameplay, decision.Mode);
                AssertNull(decision.Waiting);
                AssertEqual("—", decision.RemainingDisplayTextOverride);
                ParticipantSnapshot local = ResolveLocal(snapshot);
                AssertTrue(Classify(snapshot).IsLocalEligible);
                var timing = OverlayViewModel.Build(snapshot, local, Classify(snapshot), 30, 20, false, "TEST",
                    eventTimeRemainingOverride: decision.EffectiveRemainingSeconds,
                    eventTimeRemainingTextOverride: decision.RemainingDisplayTextOverride);
                AssertEqual("—", OverlayShellViewModel.Build(snapshot, timing, null, false).Session.PrimaryValue);
                AssertEqual(MultiplayerOverlayMode.Waiting, controller.Observe(
                    Parse(fixture.SetGameState(GameState.InGameMenuTimeTicking)), 2, FixedTime(), mode).Mode);
                foreach (GameState hidden in new[] { GameState.FrontEnd, GameState.InGamePaused, GameState.InGameReplay })
                    AssertEqual(MultiplayerOverlayMode.Hidden, controller.Observe(
                        Parse(fixture.SetGameState(hidden)), 3, FixedTime(), mode).Mode);
            }
        }
    }
}
