using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void RaceEndRuntimeDelivery()
        {
            foreach (string scenario in new[] { "partial", "ceiling", "idle-view", "all-terminal" })
            WithTemporaryDirectory(root =>
            {
                DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-10);
                string archive = Path.Combine(root, "future-telemetry");
                Directory.CreateDirectory(archive);
                foreach (var policy in new[] { ("phase-link-policy.json", "ams2-phase-link-policy-v1"),
                    ("observer-upload-policy-v2.json", "ams2-observer-upload-policy-v2") })
                    File.WriteAllText(Path.Combine(archive, policy.Item1), JsonSerializer.Serialize(new {
                        schema = policy.Item2, activatedAtUtc = start.AddMinutes(-1) }));
                var mode = RaceEndMode(root, start);
                using var logger = new FileLogger(Path.Combine(root, "logs"));
                var fixture = new RawFixtureBuilder(scenario == "all-terminal" ? 6 : 7)
                    .SetSession(SessionState.Race).SetViewedIndex(scenario == "idle-view" ? 5 : 0).SetGlobalRaceState(RaceState.NotStarted);
                int safety = scenario == "all-terminal" ? 5 : 6;
                void Driver(int index, RaceState state, uint laps)
                    => fixture.SetParticipant(index, true, "END_DRIVER_" + index, (uint)(index + 1), laps, laps + 1, state, PitMode.None);
                for (int i = 0; i < safety; i++) Driver(i, RaceState.NotStarted, 0);
                fixture.SetParticipant(safety, true, "Safety Car", 0, 0, 1, RaceState.Racing, PitMode.None)
                    .SetParticipantVehicle(safety, "Camaro SafetyCar", "SafetyCar");
                var pending = new ActivityUploadQueue(Path.Combine(root, "upload-queue"));
                ActivityUploadItem witness;
                using (var runtime = new ActivityCaptureRuntime(root, "race-end-installation", "0.9.5", logger, null, mode))
                {
                    runtime.Observe(Parse(fixture, start));
                    fixture.SetGlobalRaceState(scenario == "idle-view" ? RaceState.NotStarted : RaceState.Racing);
                    for (int i = 0; i < 5; i++) Driver(i, RaceState.Racing, 0);
                    runtime.Observe(Parse(fixture, start.AddSeconds(2)));
                    Driver(0, RaceState.Finished, 10); Driver(1, RaceState.Racing, 9);
                    Driver(2, RaceState.Retired, 8); Driver(3, RaceState.Dnf, 7); Driver(4, RaceState.Disqualified, 6);
                    runtime.Observe(Parse(fixture, start.AddSeconds(30)));
                    if (scenario == "partial")
                    {
                        fixture.SetGameState(GameState.FrontEnd).SetSession(SessionState.Invalid);
                        runtime.Observe(Parse(fixture, start.AddSeconds(35)));
                    }
                    else
                    {
                        Driver(1, RaceState.Finished, 10);
                        runtime.Observe(Parse(fixture, start.AddSeconds(50)));
                        runtime.Observe(Parse(fixture, start.AddSeconds(52)));
                        if (scenario == "ceiling" || scenario == "idle-view") runtime.Observe(Parse(fixture, start.AddSeconds(330)));
                        AssertNull(runtime.CurrentTelemetryIdentity);
                        // A car still Racing/NotStarted at the cap is not a new race.
                        runtime.Observe(Parse(fixture, start.AddSeconds(331)));
                        AssertNull(runtime.CurrentTelemetryIdentity);
                    }
                    AssertTrue(SpinWait.SpinUntil(() => pending.Scan().Any(item =>
                        item.Metadata.Endpoint == Cafe24Routes.SessionWitnesses), TimeSpan.FromSeconds(12)));
                    witness = pending.Scan().Single(item => item.Metadata.Endpoint == Cafe24Routes.SessionWitnesses);
                    AssertTrue(runtime.IsActivityUploadAllowed(witness));
                }
                AssertEqual(2, witness.Metadata.PhaseLinkPolicyVersion);
                using (var wire = JsonDocument.Parse(witness.PayloadUtf8))
                {
                    JsonElement result = wire.RootElement.GetProperty("session").GetProperty("raceResult");
                    AssertEqual(scenario != "partial", result.GetProperty("stable").GetBoolean());
                    AssertEqual(start.AddSeconds(30), result.GetProperty("leaderFinishedAtUtc").GetDateTimeOffset());
                    bool capped = scenario == "ceiling" || scenario == "idle-view";
                    DateTimeOffset expected = start.AddSeconds(scenario == "partial" ? 30 : capped ? 330 : 52);
                    AssertEqual(expected, result.GetProperty("capturedAtUtc").GetDateTimeOffset());
                    AssertEqual(start.AddSeconds(scenario == "partial" ? 35 : capped ? 330 : 52),
                        wire.RootElement.GetProperty("captureEndedAtUtc").GetDateTimeOffset());
                    foreach (string terminal in new[] { "Retired", "Dnf", "Disqualified" })
                        AssertTrue(result.GetProperty("participants").EnumerateArray().Any(p =>
                            string.Equals(p.GetProperty("resultState").GetString(), terminal, StringComparison.OrdinalIgnoreCase)));
                }
                var gate = new RaceUploadCompletionGate(archive);
                var clock = new RaceEndUploadClock { UtcNow = DateTimeOffset.UtcNow };
                var queue = new ActivityUploadQueue(pending.Root, clock: clock, uploadEligibility: item =>
                    item.Metadata.Endpoint == Cafe24Routes.SessionWitnesses && gate.Allows(item));
                gate.Refresh(queue.Scan()); AssertTrue(gate.Allows(witness));
                var options = ActivityConnectionOptions.Load(Path.Combine(root, "test-connection.json"));
                options.ApiBaseUrl = "https://fixture.invalid/ams2"; options.BearerToken = new string('a', 32);
                var handler = new RaceEndHttpHandler();
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, "race-end-installation", "0.9.5", http);
                var worker = new ActivityUploadWorker(queue, transport, clock);
                AssertEqual(0, worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Sent);
                AssertEqual(ActivityUploadStatus.FAILED_RETRYABLE, queue.Scan().Single(p => p.Metadata.Endpoint == Cafe24Routes.SessionWitnesses).State.Status);
                clock.UtcNow = clock.UtcNow.AddMinutes(1);
                AssertEqual(1, worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Sent);
                AssertEqual(2, handler.Bodies.Count);
                AssertTrue(handler.Bodies.All(body => body.AsSpan().SequenceEqual(witness.PayloadUtf8.Span)));
                AssertTrue(handler.Keys.All(key => key == witness.Metadata.IdempotencyKey));
                var sent = new ActivityUploadQueue(queue.Root).Scan().Single(p => p.Metadata.Endpoint == Cafe24Routes.SessionWitnesses);
                AssertEqual(ActivityUploadStatus.SENT, sent.State.Status); AssertEqual((int?)201, sent.State.LastHttpStatus);
                AssertEqual(2, sent.State.AttemptCount);
                using (var restarted = new ActivityCaptureRuntime(root, "race-end-installation", "0.9.5", logger, null, RaceEndMode(root, start)))
                    AssertTrue(restarted.IsActivityUploadAllowed(sent));
                var restored = new ActivityUploadQueue(queue.Root);
                restored.Enqueue(witness.Metadata.ActivityId, witness.Metadata.Endpoint, witness.Metadata.IdempotencyKey, witness.PayloadUtf8.ToArray());
                AssertEqual(1, restored.Scan().Count(p => p.Metadata.Endpoint == Cafe24Routes.SessionWitnesses));
                AssertEqual(ActivityUploadStatus.SENT, restored.Scan().Single(p => p.Metadata.Endpoint == Cafe24Routes.SessionWitnesses).State.Status);
                AssertEqual(0, worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Attempted);
                Console.WriteLine("PROOF runtime " + scenario + ": clocks intact; policy-v2 raw witness -> queue -> HTTP503 retry -> HTTP201 ACK -> restart SENT; duplicate uploads=0");
            });
        }

        private static SessionPlayModeDetector RaceEndMode(string root, DateTimeOffset start)
        {
            string logs = Path.Combine(root, "game-log"); Directory.CreateDirectory(logs);
            DateTimeOffset boot = start.AddMinutes(-1);
            string Stamp(DateTimeOffset at) => "[" + at.UtcDateTime.ToString("HH:mm:ss:fff", CultureInfo.InvariantCulture) + "][0x00000001] [Info ] ";
            File.WriteAllText(Path.Combine(logs, "online.log"), "[Session][Platform] PC\n[Session][Exe] .\\AMS2AVX.exe\n[Session][Date] "
                + boot.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + "\n[Session][Time] "
                + boot.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "\n[Session][UTC] "
                + boot.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "\n"
                + Stamp(boot.AddSeconds(1)) + "[SessionMessageManager::MemberConnected] [SessionNetworking] Session networking joined session at slot index 0\n"
                + Stamp(DateTimeOffset.UtcNow) + "[GameManager] ping\n");
            return new SessionPlayModeDetector(logs, Path.Combine(root, "mode-history.json"));
        }
        private static void RaceEndRestartBoundary()
        {
            WithTemporaryDirectory(root =>
            {
                DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-10);
                using var logger = new FileLogger(Path.Combine(root, "logs"));
                using var runtime = new ActivityCaptureRuntime(root, "restart-after-cap", "0.9.5", logger, null, RaceEndMode(root, start));
                var fixture = new RawFixtureBuilder(2).SetViewedIndex(0).SetSession(SessionState.Race)
                    .SetGlobalRaceState(RaceState.NotStarted);
                for (int i = 0; i < 2; i++) fixture.SetParticipant(i, true, "RESTART_" + i, (uint)(i + 1), 0, 1, RaceState.NotStarted, PitMode.None);
                runtime.Observe(Parse(fixture, start));
                string first = runtime.CurrentTelemetryIdentity!.SessionId;
                fixture.SetGlobalRaceState(RaceState.Racing);
                for (int i = 0; i < 2; i++) fixture.SetParticipant(i, true, "RESTART_" + i, (uint)(i + 1), 9, 10, RaceState.Racing, PitMode.None);
                runtime.Observe(Parse(fixture, start.AddSeconds(2)));
                fixture.SetParticipant(0, true, "RESTART_0", 1, 10, 11, RaceState.Finished, PitMode.None);
                runtime.Observe(Parse(fixture, start.AddSeconds(30)));
                runtime.Observe(Parse(fixture, start.AddSeconds(330))); AssertNull(runtime.CurrentTelemetryIdentity);
                runtime.Observe(Parse(fixture, start.AddSeconds(331))); AssertNull(runtime.CurrentTelemetryIdentity);
                fixture.SetGameState(GameState.InGameRestarting);
                runtime.Observe(Parse(fixture, start.AddSeconds(332))); AssertNull(runtime.CurrentTelemetryIdentity);
                fixture.SetGameState(GameState.InGamePlaying).SetGlobalRaceState(RaceState.NotStarted);
                for (int i = 0; i < 2; i++) fixture.SetParticipant(i, true, "RESTART_" + i, (uint)(i + 1), 0, 1, RaceState.NotStarted, PitMode.None);
                runtime.Observe(Parse(fixture, start.AddSeconds(333)));
                AssertTrue(runtime.CurrentTelemetryIdentity != null && runtime.CurrentTelemetryIdentity.SessionId != first);
            });
        }
        private sealed class RaceEndUploadClock : IActivityUploadClock { public DateTimeOffset UtcNow { get; set; } }
        private sealed class RaceEndHttpHandler : HttpMessageHandler
        {
            public List<byte[]> Bodies { get; } = new List<byte[]>();
            public List<string> Keys { get; } = new List<string>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                byte[] body = await request.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (request.Content.Headers.ContentEncoding.Contains("gzip"))
                    body = AMS2LeagueClient.Core.FutureTelemetry.TelemetryChunkSerializer.Gunzip(new MemoryStream(body));
                Bodies.Add(body); Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                using var wire = JsonDocument.Parse(body);
                return new HttpResponseMessage(Bodies.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created)
                {
                    Content = new StringContent(Bodies.Count == 1 ? "{\"error\":\"RETRY_FIXTURE\"}" : JsonSerializer.Serialize(new {
                        status = "stored", witnessId = wire.RootElement.GetProperty("witnessId").GetString(), canonicalSessionId = (string?)null }), Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
