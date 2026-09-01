using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.ActivityCapture.Upload;

namespace AMS2LeagueActivity.Tests
{
    internal static class UploadQueueTests
    {
        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Upload queue recovers after process restart", QueueRecoversAfterRestart);
            yield return new TestCase("Upload queue duplicate and conflict are idempotent", QueueDuplicateAndConflict);
            yield return new TestCase("Upload retry uses exponential backoff then sends", RetryBackoffThenSent);
            yield return new TestCase("HTTP 429 and 5xx are retryable", Http429And5xxAreRetryable);
            yield return new TestCase("HTTP 409 becomes conflict", Http409BecomesConflict);
            yield return new TestCase("HTTP 422 becomes quarantined", Http422BecomesQuarantined);
            yield return new TestCase("Server duplicate response becomes sent", DuplicateResponseBecomesSent);
            yield return new TestCase("Due upload batch is bounded", DueBatchIsBounded);
            yield return new TestCase("Payload and metadata stay immutable", PayloadAndMetadataStayImmutable);
            yield return new TestCase("Corrupt payload is quarantined on scan", CorruptPayloadIsQuarantined);
            yield return new TestCase("Blocked transport does not block enqueue", BlockedTransportDoesNotBlockEnqueue);
        }

        private static void QueueRecoversAfterRestart()
        {
            using var scope = new TemporaryDirectory("queue-restart");
            var clock = new MutableClock(FixedTime());
            var queue = new ActivityUploadQueue(scope.Root, Options(), clock);
            ActivityEnqueueOutcome outcome = queue.Enqueue("activity-restart", "v1/player/activities", "idem-restart-0001", "{\"activity\":1}");
            AssertEx.Equal(ActivityEnqueueDisposition.Enqueued, outcome.Disposition);
            AssertEx.True(File.Exists(Path.Combine(outcome.Item.DirectoryPath, "payload.json")));
            AssertEx.True(File.Exists(Path.Combine(outcome.Item.DirectoryPath, "metadata.json")));
            AssertEx.True(File.Exists(Path.Combine(outcome.Item.DirectoryPath, "state.json")));

            var restarted = new ActivityUploadQueue(scope.Root, Options(), clock);
            ActivityUploadItem restored = AssertEx.Single(restarted.Scan());
            AssertEx.Equal(ActivityUploadStatus.PENDING, restored.State.Status);
            AssertEx.Equal("idem-restart-0001", restored.Metadata.IdempotencyKey);
            AssertEx.Equal("{\"activity\":1}", Encoding.UTF8.GetString(restored.PayloadUtf8.Span));
            AssertEx.Equal(1, restarted.GetDueBatch().Count);
        }

        private static void QueueDuplicateAndConflict()
        {
            using var scope = new TemporaryDirectory("queue-idempotency");
            var queue = new ActivityUploadQueue(scope.Root, Options(), new MutableClock(FixedTime()));
            ActivityEnqueueOutcome first = queue.Enqueue("activity-idem", "v1/player/activities", "idem-same-0001", "{\"value\":1}");
            string originalPayloadHash = FileHash.Sha256(Path.Combine(first.Item.DirectoryPath, "payload.json"));
            ActivityEnqueueOutcome duplicate = queue.Enqueue("activity-idem", "v1/player/activities", "idem-same-0001", "{\"value\":1}");
            ActivityEnqueueOutcome conflict = queue.Enqueue("activity-idem", "v1/player/activities", "idem-same-0001", "{\"value\":2}");

            AssertEx.Equal(ActivityEnqueueDisposition.Duplicate, duplicate.Disposition);
            AssertEx.Equal(first.Item.Metadata.QueueItemId, duplicate.Item.Metadata.QueueItemId);
            AssertEx.Equal(ActivityEnqueueDisposition.Conflict, conflict.Disposition);
            AssertEx.Equal(ActivityUploadStatus.CONFLICT, conflict.Item.State.Status);
            AssertEx.Equal(first.Item.Metadata.QueueItemId, conflict.Item.Metadata.RelatedQueueItemId);
            AssertEx.Equal(2, queue.Scan().Count);
            AssertEx.Equal(originalPayloadHash, FileHash.Sha256(Path.Combine(first.Item.DirectoryPath, "payload.json")));
        }

