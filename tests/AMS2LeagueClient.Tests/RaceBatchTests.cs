using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void RaceBatchCompletionAndLateJoin()
        {
            WithTemporaryDirectory(directory =>
            {
                DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-3);
                var transports = new[] { new DualUploadFixtureTransport(), new DualUploadFixtureTransport() };
                string[] roots = { Path.Combine(directory, "early"), Path.Combine(directory, "late") };
                string unfinished = CreatePendingTelemetryChunk(Path.Combine(roots[0], "future-telemetry"), start);
                ActivityCaptureRuntime[] runtimes = roots.Select((root, index) => new ActivityCaptureRuntime(root,
                    "client-race-batch-" + index, "0.4.5", new FileLogger(Path.Combine(root, "logs")),
                    transports[index], AutomaticModeTests.CreateMultiplayerDetector(root))).ToArray();
                string[] ids = new string[2];
                try
                {
                    var fixture = new RawFixtureBuilder(5).SetViewedIndex(0).SetSession(SessionState.Practice)
                        .SetParticipant(4, true, "Safety Car", 0, 0, 1, RaceState.Racing, PitMode.None)
                        .SetParticipantVehicle(4, "Camaro SafetyCar", "SafetyCar");
                    runtimes[0].Observe(Parse(fixture, start));
                    ids[0] = runtimes[0].CurrentTelemetryIdentity!.SessionId;
                    Thread.Sleep(65);
                    fixture.SetSession(SessionState.Qualify);
                    runtimes[0].Observe(Parse(fixture, start.AddSeconds(10)));
                    Thread.Sleep(65);
                    fixture.SetSession(SessionState.Race);
                    runtimes[0].Observe(Parse(fixture, start.AddSeconds(30)));
                    runtimes[1].Observe(Parse(fixture, start.AddSeconds(40)));
                    ids[1] = runtimes[1].CurrentTelemetryIdentity!.SessionId;
                    // A personal finish is not the whole-field race end.
                    fixture.SetGlobalRaceState(RaceState.Finished)
                        .SetParticipant(0, true, "DRIVER_0", 1, 10, 11, RaceState.Finished, PitMode.None);
                    foreach (var runtime in runtimes) runtime.Observe(Parse(fixture, start.AddSeconds(60)));
                    Thread.Sleep(5500); // Allow the real background upload poll while the race is unfinished.
                    foreach (var transport in transports) AssertEqual(0, transport.TelemetryCalls);
                    foreach (var runtime in runtimes) AssertTrue(runtime.CurrentTelemetryIdentity != null);

                    for (int index = 0; index < 4; index++)
                        fixture.SetParticipant(index, true, "DRIVER_" + index, (uint)(index + 1), 10, 11, RaceState.Finished, PitMode.None);
                    foreach (var runtime in runtimes) runtime.Observe(Parse(fixture, start.AddSeconds(70)));
                    foreach (var runtime in runtimes) runtime.Observe(Parse(fixture, start.AddSeconds(72)));
                    foreach (var runtime in runtimes) AssertNull(runtime.CurrentTelemetryIdentity);
                    foreach (var runtime in runtimes) runtime.Observe(Parse(fixture, start.AddSeconds(80)));
                    foreach (var runtime in runtimes) AssertNull(runtime.CurrentTelemetryIdentity);
                    AssertTrue(SpinWait.SpinUntil(() => transports.All(value => value.TelemetryCalls > 0), TimeSpan.FromSeconds(12)));
                    AssertEqual(TelemetryUploadStatus.PENDING, ReadTelemetryStatus(unfinished));
                    for (int index = 0; index < 2; index++)
                    {
                        var queue = new ActivityUploadQueue(Path.Combine(roots[index], "upload-queue"));
                        var items = queue.Scan();
                        var witnessItem = items.Single(item => item.Metadata.Endpoint == Cafe24Routes.SessionWitnesses);
                        using var json = JsonDocument.Parse(witnessItem.PayloadUtf8);
                        var witness = json.RootElement;
                        AssertEqual(start.AddSeconds(index == 0 ? 0 : 40), witness.GetProperty("captureStartedAtUtc").GetDateTimeOffset());
                        AssertEqual(start.AddSeconds(72), witness.GetProperty("captureEndedAtUtc").GetDateTimeOffset());
                        AssertEqual("RACE_RESULTS_READY", witness.GetProperty("session").GetProperty("closingReason").GetString());
                        AssertTrue(witness.GetProperty("session").GetProperty("raceResult").GetProperty("stable").GetBoolean());
                        var resultRows = witness.GetProperty("session").GetProperty("raceResult").GetProperty("participants");
                        AssertEqual(5, resultRows.GetArrayLength());
                        AssertEqual(0u, resultRows.EnumerateArray().Single(row => row.GetProperty("slot").GetInt32() == 4).GetProperty("position").GetUInt32());
                        var gate = new RaceUploadCompletionGate(Path.Combine(roots[index], "future-telemetry"));
                        gate.Refresh(items); AssertTrue(gate.Allows(ids[index]));
                        // A different capture cannot be unlocked by an older finished race.
                        AssertFalse(gate.Allows("unrelated-race"));
                        string ledgerPath = Directory.GetFiles(Path.Combine(roots[index], "future-telemetry", "attempt-ledgers")).Single();
                        byte[] ledger = File.ReadAllBytes(ledgerPath);
                        File.WriteAllText(ledgerPath, "{}");
                        gate.Refresh(items); AssertFalse(gate.Allows(ids[index]));
                        File.WriteAllBytes(ledgerPath, ledger);
                        gate.Refresh(items); AssertTrue(gate.Allows(ids[index]));
                    }
                    fixture.SetSession(SessionState.Practice).SetGlobalRaceState(RaceState.Racing);
                    for (int index = 0; index < 4; index++)
                        fixture.SetParticipant(index, true, "DRIVER_" + index, (uint)(index + 1), 0, 1, RaceState.Racing, PitMode.None);
                    runtimes[0].Observe(Parse(fixture, start.AddSeconds(90)));
                    AssertTrue(runtimes[0].CurrentTelemetryIdentity != null);
                    AssertFalse(runtimes[0].CurrentTelemetryIdentity!.SessionId == ids[0]);
                    Console.WriteLine("PROOF race-batch upload-before-finish=0; starts=0s/40s ends=72s/72s; repeated-results=no-new-capture; durable-recovery=PASS");
                }
                finally { foreach (var runtime in runtimes) runtime.Dispose(); }
            });
        }
    }
}
