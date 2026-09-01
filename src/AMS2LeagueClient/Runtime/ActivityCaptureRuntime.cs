using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Runtime
{
    /// <summary>
    /// Owns Player activity capture, immutable local persistence and optional
    /// Cafe24 delivery. Observe performs no filesystem or network I/O.
    /// </summary>
    public sealed class ActivityCaptureRuntime : IDisposable
    {
        private static readonly TimeSpan UploadPollInterval = TimeSpan.FromSeconds(5);
        private const int PersistenceAttemptLimit = 3;
        private readonly object _engineGate = new object();
        private readonly ActivityCaptureEngine _engine;
        private readonly ActivityLocalParticipantResolver _localResolver = new ActivityLocalParticipantResolver();
        private readonly ActivityRecordStore _recordStore;
        private readonly ActivityUploadQueue _uploadQueue;
        private readonly ActivityUploadWorker? _uploadWorker;
        private readonly IDisposable? _uploadTransportDisposable;
        private readonly FileLogger _logger;
        private readonly Channel<ActivityCaptureUpdate> _persistChannel;
        private readonly CancellationTokenSource _uploadCancellation = new CancellationTokenSource();
        private readonly Task _recordTask;
        private readonly Task? _uploadTask;
        private bool _disposed;

        public ActivityCaptureRuntime(
            string dataRoot,
            string installationId,
            string clientVersion,
            FileLogger logger,
            IActivityUploadTransport? uploadTransport = null)
        {
            if (string.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("Activity data root is required.", nameof(dataRoot));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            string root = Path.GetFullPath(dataRoot);
            _engine = new ActivityCaptureEngine(installationId, clientVersion);
            _recordStore = new ActivityRecordStore(root);
            _uploadQueue = new ActivityUploadQueue(Path.Combine(root, "upload-queue"));
            _persistChannel = Channel.CreateUnbounded<ActivityCaptureUpdate>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            int pending = _uploadQueue.Scan().Count(item =>
                item.State.Status == ActivityUploadStatus.PENDING
                || item.State.Status == ActivityUploadStatus.FAILED_RETRYABLE);
            _logger.Info(
                "ACTIVITY_CAPTURE",
                "enabled=true source=SHARED_MEMORY_V14 localDurable=true uploadConfigured=" + (uploadTransport != null)
                + " pending=" + pending + " data=" + root);
            foreach (ActivityUploadScanIssue issue in _uploadQueue.LastScanIssues)
            {
                _logger.Warning("ACTIVITY_QUEUE_SCAN", "code=" + issue.Code + " path=" + issue.DirectoryPath);
            }

            if (uploadTransport != null)
            {
                _uploadTransportDisposable = uploadTransport as IDisposable;
                _uploadWorker = new ActivityUploadWorker(_uploadQueue, uploadTransport);
            }
            _recordTask = Task.Run(PersistLoopAsync);
            if (_uploadWorker != null)
            {
                _uploadTask = Task.Run(() => UploadLoopAsync(_uploadCancellation.Token));
            }
        }

        public void SetScheduledEvent(ScheduledLeagueEvent? scheduledEvent)
        {
            lock (_engineGate)
            {
                if (_disposed) return;
                _engine.SetScheduledEvent(scheduledEvent);
            }
        }

        public void Observe(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (_engineGate)
            {
                if (_disposed) return;
                ActivityLocalParticipantResolution local = _localResolver.Resolve(snapshot);
                Handle(_engine.Observe(snapshot, local.IsValid ? local.Participant : null));
            }
        }

        public void GameDetached()
        {
            lock (_engineGate)
            {
                if (_disposed) return;
                Handle(_engine.Close(DateTimeOffset.UtcNow, "GAME_DETACHED"));
            }
        }

        public void Dispose()
        {
            lock (_engineGate)
            {
                if (_disposed) return;
                Handle(_engine.Close(DateTimeOffset.UtcNow, "CLIENT_STOP"));
                _disposed = true;
                _persistChannel.Writer.TryComplete();
            }

            // Finish immutable local commits on a clean shutdown. Network delivery
            // is never required for exit because its queue is already durable.
            try
            {
                _recordTask.GetAwaiter().GetResult();
            }
            finally
            {
                _uploadCancellation.Cancel();
                try
                {
                    if (_uploadTask != null)
                    {
                        try
                        {
                            _uploadTask.GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                }
                finally
                {
                    _uploadCancellation.Dispose();
                    _uploadTransportDisposable?.Dispose();
                }
            }
        }

        private void Handle(ActivityCaptureUpdate update)
        {
            if (update.Events.Count == 0 && update.CompletedRecords.Count == 0)
            {
                return;
            }

            // The writer is completed under the same engine gate as all Handle
            // calls, so a rejection here is an invariant violation rather than
            // normal backpressure. The unbounded channel contains only lifecycle
            // events and finalized records, never 30 Hz snapshots.
            if (!_persistChannel.Writer.TryWrite(update))
            {
                throw new InvalidOperationException("Activity persistence channel rejected a finalized update.");
            }
        }

        private async Task PersistLoopAsync()
        {
            await foreach (ActivityCaptureUpdate update in _persistChannel.Reader.ReadAllAsync())
            {
                foreach (ActivityRecord record in update.CompletedRecords)
                {
                    await PersistRecordAsync(record).ConfigureAwait(false);
                }

                foreach (string eventValue in update.Events)
                {
                    LogInfoSafely("ACTIVITY_EVENT", eventValue);
                }
            }
        }

        private async Task PersistRecordAsync(ActivityRecord record)
        {
            Exception? lastError = null;
            for (int attempt = 1; attempt <= PersistenceAttemptLimit; attempt++)
            {
                try
                {
                    ActivityStoreOutcome stored = _recordStore.Commit(record);
                    if (!PlayerActivityUploadPayloadBuilder.TryBuild(record, out byte[] payload, out string reason))
                    {
                        LogInfoSafely("ACTIVITY_LOCAL_COMMIT", CommitDetails(record, stored));
                        LogInfoSafely("ACTIVITY_UPLOAD_SKIPPED", "activity=" + record.ActivityId + " reason=" + reason);
                        return;
                    }

                    ActivityEnqueueOutcome queued = _uploadQueue.Enqueue(
                        record.ActivityId,
                        Cafe24Routes.PlayerActivities,
                        PlayerActivityUploadPayloadBuilder.CreateIdempotencyKey(record),
                        payload);
                    LogInfoSafely("ACTIVITY_LOCAL_COMMIT", CommitDetails(record, stored));
                    LogInfoSafely(
                        "ACTIVITY_UPLOAD_QUEUE",
                        "activity=" + record.ActivityId + " disposition=" + queued.Disposition
                        + " queueItem=" + queued.Item.Metadata.QueueItemId
                        + " payloadSha256=" + queued.Item.Metadata.BodySha256);
                    return;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                    if (attempt < PersistenceAttemptLimit)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt)).ConfigureAwait(false);
                    }
                }
            }

            LogErrorSafely(
                "ACTIVITY_LOCAL_COMMIT_EXCEPTION",
                lastError ?? new IOException("Activity persistence failed without an exception."));
        }

        private static string CommitDetails(ActivityRecord record, ActivityStoreOutcome stored)
            => "activity=" + record.ActivityId + " type=" + record.ActivityType
                + " scopeHint=" + record.RecordScopeHint + " disposition=" + stored.Disposition
                + " payloadSha256=" + stored.PayloadSha256 + " path=" + stored.ActivityPath;

        private void LogInfoSafely(string eventName, string details)
        {
            try
            {
                _logger.Info(eventName, details);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }

        private void LogErrorSafely(string eventName, Exception exception)
        {
            try
            {
                _logger.Error(eventName, exception);
            }
            catch (Exception logException) when (logException is IOException || logException is UnauthorizedAccessException)
            {
            }
        }

        private async Task UploadLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    ActivityUploadWorkerSummary summary = await _uploadWorker!.ProcessDueAsync(cancellationToken).ConfigureAwait(false);
                    if (summary.Attempted > 0)
                    {
                        _logger.Info(
                            "ACTIVITY_UPLOAD_BATCH",
                            "attempted=" + summary.Attempted + " sent=" + summary.Sent
                            + " retryable=" + summary.Retryable + " conflict=" + summary.Conflicts
                            + " quarantined=" + summary.Quarantined);
                    }
                    await Task.Delay(UploadPollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.Error("ACTIVITY_UPLOAD_LOOP_EXCEPTION", exception);
                    await Task.Delay(UploadPollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    public static class Cafe24Routes
    {
        public const string PlayerActivities = "v1/player/activities";
    }
}