        private static void RetryBackoffThenSent()
        {
            using var scope = new TemporaryDirectory("queue-retry");
            var clock = new MutableClock(FixedTime());
            ActivityUploadQueueOptions options = Options();
            options.BaseRetryDelay = TimeSpan.FromSeconds(10);
            options.MaximumRetryDelay = TimeSpan.FromSeconds(40);
            var queue = new ActivityUploadQueue(scope.Root, options, clock);
            queue.Enqueue("activity-retry", "v1/player/activities", "idem-retry-0001", "{\"retry\":true}");
            var transport = new SequenceTransport(
                ActivityUploadTransportResult.NetworkFailure(),
                ActivityUploadTransportResult.Http(201));
            var worker = new ActivityUploadWorker(queue, transport, clock, new FixedJitter(0.5));

            ActivityUploadWorkerSummary first = worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
            AssertEx.Equal(1, first.Retryable);
            ActivityUploadItem retryable = AssertEx.Single(queue.Scan());
            AssertEx.Equal(ActivityUploadStatus.FAILED_RETRYABLE, retryable.State.Status);
            AssertEx.Equal(clock.UtcNow.AddSeconds(10), retryable.State.NextAttemptAtUtc);

            clock.UtcNow = clock.UtcNow.AddSeconds(9);
            AssertEx.Equal(0, worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Attempted);
            clock.UtcNow = clock.UtcNow.AddSeconds(1);
            AssertEx.Equal(1, worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Sent);
            ActivityUploadItem sent = AssertEx.Single(queue.Scan());
            AssertEx.Equal(ActivityUploadStatus.SENT, sent.State.Status);
            AssertEx.Equal(2, sent.State.AttemptCount);
            AssertEx.Equal(2, transport.Calls);

            var retryPolicy = new ActivityUploadRetryPolicy(options, new FixedJitter(0.5));
            AssertEx.Equal(TimeSpan.FromSeconds(10), retryPolicy.GetDelay(1));
            AssertEx.Equal(TimeSpan.FromSeconds(20), retryPolicy.GetDelay(2));
            AssertEx.Equal(TimeSpan.FromSeconds(40), retryPolicy.GetDelay(3));
            AssertEx.Equal(TimeSpan.FromSeconds(40), retryPolicy.GetDelay(4));
        }

        private static void Http429And5xxAreRetryable()
        {
            using var scope = new TemporaryDirectory("queue-http-retry");
            var clock = new MutableClock(FixedTime());
            var queue = new ActivityUploadQueue(scope.Root, Options(), clock);
            queue.Enqueue("activity-429", "v1/player/activities", "idem-http-0429", "{\"status\":429}");
            queue.Enqueue("activity-503", "v1/player/activities", "idem-http-0503", "{\"status\":503}");
            var transport = new SequenceTransport(
                ActivityUploadTransportResult.Http(429),
                ActivityUploadTransportResult.Http(503));
            var worker = new ActivityUploadWorker(queue, transport, clock, new FixedJitter(0.5));
            ActivityUploadWorkerSummary summary = worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
            AssertEx.Equal(2, summary.Retryable);
            AssertEx.True(queue.Scan().All(item => item.State.Status == ActivityUploadStatus.FAILED_RETRYABLE));
        }

        private static void Http409BecomesConflict()
        {
            ActivityUploadItem item = ProcessSingle("queue-409", "idem-http-0409", ActivityUploadTransportResult.Http(409));
            AssertEx.Equal(ActivityUploadStatus.CONFLICT, item.State.Status);
            AssertEx.Equal(409, item.State.LastHttpStatus);
        }

        private static void Http422BecomesQuarantined()
        {
            ActivityUploadItem item = ProcessSingle("queue-422", "idem-http-0422", ActivityUploadTransportResult.Http(422));
            AssertEx.Equal(ActivityUploadStatus.QUARANTINED, item.State.Status);
            AssertEx.Equal(422, item.State.LastHttpStatus);
        }

        private static void DuplicateResponseBecomesSent()
        {
            ActivityUploadItem item = ProcessSingle("queue-duplicate", "idem-duplicate-01", ActivityUploadTransportResult.Http(200, true));
            AssertEx.Equal(ActivityUploadStatus.SENT, item.State.Status);
            AssertEx.Equal("DUPLICATE", item.State.LastResult);
        }

        private static void DueBatchIsBounded()
        {
            using var scope = new TemporaryDirectory("queue-bounded");
            var clock = new MutableClock(FixedTime());
            ActivityUploadQueueOptions options = Options();
            options.MaximumDueBatchSize = 2;
            var queue = new ActivityUploadQueue(scope.Root, options, clock);
            queue.Enqueue("activity-batch-1", "v1/player/activities", "idem-batch-0001", "{\"n\":1}");
            queue.Enqueue("activity-batch-2", "v1/player/activities", "idem-batch-0002", "{\"n\":2}");
            queue.Enqueue("activity-batch-3", "v1/player/activities", "idem-batch-0003", "{\"n\":3}");
            AssertEx.Equal(2, queue.GetDueBatch(99).Count);
            var worker = new ActivityUploadWorker(
                queue,
                new SequenceTransport(ActivityUploadTransportResult.Http(201), ActivityUploadTransportResult.Http(201)),
                clock,
                new FixedJitter(0.5));
            ActivityUploadWorkerSummary summary = worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
            AssertEx.Equal(2, summary.Attempted);
            AssertEx.Equal(2, queue.Scan().Count(item => item.State.Status == ActivityUploadStatus.SENT));
            AssertEx.Equal(1, queue.Scan().Count(item => item.State.Status == ActivityUploadStatus.PENDING));
        }

