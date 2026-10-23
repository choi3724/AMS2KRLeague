using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.HostRecording;
using AMS2LeagueClient.Core.SessionWitness;

namespace AMS2LeagueActivity.Tests
{
    internal static class RaceEndClockTests
    {
        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Race end waits for the leader rather than a finished trailing car", TrailingFinish);
            yield return new TestCase("Late join never invents the leader finish clock", LateJoin);
            yield return new TestCase("Unknown late-join finish remains bounded without claiming a full session", LateJoinDeadline);
            yield return new TestCase("Safety car finish and missing leader do not become the winner clock", SafetyAndMissingLeader);
            yield return new TestCase("Safety car positions stay raw while the league leader sets the clock", SafetyPosition);
            yield return new TestCase("Early all-terminal result separates finish, result and collection clocks", EarlyTerminal);
        }

        private static ActivityFixtureSnapshot Fixture() => new ActivityFixtureSnapshot
        {
            CapturedAtUtc = Start, RaceState = "NotStarted", CurrentTime = 0,
            Participants = new List<ActivityFixtureParticipant>
            {
                new ActivityFixtureParticipant { Name = "Winner", Position = 1, RaceState = "NotStarted" },
                new ActivityFixtureParticipant { Name = "Finisher", Position = 2, RaceState = "NotStarted" },
                new ActivityFixtureParticipant { Name = "Idle", Position = 3, RaceState = "NotStarted" },
                new ActivityFixtureParticipant { Name = "Safety", Position = 0, RaceState = "Racing", Vehicle = "Camaro SafetyCar", VehicleClass = "SafetyCar" }
            }
        };
        private static void Step(SessionWitnessCaptureEngine engine, ActivityFixtureSnapshot f, int seconds)
        {
            f.CapturedAtUtc = Start.AddSeconds(seconds);
            engine.Observe(f.ToSnapshot((uint)(seconds * 2)));
        }
        private static SessionWitnessCaptureEngine Begin(ActivityFixtureSnapshot f)
        {
            var engine = new SessionWitnessCaptureEngine("race-end-clock", "test");
            Step(engine, f, 0); f.RaceState = "Racing";
            foreach (var p in f.Participants.Take(2)) p.RaceState = "Racing";
            Step(engine, f, 2); return engine;
        }
        private static HostRaceResult Close(SessionWitnessCaptureEngine e, int second)
            => e.Close(Start.AddSeconds(second), "SESSION_RESET").FinalizedWitness!.Session.RaceResult!;

        private static void TrailingFinish()
        {
            var f = Fixture(); var e = Begin(f);
            f.Participants[1].RaceState = "Finished"; Step(e, f, 100);
            f.Participants[0].RaceState = "Finished"; Step(e, f, 130);
            AssertEx.Equal(Start.AddSeconds(130), Close(e, 140).LeaderFinishedAtUtc!.Value);
        }
        private static void LateJoin()
        {
            var f = Fixture(); f.RaceState = "Racing"; f.Participants[0].RaceState = "Finished";
            f.Participants[1].RaceState = "Racing";
            var e = new SessionWitnessCaptureEngine("late-join-clock", "test"); Step(e, f, 100);
            f.Participants[1].RaceState = "Finished"; Step(e, f, 120);
            AssertEx.True(!Close(e, 130).LeaderFinishedAtUtc.HasValue, "First observation is not a witnessed finish transition.");
        }
        private static void LateJoinDeadline()
        {
            var f = Fixture(); f.RaceState = "Racing"; f.Participants[0].RaceState = "Finished";
            f.Participants[1].RaceState = "Racing";
            var e = new SessionWitnessCaptureEngine("late-deadline", "test"); Step(e, f, 100);
            Step(e, f, 399); AssertEx.True(!e.HasStableRaceResult, "Do not finalize before the observed collection limit.");
            Step(e, f, 400); AssertEx.True(e.HasStableRaceResult, "An unknown finish clock must not leave collection unbounded.");
            var witness = e.Close(Start.AddSeconds(405), "SESSION_RESET").FinalizedWitness!;
            AssertEx.True(!witness.Session.RaceResult!.LeaderFinishedAtUtc.HasValue, "No invented finish time on the wire.");
            AssertEx.True(witness.CaptureCompleteness != SessionWitnessCompleteness.FullSession, "Late-join collection is not a full observed race.");
        }
        private static void SafetyAndMissingLeader()
        {
            var f = Fixture(); var e = Begin(f);
            f.Participants[3].RaceState = "Finished"; Step(e, f, 100);
            AssertEx.True(!Close(e, 110).LeaderFinishedAtUtc.HasValue, "Safety car is not a winner.");
            f = Fixture(); e = Begin(f);
            f.Participants[0].Active = false; f.Participants[0].Name = ""; f.Participants[0].RaceState = "Invalid";
            f.Participants[1].RaceState = "Finished"; Step(e, f, 100);
            AssertEx.True(!Close(e, 110).LeaderFinishedAtUtc.HasValue, "Do not promote a missing leader's follower.");
        }
        private static void SafetyPosition()
        {
            var f = Fixture(); f.Participants[3].Position = 1;
            for (int i = 0; i < 3; i++) f.Participants[i].Position = (uint)(i + 2);
            var e = Begin(f); f.Participants[1].RaceState = "Finished"; Step(e, f, 100);
            f.Participants[0].RaceState = "Finished"; Step(e, f, 130);
            var result = Close(e, 140);
            AssertEx.Equal(Start.AddSeconds(130), result.LeaderFinishedAtUtc!.Value);
            AssertEx.Equal((uint)2, result.Participants.Single(p => p.NameSnapshot == "Winner").Position);
        }
        private static void EarlyTerminal()
        {
            var f = Fixture(); var e = Begin(f);
            f.Participants[0].RaceState = "Finished"; Step(e, f, 100);
            f.Participants[1].RaceState = "Finished"; f.Participants[1].LapsCompleted = 20;
            f.Participants[2].RaceState = "Dnf"; Step(e, f, 120); Step(e, f, 122);
            AssertEx.True(e.HasStableRaceResult, "300 seconds is a ceiling, not a required delay.");
            var witness = e.Close(Start.AddSeconds(125), "SESSION_RESET").FinalizedWitness!;
            using var wire = JsonDocument.Parse(SessionWitnessUploadPayloadBuilder.Build(witness));
            var result = wire.RootElement.GetProperty("session").GetProperty("raceResult");
            AssertEx.Equal(Start.AddSeconds(100), result.GetProperty("leaderFinishedAtUtc").GetDateTimeOffset());
            AssertEx.Equal(Start.AddSeconds(122), result.GetProperty("capturedAtUtc").GetDateTimeOffset());
            AssertEx.Equal(Start.AddSeconds(125), wire.RootElement.GetProperty("captureEndedAtUtc").GetDateTimeOffset());
            AssertEx.Equal((uint)20, witness.Session.RaceResult!.Participants.Single(p => p.NameSnapshot == "Finisher").LapsCompleted);
        }
    }
}
