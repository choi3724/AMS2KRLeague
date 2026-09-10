using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private sealed class ThrowingTelemetryFixture : ITelemetryChunkUploadTransport
        {
            public System.Threading.Tasks.Task<TelemetryChunkUploadTransportResult> SendTelemetryChunkAsync(
                TelemetryChunkUploadItem item, CancellationToken cancellationToken)
                => throw new System.IO.IOException("must-not-be-logged");
        }
        private static void ActivitySemanticAcknowledgements()
        {
            foreach (var response in new[] {
                (Code: 200, Body: "{\"error\":\"DB_UNAVAILABLE\"}", Sent: false),
                (Code: 201, Body: "{\"status\":\"quarantined\"}", Sent: false),
                (Code: 201, Body: "{\"status\":\"stored\",\"error\":{\"code\":\"FAILED\"}}", Sent: false),
                (Code: 200, Body: "{\"status\":\"stored\",\"errors\":[\"FAILED\"]}", Sent: false),
                (Code: 200, Body: "{\"status\":\"stored\",\"error\":\"DB_UNAVAILABLE\",\"duplicate\":true}", Sent: false),
                (Code: 200, Body: "", Sent: false),
                (Code: 201, Body: "<html>upstream error</html>", Sent: false),
                (Code: 200, Body: "{", Sent: false),
                (Code: 200, Body: "{\"status\":\"queued\"}", Sent: false),
                (Code: 201, Body: "{\"status\":\"stored\",\"witnessId\":\"different-witness\"}", Sent: false),
                (Code: 200, Body: "{\"status\":\"stored\",\"bodySha256\":\"different-hash\"}", Sent: false),
                (Code: 201, Body: "{\"status\":\"stored\",\"witnessId\":\"delivery-witness\"}", Sent: true),
                (Code: 200, Body: "{\"status\":\"stored\",\"duplicate\":true}", Sent: true),
                (Code: 200, Body: "{\"status\":\"duplicate\"}", Sent: true) })
            WithTemporaryDirectory(directory => {
                var options = ActivityConnectionOptions.Load(System.IO.Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                options.BearerToken = new string('a', 32);
                var handler = new EnrollmentFixtureHandler("delivery-test-installation", options.BearerToken) {
                    UploadReply = () => new HttpResponseMessage((HttpStatusCode)response.Code) {
                        Content = new StringContent(response.Body, Encoding.UTF8, "application/json") } };
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, "delivery-test-installation", "0.7.0", http);
                var queue = new ActivityUploadQueue(System.IO.Path.Combine(directory, "queue"));
                var original = queue.Enqueue("delivery-witness", Cafe24ActivityUploadTransport.SessionWitnessEndpoint,
                    "witness:delivery-test", "{\"schema\":\"ams2-session-witness-v1\",\"witnessId\":\"delivery-witness\"}").Item;
                var result = new ActivityUploadWorker(queue, transport).ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                var stored = queue.Scan().Single();
                AssertEqual(response.Sent ? 1 : 0, result.Sent);
                AssertEqual(response.Sent ? ActivityUploadStatus.SENT : ActivityUploadStatus.FAILED_RETRYABLE, stored.State.Status);
                AssertTrue(original.PayloadUtf8.Span.SequenceEqual(stored.PayloadUtf8.Span));
                AssertEqual(original.Metadata.BodySha256, stored.Metadata.BodySha256);
                AssertEqual(original.Metadata.IdempotencyKey, stored.Metadata.IdempotencyKey);
                AssertEqual((int?)response.Code, stored.State.LastHttpStatus);
                if (!response.Sent) AssertTrue(stored.State.NextAttemptAtUtc > DateTimeOffset.UtcNow);
            });
        }

        private static void AmbiguousTelemetryAcknowledgements()
        {
            WithTemporaryDirectory(directory => {
                string root = System.IO.Path.Combine(directory, "telemetry");
                CreatePendingCompactTelemetryChunk(root);
                var queue = new TelemetryChunkUploadQueue(root);
                var original = queue.GetDueBatch(1, DateTimeOffset.UtcNow).Single();
                var result = new TelemetryChunkUploadWorker(queue, new ThrowingTelemetryFixture())
                    .ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                var stored = TelemetryChunkSerializer.DeserializeMetadata(System.IO.File.ReadAllBytes(original.MetadataPath));
                AssertEqual(1, result.Retryable); AssertEqual(0, result.Sent);
                AssertEqual(TelemetryUploadStatus.FAILED_RETRYABLE, stored.Status);
                AssertEqual(1, stored.AttemptCount);
                AssertEqual("TRANSPORT_IOEXCEPTION", stored.LastResultCode);
                AssertTrue(stored.NextAttemptAtUtc > DateTimeOffset.UtcNow);
                AssertTrue(original.CompressedPayload.Span.SequenceEqual(System.IO.File.ReadAllBytes(original.ChunkPath)));
            });
            foreach (string body in new[] { "{\"status\":\"stored\",\"error\":{\"code\":\"FAILED\"}}", "{\"status\":\"stored\",\"errors\":[\"FAILED\"]}", "{\"error\":\"" + new string('b', 32) + "\"}", "", "<html>upstream error</html>", "{", "[]", "{\"status\":\"queued\"}",
                "{\"error\":\"DB_UNAVAILABLE\"}", "{\"status\":\"stored\",\"error\":\"DB_UNAVAILABLE\",\"duplicate\":true}" })
            WithTemporaryDirectory(directory => {
                var options = ActivityConnectionOptions.Load(System.IO.Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                options.BearerToken = new string('b', 32);
                var handler = new EnrollmentFixtureHandler("delivery-test-installation", options.BearerToken) {
                    UploadReply = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) } };
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, "delivery-test-installation", "0.7.0", http);
                string root = System.IO.Path.Combine(directory, "telemetry");
                CreatePendingCompactTelemetryChunk(root);
                var queue = new TelemetryChunkUploadQueue(root);
                var original = queue.GetDueBatch(1, DateTimeOffset.UtcNow).Single();
                var result = new TelemetryChunkUploadWorker(queue, transport).ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                var stored = TelemetryChunkSerializer.DeserializeMetadata(System.IO.File.ReadAllBytes(original.MetadataPath));
                AssertEqual(0, result.Sent); AssertEqual(0, result.Quarantined); AssertEqual(1, result.Retryable);
                AssertEqual(TelemetryUploadStatus.FAILED_RETRYABLE, stored.Status);
                AssertEqual((int?)200, stored.LastHttpStatus);
                AssertTrue(!string.IsNullOrEmpty(stored.LastResultCode));
                AssertTrue(original.CompressedPayload.Span.SequenceEqual(System.IO.File.ReadAllBytes(original.ChunkPath)));
                AssertEqual(original.Metadata.ChunkId, stored.ChunkId);
                AssertEqual(original.Metadata.PayloadSha256, stored.PayloadSha256);
                AssertTrue(stored.NextAttemptAtUtc > DateTimeOffset.UtcNow);
                AssertFalse((stored.LastResultCode ?? "").Contains(new string('B', 32), StringComparison.Ordinal));
            });
        }
    }
}
