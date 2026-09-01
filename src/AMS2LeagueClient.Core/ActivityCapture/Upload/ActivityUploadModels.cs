using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMS2LeagueClient.Core.ActivityCapture.Upload
{
    public enum ActivityUploadStatus
    {
        PENDING,
        SENT,
        FAILED_RETRYABLE,
        CONFLICT,
        QUARANTINED
    }

    public enum ActivityEnqueueDisposition
    {
        Enqueued,
        Duplicate,
        Conflict
    }

    public sealed class ActivityUploadMetadata
    {
        public string Schema { get; set; } = "ams2-activity-upload-metadata-v1";
        public string QueueItemId { get; set; } = string.Empty;
        public string ActivityId { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public string BodySha256 { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/json";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string RelatedQueueItemId { get; set; } = string.Empty;
    }

    public sealed class ActivityUploadState
    {
        public string Schema { get; set; } = "ams2-activity-upload-state-v1";
        public ActivityUploadStatus Status { get; set; } = ActivityUploadStatus.PENDING;
        public int AttemptCount { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public DateTimeOffset? LastAttemptAtUtc { get; set; }
        public DateTimeOffset? NextAttemptAtUtc { get; set; }
        public int? LastHttpStatus { get; set; }
        public string LastResult { get; set; } = string.Empty;
    }

    public sealed class ActivityUploadItem
    {
        private readonly byte[] _payloadUtf8;

        internal ActivityUploadItem(
            string directoryPath,
            ActivityUploadMetadata metadata,
            ActivityUploadState state,
            byte[] payloadUtf8)
        {
            DirectoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            State = state ?? throw new ArgumentNullException(nameof(state));
            _payloadUtf8 = (byte[])(payloadUtf8 ?? throw new ArgumentNullException(nameof(payloadUtf8))).Clone();
        }

        public string DirectoryPath { get; }
        public ActivityUploadMetadata Metadata { get; }
        public ActivityUploadState State { get; }
        public ReadOnlyMemory<byte> PayloadUtf8 => new ReadOnlyMemory<byte>(_payloadUtf8);
    }

    public sealed class ActivityEnqueueOutcome
    {
        public ActivityEnqueueDisposition Disposition { get; set; }
        public ActivityUploadItem Item { get; set; } = null!;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class ActivityUploadScanIssue
    {
        public string DirectoryPath { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public sealed class ActivityUploadQueueOptions
    {
        public int MaximumDueBatchSize { get; set; } = 8;
        public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromMinutes(15);
        public double RetryJitterRatio { get; set; } = 0.20;

        internal ActivityUploadQueueOptions ValidatedCopy()
        {
            if (MaximumDueBatchSize < 1 || MaximumDueBatchSize > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumDueBatchSize));
            }
            if (BaseRetryDelay <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(BaseRetryDelay));
            }
            if (MaximumRetryDelay < BaseRetryDelay)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumRetryDelay));
            }
            if (RetryJitterRatio < 0 || RetryJitterRatio > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(RetryJitterRatio));
            }

            return new ActivityUploadQueueOptions
            {
                MaximumDueBatchSize = MaximumDueBatchSize,
                BaseRetryDelay = BaseRetryDelay,
                MaximumRetryDelay = MaximumRetryDelay,
                RetryJitterRatio = RetryJitterRatio
            };
        }
    }

    public interface IActivityUploadClock
    {
        DateTimeOffset UtcNow { get; }
    }

    public sealed class SystemActivityUploadClock : IActivityUploadClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public interface IActivityUploadJitter
    {
        double NextDouble();
    }

    public sealed class RandomActivityUploadJitter : IActivityUploadJitter
    {
        private readonly object _gate = new object();
        private readonly Random _random;

        public RandomActivityUploadJitter()
            : this(Environment.TickCount)
        {
        }

        public RandomActivityUploadJitter(int seed)
        {
            _random = new Random(seed);
        }

        public double NextDouble()
        {
            lock (_gate)
            {
                return _random.NextDouble();
            }
        }
    }

    public sealed class ActivityUploadTransportResult
    {
        private ActivityUploadTransportResult(int? statusCode, bool duplicate, string resultCode)
        {
            StatusCode = statusCode;
            Duplicate = duplicate;
            ResultCode = resultCode ?? string.Empty;
        }

        public int? StatusCode { get; }
        public bool Duplicate { get; }
        public string ResultCode { get; }

        public static ActivityUploadTransportResult Http(int statusCode, bool duplicate = false, string resultCode = "")
            => new ActivityUploadTransportResult(statusCode, duplicate, resultCode);

        public static ActivityUploadTransportResult NetworkFailure(string resultCode = "NETWORK_UNAVAILABLE")
            => new ActivityUploadTransportResult(null, false, resultCode);
    }

    public interface IActivityUploadTransport
    {
        Task<ActivityUploadTransportResult> SendAsync(ActivityUploadItem item, CancellationToken cancellationToken);
    }

    public sealed class ActivityUploadWorkerSummary
    {
        public int Attempted { get; set; }
        public int Sent { get; set; }
        public int Retryable { get; set; }
        public int Conflicts { get; set; }
        public int Quarantined { get; set; }
    }
}
