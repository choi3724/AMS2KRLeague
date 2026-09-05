using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public enum TelemetryArchiveFormat
    {
        LEGACY_P023_JSON_GZIP,
        COMPACT_A2CT_V1
    }

    public sealed class FutureTelemetryCaptureRuntimeCounters
    {
        public long AcceptedBatches { get; set; }
        public long DroppedBatches { get; set; }
        public long SkippedByTwentyHertzGate { get; set; }
        public long StartedAttempts { get; set; }
        public long CompletedAttempts { get; set; }
        public long BackgroundFailures { get; set; }
        public long IdentityNotificationFailures { get; set; }
        public long CommittedChunks { get; set; }
        public long ArchiveDroppedMessages { get; set; }
    }

    /// <summary>
    /// Production front door for future telemetry. Snapshot mapping is capped at
    /// 20 Hz and the hot path only performs bounded TryWrite. Directory creation,
    /// recovery, JSON, gzip, hashing and durable commit are owned by the worker.
    /// </summary>
    public sealed class FutureTelemetryCaptureRuntime : IDisposable
    {
        private const int MappingIntervalMs = 50;
        private readonly object _gate = new object();
        private readonly string _archiveRoot;
        private readonly string _clientVersion;
        private readonly string _parserVersion;
        private readonly TelemetryArchiveOptions _options;
        private readonly Func<TelemetrySessionClock> _clockFactory;
        private readonly Func<string, TelemetryArchiveIdentity, TelemetryArchiveOptions, LocalDurableTelemetryArchive> _archiveFactory;
        private readonly Action? _beforeWorkerMessage;
        private readonly bool _emitCompactAttemptIntegrity;
        private readonly Func<
            TelemetryArchiveIdentity,
            TelemetryAttemptLossLedger,
            long,
            DateTimeOffset,
            int,
            IReadOnlyList<TelemetryChunkCommitOutcome>>? _compactAttemptIntegrityCommit;
        private readonly Channel<RuntimeCaptureMessage> _channel;
        private readonly Task _worker;
        private readonly object _ledgerGate = new object();
        private readonly Dictionary<string, AttemptLedgerState> _attemptLedgers =
            new Dictionary<string, AttemptLedgerState>(StringComparer.Ordinal);
        private FutureTelemetrySnapshotAdapter? _adapter;
        private TelemetrySessionClock? _clock;
        private TelemetryArchiveIdentity? _identity;
        private TelemetryArchiveIdentity? _pendingAttemptIdentity;
        private TelemetrySnapshot? _lastSnapshot;
        private string _trackKey = string.Empty;
        private string? _scheduledEventHint;
        private long? _nextMappingDueMs;
        private uint? _lastRaceState;
        private long _acceptedBatches;
        private long _droppedBatches;
        private long _skippedByGate;
        private long _startedAttempts;
        private long _completedAttempts;
        private long _backgroundFailures;
        private long _identityNotificationFailures;
        private long _committedChunks;
        private long _archiveDroppedMessages;
        private bool _disposed;

        public FutureTelemetryCaptureRuntime(
            string dataRoot,
            string installationId,
            string clientVersion,
            string parserVersion = "AMS2_SHM_V14",
            TelemetryArchiveOptions? options = null,
            Func<TelemetrySessionClock>? clockFactory = null,
            TelemetryArchiveFormat archiveFormat = TelemetryArchiveFormat.LEGACY_P023_JSON_GZIP)
            : this(
                dataRoot,
                installationId,
                clientVersion,
                parserVersion,
                options,
                clockFactory,
                CreateArchiveFactory(archiveFormat),
                null,
                archiveFormat == TelemetryArchiveFormat.COMPACT_A2CT_V1)
        {
        }

        private static Func<string, TelemetryArchiveIdentity, TelemetryArchiveOptions, LocalDurableTelemetryArchive>
            CreateArchiveFactory(TelemetryArchiveFormat archiveFormat)
        {
            if (!Enum.IsDefined(typeof(TelemetryArchiveFormat), archiveFormat))
            {
                throw new ArgumentOutOfRangeException(nameof(archiveFormat));
            }
            if (archiveFormat == TelemetryArchiveFormat.LEGACY_P023_JSON_GZIP)
            {
                return (root, identity, archiveOptions) =>
                    new LocalDurableTelemetryArchive(root, identity, archiveOptions);
            }
            return (root, identity, archiveOptions) =>
            {
                var compactStore = new CompactTelemetryChunkStore(root, identity, archiveOptions);
                return new LocalDurableTelemetryArchive(
                    root,
                    identity,
                    archiveOptions,
                    compactStore.Commit,
                    null);
            };
        }

        internal FutureTelemetryCaptureRuntime(
            string dataRoot,
            string installationId,
            string clientVersion,
            string parserVersion,
            TelemetryArchiveOptions? options,
            Func<TelemetrySessionClock>? clockFactory,
            Func<string, TelemetryArchiveIdentity, TelemetryArchiveOptions, LocalDurableTelemetryArchive>? archiveFactory,
            Action? beforeWorkerMessage,
            bool emitCompactAttemptIntegrity = false,
            Func<
                TelemetryArchiveIdentity,
                TelemetryAttemptLossLedger,
                long,
                DateTimeOffset,
                int,
                IReadOnlyList<TelemetryChunkCommitOutcome>>? compactAttemptIntegrityCommit = null)
        {
            if (string.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("Telemetry data root is required.", nameof(dataRoot));
            if (string.IsNullOrWhiteSpace(installationId)) throw new ArgumentException("Installation ID is required.", nameof(installationId));
            _archiveRoot = Path.GetFullPath(dataRoot);
            _clientVersion = clientVersion ?? string.Empty;
            _parserVersion = parserVersion ?? string.Empty;
            _options = (options ?? new TelemetryArchiveOptions()).ValidatedCopy();
            _clockFactory = clockFactory ?? (() => new TelemetrySessionClock());
            _archiveFactory = archiveFactory ?? ((root, identity, archiveOptions) =>
                new LocalDurableTelemetryArchive(root, identity, archiveOptions));
            _beforeWorkerMessage = beforeWorkerMessage;
            _emitCompactAttemptIntegrity = emitCompactAttemptIntegrity;
            _compactAttemptIntegrityCommit = compactAttemptIntegrityCommit;
            _channel = Channel.CreateBounded<RuntimeCaptureMessage>(new BoundedChannelOptions(_options.InputChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _worker = Task.Run(ProcessLoopAsync);
        }

        /// <summary>
        /// Raised once for every capture attempt before its first batch is queued.
        /// The value is the one common session/fingerprint/witness/attempt identity;
        /// SessionWitness integration must reuse it instead of generating another ID.
        /// </summary>
        public event Action<TelemetryArchiveIdentity>? IdentityStarted;

        public TelemetryArchiveIdentity? CurrentIdentity
        {
            get
            {
                lock (_gate)
                {
                    return _identity?.ValidatedCopy();
                }
            }
        }

        public TelemetryArchiveIdentity? PendingRestartIdentity
        {
            get
            {
                lock (_gate)
                {
                    return _pendingAttemptIdentity?.ValidatedCopy();
                }
            }
        }

        public string ArchiveRoot => _archiveRoot;

        public IReadOnlyList<TelemetryAttemptLossLedger> AttemptLossLedgers
        {
            get
            {
                lock (_ledgerGate)
                {
                    return _attemptLedgers.Values
                        .OrderBy(value => value.AttemptNumber)
                        .Select(value => value.Snapshot())
                        .ToArray();
                }
            }
        }

        public FutureTelemetryCaptureRuntimeCounters Counters => new FutureTelemetryCaptureRuntimeCounters
        {
            AcceptedBatches = Interlocked.Read(ref _acceptedBatches),
            DroppedBatches = Interlocked.Read(ref _droppedBatches),
            SkippedByTwentyHertzGate = Interlocked.Read(ref _skippedByGate),
            StartedAttempts = Interlocked.Read(ref _startedAttempts),
            CompletedAttempts = Interlocked.Read(ref _completedAttempts),
            BackgroundFailures = Interlocked.Read(ref _backgroundFailures),
            IdentityNotificationFailures = Interlocked.Read(ref _identityNotificationFailures),
            CommittedChunks = Interlocked.Read(ref _committedChunks),
            ArchiveDroppedMessages = Interlocked.Read(ref _archiveDroppedMessages)
        };

        public void SetScheduledEventHint(string? scheduledEventHint)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _scheduledEventHint = string.IsNullOrWhiteSpace(scheduledEventHint)
                    ? null
                    : scheduledEventHint.Trim();
                if (_identity != null) _identity.ScheduledEventHint = _scheduledEventHint;
                if (_pendingAttemptIdentity != null) _pendingAttemptIdentity.ScheduledEventHint = _scheduledEventHint;
            }
        }

        /// <summary>
        /// Reconciles the independently observed SessionWitness restart boundary
        /// before the next attempt begins. Only the attempt ID may differ; all
        /// stable join keys and the attempt number must already agree.
        /// </summary>
        public bool SynchronizePendingRestartIdentity(TelemetryArchiveIdentity identity)
        {
            TelemetryArchiveIdentity candidate =
                (identity ?? throw new ArgumentNullException(nameof(identity))).ValidatedCopy();
            lock (_gate)
            {
                if (_disposed || _identity != null || _pendingAttemptIdentity == null) return false;
                TelemetryArchiveIdentity pending = _pendingAttemptIdentity;
                if (!string.Equals(pending.SessionId, candidate.SessionId, StringComparison.Ordinal)
                    || !string.Equals(pending.SessionFingerprint, candidate.SessionFingerprint, StringComparison.Ordinal)
                    || !string.Equals(pending.WitnessId, candidate.WitnessId, StringComparison.Ordinal)
                    || pending.AttemptNumber != candidate.AttemptNumber)
                {
                    return false;
                }
                candidate.ScheduledEventHint = _scheduledEventHint;
                _pendingAttemptIdentity = candidate;
                return true;
            }
        }

        public bool Observe(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (_gate)
            {
                if (_disposed) return false;

                string trackKey = TrackKey(snapshot);
                if (_identity != null && _trackKey.Length > 0 && trackKey.Length > 0
                    && !string.Equals(_trackKey, trackKey, StringComparison.Ordinal))
                {
                    CloseCurrent(snapshot.CapturedAt, "TRACK_CHANGED", preserveSessionForNextAttempt: false);
                }

                if (IsExplicitRestart(snapshot))
                {
                    CloseCurrent(snapshot.CapturedAt, "RACE_RESTART", preserveSessionForNextAttempt: true);
                    _lastRaceState = snapshot.RaceStateRaw;
                    _lastSnapshot = snapshot;
                    return false;
                }

                if (!IsCaptureScope(snapshot))
                {
                    if (_identity != null)
                    {
                        CloseCurrent(snapshot.CapturedAt, "CAPTURE_SCOPE_ENDED", preserveSessionForNextAttempt: false);
                    }
                    _lastRaceState = snapshot.RaceStateRaw;
                    _lastSnapshot = snapshot;
                    return false;
                }

                if (_identity == null)
                {
                    StartAttempt(snapshot, trackKey);
                }

                TelemetryCaptureStamp stamp = _clock!.Capture(snapshot.CapturedAt.ToUniversalTime());
                if (!TakeTwentyHertzSlot(stamp.SessionElapsedMs))
                {
                    Interlocked.Increment(ref _skippedByGate);
                    _lastRaceState = snapshot.RaceStateRaw;
                    _lastSnapshot = snapshot;
                    return false;
                }

                FutureTelemetryCaptureBatch batch = _adapter!.Observe(snapshot, stamp);
                bool accepted = TryQueue(new RuntimeCaptureMessage(_identity!, batch));
                _lastRaceState = snapshot.RaceStateRaw;
                _lastSnapshot = snapshot;
                return accepted;
            }
        }

        public void GameDetached()
        {
            lock (_gate)
            {
                if (_disposed) return;
                CloseCurrent(DateTimeOffset.UtcNow, "GAME_DETACHED", preserveSessionForNextAttempt: false);
                _lastSnapshot = null;
                _lastRaceState = null;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                CloseCurrent(DateTimeOffset.UtcNow, "CLIENT_STOP", preserveSessionForNextAttempt: false);
                _disposed = true;
                _channel.Writer.TryComplete();
            }
            _worker.GetAwaiter().GetResult();
        }

        private void StartAttempt(TelemetrySnapshot snapshot, string trackKey)
        {
            TelemetryArchiveIdentity identity;
            if (_pendingAttemptIdentity != null)
            {
                identity = _pendingAttemptIdentity;
                _pendingAttemptIdentity = null;
            }
            else
            {
                identity = TelemetryArchiveIdentityFactory.StartSession(
                    CreateSessionFingerprint(snapshot),
                    witnessId: null,
                    attemptNumber: 1);
            }
            identity.ScheduledEventHint = _scheduledEventHint;
            _identity = identity;
            _adapter = new FutureTelemetrySnapshotAdapter(_clientVersion, _parserVersion);
            _clock = _clockFactory();
            _nextMappingDueMs = null;
            _trackKey = trackKey;
            _lastRaceState = null;
            lock (_ledgerGate)
            {
                _attemptLedgers[identity.AttemptId] = new AttemptLedgerState(identity);
            }
            Interlocked.Increment(ref _startedAttempts);
            NotifyIdentityStarted(identity);
        }

        private void CloseCurrent(
            DateTimeOffset capturedAtUtc,
            string reason,
            bool preserveSessionForNextAttempt)
        {
            if (_identity == null || _adapter == null || _clock == null) return;
            TelemetryArchiveIdentity closingIdentity = _identity;
            TelemetryCaptureStamp stamp = _clock.Capture(capturedAtUtc.ToUniversalTime());
            FutureTelemetryCaptureBatch end = _adapter.End(_lastSnapshot, stamp, reason);
            AttemptLedgerState ledger = GetAttemptLedger(closingIdentity.AttemptId);
            ledger.MarkCloseRequested();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var closeMessage = new RuntimeCaptureMessage(closingIdentity, end, closesAttempt: true, completion);
            try
            {
                // Close/finalize is deliberately not a hot-path TryWrite. It is
                // a bounded-channel WriteAsync followed by the worker's durable
                // acknowledgement, so a full queue cannot silently discard it.
                _channel.Writer.WriteAsync(closeMessage).AsTask().GetAwaiter().GetResult();
                Interlocked.Increment(ref _acceptedBatches);
                ledger.AddAccepted(closeMessage.Streams);
                if (completion.Task.GetAwaiter().GetResult())
                {
                    Interlocked.Increment(ref _completedAttempts);
                }
            }
            catch (Exception)
            {
                ledger.AddFailure(TelemetryStreamType.SESSION_METADATA, LossStage.FINALIZE, 1);
                ledger.MarkFinalizeAcknowledged(false);
                TryPersistLedger(ledger);
            }
            _pendingAttemptIdentity = preserveSessionForNextAttempt
                ? TelemetryArchiveIdentityFactory.NextAttempt(closingIdentity)
                : null;
            if (_pendingAttemptIdentity != null) _pendingAttemptIdentity.ScheduledEventHint = _scheduledEventHint;
            _identity = null;
            _adapter = null;
            _clock = null;
            _nextMappingDueMs = null;
            _trackKey = string.Empty;
            _lastRaceState = null;
        }

        private bool TryQueue(RuntimeCaptureMessage message)
        {
            AttemptLedgerState ledger = GetAttemptLedger(message.Identity.AttemptId);
            if (_channel.Writer.TryWrite(message))
            {
                Interlocked.Increment(ref _acceptedBatches);
                ledger.AddAccepted(message.Streams);
                return true;
            }
            Interlocked.Increment(ref _droppedBatches);
            foreach (TelemetryStreamType stream in message.Streams)
            {
                ledger.AddFailure(stream, LossStage.OUTER_QUEUE, 1);
            }
            return false;
        }

        private bool TakeTwentyHertzSlot(long elapsedMs)
        {
            if (!_nextMappingDueMs.HasValue) _nextMappingDueMs = elapsedMs;
            if (elapsedMs < _nextMappingDueMs.Value) return false;
            long slots = ((elapsedMs - _nextMappingDueMs.Value) / MappingIntervalMs) + 1;
            _nextMappingDueMs = checked(_nextMappingDueMs.Value + slots * MappingIntervalMs);
            return true;
        }

        private bool IsExplicitRestart(TelemetrySnapshot snapshot)
        {
            if (snapshot.KnownGameState == GameState.InGameRestarting) return true;
            return _identity != null
                && snapshot.SessionStateRaw == (uint)SessionState.Race
                && _lastRaceState == (uint)RaceState.Racing
                && snapshot.RaceStateRaw == (uint)RaceState.NotStarted;
        }

        private static bool IsCaptureScope(TelemetrySnapshot snapshot)
        {
            if (snapshot.Version != SharedMemoryLayout.SupportedVersion
                || !SnapshotValidator.IsParticipantCountValid(snapshot.NumParticipants)
                || snapshot.NumParticipants <= 0)
            {
                return false;
            }
            bool session = snapshot.SessionStateRaw == (uint)SessionState.Practice
                || snapshot.SessionStateRaw == (uint)SessionState.Test
                || snapshot.SessionStateRaw == (uint)SessionState.Qualify
                || snapshot.SessionStateRaw == (uint)SessionState.FormationLap
                || snapshot.SessionStateRaw == (uint)SessionState.Race
                || snapshot.SessionStateRaw == (uint)SessionState.TimeAttack;
            bool game = snapshot.KnownGameState == GameState.InGamePlaying
                || snapshot.KnownGameState == GameState.InGamePaused
                || snapshot.KnownGameState == GameState.InGameMenuTimeTicking;
            return session && game && snapshot.Participants.Take(snapshot.NumParticipants).Any(value => value.IsActive);
        }

        private async Task ProcessLoopAsync()
        {
            LocalDurableTelemetryArchive? archive = null;
            string currentAttemptId = string.Empty;
            try
            {
                await foreach (RuntimeCaptureMessage message in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    AttemptLedgerState ledger = GetAttemptLedger(message.Identity.AttemptId);
                    try
                    {
                        _beforeWorkerMessage?.Invoke();
                        if (archive == null || !string.Equals(currentAttemptId, message.Identity.AttemptId, StringComparison.Ordinal))
                        {
                            if (archive != null) await DisposeArchiveAsync(archive).ConfigureAwait(false);
                            archive = _archiveFactory(_archiveRoot, message.Identity, _options);
                            currentAttemptId = message.Identity.AttemptId;
                        }
                        Capture(archive, message.Batch);
                        if (message.ClosesAttempt)
                        {
                            foreach (TelemetryStreamLossLedger stream in ledger.Snapshot().Streams)
                            {
                                archive.RecordExternalDroppedInput(stream.StreamType, stream.OuterQueueLosses);
                            }
                            await DisposeArchiveAsync(archive).ConfigureAwait(false);
                            ApplyArchiveReport(ledger, archive.CompletionReport);
                            ledger.MarkDurableProcessingAcknowledged();
                            ledger.MarkFinalizeAcknowledged(true);
                            bool ledgerPersisted = true;
                            bool persisted = true;
                            if (_emitCompactAttemptIntegrity)
                            {
                                try
                                {
                                    TelemetryAttemptLossLedger integritySnapshot = ledger.Snapshot();
                                    long endElapsedMs = message.Batch.Metadata?.SessionElapsedMs
                                        ?? message.Batch.StoryEvents.LastOrDefault()?.SessionElapsedMs
                                        ?? 0;
                                    DateTimeOffset capturedAtUtc = message.Batch.Metadata?.CapturedAtUtc
                                        ?? message.Batch.StoryEvents.LastOrDefault()?.CapturedAtUtc
                                        ?? DateTimeOffset.UtcNow;
                                    IReadOnlyList<TelemetryChunkCommitOutcome> outcomes;
                                    if (_compactAttemptIntegrityCommit != null)
                                    {
                                        outcomes = _compactAttemptIntegrityCommit(
                                            message.Identity,
                                            integritySnapshot,
                                            endElapsedMs,
                                            capturedAtUtc,
                                            _options.ChunkDurationMs);
                                    }
                                    else
                                    {
                                        var compactStore = new CompactTelemetryChunkStore(
                                            _archiveRoot,
                                            message.Identity);
                                        outcomes = compactStore.CommitAttemptIntegrity(
                                            integritySnapshot,
                                            endElapsedMs,
                                            capturedAtUtc,
                                            _options.ChunkDurationMs);
                                    }
                                    if (outcomes.Count != 2 || outcomes.Any(value =>
                                        value.Disposition == TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED))
                                    {
                                        Interlocked.Increment(ref _backgroundFailures);
                                        ledger.AddFailure(
                                            TelemetryStreamType.SESSION_METADATA,
                                            LossStage.COMMIT_CONFLICT,
                                            1);
                                        persisted = false;
                                    }
                                    else
                                    {
                                        Interlocked.Add(ref _committedChunks, outcomes.Count);
                                    }
                                }
                                catch (Exception exception)
                                {
                                    Interlocked.Increment(ref _backgroundFailures);
                                    ledger.AddFailure(
                                        TelemetryStreamType.SESSION_METADATA,
                                        StageFor(exception),
                                        1);
                                    persisted = false;
                                }

                                // A compact attempt is authoritative only when 0x51 exists.
                                // Persist the JSON diagnostic mirror afterwards so a process
                                // crash can leave at worst a missing/stale mirror, never a
                                // durable JSON COMPLETE without its compact final ACK.
                                if (persisted && !TryPersistLedger(ledger))
                                {
                                    Interlocked.Increment(ref _backgroundFailures);
                                }
                            }
                            else
                            {
                                ledgerPersisted = TryPersistLedger(ledger);
                                persisted = ledgerPersisted;
                            }
                            if (!persisted)
                            {
                                if (!_emitCompactAttemptIntegrity && !ledgerPersisted)
                                {
                                    ledger.AddFailure(TelemetryStreamType.SESSION_METADATA, LossStage.DISK, 1);
                                }
                                ledger.AddFailure(TelemetryStreamType.SESSION_METADATA, LossStage.FINALIZE, 1);
                                ledger.MarkFinalizeAcknowledged(false);
                                TryPersistLedger(ledger);
                            }
                            message.CloseCompletion!.TrySetResult(persisted);
                            archive = null;
                            currentAttemptId = string.Empty;
                        }
                    }
                    catch (Exception exception)
                    {
                        Interlocked.Increment(ref _backgroundFailures);
                        TelemetryArchiveCompletionReport? report = archive?.CompletionReport;
                        if (message.ClosesAttempt && report != null)
                        {
                            ApplyArchiveReport(ledger, report);
                        }
                        LossStage stage = StageFor(exception);
                        if (!message.ClosesAttempt || report == null || !ReportContainsFailure(report, stage))
                        {
                            foreach (TelemetryStreamType stream in message.Streams)
                            {
                                ledger.AddFailure(stream, stage, 1);
                            }
                        }
                        if (message.ClosesAttempt)
                        {
                            if (report == null || report.Streams.All(value => value.FinalizeFailures == 0))
                            {
                                ledger.AddFailure(TelemetryStreamType.SESSION_METADATA, LossStage.FINALIZE, 1);
                            }
                            ledger.MarkFinalizeAcknowledged(false);
                            TryPersistLedger(ledger);
                            message.CloseCompletion!.TrySetResult(false);
                            archive = null;
                            currentAttemptId = string.Empty;
                        }
                    }
                }
            }
            finally
            {
                if (archive != null)
                {
                    try
                    {
                        await DisposeArchiveAsync(archive).ConfigureAwait(false);
                    }
                    catch
                    {
                        Interlocked.Increment(ref _backgroundFailures);
                    }
                }
            }
        }

        private static void Capture(LocalDurableTelemetryArchive archive, FutureTelemetryCaptureBatch batch)
        {
            if (batch.Metadata != null) archive.TryCaptureSessionMetadata(batch.Metadata);
            foreach (RaceStoryEventSample fact in batch.StoryEvents) archive.TryCaptureRaceStory(fact);
            if (batch.Frame != null) archive.TryCaptureFrame(batch.Frame);
        }

        private async Task DisposeArchiveAsync(LocalDurableTelemetryArchive archive)
        {
            await archive.DisposeAsync().ConfigureAwait(false);
            TelemetryArchiveRuntimeCounters counters = archive.Counters;
            Interlocked.Add(ref _committedChunks, counters.CommittedChunks);
            Interlocked.Add(ref _archiveDroppedMessages, counters.DroppedMessages);
        }

        private static void ApplyArchiveReport(
            AttemptLedgerState ledger,
            TelemetryArchiveCompletionReport report)
        {
            if (!ledger.TryBeginArchiveReport()) return;
            foreach (TelemetryArchiveStreamCompletion stream in report.Streams)
            {
                ledger.AddFailure(stream.StreamType, LossStage.ARCHIVE_INPUT, stream.ArchiveInputLosses);
                ledger.AddFailure(stream.StreamType, LossStage.CADENCE, stream.CadenceMissedSamples);
                ledger.AddFailure(stream.StreamType, LossStage.WORKER, stream.WorkerExceptions);
                ledger.AddFailure(stream.StreamType, LossStage.SERIALIZATION, stream.SerializationFailures);
                ledger.AddFailure(stream.StreamType, LossStage.DISK, stream.DiskWriteFailures);
                ledger.AddFailure(stream.StreamType, LossStage.COMMIT_CONFLICT, stream.CommitConflicts);
                ledger.AddFailure(stream.StreamType, LossStage.FINALIZE, stream.FinalizeFailures);
                ledger.AddDurableCommitAcks(stream.StreamType, stream.DurableCommitAcks);
            }
        }

        private static bool ReportContainsFailure(
            TelemetryArchiveCompletionReport report,
            LossStage stage)
            => stage switch
            {
                LossStage.SERIALIZATION => report.Streams.Any(value => value.SerializationFailures > 0),
                LossStage.DISK => report.Streams.Any(value => value.DiskWriteFailures > 0),
                LossStage.WORKER => report.Streams.Any(value => value.WorkerExceptions > 0),
                LossStage.COMMIT_CONFLICT => report.Streams.Any(value => value.CommitConflicts > 0),
                _ => false
            };

        private static LossStage StageFor(Exception exception)
        {
            if (exception is JsonException || exception is NotSupportedException) return LossStage.SERIALIZATION;
            if (exception is IOException || exception is UnauthorizedAccessException) return LossStage.DISK;
            return LossStage.WORKER;
        }

        private AttemptLedgerState GetAttemptLedger(string attemptId)
        {
            lock (_ledgerGate)
            {
                if (_attemptLedgers.TryGetValue(attemptId, out AttemptLedgerState? ledger)) return ledger;
                throw new InvalidOperationException("Telemetry attempt ledger was not initialized.");
            }
        }

        private bool TryPersistLedger(AttemptLedgerState ledger)
        {
            TelemetryAttemptLossLedger snapshot = ledger.Snapshot();
            string directory = Path.Combine(_archiveRoot, "attempt-ledgers");
            string key = TelemetryChunkSerializer.StableId(
                snapshot.SessionFingerprint,
                snapshot.WitnessId,
                snapshot.AttemptId).Substring(0, 32);
            string target = Path.Combine(directory, key + ".attempt-loss.json");
            string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(directory);
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };
                jsonOptions.Converters.Add(new JsonStringEnumConverter());
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, jsonOptions);
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16_384,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporary, target, true);
                return true;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException ||
                exception is NotSupportedException)
            {
                return false;
            }
        }

        private void NotifyIdentityStarted(TelemetryArchiveIdentity identity)
        {
            Action<TelemetryArchiveIdentity>? handler = IdentityStarted;
            if (handler == null) return;
            foreach (Delegate subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<TelemetryArchiveIdentity>)subscriber)(identity.ValidatedCopy());
                }
                catch
                {
                    Interlocked.Increment(ref _identityNotificationFailures);
                }
            }
        }

        private string CreateSessionFingerprint(TelemetrySnapshot snapshot)
        {
            string vehicleClass = !string.IsNullOrWhiteSpace(snapshot.RootCarClassName)
                ? snapshot.RootCarClassName
                : snapshot.Participants
                    .Take(snapshot.NumParticipants)
                    .Select(value => value.VehicleClass)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            double duration = snapshot.SessionDuration > 0
                && !float.IsNaN(snapshot.SessionDuration)
                && !float.IsInfinity(snapshot.SessionDuration)
                    ? Math.Round(snapshot.SessionDuration, 1, MidpointRounding.AwayFromZero)
                    : 0;
            // Deliberately mirrors SessionWitnessFingerprint.CreateSession so
            // different installations observing the same configuration share a
            // merge key. WitnessId/AttemptId still keep raw witnesses distinct.
            string seed = string.Join("|", new[]
            {
                "session-witness-v2",
                Normalize(snapshot.TrackLocation),
                Normalize(snapshot.TrackVariation),
                Normalize(vehicleClass),
                duration.ToString("0.0", CultureInfo.InvariantCulture),
                snapshot.LapsInEvent.ToString(CultureInfo.InvariantCulture),
                Normalize(_scheduledEventHint)
            });
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        }

        private static string TrackKey(TelemetrySnapshot snapshot)
            => Normalize(snapshot.TrackLocation) + "|" + Normalize(snapshot.TrackVariation);

        private static string Normalize(string? value)
            => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

        private sealed class RuntimeCaptureMessage
        {
            public RuntimeCaptureMessage(
                TelemetryArchiveIdentity identity,
                FutureTelemetryCaptureBatch batch,
                bool closesAttempt = false,
                TaskCompletionSource<bool>? closeCompletion = null)
            {
                Identity = identity.ValidatedCopy();
                Batch = batch ?? throw new ArgumentNullException(nameof(batch));
                ClosesAttempt = closesAttempt;
                CloseCompletion = closeCompletion;
                Streams = StreamsFor(batch);
                if (closesAttempt && closeCompletion == null)
                {
                    throw new ArgumentException("A closing telemetry message requires acknowledgement.", nameof(closeCompletion));
                }
            }

            public TelemetryArchiveIdentity Identity { get; }
            public FutureTelemetryCaptureBatch Batch { get; }
            public bool ClosesAttempt { get; }
            public TaskCompletionSource<bool>? CloseCompletion { get; }
            public TelemetryStreamType[] Streams { get; }

            private static TelemetryStreamType[] StreamsFor(FutureTelemetryCaptureBatch batch)
            {
                var streams = new HashSet<TelemetryStreamType>();
                if (batch.Metadata != null) streams.Add(TelemetryStreamType.SESSION_METADATA);
                if (batch.StoryEvents.Count > 0) streams.Add(TelemetryStreamType.RACE_STORY);
                if (batch.Frame != null)
                {
                    streams.Add(TelemetryStreamType.PARTICIPANT_REPLAY);
                    streams.Add(TelemetryStreamType.INCIDENT_TRACE);
                    if (batch.Frame.LocalDriver != null && batch.Frame.LocalDriver.LocalParticipantResolved)
                    {
                        streams.Add(TelemetryStreamType.DRIVER_TELEMETRY);
                    }
                }
                return streams.OrderBy(value => value).ToArray();
            }
        }

        private enum LossStage
        {
            OUTER_QUEUE,
            ARCHIVE_INPUT,
            CADENCE,
            WORKER,
            SERIALIZATION,
            DISK,
            COMMIT_CONFLICT,
            FINALIZE,
            UPLOAD
        }

        private sealed class AttemptLedgerState
        {
            private readonly object _gate = new object();
            private readonly TelemetryArchiveIdentity _identity;
            private readonly TelemetryStreamLossLedger[] _streams;
            private bool _closeRequested;
            private bool _finalizeAcknowledged;
            private bool _archiveReportApplied;

            public AttemptLedgerState(TelemetryArchiveIdentity identity)
            {
                _identity = identity.ValidatedCopy();
                _streams = Enum.GetValues<TelemetryStreamType>()
                    .Select(value => new TelemetryStreamLossLedger { StreamType = value })
                    .ToArray();
            }

            public int AttemptNumber => _identity.AttemptNumber;

            public void AddAccepted(IEnumerable<TelemetryStreamType> streams)
            {
                lock (_gate)
                {
                    foreach (TelemetryStreamType stream in streams)
                    {
                        _streams[(int)stream].AcceptedWorkUnits = checked(
                            _streams[(int)stream].AcceptedWorkUnits + 1);
                    }
                }
            }

            public void AddFailure(TelemetryStreamType stream, LossStage stage, long count)
            {
                if (count <= 0) return;
                lock (_gate)
                {
                    TelemetryStreamLossLedger value = _streams[(int)stream];
                    switch (stage)
                    {
                        case LossStage.OUTER_QUEUE:
                            value.OuterQueueLosses = checked(value.OuterQueueLosses + count);
                            break;
                        case LossStage.ARCHIVE_INPUT:
                            value.ArchiveInputLosses = checked(value.ArchiveInputLosses + count);
                            break;
                        case LossStage.CADENCE:
                            value.CadenceMissedSamples = checked(value.CadenceMissedSamples + count);
                            break;
                        case LossStage.WORKER:
                            value.WorkerExceptions = checked(value.WorkerExceptions + count);
                            break;
                        case LossStage.SERIALIZATION:
                            value.SerializationFailures = checked(value.SerializationFailures + count);
                            break;
                        case LossStage.DISK:
                            value.DiskWriteFailures = checked(value.DiskWriteFailures + count);
                            break;
                        case LossStage.COMMIT_CONFLICT:
                            value.CommitConflicts = checked(value.CommitConflicts + count);
                            break;
                        case LossStage.FINALIZE:
                            value.FinalizeFailures = checked(value.FinalizeFailures + count);
                            break;
                        case LossStage.UPLOAD:
                            value.UploadFailures = checked(value.UploadFailures + count);
                            break;
                    }
                }
            }

            public void AddDurableCommitAcks(TelemetryStreamType stream, long count)
            {
                if (count <= 0) return;
                lock (_gate)
                {
                    TelemetryStreamLossLedger value = _streams[(int)stream];
                    value.DurableCommitAcks = checked(value.DurableCommitAcks + count);
                }
            }

            public void MarkCloseRequested()
            {
                lock (_gate) _closeRequested = true;
            }

            public void MarkFinalizeAcknowledged(bool value)
            {
                lock (_gate) _finalizeAcknowledged = value;
            }

            public void MarkDurableProcessingAcknowledged()
            {
                lock (_gate)
                {
                    foreach (TelemetryStreamLossLedger stream in _streams.Where(value => value.AcceptedWorkUnits > 0))
                    {
                        stream.DurableProcessingAcknowledged = true;
                    }
                }
            }

            public bool TryBeginArchiveReport()
            {
                lock (_gate)
                {
                    if (_archiveReportApplied) return false;
                    _archiveReportApplied = true;
                    return true;
                }
            }

            public TelemetryAttemptLossLedger Snapshot()
            {
                lock (_gate)
                {
                    List<TelemetryStreamLossLedger> streams = _streams.Select(Copy).ToList();
                    bool durableAck = streams
                        .Where(value => value.AcceptedWorkUnits > 0)
                        .All(value => value.DurableProcessingAcknowledged);
                    long knownLoss = streams.Sum(value => value.KnownLossCount);
                    TelemetryAttemptCompleteness completeness = !_closeRequested
                        ? TelemetryAttemptCompleteness.IN_PROGRESS
                        : _finalizeAcknowledged && durableAck && knownLoss == 0
                            ? TelemetryAttemptCompleteness.COMPLETE
                            : TelemetryAttemptCompleteness.PARTIAL;
                    return new TelemetryAttemptLossLedger
                    {
                        SessionId = _identity.SessionId,
                        SessionFingerprint = _identity.SessionFingerprint,
                        WitnessId = _identity.WitnessId,
                        AttemptId = _identity.AttemptId,
                        AttemptNumber = _identity.AttemptNumber,
                        CloseRequested = _closeRequested,
                        FinalizeAcknowledged = _finalizeAcknowledged,
                        DurableAck = durableAck,
                        Completeness = completeness,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Streams = streams
                    };
                }
            }

            private static TelemetryStreamLossLedger Copy(TelemetryStreamLossLedger value)
                => new TelemetryStreamLossLedger
                {
                    StreamType = value.StreamType,
                    AcceptedWorkUnits = value.AcceptedWorkUnits,
                    OuterQueueLosses = value.OuterQueueLosses,
                    ArchiveInputLosses = value.ArchiveInputLosses,
                    CadenceMissedSamples = value.CadenceMissedSamples,
                    WorkerExceptions = value.WorkerExceptions,
                    SerializationFailures = value.SerializationFailures,
                    DiskWriteFailures = value.DiskWriteFailures,
                    CommitConflicts = value.CommitConflicts,
                    FinalizeFailures = value.FinalizeFailures,
                    UploadFailures = value.UploadFailures,
                    DurableCommitAcks = value.DurableCommitAcks,
                    DurableProcessingAcknowledged = value.DurableProcessingAcknowledged
                };
        }
    }
}
