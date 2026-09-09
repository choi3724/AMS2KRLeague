using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static readonly string[] GzipJsonRoutes = {
            Cafe24ActivityUploadTransport.PlayerActivitiesEndpoint,
            Cafe24ActivityUploadTransport.SessionWitnessEndpoint
        };

        private static void JsonGzipCapabilities()
        {
            foreach (string extra in new[] {
                "", ",\"capabilities\":null", ",\"capabilities\":[]",
                ",\"capabilities\":{\"gzipRequestRoutes\":true}",
                ",\"capabilities\":{\"gzipRequestRoutes\":[\"v1/session/witness\",null]}",
                ",\"capabilities\":{\"gzipRequestRoutes\":[\"v1/telemetry/chunks\",\"https://other.invalid\"]}"
            })
            {
                var value = Cafe24ActivityUploadTransport.ParseBootstrap(Encoding.UTF8.GetBytes(
                    "{\"event\":{\"publicId\":\"fixture-event\"},\"serviceVersion\":\"fixture\"" + extra + "}"));
                AssertEqual(0, value.GzipRequestRoutes.Length);
                AssertEqual("fixture-event", value.ScheduledEvent.PublicId);
                AssertEqual("fixture", value.ServiceVersion);
            }
            var valid = Cafe24ActivityUploadTransport.ParseBootstrap(Encoding.UTF8.GetBytes(
                GzipBootstrap("v1/session/witness", "v1/session/witness", "v1/telemetry/chunks")));
            AssertEqual(1, valid.GzipRequestRoutes.Length);
            AssertEqual("v1/session/witness", valid.GzipRequestRoutes[0]);
        }

        private static void JsonGzipUploadContract()
        {
            WithTemporaryDirectory(directory =>
            {
                var options = GzipOptions(directory);
                var handler = new GzipUploadFixtureHandler();
                using var http = new HttpClient(handler);
                using var upload = new Cafe24ActivityUploadTransport(options, http);
                var queue = new ActivityUploadQueue(Path.Combine(directory, "queue"));
                byte[] original = Encoding.UTF8.GetBytes("{ \"raceMode\":\"MULTIPLAYER\", \"number\":1.2300e+2,"
                    + "\"text\":\"이름\\uD55C\",\"padding\":\"" + new string('x', 8000) + "\" }\n");
                upload.GetBootstrapAsync(CancellationToken.None).GetAwaiter().GetResult();
                foreach (string route in GzipJsonRoutes)
                {
                    var saved = queue.Enqueue("activity-" + route, route, "gzip:" + route.Replace('/', '-'), original).Item;
                    string payloadPath = Path.Combine(saved.DirectoryPath, "payload.json");
                    string metadataPath = Path.Combine(saved.DirectoryPath, "metadata.json");
                    byte[] beforeMetadata = File.ReadAllBytes(metadataPath);
                    // Reopen the durable queue, just as an already pending item is recovered.
                    var item = new ActivityUploadQueue(queue.Root).Scan().Single(x => x.Metadata.QueueItemId == saved.Metadata.QueueItemId);
                    string originalHash = item.Metadata.BodySha256;
                    handler.UploadStatus = HttpStatusCode.Created;
                    var result = upload.SendAsync(item, CancellationToken.None).GetAwaiter().GetResult();
                    AssertEqual(201, result.StatusCode);
                    AssertEqual("gzip", handler.Encoding);
                    AssertEqual("application/json", handler.ContentType);
                    AssertEqual((long)handler.Wire.Length, handler.ContentLength);
                    AssertEqual(item.Metadata.IdempotencyKey, handler.Key);
                    AssertEqual("Bearer " + options.BearerToken, handler.Authorization);
                    AssertEqual(handler.Authorization, handler.CompatibilityAuthorization);
                    using (var compressed = new MemoryStream(handler.Wire))
                        AssertTrue(original.SequenceEqual(TelemetryChunkSerializer.Gunzip(compressed)));
                    AssertTrue(handler.Wire.Length < original.Length);
                    AssertTrue(original.SequenceEqual(File.ReadAllBytes(payloadPath)));
                    AssertTrue(beforeMetadata.SequenceEqual(File.ReadAllBytes(metadataPath)));
                    AssertEqual(originalHash, TelemetryChunkSerializer.Sha256(original));

                    foreach (HttpStatusCode status in new[] { HttpStatusCode.OK, HttpStatusCode.Conflict,
                        HttpStatusCode.UnprocessableEntity, HttpStatusCode.TooManyRequests })
                    {
                        handler.UploadStatus = status;
                        result = upload.SendAsync(item, CancellationToken.None).GetAwaiter().GetResult();
                        AssertEqual((int)status, result.StatusCode);
                        AssertEqual(status == HttpStatusCode.OK, result.Duplicate);
                        AssertEqual(item.Metadata.IdempotencyKey, handler.Key);
                    }
                    handler.FailUpload = true;
                    result = upload.SendAsync(item, CancellationToken.None).GetAwaiter().GetResult();
                    AssertFalse(result.StatusCode.HasValue);
                    handler.FailUpload = false;
                    int calls = handler.UploadCalls;
                    item.Metadata.BodySha256 = new string('0', 64);
                    result = upload.SendAsync(item, CancellationToken.None).GetAwaiter().GetResult();
                    AssertEqual("LOCAL_BODY_HASH_MISMATCH", result.ResultCode);
                    AssertEqual(calls, handler.UploadCalls);
                }
                handler.UploadStatus = HttpStatusCode.Created;
                var tiny = queue.Enqueue("tiny", GzipJsonRoutes[0], "gzip:tiny-fixture", "{}").Item;
                upload.SendAsync(tiny, CancellationToken.None).GetAwaiter().GetResult();
                AssertEqual("", handler.Encoding);
                AssertEqual("{}", Encoding.UTF8.GetString(handler.Wire));

                var worker = new ActivityUploadWorker(new ActivityUploadQueue(Path.Combine(directory, "worker")), upload);
                var workerQueue = new ActivityUploadQueue(Path.Combine(directory, "worker"));
                foreach (var scenario in new[] {
                    (HttpStatusCode.Created, ActivityUploadStatus.SENT),
                    (HttpStatusCode.Conflict, ActivityUploadStatus.CONFLICT),
                    (HttpStatusCode.UnprocessableEntity, ActivityUploadStatus.QUARANTINED),
                    (HttpStatusCode.TooManyRequests, ActivityUploadStatus.FAILED_RETRYABLE)
                })
                {
                    var item = workerQueue.Enqueue("worker-" + scenario.Item1, GzipJsonRoutes[1], "gzip:worker-" + scenario.Item1, original).Item;
                    handler.UploadStatus = scenario.Item1;
                    worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                    AssertEqual(scenario.Item2, workerQueue.Scan().Single(x => x.Metadata.QueueItemId == item.Metadata.QueueItemId).State.Status);
                }
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) TelemetryChunkSerializer.Gzip(original);
                watch.Stop();
                Console.WriteLine("PROOF json-gzip exact bytes; durable files unchanged; dotnet=" + Environment.Version
                    + " sampleBytes=" + original.Length + " meanCompressionMs=" + (watch.Elapsed.TotalMilliseconds / 100).ToString("F3"));
            });
        }

        private static void JsonGzipNegotiationLifecycle()
        {
            WithTemporaryDirectory(directory =>
            {
                var options = GzipOptions(directory);
                var handler = new GzipUploadFixtureHandler { Bootstrap = GzipBootstrap(GzipJsonRoutes[1]) };
                using var http = new HttpClient(handler);
                // Startup constructs separate bootstrap and upload transports sharing options.
                using var upload = new Cafe24ActivityUploadTransport(options, http);
                using var bootstrap = new Cafe24ActivityUploadTransport(options, http);
                var queue = new ActivityUploadQueue(Path.Combine(directory, "queue"));
                string payload = "{\"text\":\"" + new string('a', 4000) + "\"}";
                var witness = queue.Enqueue("witness", GzipJsonRoutes[1], "gzip:lifecycle-witness", payload).Item;
                var personal = queue.Enqueue("personal", GzipJsonRoutes[0], "gzip:lifecycle-personal", payload).Item;
                Action<ActivityUploadItem, string> check = (item, encoding) => {
                    upload.SendAsync(item, CancellationToken.None).GetAwaiter().GetResult();
                    AssertEqual(encoding, handler.Encoding);
                };
                check(witness, "");
                bootstrap.GetBootstrapAsync(CancellationToken.None).GetAwaiter().GetResult();
                check(witness, "gzip"); check(personal, "");
                foreach (string other in new[] { "https://other.invalid/ams2", "https://fixture.invalid/other" })
                {
                    options.ApiBaseUrl = other;
                    check(witness, "");
                }
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                handler.Bootstrap = "{\"event\":{}}";
                bootstrap.GetBootstrapAsync(CancellationToken.None).GetAwaiter().GetResult();
                check(witness, "");
                handler.Bootstrap = GzipBootstrap(GzipJsonRoutes);
                bootstrap.GetBootstrapAsync(CancellationToken.None).GetAwaiter().GetResult();
                check(personal, "gzip");
                handler.FailBootstrap = true;
                bool failed = false;
                try { bootstrap.GetBootstrapAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                catch (HttpRequestException) { failed = true; }
                AssertTrue(failed); check(witness, "");
                handler.FailBootstrap = false;
                bootstrap.GetBootstrapAsync(CancellationToken.None).GetAwaiter().GetResult();
                var freshOptions = GzipOptions(directory);
                using var fresh = new Cafe24ActivityUploadTransport(freshOptions, http);
                fresh.SendAsync(witness, CancellationToken.None).GetAwaiter().GetResult();
                AssertEqual("", handler.Encoding);
                AssertFalse(JsonSerializer.Serialize(options).Contains("gzip", StringComparison.OrdinalIgnoreCase));
            });
        }

        private static ActivityConnectionOptions GzipOptions(string directory)
        {
            var options = ActivityConnectionOptions.Load(Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
            options.ApiBaseUrl = "https://fixture.invalid/ams2";
            options.BearerToken = new string('a', 40);
            return options;
        }

        private static string GzipBootstrap(params string[] routes)
            => JsonSerializer.Serialize(new { @event = new { publicId = "gzip-fixture" },
                capabilities = new { gzipRequestRoutes = routes } });

        private sealed class GzipUploadFixtureHandler : HttpMessageHandler
        {
            public string Bootstrap { get; set; } = GzipBootstrap(GzipJsonRoutes);
            public bool FailBootstrap { get; set; }
            public bool FailUpload { get; set; }
            public HttpStatusCode UploadStatus { get; set; } = HttpStatusCode.Created;
            public string Encoding { get; private set; } = "";
            public string ContentType { get; private set; } = "";
            public long? ContentLength { get; private set; }
            public string Key { get; private set; } = "";
            public string Authorization { get; private set; } = "";
            public string CompatibilityAuthorization { get; private set; } = "";
            public byte[] Wire { get; private set; } = Array.Empty<byte>();
            public int UploadCalls { get; private set; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Get)
                {
                    if (FailBootstrap) throw new HttpRequestException("fixture bootstrap failure");
                    return Reply(HttpStatusCode.OK, Bootstrap);
                }
                UploadCalls++;
                if (FailUpload) throw new HttpRequestException("fixture upload failure");
                Wire = await request.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
                Encoding = string.Join(",", request.Content.Headers.ContentEncoding);
                ContentType = request.Content.Headers.ContentType?.MediaType ?? "";
                ContentLength = request.Content.Headers.ContentLength;
                Key = request.Headers.GetValues("Idempotency-Key").Single();
                Authorization = request.Headers.Authorization?.ToString() ?? "";
                CompatibilityAuthorization = request.Headers.GetValues("X-AMS2-Authorization").Single();
                return Reply(UploadStatus, UploadStatus == HttpStatusCode.OK ? "{\"status\":\"duplicate\",\"duplicate\":true}"
                    : UploadStatus == HttpStatusCode.Created ? "{\"status\":\"stored\"}" : "{\"error\":\"FIXTURE_ERROR\"}");
            }
            private static HttpResponseMessage Reply(HttpStatusCode code, string body)
                => new HttpResponseMessage(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
