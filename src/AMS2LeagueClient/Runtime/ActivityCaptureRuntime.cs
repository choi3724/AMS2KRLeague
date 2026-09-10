using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Presentation;
using System.Threading.Channels;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.SessionWitness;
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
        private static readonly ParticipantRoleClassifier Roles = new ParticipantRoleClassifier();
        private readonly object _engineGate = new object();
        private readonly ActivityCaptureEngine _engine;
        private readonly SessionPlayModeDetector _playModeDetector;
        private readonly SessionWitnessCaptureEngine _witnessEngine;
        private readonly FutureTelemetryCaptureRuntime _futureTelemetry;
        private readonly ActivityLocalParticipantResolver _localResolver = new ActivityLocalParticipantResolver();
        private readonly RaceUploadCompletionGate _completionGate;
        private TelemetrySnapshot? _completedRace;
        private readonly ActivityRecordStore _recordStore;
        private readonly SessionWitnessStore _witnessStore;
        private readonly ActivityUploadQueue _uploadQueue;
        private readonly ProvisionalActivityStore _provisional;
        private readonly Task _modeTask;
        private readonly TelemetryChunkUploadQueue _telemetryUploadQueue;
        private readonly ActivityUploadWorker? _uploadWorker;
        private readonly TelemetryChunkUploadWorker? _telemetryUploadWorker;
        private readonly IDisposable? _uploadTransportDisposable;
        private readonly FileLogger _logger;
        private readonly Channel<ActivityCaptureUpdate> _persistChannel;
        private readonly Channel<SessionWitnessUpdate> _witnessChannel;
        private readonly CancellationTokenSource _uploadCancellation = new CancellationTokenSource();
        private readonly Task _recordTask;
        private readonly Task _witnessTask;
        private readonly Task? _uploadTask;
        private bool _disposed;
        private bool _updateExitReserved;

        // No filesystem work or waits on a busy capture thread from the UI.
        public bool CanInstallUpdate
        {
            get
            {
                if (!Monitor.TryEnter(_engineGate)) return false;
                try
                {
                    return !_disposed && !_updateExitReserved
                        && _futureTelemetry.CurrentIdentity == null && _futureTelemetry.PendingRestartIdentity == null
                        && _futureTelemetry.AttemptLossLedgers.All(value => value.CloseRequested && value.FinalizeAcknowledged && value.DurableAck);
                }
                finally { Monitor.Exit(_engineGate); }
            }
        }

        public bool TryReserveUpdateExit()
        {
            if (!Monitor.TryEnter(_engineGate)) return false;
            try
            {
                if (!CanInstallUpdate) return false;
                _updateExitReserved = true;
                return true;
            }
            finally { Monitor.Exit(_engineGate); }
        }
        private string _lastModeDiagnostic = string.Empty;
        private readonly Dictionary<string, string> _sessionDiagnostics = new Dictionary<string, string>();

        public ActivityCaptureRuntime(
            string dataRoot,
            string installationId,
            string clientVersion,
            FileLogger logger,
            IActivityUploadTransport? uploadTransport = null,
            SessionPlayModeDetector? playModeDetector = null)
        {
            if (string.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("Activity data root is required.", nameof(dataRoot));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            string root = Path.GetFullPath(dataRoot);
            _playModeDetector = playModeDetector ?? new SessionPlayModeDetector(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Automobilista 2", "log"),
                Path.Combine(root, "session-play-mode-history.json"));
            _engine = new ActivityCaptureEngine(installationId, clientVersion);
            _witnessEngine = new SessionWitnessCaptureEngine(installationId, clientVersion);
            _futureTelemetry = new FutureTelemetryCaptureRuntime(
                Path.Combine(root, "future-telemetry"),
                installationId,
                clientVersion,
                options: new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 300_000
                },
                archiveFormat: TelemetryArchiveFormat.COMPACT_A2CT_V1);
            _futureTelemetry.FailureDiagnostic = details => LogInfoSafely("ARCHIVE_FAILURE", details);
            _futureTelemetry.IdentityStarted += BindWitnessArchiveIdentity;
            _completionGate = new RaceUploadCompletionGate(_futureTelemetry.ArchiveRoot);
            _telemetryUploadQueue = new TelemetryChunkUploadQueue(_futureTelemetry.ArchiveRoot,
                uploadEligibility: metadata => _completionGate.Allows(metadata.SessionId) && IsTelemetryUploadAllowed(metadata));
            _telemetryUploadQueue.DeliveryDiagnostic = details => LogInfoSafely("TELEMETRY_DELIVERY", details);
            _recordStore = new ActivityRecordStore(root);
            _witnessStore = new SessionWitnessStore(Path.Combine(root, "witness"));
            _uploadQueue = new ActivityUploadQueue(Path.Combine(root, "upload-queue"), uploadEligibility: item => _completionGate.Allows(item) && IsActivityUploadAllowed(item));
            _provisional = new ProvisionalActivityStore(Path.Combine(root, "provisional-activities"));
            _persistChannel = Channel.CreateUnbounded<ActivityCaptureUpdate>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _witnessChannel = Channel.CreateUnbounded<SessionWitnessUpdate>(new UnboundedChannelOptions
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
                if (uploadTransport is Cafe24ActivityUploadTransport cafe24)
                    cafe24.FailureDiagnostic = details => LogInfoSafely("UPLOAD_FORBIDDEN", details);
                _uploadTransportDisposable = uploadTransport as IDisposable;
                _uploadWorker = new ActivityUploadWorker(_uploadQueue, uploadTransport)
                { DeliveryDiagnostic = details => LogInfoSafely("ACTIVITY_DELIVERY", details) };
                if (uploadTransport is ITelemetryChunkUploadTransport telemetryTransport)
                {
                    _telemetryUploadWorker = new TelemetryChunkUploadWorker(
                        _telemetryUploadQueue,
                        telemetryTransport);
                }
                else
                {
                    _logger.Warning(
                        "FUTURE_TELEMETRY_UPLOAD_DISABLED",
                        "reason=TRANSPORT_NOT_SUPPORTED archive=" + _telemetryUploadQueue.Root);
                }
            }
            _recordTask = Task.Run(PersistLoopAsync);
            _witnessTask = Task.Run(PersistWitnessLoopAsync);
            _modeTask = Task.Run(() => ObserveModeLoopAsync(_uploadCancellation.Token));
            _uploadTask = Task.Run(() => UploadLoopAsync(_uploadCancellation.Token));
        }

        /// <summary>
        /// The common identity is available at capture start so SessionWitness can
        /// reuse these exact IDs. Consumers must not create a second witness ID.
        /// </summary>
        public event Action<TelemetryArchiveIdentity> TelemetryIdentityStarted
        {
            add => _futureTelemetry.IdentityStarted += value;
            remove => _futureTelemetry.IdentityStarted -= value;
        }

        public TelemetryArchiveIdentity? CurrentTelemetryIdentity => _futureTelemetry.CurrentIdentity;
        public SessionPlayMode DetectedPlayMode => _playModeDetector.CurrentMode;

        public void SetScheduledEvent(ScheduledLeagueEvent? scheduledEvent)
        {
            lock (_engineGate)
            {
                if (_disposed) return;
                _engine.SetScheduledEvent(scheduledEvent);
                _witnessEngine.SetScheduledEvent(scheduledEvent);
                _futureTelemetry.SetScheduledEventHint(scheduledEvent?.EventId);
            }
        }

        public void Observe(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (_engineGate)
            {
                if (_disposed || _updateExitReserved) return;
                if (_completedRace != null)
                {
                    bool gameplay = snapshot.KnownGameState == GameState.InGamePlaying
                        || snapshot.KnownGameState == GameState.InGamePaused
                        || snapshot.KnownGameState == GameState.InGameMenuTimeTicking;
                    bool newSession = snapshot.SessionStateRaw != (uint)SessionState.Race
                        && snapshot.SessionStateRaw != (uint)SessionState.FormationLap
                        && snapshot.SessionStateRaw != (uint)SessionState.Invalid;
                    bool restarted = snapshot.Participants.Any(value => value.IsActive && Roles.IsLeagueDriver(value)
                        && (value.RaceStateRaw == (uint)RaceState.Racing || value.RaceStateRaw == (uint)RaceState.NotStarted));
                    bool newTrack = snapshot.TrackLocation != _completedRace.TrackLocation
                        || snapshot.TrackVariation != _completedRace.TrackVariation;
                    if (!gameplay || string.IsNullOrWhiteSpace(snapshot.TrackLocation) || (!newSession && !restarted && !newTrack)) return;
                    _completedRace = null;
                }
                ActivityLocalParticipantResolution local = _localResolver.Resolve(snapshot);
                _futureTelemetry.Observe(snapshot);
                Handle(_engine.Observe(snapshot, local.IsValid ? local.Participant : null));
                HandleWitness(_witnessEngine.Observe(snapshot));
                if (_witnessEngine.HasStableRaceResult)
                {
                    // Both early and late observers use the whole-field terminal result gate.
                    // Persist the final replay/integrity blocks before making the witness eligible.
                    _futureTelemetry.CompleteRace(snapshot.CapturedAt);
                    Handle(_engine.Close(snapshot.CapturedAt, "RACE_RESULTS_READY"));
                    HandleWitness(_witnessEngine.Close(snapshot.CapturedAt, "RACE_RESULTS_READY"));
                    _completedRace = snapshot;
                }
                ReconcileWitnessArchiveIdentity(snapshot.CapturedAt);
            }
        }

        public void GameDetached()
        {
            lock (_engineGate)
            {
                if (_disposed) return;
                _futureTelemetry.GameDetached();
                Handle(_engine.Close(DateTimeOffset.UtcNow, "GAME_DETACHED"));
                HandleWitness(_witnessEngine.Close(DateTimeOffset.UtcNow, "GAME_DETACHED"));
            }
        }

        public void Dispose()
        {
            lock (_engineGate)
            {
                if (_disposed) return;
                Handle(_engine.Close(DateTimeOffset.UtcNow, "CLIENT_STOP"));
                HandleWitness(_witnessEngine.Close(DateTimeOffset.UtcNow, "CLIENT_STOP"));
                _futureTelemetry.GameDetached();
                _disposed = true;
                _persistChannel.Writer.TryComplete();
                _witnessChannel.Writer.TryComplete();
            }

            // Finish immutable local commits on a clean shutdown. Network delivery
            // is never required for exit because its queue is already durable.
            try
            {
                WaitForPersistence(_recordTask, "ACTIVITY_PERSIST_LOOP_EXCEPTION");
                WaitForPersistence(_witnessTask, "SESSION_WITNESS_PERSIST_LOOP_EXCEPTION");
            }
            finally
            {
                try
                {
                    try
                    {
                        _futureTelemetry.IdentityStarted -= BindWitnessArchiveIdentity;
                        _futureTelemetry.Dispose();
                        FutureTelemetryCaptureRuntimeCounters telemetryCounters = _futureTelemetry.Counters;
                        LogInfoSafely(
                            "FUTURE_TELEMETRY_STOP",
                            "attempts=" + telemetryCounters.StartedAttempts
                            + " batches=" + telemetryCounters.AcceptedBatches
                            + " dropped=" + telemetryCounters.DroppedBatches
                            + " chunks=" + telemetryCounters.CommittedChunks
                            + " archiveDropped=" + telemetryCounters.ArchiveDroppedMessages
                            + " failures=" + telemetryCounters.BackgroundFailures);
                    }
                    catch (Exception exception)
                    {
                        LogErrorSafely("FUTURE_TELEMETRY_STOP_EXCEPTION", exception);
                    }
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
                                Task.WhenAll(_uploadTask, _modeTask).GetAwaiter().GetResult();
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
        }

        private void WaitForPersistence(Task task, string eventName)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                // One failed persistence consumer must never prevent the other
                // durable queue from flushing during a clean shutdown.
                LogErrorSafely(eventName, exception);
            }
        }

        private void BindWitnessArchiveIdentity(TelemetryArchiveIdentity identity)
            => _witnessEngine.BeginArchiveIdentity(identity);

        private void ReconcileWitnessArchiveIdentity(DateTimeOffset capturedAtUtc)
        {
            TelemetryArchiveIdentity? pendingRestart = _futureTelemetry.PendingRestartIdentity;
            TelemetryArchiveIdentity? witnessIdentity = _witnessEngine.CurrentArchiveIdentity;
            if (pendingRestart != null && witnessIdentity != null)
            {
                if (!_futureTelemetry.SynchronizePendingRestartIdentity(witnessIdentity))
                {
                    // Never continue with two attempt identities. Both capture
                    // paths are closed through their existing background queues.
                    _futureTelemetry.GameDetached();
                    HandleWitness(_witnessEngine.Close(capturedAtUtc, "ARCHIVE_IDENTITY_SYNC_FAILED"));
                }
                return;
            }

            TelemetryArchiveIdentity? current = _futureTelemetry.CurrentIdentity;
            if (current != null && witnessIdentity != null && !SameArchiveIdentity(current, witnessIdentity))
            {
                _futureTelemetry.GameDetached();
                HandleWitness(_witnessEngine.Close(capturedAtUtc, "ARCHIVE_IDENTITY_MISMATCH"));
                return;
            }

            if (current == null && pendingRestart == null && witnessIdentity != null)
            {
                // Clears an identity reserved by a one-car Practice/Time Attack
                // that ended before SessionWitness became eligible.
                HandleWitness(_witnessEngine.Close(capturedAtUtc, "ARCHIVE_SCOPE_ENDED"));
            }
        }

        private static bool SameArchiveIdentity(
            TelemetryArchiveIdentity left,
            TelemetryArchiveIdentity right)
            => string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal)
                && string.Equals(left.SessionFingerprint, right.SessionFingerprint, StringComparison.Ordinal)
                && string.Equals(left.WitnessId, right.WitnessId, StringComparison.Ordinal)
                && string.Equals(left.AttemptId, right.AttemptId, StringComparison.Ordinal)
                && left.AttemptNumber == right.AttemptNumber;

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

        private void HandleWitness(SessionWitnessUpdate update)
        {
            if (update.Events.Count == 0 && update.FinalizedWitness == null)
            {
                return;
            }
            if (!_witnessChannel.Writer.TryWrite(update))
            {
                throw new InvalidOperationException("Session witness persistence channel rejected a finalized update.");
            }
        }

        private async Task PersistWitnessLoopAsync()
        {
            await foreach (SessionWitnessUpdate update in _witnessChannel.Reader.ReadAllAsync())
            {
                if (update.FinalizedWitness != null)
                {
                    await PersistWitnessAsync(update.FinalizedWitness).ConfigureAwait(false);
                }
                foreach (string eventValue in update.Events)
                {
                    LogInfoSafely("SESSION_WITNESS_EVENT", eventValue);
                }
            }
        }

        private async Task PersistWitnessAsync(SessionWitnessRecord witness)
        {
            _playModeDetector.Refresh();
            witness.RaceMode = SessionPlayModeDetector.WireValue(_playModeDetector.Classify(witness.CaptureStartedAtUtc, witness.CaptureEndedAtUtc));
            if (witness.Session.Activity != null) witness.Session.Activity.RaceMode = witness.RaceMode;
            Exception? lastError = null;
            for (int attempt = 1; attempt <= PersistenceAttemptLimit; attempt++)
            {
                try
                {
                    byte[] payload = SessionWitnessUploadPayloadBuilder.Build(witness);
                    SessionWitnessStoreOutcome stored = _witnessStore.Commit(witness, payload);
                    if (stored.Disposition == SessionWitnessStoreDisposition.ConflictQuarantined)
                        throw new InvalidDataException("WITNESS_LOCAL_IDENTITY_CONFLICT");
                    _provisional.Stage(
                        witness.WitnessId,
                        Cafe24Routes.SessionWitnesses,
                        SessionWitnessUploadPayloadBuilder.CreateIdempotencyKey(witness),
                        payload);
                    LogInfoSafely(
                        "SESSION_WITNESS_COMMIT",
                        "witness=" + witness.WitnessId
                        + " fingerprint=" + witness.SessionFingerprint
                        + " completeness=" + witness.CaptureCompleteness
                        + " disposition=" + stored.Disposition
                        + " bytes=" + payload.Length
                        + " captureSha256=" + stored.PayloadSha256
                        + " envelope=PROVISIONAL path=" + stored.WitnessPath);
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
                "SESSION_WITNESS_COMMIT_EXCEPTION",
                lastError ?? new IOException("Session witness persistence failed without an exception."));
        }

        private async Task PersistRecordAsync(ActivityRecord record)
        {
            _playModeDetector.Refresh();
            record.ObservedConditions.RaceMode = SessionPlayModeDetector.WireValue(_playModeDetector.Classify(record.StartedAtUtc, record.EndedAtUtc));
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

                    if (stored.Disposition == ActivityStoreDisposition.ConflictQuarantined)
                        throw new InvalidDataException("ACTIVITY_LOCAL_IDENTITY_CONFLICT");
                    _provisional.Stage(
                        record.ActivityId,
                        Cafe24Routes.PlayerActivities,
                        PlayerActivityUploadPayloadBuilder.CreateIdempotencyKey(record),
                        payload);
                    LogInfoSafely("ACTIVITY_LOCAL_COMMIT", CommitDetails(record, stored));
                    LogInfoSafely(
                        "ACTIVITY_UPLOAD_QUEUE",
                        "activity=" + record.ActivityId + " envelope=PROVISIONAL"
                        + " captureSha256=" + stored.PayloadSha256);
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

        public bool IsTelemetryUploadAllowed(TelemetryPendingUploadMetadata metadata)
        {
            metadata.RaceMode = metadata.FirstCapturedAtUtc.HasValue && metadata.LastCapturedAtUtc.HasValue
                ? SessionPlayModeDetector.WireValue(_playModeDetector.Classify(metadata.FirstCapturedAtUtc.Value, metadata.LastCapturedAtUtc.Value))
                : "UNKNOWN";
            return _playModeDetector.CanUpload(metadata.FirstCapturedAtUtc, metadata.LastCapturedAtUtc);
        }

        public bool IsActivityUploadAllowed(ActivityUploadItem item)
        {
            try
            {
                using var document = JsonDocument.Parse(item.PayloadUtf8);
                var root = document.RootElement;
                if (!root.TryGetProperty("raceMode", out var mode) || mode.GetString() != "MULTIPLAYER") return false;
                bool witness = root.TryGetProperty("schema", out var schema) && schema.GetString() == "ams2-session-witness-v1";
                if (!witness && schema.GetString() != "ams2-player-activity-v2") return false;
                return root.TryGetProperty(witness ? "captureStartedAtUtc" : "startedAtUtc", out var start)
                    && root.TryGetProperty(witness ? "captureEndedAtUtc" : "endedAtUtc", out var end)
                    && start.TryGetDateTimeOffset(out var first) && end.TryGetDateTimeOffset(out var last)
                    && _playModeDetector.CanUpload(first, last);
            }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException) { return false; }
        }

        private async Task ObserveModeLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _playModeDetector.Refresh();
                    foreach (var ledger in _futureTelemetry.AttemptLossLedgers)
                    {
                        string state = "session=" + ledger.SessionId + " attempt=" + ledger.AttemptId
                            + " capture=" + ledger.Completeness + " closeRequested=" + ledger.CloseRequested
                            + " finalizeAck=" + ledger.FinalizeAcknowledged + " durableAck=" + ledger.DurableAck
                            + " knownLoss=" + ledger.KnownLossCount + " completionGate=" + _completionGate.Allows(ledger.SessionId)
                            + " currentMode=" + SessionPlayModeDetector.WireValue(DetectedPlayMode)
                            + " block=" + (!ledger.FinalizeAcknowledged ? "WAIT_DURABLE_FINALIZE"
                                : !_completionGate.Allows(ledger.SessionId) ? "WAIT_STABLE_RACE_WITNESS" : "MULTIPLAYER_RANGE_CHECK_PER_PAYLOAD");
                        if (!_sessionDiagnostics.TryGetValue(ledger.AttemptId, out string? previous) || previous != state)
                        { _sessionDiagnostics[ledger.AttemptId] = state; LogInfoSafely("SESSION_CAPTURE_STATE", state); }
                    }
                    string diagnostic = "mode=" + SessionPlayModeDetector.WireValue(DetectedPlayMode)
                        + " source=AMS2_ONLINE_LOG detail=" + _playModeDetector.Diagnostic;
                    if (_lastModeDiagnostic != diagnostic)
                    {
                        _lastModeDiagnostic = diagnostic;
                        LogInfoSafely("SESSION_PLAY_MODE", diagnostic);
                    }
                    await Task.Delay(UploadPollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    LogErrorSafely("SESSION_MODE_OBSERVATION_FAILED", exception);
                    await Task.Delay(UploadPollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task UploadLoopAsync(CancellationToken cancellationToken)
        {
            // New write-ahead manifests only. Existing unproven orphans are
            // reported without mutation or automatic resend.
            try
            {
                var recovery = CompactArchiveEvidence.Recover(_futureTelemetry.ArchiveRoot, apply: true);
                LogInfoSafely("COMPACT_RECOVERY", "validated=" + recovery.ValidChunks
                    + " rebuilt=" + recovery.RebuiltPendingMetadata + " blocked=" + recovery.Issues.Count);
            }
            catch (Exception exception) { LogErrorSafely("COMPACT_RECOVERY_FAILED", exception); }
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _provisional.Reconcile(_uploadQueue, _playModeDetector.Classify,
                        (first, last) => _playModeDetector.CanUpload(first, last),
                        details => LogInfoSafely("ACTIVITY_ELIGIBILITY", details));
                    _completionGate.Refresh(_uploadQueue.Scan());
                    if (_uploadWorker != null)
                    {
                        ActivityUploadWorkerSummary summary =
                            await _uploadWorker.ProcessDueAsync(cancellationToken).ConfigureAwait(false);
                        if (summary.Attempted > 0)
                        {
                            _logger.Info(
                                "ACTIVITY_UPLOAD_BATCH",
                                "attempted=" + summary.Attempted + " sent=" + summary.Sent
                                + " retryable=" + summary.Retryable + " conflict=" + summary.Conflicts
                                + " quarantined=" + summary.Quarantined);
                        }
                    }
                    if (_telemetryUploadWorker != null)
                    {
                        TelemetryChunkUploadBatchResult telemetry =
                            await _telemetryUploadWorker.ProcessDueAsync(cancellationToken).ConfigureAwait(false);
                        if (telemetry.Attempted > 0)
                        {
                            _logger.Info(
                                "FUTURE_TELEMETRY_UPLOAD_BATCH",
                                "attempted=" + telemetry.Attempted + " sent=" + telemetry.Sent
                                + " retryable=" + telemetry.Retryable + " conflict=" + telemetry.Conflicts
                                + " quarantined=" + telemetry.Quarantined);
                        }
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
        public const string SessionWitnesses = "v1/session/witness";
    }
}