        private static void PayloadAndMetadataStayImmutable()
        {
            using var scope = new TemporaryDirectory("queue-immutable");
            var clock = new MutableClock(FixedTime());
            var queue = new ActivityUploadQueue(scope.Root, Options(), clock);
            ActivityUploadItem item = queue.Enqueue("activity-immutable", "v1/player/activities", "idem-immutable-1", "{\"immutable\":true}").Item;
            string payloadPath = Path.Combine(item.DirectoryPath, "payload.json");
            string metadataPath = Path.Combine(item.DirectoryPath, "metadata.json");
            string statePath = Path.Combine(item.DirectoryPath, "state.json");
            string payloadHash = FileHash.Sha256(payloadPath);
            string metadataHash = FileHash.Sha256(metadataPath);
            string stateHash = FileHash.Sha256(statePath);
            queue.MarkSent(item.Metadata.QueueItemId, clock.UtcNow, 201, false);
            AssertEx.Equal(payloadHash, FileHash.Sha256(payloadPath));
            AssertEx.Equal(metadataHash, FileHash.Sha256(metadataPath));
            AssertEx.NotEqual(stateHash, FileHash.Sha256(statePath));
            AssertEx.Equal(3, Directory.GetFiles(item.DirectoryPath).Length);
        }

        private static void CorruptPayloadIsQuarantined()
        {
            using var scope = new TemporaryDirectory("queue-corrupt");
            var clock = new MutableClock(FixedTime());
            var queue = new ActivityUploadQueue(scope.Root, Options(), clock);
            ActivityUploadItem item = queue.Enqueue("activity-corrupt", "v1/player/activities", "idem-corrupt-001", "{\"ok\":true}").Item;
            File.WriteAllText(Path.Combine(item.DirectoryPath, "payload.json"), "{\"tampered\":true}", new UTF8Encoding(false));
            var restarted = new ActivityUploadQueue(scope.Root, Options(), clock);
            ActivityUploadItem restored = AssertEx.Single(restarted.Scan());
            AssertEx.Equal(ActivityUploadStatus.QUARANTINED, restored.State.Status);
            AssertEx.Equal("PAYLOAD_HASH_MISMATCH", restored.State.LastResult);
            AssertEx.Equal(0, restarted.GetDueBatch().Count);
        }

        private static void BlockedTransportDoesNotBlockEnqueue()
        {
            using var scope = new TemporaryDirectory("queue-blocked");
            var clock = new MutableClock(FixedTime());
            var queue = new ActivityUploadQueue(scope.Root, Options(), clock);
            queue.Enqueue("activity-blocked-1", "v1/player/activities", "idem-blocked-01", "{\"n\":1}");
            var transport = new BlockingTransport();
            var worker = new ActivityUploadWorker(queue, transport, clock, new FixedJitter(0.5));
            Task<ActivityUploadWorkerSummary> running = worker.ProcessDueAsync(CancellationToken.None);
            AssertEx.True(transport.Entered.Wait(TimeSpan.FromSeconds(5)), "Transport did not enter within five seconds.");
            try
            {
                ActivityEnqueueOutcome second = queue.Enqueue("activity-blocked-2", "v1/player/activities", "idem-blocked-02", "{\"n\":2}");
                AssertEx.Equal(ActivityEnqueueDisposition.Enqueued, second.Disposition);
                AssertEx.Equal(2, queue.Scan().Count);
            }
            finally
            {
                transport.Release();
            }
            AssertEx.Equal(1, running.GetAwaiter().GetResult().Sent);
        }

        private static ActivityUploadItem ProcessSingle(string purpose, string idempotencyKey, ActivityUploadTransportResult result)
        {
            using var scope = new TemporaryDirectory(purpose);
            var clock = new MutableClock(FixedTime());
            var queue = new ActivityUploadQueue(scope.Root, Options(), clock);
            queue.Enqueue("activity-" + purpose, "v1/player/activities", idempotencyKey, "{\"test\":true}");
            var worker = new ActivityUploadWorker(queue, new SequenceTransport(result), clock, new FixedJitter(0.5));
            worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
            return AssertEx.Single(queue.Scan());
        }

        private static ActivityUploadQueueOptions Options()
            => new ActivityUploadQueueOptions
            {
                MaximumDueBatchSize = 8,
                BaseRetryDelay = TimeSpan.FromSeconds(1),
                MaximumRetryDelay = TimeSpan.FromMinutes(1),
                RetryJitterRatio = 0.2
            };

        private static DateTimeOffset FixedTime()
            => new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
    }
}
