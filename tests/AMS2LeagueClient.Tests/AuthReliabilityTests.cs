using System;
using System.Collections.Generic;
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
using AMS2LeagueClient.Core.Security;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void AuthenticationRecoveryPreservesIdentity()
        {
            WithTemporaryDirectory(directory => {
                var options = ActivityConnectionOptions.Load(Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                options.BearerToken = new string('a', 32);
                PairingTokenStore.Save(directory, options.BearerToken);
                var handler = new AuthFixtureHandler();
                var clock = new DeliveryClock();
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, "auth-stable-installation", "0.7.0", http, clock);
                var queue = new ActivityUploadQueue(Path.Combine(directory, "queue"));
                var original = queue.Enqueue("auth-witness", Cafe24ActivityUploadTransport.SessionWitnessEndpoint,
                    "witness:auth-fixture", "{\"schema\":\"ams2-session-witness-v1\"}").Item;
                var first = new ActivityUploadWorker(queue, transport).ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                AssertEqual(1, first.Retryable); AssertEqual(0, first.Quarantined);
                AssertEqual("AUTH_REQUIRED", queue.Scan().Single().State.LastResult);
                var second = transport.SendAsync(original, CancellationToken.None).GetAwaiter().GetResult();
                AssertTrue(second.Acknowledged); AssertEqual(1, handler.Enrollments);
                AssertEqual("auth-stable-installation", handler.EnrolledIdentity);
                AssertTrue(handler.Bodies[0].SequenceEqual(handler.Bodies[1]));
                AssertEqual(handler.Keys[0], handler.Keys[1]);
                AssertEqual(new string('b', 32), PairingTokenStore.Load(directory));

                // Repeated rejection of the refreshed credential cannot spin enrollment.
                handler.RejectAll = true;
                AssertEqual(401, transport.SendAsync(original, CancellationToken.None).GetAwaiter().GetResult().StatusCode);
                using var shared = new Cafe24ActivityUploadTransport(options, "auth-stable-installation", "0.7.0", http, clock);
                for (int index = 0; index < 8; index++)
                    AssertEqual(401, shared.SendAsync(original, CancellationToken.None).GetAwaiter().GetResult().StatusCode);
                AssertEqual(1, handler.Enrollments);
                clock.Advance(TimeSpan.FromSeconds(31));
                handler.EnrollmentCode = HttpStatusCode.Conflict; // paired identity may not be replaced
                AssertEqual(401, shared.SendAsync(original, CancellationToken.None).GetAwaiter().GetResult().StatusCode);
                AssertEqual(2, handler.Enrollments);
                AssertEqual(new string('b', 32), PairingTokenStore.Load(directory));
                clock.Advance(TimeSpan.FromMinutes(1));
                AssertEqual(401, shared.SendAsync(original, CancellationToken.None).GetAwaiter().GetResult().StatusCode);
                AssertEqual(2, handler.Enrollments);
                AssertTrue(original.PayloadUtf8.Span.SequenceEqual(queue.Scan().Single().PayloadUtf8.Span));
            });
        }

        private static void TelemetryAuthenticationIsRetryable()
        {
            WithTemporaryDirectory(directory => {
                var options = ActivityConnectionOptions.Load(Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                options.BearerToken = new string('a', 32);
                var handler = new AuthFixtureHandler();
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, "auth-stable-installation", "0.7.0", http);
                string root = Path.Combine(directory, "telemetry");
                CreatePendingCompactTelemetryChunk(root);
                var queue = new TelemetryChunkUploadQueue(root);
                var original = queue.GetDueBatch(1, DateTimeOffset.UtcNow).Single();
                var result = new TelemetryChunkUploadWorker(queue, transport).ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                AssertEqual(1, result.Retryable); AssertEqual(0, result.Quarantined);
                var recovered = transport.SendTelemetryChunkAsync(original, CancellationToken.None).GetAwaiter().GetResult();
                AssertTrue(recovered.Success); AssertEqual(1, handler.Enrollments);
                AssertTrue(handler.Bodies[0].SequenceEqual(handler.Bodies[1]));
                AssertEqual(handler.Keys[0], handler.Keys[1]);
                AssertEqual("auth-stable-installation", handler.EnrolledIdentity);
            });
        }

        private sealed class DeliveryClock : TimeProvider
        {
            private DateTimeOffset _now = DateTimeOffset.UtcNow;
            public override DateTimeOffset GetUtcNow() => _now;
            public void Advance(TimeSpan duration) => _now += duration;
        }

        private sealed class AuthFixtureHandler : HttpMessageHandler
        {
            public int Enrollments;
            public string EnrolledIdentity = string.Empty;
            public bool RejectAll;
            public HttpStatusCode EnrollmentCode = HttpStatusCode.Created;
            public List<byte[]> Bodies = new List<byte[]>();
            public List<string> Keys = new List<string>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.Query.Contains(Cafe24ActivityUploadTransport.EnrollmentEndpoint))
                {
                    Enrollments++;
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
                    EnrolledIdentity = body.RootElement.GetProperty("installationId").GetString()!;
                    return new HttpResponseMessage(EnrollmentCode) { Content = new StringContent(JsonSerializer.Serialize(new {
                        installationId = EnrolledIdentity, installationToken = new string('b', 32), scopes = new[] { "witnesses:write", "telemetry:write" }
                    })) };
                }
                Bodies.Add(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
                Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                if (RejectAll || request.Headers.Authorization?.Parameter != new string('b', 32))
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new UnreadableErrorContent() };
                string result = "{\"status\":\"stored\"}";
                if (request.Headers.Contains("X-AMS2-Chunk-Id")) result = JsonSerializer.Serialize(new {
                    status = "stored", chunkId = request.Headers.GetValues("X-AMS2-Chunk-Id").Single(),
                    contentSha256 = request.Headers.GetValues("X-AMS2-Payload-SHA256").Single()
                });
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(result, Encoding.UTF8, "application/json") };
            }
        }
    }
}
