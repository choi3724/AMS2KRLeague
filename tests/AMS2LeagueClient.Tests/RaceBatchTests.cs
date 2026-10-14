using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                    fixture.SetGlobalRaceState(RaceState.Finished);
                    for (int i = 0; i < 4; i++)
                        fixture.SetParticipant(i, true, "DRIVER_" + i, (uint)(i + 1), 3, 4, RaceState.Finished, PitMode.None);
                    runtimes[0].Observe(Parse(fixture, start.AddSeconds(20)));
                    fixture.SetSession(SessionState.Race);
                    runtimes[0].Observe(Parse(fixture, start.AddSeconds(25)));
                    fixture.SetGlobalRaceState(RaceState.NotStarted);
                    for (int i = 0; i < 4; i++)
                        fixture.SetParticipant(i, true, "DRIVER_" + i, (uint)(i + 1), 0, 1, RaceState.NotStarted, PitMode.None);
                    runtimes[0].Observe(Parse(fixture, start.AddSeconds(30)));
                    fixture.SetGlobalRaceState(RaceState.Racing);
                    for (int i = 0; i < 4; i++)
                        fixture.SetParticipant(i, true, "DRIVER_" + i, (uint)(i + 1), 0, 1, RaceState.Racing, PitMode.None);
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
                    // Delivery was observed above. Stop its writers before the following
                    // integrity checks deliberately rewrite the same archive files.
                    foreach (var runtime in runtimes)
                    {
                        ((CancellationTokenSource)typeof(ActivityCaptureRuntime).GetField("_uploadCancellation", flags)!.GetValue(runtime)!).Cancel();
                        var upload = (System.Threading.Tasks.Task)typeof(ActivityCaptureRuntime).GetField("_uploadTask", flags)!.GetValue(runtime)!;
                        var mode = (System.Threading.Tasks.Task)typeof(ActivityCaptureRuntime).GetField("_modeTask", flags)!.GetValue(runtime)!;
                        AssertTrue(SpinWait.SpinUntil(() => upload.IsCompleted && mode.IsCompleted, TimeSpan.FromSeconds(12)));
                        try { System.Threading.Tasks.Task.WhenAll(upload, mode).GetAwaiter().GetResult(); }
                        catch (OperationCanceledException) { }
                    }
                    Console.WriteLine("PROOF real upload observed; delivery writers drained before archive integrity mutations");
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
                        if (index == 0) AssertEqual(5, witness.GetProperty("session").GetProperty("qualifying").GetProperty("participants").GetArrayLength());
                        var resultRows = witness.GetProperty("session").GetProperty("raceResult").GetProperty("participants");
                        AssertEqual(5, resultRows.GetArrayLength());
                        AssertEqual(0u, resultRows.EnumerateArray().Single(row => row.GetProperty("slot").GetInt32() == 4).GetProperty("position").GetUInt32());
                        var gate = new RaceUploadCompletionGate(Path.Combine(roots[index], "future-telemetry"), start.AddMinutes(-10));
                        gate.Refresh(items);
                        AssertTrue(gate.Allows(ids[index]));
                        if (index == 1)
                        {
                            string activationRoot = Path.Combine(roots[index], "phase-activation-proof");
                            var activation = new RaceUploadCompletionGate(activationRoot);
                            AssertEqual("ACTIVE", activation.PhaseLinkPolicyStatus);
                            string activationPath = Path.Combine(activationRoot, "phase-link-policy.json");
                            byte[] activationBytes = File.ReadAllBytes(activationPath);
                            AssertEqual("ACTIVE", new RaceUploadCompletionGate(activationRoot).PhaseLinkPolicyStatus);
                            AssertTrue(activationBytes.SequenceEqual(File.ReadAllBytes(activationPath)));
                            File.WriteAllText(activationPath, "{}");
                            AssertEqual("INVALID_ACTIVATION", new RaceUploadCompletionGate(activationRoot).PhaseLinkPolicyStatus);
                            // A qualifying witness closes before a separately captured race.
                            // Rebuild the gate from disk to prove restart-safe queue recovery.
                            JsonObject qualifying = (JsonObject)JsonNode.Parse(System.Text.Encoding.UTF8.GetString(witnessItem.PayloadUtf8.Span))!;
                            qualifying["captureSessionId"] = "capture-prior-qualifying";
                            qualifying["captureStartedAtUtc"] = start.AddSeconds(10).ToString("O");
                            qualifying["captureEndedAtUtc"] = start.AddSeconds(39).ToString("O");
                            qualifying["sourceRole"] = "HOST";
                            qualifying["session"]!["raceResult"] = null;
                            qualifying["session"]!["qualifying"] = new JsonObject();
                            ActivityUploadItem prior = queue.Enqueue("qual-proof", Cafe24Routes.SessionWitnesses,
                                "qualifying-proof-0001", qualifying.ToJsonString()).Item;
                            var restored = new RaceUploadCompletionGate(Path.Combine(roots[index], "future-telemetry"), start.AddMinutes(-10));
                            // A pre-upgrade pending phase has no local policy marker.
                            // Installing the fix must not silently send R11/R12-style backlog.
                            prior.Metadata.PhaseLinkPolicyVersion = 0;
                            restored.Refresh(new[] { witnessItem, prior });
                            AssertFalse(restored.Allows(prior));
                            prior.Metadata.PhaseLinkPolicyVersion = 1;
                            restored.Refresh(queue.Scan());
                            AssertTrue(restored.Allows(prior));
                            // An old provisional capture queued after an upgrade also
                            // gets new metadata, but its capture time predates activation.
                            var upgraded = new RaceUploadCompletionGate(Path.Combine(roots[index], "future-telemetry"), start.AddSeconds(20));
                            upgraded.Refresh(queue.Scan());
                            AssertFalse(upgraded.Allows(prior));
                            AssertFalse(restored.Allows("capture-prior-qualifying")); // no compact final ACK

                            JsonObject player = (JsonObject)JsonNode.Parse(System.Text.Encoding.UTF8.GetString(prior.PayloadUtf8.Span))!;
                            player["captureSessionId"] = "capture-player-qualifying";
                            player["sourceRole"] = "PLAYER";
                            ActivityUploadItem playerItem = queue.Enqueue("player-qual-proof", Cafe24Routes.SessionWitnesses,
                                "qualifying-player-proof", player.ToJsonString()).Item;
                            restored.Refresh(queue.Scan());
                            AssertTrue(restored.Allows(playerItem));
                            JsonObject changedEntrants = (JsonObject)player.DeepClone();
                            changedEntrants["captureSessionId"] = "capture-disjoint-qualifying";
                            changedEntrants["rosterNames"] = JsonSerializer.SerializeToNode(new[] { "Entirely different one", "Entirely different two" });
                            ActivityUploadItem disjoint = queue.Enqueue("disjoint-phase", Cafe24Routes.SessionWitnesses,
                                "disjoint-phase-proof", changedEntrants.ToJsonString()).Item;
                            restored.Refresh(new[] { witnessItem, disjoint });
                            AssertTrue(restored.Allows(disjoint));
                            var independent = new RaceUploadCompletionGate(Path.Combine(roots[index], "independent-observer"),
                                start.AddMinutes(-10), start.AddMinutes(-10));
                            independent.Refresh(new[] { disjoint });
                            AssertTrue(independent.Allows(disjoint));
                            AssertFalse(independent.Allows("capture-disjoint-qualifying"));
                            JsonObject resultOnlyPayload = (JsonObject)JsonNode.Parse(disjoint.PayloadUtf8.Span)!;
                            resultOnlyPayload.Remove("captureSessionId");
                            resultOnlyPayload["witnessId"] = "witness-without-replay";
                            ActivityUploadItem resultOnly = queue.Enqueue("witness-without-replay", Cafe24Routes.SessionWitnesses,
                                "witness-without-replay-idempotency", resultOnlyPayload.ToJsonString()).Item;
                            resultOnly.Metadata.PhaseLinkPolicyVersion = 2;
                            independent.Refresh(new[] { resultOnly });
                            AssertTrue(independent.Allows(resultOnly));
                            AssertFalse(independent.Allows("witness-only:witness-without-replay"));
                            resultOnly.Metadata.PhaseLinkPolicyVersion = 1;
                            independent.Refresh(new[] { resultOnly });
                            AssertFalse(independent.Allows(resultOnly));
                            disjoint.Metadata.PhaseLinkPolicyVersion = 1;
                            independent.Refresh(new[] { disjoint });
                            AssertFalse(independent.Allows(disjoint));
                            disjoint.Metadata.PhaseLinkPolicyVersion = 2;
                            var afterCapture = new RaceUploadCompletionGate(Path.Combine(roots[index], "post-capture-policy"),
                                start.AddMinutes(-10), start.AddMinutes(1));
                            afterCapture.Refresh(new[] { disjoint });
                            AssertFalse(afterCapture.Allows(disjoint));
                            Console.WriteLine("PROOF new observer witness delivers independently; compact ACK and historical queue activation remain enforced");

                            JsonObject early = (JsonObject)JsonNode.Parse(System.Text.Encoding.UTF8.GetString(prior.PayloadUtf8.Span))!;
                            early["captureSessionId"] = "capture-superseded-qualifying";
                            early["captureStartedAtUtc"] = start.AddSeconds(2).ToString("O");
                            early["captureEndedAtUtc"] = start.AddSeconds(10).ToString("O");
                            var earlyNames = (JsonArray)early["rosterNames"]!;
                            while (earlyNames.Count > 3) earlyNames.RemoveAt(earlyNames.Count - 1);
                            ActivityUploadItem superseded = queue.Enqueue("early-qual-proof", Cafe24Routes.SessionWitnesses,
                                "qualifying-proof-0003", early.ToJsonString()).Item;
                            restored.Refresh(queue.Scan());
                            AssertFalse(restored.Allows(superseded));

                            JsonObject practice = (JsonObject)JsonNode.Parse(System.Text.Encoding.UTF8.GetString(prior.PayloadUtf8.Span))!;
                            practice["captureSessionId"] = "capture-prior-practice";
                            practice["captureStartedAtUtc"] = start.AddMinutes(-5).ToString("O");
                            practice["captureEndedAtUtc"] = start.AddSeconds(9).ToString("O");
                            practice["session"]!["qualifying"] = null;
                            practice["session"]!["sessionTypesObserved"] = JsonSerializer.SerializeToNode(new[] { "PRACTICE" });
                            ActivityUploadItem practiced = queue.Enqueue("practice-proof", Cafe24Routes.SessionWitnesses,
                                "qualifying-proof-0004", practice.ToJsonString()).Item;
                            restored.Refresh(queue.Scan());
                            AssertTrue(restored.Allows(practiced));

                            JsonObject unrelated = (JsonObject)JsonNode.Parse(System.Text.Encoding.UTF8.GetString(prior.PayloadUtf8.Span))!;
                            unrelated["captureSessionId"] = "capture-unrelated";
                            unrelated["eventFingerprint"] = "different-meeting";
                            ActivityUploadItem other = queue.Enqueue("unrelated-proof", Cafe24Routes.SessionWitnesses,
                                "qualifying-proof-0002", unrelated.ToJsonString()).Item;
                            restored.Refresh(queue.Scan());
                            AssertFalse(restored.Allows(other));

                            var abortedQueue = new ActivityUploadQueue(Path.Combine(roots[index], "aborted-queue"));
                            ActivityUploadItem abortedPhase = abortedQueue.Enqueue("aborted-qual", Cafe24Routes.SessionWitnesses,
                                "aborted-qual-proof", qualifying.ToJsonString()).Item;
                            JsonObject abortedRace = (JsonObject)JsonNode.Parse(System.Text.Encoding.UTF8.GetString(witnessItem.PayloadUtf8.Span))!;
                            abortedRace["session"]!["raceResult"]!["stable"] = false;
                            abortedQueue.Enqueue("aborted-race", Cafe24Routes.SessionWitnesses,
                                "aborted-race-proof", abortedRace.ToJsonString());
                            var abortedGate = new RaceUploadCompletionGate(Path.Combine(roots[index], "aborted-archive"), start.AddMinutes(-10));
                            abortedGate.Refresh(abortedQueue.Scan());
                            AssertFalse(abortedGate.Allows(abortedPhase));

                            // A new qualifying capture waits for a separate stable
                            // race, then its witness ACK survives a process restart.
                            JsonObject phasePayload = (JsonObject)JsonNode.Parse(System.Text.Encoding.UTF8.GetString(prior.PayloadUtf8.Span))!;
                            phasePayload["witnessId"] = "witness-phase-delivery";
                            phasePayload["attemptId"] = "attempt-phase-delivery";
                            phasePayload["sessionFingerprint"] = "session-phase-delivery";
                            string phaseRoot = Path.Combine(roots[index], "phase-delivery");
                            string phaseArchiveRoot = Path.Combine(phaseRoot, "archive");
                            string originalLedger = Directory.GetFiles(Path.Combine(roots[index],
                                "future-telemetry", "attempt-ledgers"), "*.attempt-loss.json").Single();
                            string phaseLedgerRoot = Path.Combine(phaseArchiveRoot, "attempt-ledgers");
                            Directory.CreateDirectory(phaseLedgerRoot);
                            File.Copy(originalLedger, Path.Combine(phaseLedgerRoot, Path.GetFileName(originalLedger)));
                            var phaseGate = new RaceUploadCompletionGate(phaseArchiveRoot, start.AddMinutes(-10));
                            var phaseQueue = new ActivityUploadQueue(Path.Combine(phaseRoot, "witness-queue"),
                                uploadEligibility: phaseGate.Allows);
                            ActivityUploadItem phaseItem = phaseQueue.Enqueue("phase-delivery", Cafe24Routes.SessionWitnesses,
                                "phase-delivery-idempotency", phasePayload.ToJsonString()).Item;
                            phaseGate.Refresh(new[] { phaseItem });
                            AssertEqual(0, phaseQueue.GetDueBatch(1, DateTimeOffset.UtcNow).Count);
                            phaseGate.Refresh(new[] { witnessItem, phaseItem });
                            AssertEqual(1, phaseQueue.GetDueBatch(1, DateTimeOffset.UtcNow).Count);
                            AssertEqual(1, new ActivityUploadWorker(phaseQueue, transports[index])
                                .ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Sent);
                            var resumedPhaseQueue = new ActivityUploadQueue(Path.Combine(phaseRoot, "witness-queue"),
                                uploadEligibility: phaseGate.Allows);
                            AssertEqual(ActivityUploadStatus.SENT, resumedPhaseQueue.Scan().Single().State.Status);
                            AssertEqual(0, resumedPhaseQueue.GetDueBatch(1, DateTimeOffset.UtcNow).Count);

                            // Metadata requires this phase's own durable finalize.
                            string telemetryRoot = Path.Combine(phaseRoot, "telemetry");
                            string metadataPath = CreatePendingTelemetryChunk(telemetryRoot);
                            var metadata = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(metadataPath));
                            metadata.SessionId = "capture-prior-qualifying";
                            metadata.SessionFingerprint = "session-phase-delivery";
                            metadata.WitnessId = "witness-phase-delivery";
                            metadata.AttemptId = "attempt-phase-delivery";
                            File.WriteAllBytes(metadataPath, TelemetryChunkSerializer.SerializeMetadata(metadata));
                            var telemetryQueue = new TelemetryChunkUploadQueue(telemetryRoot,
                                uploadEligibility: value => phaseGate.Allows(value.SessionId));
                            AssertEqual(0, telemetryQueue.GetDueBatch(1, DateTimeOffset.UtcNow).Count);
                            string ledgerKey = TelemetryChunkSerializer.StableId(metadata.SessionFingerprint,
                                metadata.WitnessId, metadata.AttemptId).Substring(0, 32);
                            string ledgerDirectory = Path.Combine(phaseArchiveRoot, "attempt-ledgers");
                            Directory.CreateDirectory(ledgerDirectory);
                            File.WriteAllText(Path.Combine(ledgerDirectory, ledgerKey + ".attempt-loss.json"),
                                JsonSerializer.Serialize(new { sessionId = metadata.SessionId, attemptId = metadata.AttemptId,
                                    closeRequested = true, finalizeAcknowledged = true }));
                            phaseGate.Refresh(new[] { witnessItem, phaseItem });
                            AssertTrue(phaseGate.Allows(metadata.SessionId));
                            AssertEqual(1, telemetryQueue.GetDueBatch(1, DateTimeOffset.UtcNow).Count);
                            AssertEqual(1, new TelemetryChunkUploadWorker(telemetryQueue, transports[index])
                                .ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Sent);
                            var resumedTelemetryQueue = new TelemetryChunkUploadQueue(telemetryRoot,
                                uploadEligibility: value => phaseGate.Allows(value.SessionId));
                            AssertEqual(0, resumedTelemetryQueue.GetDueBatch(1, DateTimeOffset.UtcNow).Count);
                            AssertEqual(TelemetryUploadStatus.SENT, ReadTelemetryStatus(metadataPath));
                        }
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
