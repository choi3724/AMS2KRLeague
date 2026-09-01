using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMS2LeagueClient.Core.ActivityCapture.Upload
{
    public sealed class ActivityUploadWorker
    {
        private readonly ActivityUploadQueue _queue;
        private readonly IActivityUploadTransport _transport;
        private readonly IActivityUploadClock _clock;
        private readonly ActivityUploadRetryPolicy _retryPolicy;
        private readonly SemaphoreSlim _runGate = new SemaphoreSlim(1, 1);

        public ActivityUploadWorker(
            ActivityUploadQueue queue,
            IActivityUploadTransport transport,
            IActivityUploadClock? clock = null,
            IActivityUploadJitter? jitter = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clock = clock ?? new SystemActivityUploadClock();
            _retryPolicy = new ActivityUploadRetryPolicy(queue.Options, jitter);
        }

        public async Task<ActivityUploadWorkerSummary> ProcessDueAsync(CancellationToken cancellationToken)
        {
            await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var summary = new ActivityUploadWorkerSummary();
                // GetDueBatch returns detached item data and releases the queue lock.
                // No queue lock is held while the transport performs network I/O.
                var due = _queue.GetDueBatch(_queue.Options.MaximumDueBatchSize, _clock.UtcNow);
                foreach (ActivityUploadItem item in due)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    summary.Attempted++;
                    ActivityUploadTransportResult result;
                    try
                    {
                        result = await _transport.SendAsync(item, cancellationToken).ConfigureAwait(false);
                        if (result == null)
                        {
                            result = ActivityUploadTransportResult.NetworkFailure("NULL_TRANSPORT_RESULT");
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        result = ActivityUploadTransportResult.NetworkFailure("TRANSPORT_" + exception.GetType().Name.ToUpperInvariant());
                    }

                    DateTimeOffset attemptedAt = _clock.UtcNow;
                    if (result.Duplicate || result.StatusCode == 200 || result.StatusCode == 201)
                    {
                        _queue.MarkSent(item.Metadata.QueueItemId, attemptedAt, result.StatusCode, result.Duplicate);
                        summary.Sent++;
                    }
                    else if (result.StatusCode == 409)
                    {
                        _queue.MarkConflict(item.Metadata.QueueItemId, attemptedAt, result.StatusCode, ResultCode(result, "IDEMPOTENCY_CONFLICT"));
                        summary.Conflicts++;
                    }
                    else if (result.StatusCode == 422)
                    {
                        _queue.MarkQuarantined(item.Metadata.QueueItemId, attemptedAt, result.StatusCode, ResultCode(result, "SERVER_QUARANTINED"));
                        summary.Quarantined++;
                    }
                    else if (IsRetryable(result.StatusCode))
                    {
                        int failedAttemptCount = item.State.AttemptCount + 1;
                        DateTimeOffset nextAttempt = attemptedAt + _retryPolicy.GetDelay(failedAttemptCount);
                        _queue.MarkRetryable(item.Metadata.QueueItemId, attemptedAt, result.StatusCode, ResultCode(result, "UPLOAD_RETRYABLE"), nextAttempt);
                        summary.Retryable++;
                    }
                    else
                    {
                        _queue.MarkQuarantined(item.Metadata.QueueItemId, attemptedAt, result.StatusCode, ResultCode(result, "HTTP_NON_RETRYABLE"));
                        summary.Quarantined++;
                    }
                }

                return summary;
            }
            finally
            {
                _runGate.Release();
            }
        }

        public async Task RunAsync(TimeSpan pollInterval, CancellationToken cancellationToken)
        {
            if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessDueAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool IsRetryable(int? statusCode)
            => !statusCode.HasValue
                || statusCode == 408
                || statusCode == 425
                || statusCode == 429
                || statusCode >= 500;

        private static string ResultCode(ActivityUploadTransportResult result, string fallback)
            => string.IsNullOrWhiteSpace(result.ResultCode) ? fallback : result.ResultCode;
    }
}
