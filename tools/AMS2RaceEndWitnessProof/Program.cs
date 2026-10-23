using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AMS2LeagueActivity.Tests;
using AMS2LeagueClient.Core.SessionWitness;

namespace AMS2RaceEndWitnessProof
{
    // Writes the session witnesses several observers upload for one race end,
    // through the real capture engine and payload builder. The portal test
    // tests/e2e_race_end_mariadb.php replays them against the real API.
    //   Alpha wins and leaves 5 s later; Bravo and Charlie finish; Delta stops
    //   on track; Echo never starts. Scenarios differ in when the others leave.
    internal static class Program
    {
        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset LeaderFinish = Start.AddMinutes(30);

        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: AMS2RaceEndWitnessProof <output directory>");
                return 2;
            }
            Directory.CreateDirectory(args[0]);
            foreach (string scenario in new[] { "grace", "hostEndsEarly", "allFinish" })
            {
                foreach (var observer in Observers(scenario))
                {
                    SessionWitnessRecord witness = Capture(scenario, observer.Key, observer.Value);
                    File.WriteAllBytes(Path.Combine(args[0], scenario + "-" + observer.Key + ".json"), SessionWitnessUploadPayloadBuilder.Build(witness));
                    Console.WriteLine(scenario + " " + observer.Key + ": stable=" + witness.Session.RaceResult!.Stable + " rows="
                        + string.Join(",", witness.Session.RaceResult.Participants.Select(p => p.NameSnapshot + ":" + p.ResultState)));
                }
            }
            return 0;
        }

        private static Dictionary<string, DateTimeOffset> Observers(string scenario)
        {
            DateTimeOffset stayed = scenario == "hostEndsEarly" ? LeaderFinish.AddSeconds(120) : LeaderFinish.AddSeconds(320);
            return new Dictionary<string, DateTimeOffset>
            {
                ["pc-alpha"] = LeaderFinish.AddSeconds(5),
                ["pc-bravo"] = stayed,
                ["pc-charlie"] = stayed,
                ["pc-delta"] = stayed
            };
        }

        private static SessionWitnessRecord Capture(string scenario, string observer, DateTimeOffset leaveAt)
        {
            bool withIdle = scenario != "allFinish";
            var fixture = new ActivityFixtureSnapshot
            {
                CapturedAtUtc = Start, RaceState = "NotStarted", Track = "Nurburgring_2025", Layout = "Nurb_GP_2025",
                SessionDuration = 30, Participants = new List<ActivityFixtureParticipant>()
            };
            string[] names = withIdle ? new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" } : new[] { "Alpha", "Bravo", "Charlie", "Delta" };
            for (int i = 0; i < names.Length; i++)
                fixture.Participants.Add(new ActivityFixtureParticipant { Name = names[i], Position = (uint)(i + 1), RaceState = "NotStarted", Vehicle = "BMW M4 GT4", VehicleClass = "GT4" });
            fixture.Participants.Add(new ActivityFixtureParticipant { Name = "Safety Car (AI)", Position = 0, RaceState = "Racing", Vehicle = "Camaro SafetyCar", VehicleClass = "SafetyCar" });

            var steps = new List<KeyValuePair<DateTimeOffset, Action<ActivityFixtureSnapshot>>>();
            void Step(DateTimeOffset at, Action<ActivityFixtureSnapshot> change) => steps.Add(new KeyValuePair<DateTimeOffset, Action<ActivityFixtureSnapshot>>(at, change));
            Step(Start, f => { });
            Step(Start.AddSeconds(2), f => { f.RaceState = "Racing"; foreach (var p in f.Participants.Take(4)) p.RaceState = "Racing"; });
            for (int minute = 5; minute < 30; minute += 5)
            {
                uint lap = (uint)(minute * 2 / 3);
                Step(Start.AddMinutes(minute), f => { foreach (var p in f.Participants.Take(4)) { p.LapsCompleted = lap; p.CurrentLap = lap + 1; p.BestLapTime = 90 + p.Position; } });
            }
            Step(LeaderFinish, f => { f.Participants[0].RaceState = "Finished"; f.Participants[0].LapsCompleted = 20; f.Participants[1].LapsCompleted = 19; f.Participants[2].LapsCompleted = 19; f.Participants[3].LapsCompleted = 18; });
            Step(LeaderFinish.AddSeconds(20), f => { f.Participants[1].RaceState = "Finished"; f.Participants[1].LapsCompleted = 20; });
            Step(LeaderFinish.AddSeconds(45), f => { f.Participants[2].RaceState = "Finished"; f.Participants[2].LapsCompleted = 20; });
            if (scenario == "allFinish") Step(LeaderFinish.AddSeconds(90), f => { f.Participants[3].RaceState = "Finished"; f.Participants[3].LapsCompleted = 19; });
            for (int second = 60; second <= 300; second += 30) Step(LeaderFinish.AddSeconds(second), f => { });

            var engine = new SessionWitnessCaptureEngine("install-" + observer, "race-end-proof");
            uint sequence = 0;
            foreach (var step in steps.OrderBy(value => value.Key).Where(value => value.Key < leaveAt))
            {
                step.Value(fixture);
                fixture.CapturedAtUtc = step.Key;
                fixture.CurrentTime = (float)(step.Key - Start).TotalSeconds;
                engine.Observe(fixture.ToSnapshot(sequence += 2));
            }
            SessionWitnessRecord witness = engine.Close(leaveAt, "SESSION_RESET").FinalizedWitness
                ?? throw new InvalidOperationException("No witness for " + observer);
            // The runtime sets the play mode from the AMS2 online log before upload.
            witness.RaceMode = "MULTIPLAYER";
            return witness;
        }
    }
}
