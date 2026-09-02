using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    /// <summary>
    /// Bounded, non-blocking ingestion front door for future telemetry facts.
    /// TryCapture methods perform no file, compression, hashing, JSON, HTTP, or DB work.
    /// Callers must treat accepted DTOs and their participant collections as immutable.
    /// </summary>
    public sealed class LocalDurableTelemetryArchive : IAsyncDisposable
    {
        private const double IncidentNearbyRadiusMeters = 50.0;
        private const int MaximumNearbyIncidentParticipants = 4;
        private readonly TelemetryArchiveOptions _options;
        private readonly TelemetryChunkStore _store;
        private readonly Func<TelemetryChunkEnvelope, TelemetryChunkCommitOutcome> _commit;
        private readonly Action? _beforeFinalize;
        private readonly Channel<CaptureMessage> _channel;
        private readonly Task _worker;
        private readonly Dictionary<(TelemetryStreamType Stream, int Index), TelemetryChunkAccumulator> _chunks =
            new Dictionary<(TelemetryStreamType Stream, int Index), TelemetryChunkAccumulator>();
        private readonly Queue<IncidentFrame> _incidentRing = new Queue<IncidentFrame>();
        private readonly List<ActiveIncidentBurst> _activeIncidents = new List<ActiveIncidentBurst>();
        private readonly long[] _droppedInputByStream = new long[5];
        private readonly long[] _archiveInputLossByStream = new long[5];
        private readonly long[] _cadenceLossByStream = new long[5];
        private readonly long[] _workerLossByStream = new long[5];
        private readonly long[] _serializationLossByStream = new long[5];
        private readonly long[] _diskLossByStream = new long[5];
        private readonly long[] _commitConflictByStream = new long[5];
        private readonly long[] _finalizeLossByStream = new long[5];
        private readonly long[] _durableCommitAcksByStream = new long[5];
        private long _acceptedMessages;
        private long _droppedMessages;
        private long _committedChunks;
        private long _commitFailures;
        private long? _nextReplayDueMs;
        private long? _nextDriverDueMs;
        private long? _nextIncidentDueMs;
        private long _lastMetadataElapsedMs = -1;
        private long _lastStoryElapsedMs = -1;
        private long _lastFrameElapsedMs = -1;
        private int _finalizeAcknowledged;
        private int _disposed;

        public LocalDurableTelemetryArchive(
            string root,
            TelemetryArchiveIdentity identity,
            TelemetryArchiveOptions? options = null)
            : this(root, identity, options, null, null)
        {
        }

        internal LocalDurableTelemetryArchive(
            string root,
            TelemetryArchiveIdentity identity,
            TelemetryArchiveOptions? options,
            Func<TelemetryChunkEnvelope, TelemetryChunkCommitOutcome>? commitOverride,
            Action? beforeFinalize)
        {
            _options = (options ?? new TelemetryArchiveOptions()).ValidatedCopy();
            _store = new TelemetryChunkStore(root, identity);
            _commit = commitOverride ?? _store.Commit;
            _beforeFinalize = beforeFinalize;
            RecoveryReport = _store.Recover();
            _channel = Channel.CreateBounded<CaptureMessage>(new BoundedChannelOptions(_options.InputChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _worker = Task.Run(ProcessLoopAsync);
        }

        public TelemetryArchiveIdentity Identity => _store.Identity;
        public string SessionDirectory => _store.SessionDirectory;
        public TelemetryArchiveRecoveryReport RecoveryReport { get; }

        public TelemetryArchiveRuntimeCounters Counters => new TelemetryArchiveRuntimeCounters
        {
            AcceptedMessages = Interlocked.Read(ref _acceptedMessages),
            DroppedMessages = Interlocked.Read(ref _droppedMessages),
            CommittedChunks = Interlocked.Read(ref _committedChunks),
            CommitFailures = Interlocked.Read(ref _commitFailures)
        };

        public TelemetryArchiveCompletionReport CompletionReport
        {
            get
            {
                var report = new TelemetryArchiveCompletionReport
                {
                    FinalizeAcknowledged = Volatile.Read(ref _finalizeAcknowledged) != 0
                };
                foreach (TelemetryStreamType stream in Enum.GetValues<TelemetryStreamType>())
                {
                    int index = (int)stream;
                    report.Streams.Add(new TelemetryArchiveStreamCompletion
                    {
                        StreamType = stream,
                        ArchiveInputLosses = Interlocked.Read(ref _archiveInputLossByStream[index]),
                        CadenceMissedSamples = Interlocked.Read(ref _cadenceLossByStream[index]),
                        WorkerExceptions = Interlocked.Read(ref _workerLossByStream[index]),
                        SerializationFailures = Interlocked.Read(ref _serializationLossByStream[index]),
                        DiskWriteFailures = Interlocked.Read(ref _diskLossByStream[index]),
                        CommitConflicts = Interlocked.Read(ref _commitConflictByStream[index]),
                        FinalizeFailures = Interlocked.Read(ref _finalizeLossByStream[index]),
                        DurableCommitAcks = Interlocked.Read(ref _durableCommitAcksByStream[index])
                    });
                }
                return report;
            }
        }

        public bool TryCaptureSessionMetadata(SessionMetadataSample sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (sample.SessionElapsedMs < 0 || sample.Fields.Count > _options.MaximumMetadataFieldsPerRecord ||
                sample.Participants.Count > _options.MaximumParticipantsPerFrame ||
                TooLong(sample.ClientVersion) || TooLong(sample.ParserVersion) || TooLong(sample.Track) ||
                TooLong(sample.Layout) || TooLong(sample.SessionType) || TooLong(sample.CaptureCompleteness) ||
                sample.Fields.Any(pair => TooLong(pair.Key) || TooLong(pair.Value.TextValue) || TooLong(pair.Value.Unit)) ||
                sample.Participants.Any(value => TooLong(value.NameSnapshot) || TooLong(value.VehicleRef) || TooLong(value.VehicleClassRef)))
            {
                return Drop(TelemetryStreamType.SESSION_METADATA);
            }
            return TryEnqueue(CaptureMessage.ForMetadata(sample), TelemetryStreamType.SESSION_METADATA);
        }

        public bool TryCaptureRaceStory(RaceStoryEventSample sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (sample.SessionElapsedMs < 0 || string.IsNullOrWhiteSpace(sample.EventId) ||
                string.IsNullOrWhiteSpace(sample.EventType) || TooLong(sample.EventId) ||
                TooLong(sample.EventType) || TooLong(sample.FactCode))
            {
                return Drop(TelemetryStreamType.RACE_STORY);
            }
            return TryEnqueue(CaptureMessage.ForStory(sample), TelemetryStreamType.RACE_STORY);
        }

        public bool TryCaptureFrame(TelemetryFrameSample sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (sample.SessionElapsedMs < 0 || sample.Participants.Count > _options.MaximumParticipantsPerFrame ||
                sample.Participants.Any(value => TooLong(value.NameSnapshot) || TooLong(value.VehicleRef) ||
                    TooLong(value.VehicleClassRef)) ||
                (sample.IncidentCandidate != null &&
                    (TooLong(sample.IncidentCandidate.CandidateId) || TooLong(sample.IncidentCandidate.TriggerCode) ||
                     sample.IncidentCandidate.RelatedParticipantRefs.Length > _options.MaximumParticipantsPerFrame)) ||
                (sample.LocalDriver != null &&
                    (sample.LocalDriver.TyreTemperaturesCelsius.Length > 4 ||
                     sample.LocalDriver.TyrePressuresKpa.Length > 4 || sample.LocalDriver.TyreWear.Length > 4)))
            {
                return DropFrame(sample);
            }
            if (Volatile.Read(ref _disposed) != 0) return DropFrame(sample);
            if (_channel.Writer.TryWrite(CaptureMessage.ForFrame(sample)))
            {
                Interlocked.Increment(ref _acceptedMessages);
                return true;
            }
            return DropFrame(sample);
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _channel.Writer.WriteAsync(CaptureMessage.ForFlush(completion), cancellationToken).ConfigureAwait(false);
            using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
            {
                await completion.Task.ConfigureAwait(false);
            }
        }

        public IReadOnlyList<TelemetryPendingUploadMetadata> ScanPending()
            => _store.ScanPending();

        internal void RecordExternalDroppedInput(TelemetryStreamType streamType, long count)
        {
            if (count <= 0) return;
            Interlocked.Add(ref _droppedInputByStream[(int)streamType], count);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? failure = null;
            try
            {
                await _channel.Writer.WriteAsync(CaptureMessage.ForFinalize(completion)).ConfigureAwait(false);
                await completion.Task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _channel.Writer.TryComplete();
                try
                {
                    await _worker.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
            if (failure != null) throw failure;
        }

        private bool TryEnqueue(CaptureMessage message, TelemetryStreamType streamType)
        {
            if (Volatile.Read(ref _disposed) != 0) return Drop(streamType);
            if (_channel.Writer.TryWrite(message))
            {
                Interlocked.Increment(ref _acceptedMessages);
                return true;
            }
            return Drop(streamType);
        }

        private bool Drop(TelemetryStreamType streamType)
        {
            Interlocked.Increment(ref _droppedMessages);
            Interlocked.Increment(ref _droppedInputByStream[(int)streamType]);
            Interlocked.Increment(ref _archiveInputLossByStream[(int)streamType]);
            return false;
        }

        private bool DropFrame(TelemetryFrameSample sample)
        {
            Interlocked.Increment(ref _droppedMessages);
            Interlocked.Increment(ref _droppedInputByStream[(int)TelemetryStreamType.PARTICIPANT_REPLAY]);
            Interlocked.Increment(ref _droppedInputByStream[(int)TelemetryStreamType.INCIDENT_TRACE]);
            Interlocked.Increment(ref _archiveInputLossByStream[(int)TelemetryStreamType.PARTICIPANT_REPLAY]);
            Interlocked.Increment(ref _archiveInputLossByStream[(int)TelemetryStreamType.INCIDENT_TRACE]);
            if (sample.LocalDriver != null && sample.LocalDriver.LocalParticipantResolved)
            {
                Interlocked.Increment(ref _droppedInputByStream[(int)TelemetryStreamType.DRIVER_TELEMETRY]);
                Interlocked.Increment(ref _archiveInputLossByStream[(int)TelemetryStreamType.DRIVER_TELEMETRY]);
            }
            return false;
        }

        private async Task ProcessLoopAsync()
        {
            await foreach (CaptureMessage message in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (message.Kind == CaptureMessageKind.FLUSH || message.Kind == CaptureMessageKind.FINALIZE)
                {
                    bool finalizing = message.Kind == CaptureMessageKind.FINALIZE;
                    try
                    {
                        if (finalizing) _beforeFinalize?.Invoke();
                        FinalizeActiveIncidents();
                        FlushAll();
                        if (finalizing) Volatile.Write(ref _finalizeAcknowledged, 1);
                        message.Completion!.TrySetResult(true);
                    }
                    catch (Exception exception)
                    {
                        if (finalizing) RecordFinalizeFailure();
                        message.Completion!.TrySetException(exception);
                    }
                    continue;
                }

                try
                {
                    switch (message.Kind)
                    {
                        case CaptureMessageKind.METADATA:
                            ProcessMetadata(message.Metadata!);
                            break;
                        case CaptureMessageKind.STORY:
                            ProcessStory(message.Story!);
                            break;
                        case CaptureMessageKind.FRAME:
                            ProcessFrame(message.Frame!);
                            break;
                    }
                }
                catch (Exception)
                {
                    // A malformed input fact is isolated from subsequent facts. Durable commit
                    // exceptions are counted inside Commit and retried on the next flush.
                    Interlocked.Increment(ref _droppedMessages);
                    foreach (TelemetryStreamType stream in message.Streams())
                    {
                        Interlocked.Increment(ref _workerLossByStream[(int)stream]);
                    }
                }
            }
        }

        private void ProcessMetadata(SessionMetadataSample sample)
        {
            if (sample.SessionElapsedMs < _lastMetadataElapsedMs)
            {
                Drop(TelemetryStreamType.SESSION_METADATA);
                return;
            }
            _lastMetadataElapsedMs = sample.SessionElapsedMs;
            FlushExpired(sample.SessionElapsedMs);
            GetChunk(TelemetryStreamType.SESSION_METADATA, sample.SessionElapsedMs)
                .AddMetadata(sample, _options.MaximumMetadataRecordsPerChunk);
        }

        private void ProcessStory(RaceStoryEventSample sample)
        {
            if (sample.SessionElapsedMs < _lastStoryElapsedMs)
            {
                Drop(TelemetryStreamType.RACE_STORY);
                return;
            }
            _lastStoryElapsedMs = sample.SessionElapsedMs;
            FlushExpired(sample.SessionElapsedMs);
            GetChunk(TelemetryStreamType.RACE_STORY, sample.SessionElapsedMs)
                .AddStory(sample, _options.MaximumStoryEventsPerChunk);
        }

        private void ProcessFrame(TelemetryFrameSample frame)
        {
            if (frame.SessionElapsedMs < _lastFrameElapsedMs)
            {
                DropFrame(frame);
                return;
            }
            _lastFrameElapsedMs = frame.SessionElapsedMs;
            FlushExpired(frame.SessionElapsedMs);

            if (TakeDue(ref _nextReplayDueMs, frame.SessionElapsedMs, _options.ReplayIntervalMs, out int replaySlots))
            {
                GetChunk(TelemetryStreamType.PARTICIPANT_REPLAY, frame.SessionElapsedMs)
                    .AddReplay(frame, replaySlots, _options.MaximumParticipantsPerFrame);
            }

            if (frame.LocalDriver != null && frame.LocalDriver.LocalParticipantResolved &&
                frame.LocalDriver.SourceParticipantRef.HasValue &&
                frame.LocalDriver.SourceParticipantRef.Value == frame.LocalDriver.DriverRef &&
                TakeDue(ref _nextDriverDueMs, frame.SessionElapsedMs, _options.DriverTelemetryIntervalMs, out int driverSlots))
            {
                GetChunk(TelemetryStreamType.DRIVER_TELEMETRY, frame.SessionElapsedMs)
                    .AddDriver(frame, driverSlots);
            }

            if (TakeDue(ref _nextIncidentDueMs, frame.SessionElapsedMs, _options.IncidentIntervalMs, out int incidentSlots))
            {
                ProcessIncidentFrame(frame, incidentSlots);
            }
            else if (frame.IncidentCandidate != null)
            {
                // A discrete candidate must not disappear merely because it arrived between
                // two 20 Hz ring samples. Preserve its exact fact/frame without shifting the gate.
                ProcessIncidentFrame(frame, 1);
            }
        }

        private void ProcessIncidentFrame(TelemetryFrameSample frame, int expectedSlots)
        {
            var incidentFrame = new IncidentFrame
            {
                CapturedAtUtc = frame.CapturedAtUtc,
                SessionElapsedMs = frame.SessionElapsedMs,
                ExpectedSlots = expectedSlots,
                Participants = frame.Participants.Take(_options.MaximumParticipantsPerFrame).ToArray(),
                FlagColourRaw = frame.FlagColourRaw,
                FlagReasonRaw = frame.FlagReasonRaw,
                ParticipantDisappeared = frame.ParticipantDisappeared,
                PositionChangeMagnitude = frame.PositionChangeMagnitude,
                YellowFlagStateRaw = frame.YellowFlagStateRaw,
                ViewedParticipantRef = frame.ViewedParticipantRef,
                CollisionOpponentSlotRaw = frame.CollisionOpponentSlotRaw,
                CollisionOpponentRef = frame.CollisionOpponentRef,
                CollisionMagnitude = frame.CollisionMagnitude,
                CrashStateRaw = frame.CrashStateRaw
            };
            _incidentRing.Enqueue(incidentFrame);
            while (_incidentRing.Count > 0 &&
                (_incidentRing.Peek().SessionElapsedMs < frame.SessionElapsedMs - _options.IncidentRingDurationMs ||
                 _incidentRing.Count > (_options.IncidentRingDurationMs / _options.IncidentIntervalMs) + 2))
            {
                _incidentRing.Dequeue();
            }

            for (int index = _activeIncidents.Count - 1; index >= 0; index--)
            {
                ActiveIncidentBurst burst = _activeIncidents[index];
                if (frame.SessionElapsedMs > burst.EndElapsedMs)
                {
                    _activeIncidents.RemoveAt(index);
                    continue;
                }
                AppendIncidentFrame(burst, incidentFrame);
            }

            if (frame.IncidentCandidate != null) StartIncident(frame, frame.IncidentCandidate);
        }

        private void StartIncident(TelemetryFrameSample frame, IncidentCandidateSample candidate)
        {
            int[] related = SelectIncidentParticipantRefs(candidate);
            if (string.IsNullOrWhiteSpace(candidate.CandidateId) || string.IsNullOrWhiteSpace(candidate.TriggerCode) ||
                related.Length == 0 || _activeIncidents.Count >= _options.MaximumConcurrentIncidentBursts)
            {
                Drop(TelemetryStreamType.INCIDENT_TRACE);
                return;
            }

            var burst = new ActiveIncidentBurst
            {
                Candidate = candidate,
                TriggerElapsedMs = frame.SessionElapsedMs,
                EndElapsedMs = checked(frame.SessionElapsedMs + _options.IncidentPostRollMs),
                RelatedParticipantRefs = new HashSet<int>(related),
                ChunkIndex = ChunkIndex(frame.SessionElapsedMs)
            };
            long preRollStart = Math.Max(0, frame.SessionElapsedMs - _options.IncidentPreRollMs);
            foreach (IncidentFrame prior in _incidentRing.Where(value => value.SessionElapsedMs >= preRollStart))
            {
                AppendIncidentFrame(burst, prior);
            }
            _activeIncidents.Add(burst);

            ProcessStory(new RaceStoryEventSample
            {
                EventId = candidate.CandidateId,
                EventType = "INCIDENT_CANDIDATE",
                FactCode = candidate.TriggerCode,
                CapturedAtUtc = frame.CapturedAtUtc,
                SessionElapsedMs = frame.SessionElapsedMs,
                RaceStateRaw = frame.RaceStateRaw,
                FlagColourRaw = frame.FlagColourRaw,
                FlagReasonRaw = frame.FlagReasonRaw
            });
        }

        private int[] SelectIncidentParticipantRefs(IncidentCandidateSample candidate)
        {
            var selected = candidate.RelatedParticipantRefs
                .Distinct()
                .Take(_options.MaximumIncidentParticipants)
                .ToList();
            int available = Math.Min(
                MaximumNearbyIncidentParticipants,
                _options.MaximumIncidentParticipants - selected.Count);
            if (available <= 0) return selected.ToArray();

            IncidentFrame? context = _incidentRing
                .Reverse()
                .FirstOrDefault(value => value.Participants.Any(participant =>
                    selected.Contains(participant.ParticipantRef) && HasIncidentPosition(participant)));
            if (context == null) return selected.ToArray();
            ReplayParticipantSample[] anchors = context.Participants
                .Where(participant => selected.Contains(participant.ParticipantRef) && HasIncidentPosition(participant))
                .ToArray();
            if (anchors.Length == 0) return selected.ToArray();

            double radiusSquared = IncidentNearbyRadiusMeters * IncidentNearbyRadiusMeters;
            selected.AddRange(context.Participants
                .Where(participant => !selected.Contains(participant.ParticipantRef) && HasIncidentPosition(participant))
                .Select(participant => new
                {
                    participant.ParticipantRef,
                    DistanceSquared = anchors.Min(anchor => IncidentDistanceSquared(anchor, participant))
                })
                .Where(value => value.DistanceSquared <= radiusSquared)
                .OrderBy(value => value.DistanceSquared)
                .ThenBy(value => value.ParticipantRef)
                .Take(available)
                .Select(value => value.ParticipantRef));
            return selected.ToArray();
        }

        private static bool HasIncidentPosition(ReplayParticipantSample participant)
            => participant.WorldX.HasValue && participant.WorldZ.HasValue
                && !double.IsNaN(participant.WorldX.Value) && !double.IsInfinity(participant.WorldX.Value)
                && !double.IsNaN(participant.WorldZ.Value) && !double.IsInfinity(participant.WorldZ.Value);

        private static double IncidentDistanceSquared(
            ReplayParticipantSample first,
            ReplayParticipantSample second)
        {
            double x = first.WorldX!.Value - second.WorldX!.Value;
            double z = first.WorldZ!.Value - second.WorldZ!.Value;
            return x * x + z * z;
        }

        private void AppendIncidentFrame(ActiveIncidentBurst burst, IncidentFrame frame)
        {
            if (frame.SessionElapsedMs <= burst.LastRecordedElapsedMs) return;
            GetChunk(TelemetryStreamType.INCIDENT_TRACE, burst.ChunkIndex)
                .AddIncident(burst.Candidate, burst.TriggerElapsedMs, frame, burst.RelatedParticipantRefs);
            burst.LastRecordedElapsedMs = frame.SessionElapsedMs;
        }

        private void FinalizeActiveIncidents()
        {
            foreach (ActiveIncidentBurst burst in _activeIncidents)
            {
                if (burst.LastRecordedElapsedMs < burst.EndElapsedMs)
                {
                    int missingFrames = (int)((burst.EndElapsedMs - burst.LastRecordedElapsedMs) / _options.IncidentIntervalMs);
                    GetChunk(TelemetryStreamType.INCIDENT_TRACE, burst.ChunkIndex)
                        .AddKnownDroppedSamples(checked(missingFrames * burst.RelatedParticipantRefs.Count));
                }
            }
            _activeIncidents.Clear();
        }

        private void FlushExpired(long currentElapsedMs)
        {
            foreach (var pair in _chunks.ToArray())
            {
                long grace = pair.Key.Stream == TelemetryStreamType.INCIDENT_TRACE ? _options.IncidentPostRollMs : 0;
                long bucketEnd = checked((long)(pair.Key.Index + 1) * _options.ChunkDurationMs);
                if (bucketEnd + grace <= currentElapsedMs) Commit(pair.Key, pair.Value);
            }
        }

        private void FlushAll()
        {
            foreach (var pair in _chunks.OrderBy(value => value.Key.Index).ThenBy(value => value.Key.Stream).ToArray())
            {
                Commit(pair.Key, pair.Value);
            }
        }

        private void Commit((TelemetryStreamType Stream, int Index) key, TelemetryChunkAccumulator chunk)
        {
            if (!chunk.HasData)
            {
                _chunks.Remove(key);
                return;
            }
            int droppedInputs = checked((int)Math.Min(
                int.MaxValue,
                Interlocked.Exchange(ref _droppedInputByStream[(int)key.Stream], 0)));
            chunk.AddDroppedInputMessages(droppedInputs);
            try
            {
                TelemetryChunkEnvelope envelope = chunk.Build();
                TelemetryChunkCommitOutcome outcome = _commit(envelope);
                _chunks.Remove(key);
                if (outcome.Disposition == TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED)
                {
                    Interlocked.Increment(ref _commitConflictByStream[(int)key.Stream]);
                    Interlocked.Increment(ref _commitFailures);
                    return;
                }
                Interlocked.Add(ref _cadenceLossByStream[(int)key.Stream], envelope.Quality.MissingSamples);
                Interlocked.Increment(ref _durableCommitAcksByStream[(int)key.Stream]);
                Interlocked.Increment(ref _committedChunks);
            }
            catch (Exception exception)
            {
                Interlocked.Add(ref _droppedInputByStream[(int)key.Stream], droppedInputs);
                Interlocked.Increment(ref _commitFailures);
                if (exception is JsonException || exception is NotSupportedException)
                {
                    Interlocked.Increment(ref _serializationLossByStream[(int)key.Stream]);
                }
                else if (exception is IOException || exception is UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref _diskLossByStream[(int)key.Stream]);
                }
                else
                {
                    Interlocked.Increment(ref _workerLossByStream[(int)key.Stream]);
                }
                throw;
            }
        }

        private void RecordFinalizeFailure()
        {
            TelemetryStreamType[] streams = _chunks.Keys
                .Select(value => value.Stream)
                .Distinct()
                .ToArray();
            if (streams.Length == 0) streams = new[] { TelemetryStreamType.SESSION_METADATA };
            foreach (TelemetryStreamType stream in streams)
            {
                Interlocked.Increment(ref _finalizeLossByStream[(int)stream]);
            }
        }

        private TelemetryChunkAccumulator GetChunk(TelemetryStreamType stream, long elapsedMs)
            => GetChunk(stream, ChunkIndex(elapsedMs));

        private TelemetryChunkAccumulator GetChunk(TelemetryStreamType stream, int chunkIndex)
        {
            var key = (stream, chunkIndex);
            if (_chunks.TryGetValue(key, out TelemetryChunkAccumulator? value)) return value;
            value = new TelemetryChunkAccumulator(Identity, stream, chunkIndex, RateFor(stream));
            _chunks.Add(key, value);
            return value;
        }

        private int ChunkIndex(long elapsedMs)
        {
            long index = elapsedMs / _options.ChunkDurationMs;
            if (index > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(elapsedMs));
            return (int)index;
        }

        private double RateFor(TelemetryStreamType stream)
        {
            switch (stream)
            {
                case TelemetryStreamType.PARTICIPANT_REPLAY: return _options.ReplayRateHz;
                case TelemetryStreamType.DRIVER_TELEMETRY: return _options.DriverTelemetryRateHz;
                case TelemetryStreamType.INCIDENT_TRACE: return _options.IncidentRateHz;
                default: return 0;
            }
        }

        private static bool TakeDue(ref long? nextDueMs, long elapsedMs, int intervalMs, out int expectedSlots)
        {
            if (!nextDueMs.HasValue) nextDueMs = elapsedMs;
            if (elapsedMs < nextDueMs.Value)
            {
                expectedSlots = 0;
                return false;
            }
            expectedSlots = checked((int)((elapsedMs - nextDueMs.Value) / intervalMs) + 1);
            nextDueMs = checked(nextDueMs.Value + (long)expectedSlots * intervalMs);
            return true;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(LocalDurableTelemetryArchive));
        }

        private bool TooLong(string? value)
            => value != null && value.Length > _options.MaximumTextLength;

        private enum CaptureMessageKind
        {
            METADATA,
            STORY,
            FRAME,
            FLUSH,
            FINALIZE
        }

        private sealed class CaptureMessage
        {
            public CaptureMessageKind Kind { get; private set; }
            public SessionMetadataSample? Metadata { get; private set; }
            public RaceStoryEventSample? Story { get; private set; }
            public TelemetryFrameSample? Frame { get; private set; }
            public TaskCompletionSource<bool>? Completion { get; private set; }

            public static CaptureMessage ForMetadata(SessionMetadataSample value)
                => new CaptureMessage { Kind = CaptureMessageKind.METADATA, Metadata = value };

            public static CaptureMessage ForStory(RaceStoryEventSample value)
                => new CaptureMessage { Kind = CaptureMessageKind.STORY, Story = value };

            public static CaptureMessage ForFrame(TelemetryFrameSample value)
                => new CaptureMessage { Kind = CaptureMessageKind.FRAME, Frame = value };

            public static CaptureMessage ForFlush(TaskCompletionSource<bool> value)
                => new CaptureMessage { Kind = CaptureMessageKind.FLUSH, Completion = value };

            public static CaptureMessage ForFinalize(TaskCompletionSource<bool> value)
                => new CaptureMessage { Kind = CaptureMessageKind.FINALIZE, Completion = value };

            public IEnumerable<TelemetryStreamType> Streams()
            {
                switch (Kind)
                {
                    case CaptureMessageKind.METADATA:
                        yield return TelemetryStreamType.SESSION_METADATA;
                        break;
                    case CaptureMessageKind.STORY:
                        yield return TelemetryStreamType.RACE_STORY;
                        break;
                    case CaptureMessageKind.FRAME:
                        yield return TelemetryStreamType.PARTICIPANT_REPLAY;
                        yield return TelemetryStreamType.INCIDENT_TRACE;
                        if (Frame?.LocalDriver != null && Frame.LocalDriver.LocalParticipantResolved)
                        {
                            yield return TelemetryStreamType.DRIVER_TELEMETRY;
                        }
                        break;
                }
            }
        }

        private sealed class ActiveIncidentBurst
        {
            public IncidentCandidateSample Candidate { get; set; } = null!;
            public long TriggerElapsedMs { get; set; }
            public long EndElapsedMs { get; set; }
            public HashSet<int> RelatedParticipantRefs { get; set; } = new HashSet<int>();
            public int ChunkIndex { get; set; }
            public long LastRecordedElapsedMs { get; set; } = -1;
        }
    }
}
