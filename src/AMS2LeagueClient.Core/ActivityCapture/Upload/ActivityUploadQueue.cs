using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AMS2LeagueClient.Core.ActivityCapture.Upload
{
    public sealed class ActivityUploadQueue
    {
        private const string PayloadFileName = "payload.json";
        private const string MetadataFileName = "metadata.json";
        private const string StateFileName = "state.json";

        private readonly object _gate = new object();
        private readonly string _root;
        private readonly string _itemsRoot;
        private readonly string _stagingRoot;
        private readonly ActivityUploadQueueOptions _options;
        private readonly IActivityUploadClock _clock;
        private readonly JsonSerializerOptions _json;
        private List<ActivityUploadScanIssue> _lastScanIssues = new List<ActivityUploadScanIssue>();

        public ActivityUploadQueue(
            string root,
            ActivityUploadQueueOptions? options = null,
            IActivityUploadClock? clock = null)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Queue root is required.", nameof(root));
            _root = Path.GetFullPath(root);
            _itemsRoot = Path.Combine(_root, "items");
            _stagingRoot = Path.Combine(_root, "staging");
            _options = (options ?? new ActivityUploadQueueOptions()).ValidatedCopy();
            _clock = clock ?? new SystemActivityUploadClock();
            _json = ActivityUploadJson.CreateOptions();
            Directory.CreateDirectory(_itemsRoot);
            Directory.CreateDirectory(_stagingRoot);
        }

        public string Root => _root;

        public ActivityUploadQueueOptions Options => _options.ValidatedCopy();

        public IReadOnlyList<ActivityUploadScanIssue> LastScanIssues
        {
            get
            {
                lock (_gate)
                {
                    return _lastScanIssues.Select(CloneIssue).ToArray();
                }
            }
        }

        public ActivityEnqueueOutcome Enqueue(
            string activityId,
            string endpoint,
            string idempotencyKey,
            string payloadJson)
        {
            if (payloadJson == null) throw new ArgumentNullException(nameof(payloadJson));
            return Enqueue(activityId, endpoint, idempotencyKey, new UTF8Encoding(false).GetBytes(payloadJson));
        }

        public ActivityEnqueueOutcome Enqueue(
            string activityId,
            string endpoint,
            string idempotencyKey,
            byte[] payloadUtf8)
        {
            ValidateText(activityId, nameof(activityId), 1, 128);
            ValidateText(endpoint, nameof(endpoint), 1, 512);
            ValidateText(idempotencyKey, nameof(idempotencyKey), 8, 128);
            if (payloadUtf8 == null) throw new ArgumentNullException(nameof(payloadUtf8));
            if (payloadUtf8.Length == 0) throw new ArgumentException("Payload JSON cannot be empty.", nameof(payloadUtf8));
            using (JsonDocument.Parse(payloadUtf8))
            {
            }

            byte[] immutablePayload = (byte[])payloadUtf8.Clone();
            string bodySha256 = ComputeSha256(immutablePayload);
            string primaryId = ComputeTextSha256("idempotency|" + idempotencyKey);
            lock (_gate)
            {
                string primaryPath = ItemPath(primaryId);
                if (Directory.Exists(primaryPath))
                {
                    ActivityUploadItem existing = LoadItem(primaryPath, false);
                    if (string.Equals(existing.Metadata.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)
                        && string.Equals(existing.Metadata.BodySha256, bodySha256, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ActivityEnqueueOutcome
                        {
                            Disposition = ActivityEnqueueDisposition.Duplicate,
                            Item = existing,
                            Message = "same idempotency key and body already queued"
                        };
                    }

                    string conflictId = ComputeTextSha256("conflict|" + idempotencyKey + "|" + bodySha256);
                    string conflictPath = ItemPath(conflictId);
                    ActivityUploadItem conflict;
                    if (Directory.Exists(conflictPath))
                    {
                        conflict = LoadItem(conflictPath, false);
                    }
                    else
                    {
                        conflict = CreateItem(
                            conflictId,
                            activityId,
                            endpoint,
                            idempotencyKey,
                            bodySha256,
                            immutablePayload,
                            ActivityUploadStatus.CONFLICT,
                            "IDEMPOTENCY_CONFLICT",
                            existing.Metadata.QueueItemId);
                    }

                    return new ActivityEnqueueOutcome
                    {
                        Disposition = ActivityEnqueueDisposition.Conflict,
                        Item = conflict,
                        Message = "same idempotency key has different immutable content"
                    };
                }

                ActivityUploadItem created = CreateItem(
                    primaryId,
                    activityId,
                    endpoint,
                    idempotencyKey,
                    bodySha256,
                    immutablePayload,
                    ActivityUploadStatus.PENDING,
                    string.Empty,
                    string.Empty);
                return new ActivityEnqueueOutcome
                {
                    Disposition = ActivityEnqueueDisposition.Enqueued,
                    Item = created,
                    Message = "immutable upload item queued"
                };
            }
        }

        public IReadOnlyList<ActivityUploadItem> Scan()
        {
            lock (_gate)
            {
                return ScanInternal();
            }
        }

        public IReadOnlyList<ActivityUploadItem> GetDueBatch()
            => GetDueBatch(_options.MaximumDueBatchSize, _clock.UtcNow);

        public IReadOnlyList<ActivityUploadItem> GetDueBatch(int maximumItems)
            => GetDueBatch(maximumItems, _clock.UtcNow);

        public IReadOnlyList<ActivityUploadItem> GetDueBatch(int maximumItems, DateTimeOffset nowUtc)
        {
            if (maximumItems < 1) throw new ArgumentOutOfRangeException(nameof(maximumItems));
            int boundedMaximum = Math.Min(maximumItems, _options.MaximumDueBatchSize);
            lock (_gate)
            {
                return ScanInternal()
                    .Where(item => IsDue(item.State, nowUtc))
                    .OrderBy(item => item.Metadata.CreatedAtUtc)
                    .ThenBy(item => item.Metadata.QueueItemId, StringComparer.Ordinal)
                    .Take(boundedMaximum)
                    .ToArray();
            }
        }

        public ActivityUploadItem MarkSent(string queueItemId, DateTimeOffset attemptedAtUtc, int? httpStatus, bool duplicate)
            => Transition(queueItemId, ActivityUploadStatus.SENT, attemptedAtUtc, httpStatus, duplicate ? "DUPLICATE" : "STORED", null, true);

        public ActivityUploadItem MarkRetryable(string queueItemId, DateTimeOffset attemptedAtUtc, int? httpStatus, string resultCode, DateTimeOffset nextAttemptAtUtc)
            => Transition(queueItemId, ActivityUploadStatus.FAILED_RETRYABLE, attemptedAtUtc, httpStatus, resultCode, nextAttemptAtUtc, true);

        public ActivityUploadItem MarkConflict(string queueItemId, DateTimeOffset attemptedAtUtc, int? httpStatus, string resultCode)
            => Transition(queueItemId, ActivityUploadStatus.CONFLICT, attemptedAtUtc, httpStatus, resultCode, null, true);

        public ActivityUploadItem MarkQuarantined(string queueItemId, DateTimeOffset attemptedAtUtc, int? httpStatus, string resultCode)
            => Transition(queueItemId, ActivityUploadStatus.QUARANTINED, attemptedAtUtc, httpStatus, resultCode, null, true);

        private IReadOnlyList<ActivityUploadItem> ScanInternal()
        {
            var items = new List<ActivityUploadItem>();
            var issues = new List<ActivityUploadScanIssue>();
            foreach (string directory in Directory.GetDirectories(_itemsRoot).OrderBy(value => value, StringComparer.Ordinal))
            {
                try
                {
                    items.Add(LoadItem(directory, true));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException || exception is InvalidDataException)
                {
                    issues.Add(new ActivityUploadScanIssue
                    {
                        DirectoryPath = directory,
                        Code = "QUEUE_ITEM_SCAN_FAILED_" + exception.GetType().Name.ToUpperInvariant()
                    });
                }
            }
            _lastScanIssues = issues;
            return items;
        }

        private ActivityUploadItem CreateItem(
            string queueItemId,
            string activityId,
            string endpoint,
            string idempotencyKey,
            string bodySha256,
            byte[] payloadUtf8,
            ActivityUploadStatus initialStatus,
            string initialResult,
            string relatedQueueItemId)
        {
            DateTimeOffset now = _clock.UtcNow;
            var metadata = new ActivityUploadMetadata
            {
                QueueItemId = queueItemId,
                ActivityId = activityId,
                Endpoint = endpoint,
                IdempotencyKey = idempotencyKey,
                BodySha256 = bodySha256,
                CreatedAtUtc = now,
                RelatedQueueItemId = relatedQueueItemId
            };
            var state = new ActivityUploadState
            {
                Status = initialStatus,
                UpdatedAtUtc = now,
                NextAttemptAtUtc = initialStatus == ActivityUploadStatus.PENDING ? now : (DateTimeOffset?)null,
                LastResult = NormalizeResultCode(initialResult)
            };

            string staging = Path.Combine(_stagingRoot, queueItemId + ".tmp-" + Guid.NewGuid().ToString("N"));
            string target = ItemPath(queueItemId);
            Directory.CreateDirectory(staging);
            try
            {
                WriteNew(Path.Combine(staging, PayloadFileName), payloadUtf8);
                WriteNew(Path.Combine(staging, MetadataFileName), JsonSerializer.SerializeToUtf8Bytes(metadata, _json));
                WriteNew(Path.Combine(staging, StateFileName), JsonSerializer.SerializeToUtf8Bytes(state, _json));
                Directory.Move(staging, target);
            }
            catch
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                throw;
            }

            return new ActivityUploadItem(target, metadata, state, payloadUtf8);
        }

        private ActivityUploadItem LoadItem(string directory, bool quarantineCorruption)
        {
            string metadataPath = Path.Combine(directory, MetadataFileName);
            string payloadPath = Path.Combine(directory, PayloadFileName);
            string statePath = Path.Combine(directory, StateFileName);
            ActivityUploadMetadata metadata = Deserialize<ActivityUploadMetadata>(metadataPath);
            byte[] payload = File.ReadAllBytes(payloadPath);
            string directoryName = Path.GetFileName(directory);
            if (!string.Equals(metadata.QueueItemId, directoryName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Queue metadata ID does not match its immutable directory.");
            }

            ActivityUploadState state;
            try
            {
                state = Deserialize<ActivityUploadState>(statePath);
            }
            catch (Exception exception) when (quarantineCorruption && (exception is IOException || exception is JsonException || exception is InvalidDataException))
            {
                state = NewQuarantineState("STATE_READ_FAILED_" + exception.GetType().Name.ToUpperInvariant());
                AtomicWriteState(statePath, state);
            }

            string actualHash = ComputeSha256(payload);
            if (!string.Equals(actualHash, metadata.BodySha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!quarantineCorruption)
                {
                    throw new InvalidDataException("Immutable payload hash does not match metadata.");
                }
                state = NewQuarantineState("PAYLOAD_HASH_MISMATCH");
                AtomicWriteState(statePath, state);
            }

            return new ActivityUploadItem(directory, metadata, state, payload);
        }

        private ActivityUploadItem Transition(
            string queueItemId,
            ActivityUploadStatus status,
            DateTimeOffset attemptedAtUtc,
            int? httpStatus,
            string resultCode,
            DateTimeOffset? nextAttemptAtUtc,
            bool incrementAttempt)
        {
            ValidateQueueItemId(queueItemId);
            lock (_gate)
            {
                string directory = ItemPath(queueItemId);
                ActivityUploadItem current = LoadItem(directory, false);
                var state = new ActivityUploadState
                {
                    Status = status,
                    AttemptCount = current.State.AttemptCount + (incrementAttempt ? 1 : 0),
                    UpdatedAtUtc = attemptedAtUtc,
                    LastAttemptAtUtc = attemptedAtUtc,
                    NextAttemptAtUtc = nextAttemptAtUtc,
                    LastHttpStatus = httpStatus,
                    LastResult = NormalizeResultCode(resultCode)
                };
                AtomicWriteState(Path.Combine(directory, StateFileName), state);
                return new ActivityUploadItem(directory, current.Metadata, state, current.PayloadUtf8.ToArray());
            }
        }

        private ActivityUploadState NewQuarantineState(string resultCode)
        {
            DateTimeOffset now = _clock.UtcNow;
            return new ActivityUploadState
            {
                Status = ActivityUploadStatus.QUARANTINED,
                UpdatedAtUtc = now,
                LastResult = NormalizeResultCode(resultCode)
            };
        }

        private void AtomicWriteState(string path, ActivityUploadState state)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                WriteNew(temporary, JsonSerializer.SerializeToUtf8Bytes(state, _json));
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private T Deserialize<T>(string path) where T : class
        {
            T? value = JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), _json);
            return value ?? throw new InvalidDataException("Queue JSON contained null: " + Path.GetFileName(path));
        }

        private string ItemPath(string queueItemId)
        {
            ValidateQueueItemId(queueItemId);
            return Path.Combine(_itemsRoot, queueItemId);
        }

        private static bool IsDue(ActivityUploadState state, DateTimeOffset nowUtc)
            => (state.Status == ActivityUploadStatus.PENDING || state.Status == ActivityUploadStatus.FAILED_RETRYABLE)
                && (!state.NextAttemptAtUtc.HasValue || state.NextAttemptAtUtc.Value <= nowUtc);

        private static void ValidateQueueItemId(string queueItemId)
        {
            if (queueItemId == null || queueItemId.Length != 64 || queueItemId.Any(value => !Uri.IsHexDigit(value)))
            {
                throw new ArgumentException("Queue item ID must be a SHA-256 hex value.", nameof(queueItemId));
            }
        }

        private static void ValidateText(string value, string parameterName, int minimumLength, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < minimumLength || value.Length > maximumLength || value.Any(char.IsControl))
            {
                throw new ArgumentException(parameterName + " is invalid.", parameterName);
            }
        }

        private static string NormalizeResultCode(string value)
        {
            string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= 256 ? normalized : normalized.Substring(0, 256);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }

        private static string ComputeTextSha256(string value)
            => ComputeSha256(Encoding.UTF8.GetBytes(value));

        private static void WriteNew(string path, byte[] bytes)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        private static ActivityUploadScanIssue CloneIssue(ActivityUploadScanIssue issue)
            => new ActivityUploadScanIssue { DirectoryPath = issue.DirectoryPath, Code = issue.Code };
    }
}
