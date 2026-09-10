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
                FileLogger[] loggers = roots.Select(root => new FileLogger(Path.Combine(root, "logs"))).ToArray();
                ActivityCaptureRuntime[] runtimes = roots.Select((root, index) => new ActivityCaptureRuntime(root,
                    "client-race-batch-" + index, "0.4.5", loggers[index],
                    transports[index], AutomaticModeTests.CreateMultiplayerDetector(root))).ToArray();
                string[] ids = new string[2];
                try
                {
                    foreach (var runtime in runtimes) AssertTrue(runtime.CanInstallUpdate);
                    var fixture = new RawFixtureBuilder(5).SetViewedIndex(0).SetSession(SessionState.Practice)
                        .SetParticipant(4, true, "Safety Car", 0, 0, 1, RaceState.Racing, PitMode.None)
                        .SetParticipantVehicle(4, "Camaro SafetyCar", "SafetyCar");
                    runtimes[0].Observe(Parse(fixture, start));
                    ids[0] = runtimes[0].CurrentTelemetryIdentity!.SessionId;
                    AssertFalse(runtimes[0].CanInstallUpdate); AssertFalse(runtimes[0].TryReserveUpdateExit());
                    Thread.Sleep(65);
                    fixture.SetSession(SessionState.Qualify);
                    runtimes[0].Observe(Parse(fixture, start.AddSeconds(10)));
                    AssertFalse(runtimes[0].CanInstallUpdate);
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
                    foreach (var runtime in runtimes) { AssertTrue(runtime.CurrentTelemetryIdentity != null); AssertFalse(runtime.CanInstallUpdate); }

                    for (int index = 0; index < 4; index++)
                        fixture.SetParticipant(index, true, "DRIVER_" + index, (uint)(index + 1), 10, 11, RaceState.Finished, PitMode.None);
                    foreach (var runtime in runtimes) runtime.Observe(Parse(fixture, start.AddSeconds(70)));
                    foreach (var runtime in runtimes) runtime.Observe(Parse(fixture, start.AddSeconds(72)));
                    foreach (var runtime in runtimes) AssertNull(runtime.CurrentTelemetryIdentity);
                    foreach (var runtime in runtimes) AssertTrue(runtime.CanInstallUpdate);
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var capture = (FutureTelemetryCaptureRuntime)typeof(ActivityCaptureRuntime).GetField("_futureTelemetry", flags)!.GetValue(runtimes[0])!;
                    var ledgers = (System.Collections.IDictionary)typeof(FutureTelemetryCaptureRuntime).GetField("_attemptLedgers", flags)!.GetValue(capture)!;
                    object ledgerState = ledgers.Values.Cast<object>().Single();
                    var ack = ledgerState.GetType().GetMethod("MarkFinalizeAcknowledged")!;
                    ack.Invoke(ledgerState, new object[] { false });
                    try { AssertFalse(runtimes[0].CanInstallUpdate); AssertFalse(runtimes[0].TryReserveUpdateExit()); }
                    finally { ack.Invoke(ledgerState, new object[] { true }); }
                    AssertTrue(runtimes[0].CanInstallUpdate);
                    Console.WriteLine("PROOF update idle/active/stable-result-without-finalize-ACK/finalize-ACK gates checked");
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
                        gate.Refresh(items);
                        AssertTrue(gate.Allows(ids[index]));
                        var originalStatus = witnessItem.State.Status;
                        var originalHttp = witnessItem.State.LastHttpStatus;
                        witnessItem.State.Status = ActivityUploadStatus.QUARANTINED;
                        witnessItem.State.LastHttpStatus = 401;
                        gate.Refresh(items); AssertTrue(gate.Allows(ids[index]));
                        AssertEqual(ActivityUploadStatus.QUARANTINED, witnessItem.State.Status);
                        witnessItem.State.LastHttpStatus = 403;
                        gate.Refresh(items); AssertFalse(gate.Allows(ids[index]));
                        witnessItem.State.Status = ActivityUploadStatus.CONFLICT;
                        witnessItem.State.LastHttpStatus = 409;
                        gate.Refresh(items); AssertFalse(gate.Allows(ids[index]));
                        witnessItem.State.Status = originalStatus; witnessItem.State.LastHttpStatus = originalHttp;
                        gate.Refresh(items);
                        // A different capture cannot be unlocked by an older finished race.
                        AssertFalse(gate.Allows("unrelated-race"));
                        string ledgerPath = Directory.GetFiles(Path.Combine(roots[index], "future-telemetry", "attempt-ledgers")).Single();
                        byte[] ledger = File.ReadAllBytes(ledgerPath);
                        File.WriteAllText(ledgerPath, "{}");
                        gate.Refresh(items); AssertTrue(gate.Allows(ids[index]));
                        File.Delete(ledgerPath);
                        gate.Refresh(items); AssertTrue(gate.Allows(ids[index]));
                        string finalChunk = Directory.GetFiles(Path.Combine(roots[index], "future-telemetry"),
                            "*-0051.a2ct.gz", SearchOption.AllDirectories).Single();
                        // Isolated copy: a durable journal must not mask a later
                        // integrity conflict, while delivery-only 401 stays harmless.
                        string archiveRoot = Path.Combine(roots[index], "future-telemetry");
                        string proofRoot = Path.Combine(directory, "conflict-proof-" + index);
                        foreach (string chunk in Directory.GetFiles(Path.GetDirectoryName(finalChunk)!, "*.a2ct.gz"))
                        {
                            string relative = Path.GetRelativePath(archiveRoot, chunk);
                            string target = Path.Combine(proofRoot, relative);
                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                            File.Copy(chunk, target);
                            string sidecar = chunk.Replace(".a2ct.gz", ".upload.json", StringComparison.Ordinal);
                            string targetSidecar = target.Replace(".a2ct.gz", ".upload.json", StringComparison.Ordinal);
                            File.Copy(sidecar + ".commit", targetSidecar + ".commit");
                            File.Copy(sidecar + ".commit", targetSidecar);
                        }
                        string proofMetadata = Path.Combine(proofRoot, Path.GetRelativePath(archiveRoot, finalChunk))
                            .Replace(".a2ct.gz", ".upload.json", StringComparison.Ordinal);
                        var state = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(proofMetadata));
                        bool Proof() => CompactArchiveEvidence.HasDurableFinalize(proofRoot, state.SessionId,
                            state.SessionFingerprint, state.WitnessId, state.AttemptId);
                        using (var replacementHandle = new FileStream(proofMetadata, FileMode.Open, FileAccess.ReadWrite,
                            FileShare.ReadWrite | FileShare.Delete))
                            AssertTrue(Proof());
                        state.Status = TelemetryUploadStatus.FAILED_RETRYABLE; state.LastHttpStatus = 401;
                        File.WriteAllBytes(proofMetadata, TelemetryChunkSerializer.SerializeMetadata(state));
                        AssertTrue(Proof());
                        state.Status = TelemetryUploadStatus.CONFLICT; state.LastHttpStatus = 409;
                        File.WriteAllBytes(proofMetadata, TelemetryChunkSerializer.SerializeMetadata(state));
                        AssertFalse(Proof());
                        byte[] finalBytes = File.ReadAllBytes(finalChunk);
                        File.WriteAllBytes(finalChunk, new byte[] { 1, 2, 3 });
                        gate.Refresh(items); AssertFalse(gate.Allows(ids[index]));
                        File.WriteAllBytes(finalChunk, finalBytes);
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
                finally { foreach (var runtime in runtimes) runtime.Dispose(); foreach (var logger in loggers) logger.Dispose(); }
            });
        }
    }
}
