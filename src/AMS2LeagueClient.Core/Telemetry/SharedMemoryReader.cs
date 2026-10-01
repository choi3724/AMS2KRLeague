using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace AMS2LeagueClient.Core.Telemetry
{
    public sealed class SharedMemoryReader : IDisposable
    {
        private const int MaxSequenceAttempts = 3;
        private readonly string _mappingName;
        public SharedMemoryReader(string mappingName = SharedMemoryLayout.MappingName) => _mappingName = mappingName;
        private readonly SharedMemoryParser _parser = new SharedMemoryParser();
        private readonly byte[] _buffer = new byte[SharedMemoryLayout.RequiredBytes];
        private readonly byte[] _drivingBuffer = new byte[DrivingBlockBytes];
        private readonly object _viewGate = new object();
        private MemoryMappedFile? _mapping;
        private MemoryMappedViewAccessor? _view;
        private DateTimeOffset _nextAttachAttempt = DateTimeOffset.MinValue;
        private bool _disposed;

        public long SuccessfulSnapshots { get; private set; }
        public long SequenceRetries { get; private set; }
        public long SequenceDrops { get; private set; }

        public TelemetryReadResult TryRead()
        {
            int attempt;
            bool copied = false;
            // The view lock covers attach and the raw copy only. Parsing runs outside it so the
            // display read never waits for participant parsing. TryRead has a single caller.
            lock (_viewGate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(SharedMemoryReader));
                }

                TelemetryReadResult? attachFailure = EnsureAttached();
                if (attachFailure != null)
                {
                    return attachFailure;
                }

                for (attempt = 0; attempt < MaxSequenceAttempts; attempt++)
                {
                    try
                    {
                        uint before = _view!.ReadUInt32(SharedMemoryLayout.SequenceNumber);
                        if ((before & 1U) != 0U)
                        {
                            SequenceRetries++;
                            Thread.SpinWait(32);
                            continue;
                        }

                        int bytesRead = _view.ReadArray(0, _buffer, 0, _buffer.Length);
                        uint after = _view.ReadUInt32(SharedMemoryLayout.SequenceNumber);
                        uint copiedSequence = SharedMemoryLayout.ReadUInt32(_buffer, SharedMemoryLayout.SequenceNumber);

                        if (bytesRead == _buffer.Length && SnapshotValidator.IsConsistent(before, copiedSequence, after))
                        {
                            copied = true;
                            break;
                        }

                        SequenceRetries++;
                        Thread.SpinWait(32);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        Reset();
                        return TelemetryReadResult.Failure(TelemetryReadStatus.Error, exception.GetType().Name + ": " + exception.Message);
                    }
                }
            }

            if (!copied)
            {
                SequenceDrops++;
                return TelemetryReadResult.Failure(
                    TelemetryReadStatus.InconsistentSnapshot,
                    "Sequence changed or remained odd across three bounded attempts.",
                    MaxSequenceAttempts);
            }

            TelemetryReadResult parsed = _parser.Parse(_buffer, DateTimeOffset.UtcNow, attempt);
            if (parsed.Status == TelemetryReadStatus.Success)
            {
                SuccessfulSnapshots++;
            }

            return parsed;
        }

        // A display-only read. It never parses participants/archives or changes recorder counters.
        // One contiguous copy keeps the read window short; a copy that overlaps a game write is
        // retried within the same frame instead of dropping the frame.
        public AMS2LeagueClient.Core.Presentation.DrivingTelemetrySample? TryReadDriving(
            int localIndex, int generation, uint gameState, uint sessionState)
        {
            if (localIndex < 0 || localIndex >= SharedMemoryLayout.MaxParticipants) return null;
            lock (_viewGate)
            {
                if (_disposed || _view == null) return null;
                for (int attempt = 0; attempt < MaxSequenceAttempts; attempt++)
                {
                    try
                    {
                        uint before = _view.ReadUInt32(SharedMemoryLayout.SequenceNumber);
                        if ((before & 1U) != 0U)
                        {
                            Thread.SpinWait(32);
                            continue;
                        }

                        int bytesRead = _view.ReadArray(0, _drivingBuffer, 0, _drivingBuffer.Length);
                        uint after = _view.ReadUInt32(SharedMemoryLayout.SequenceNumber);
                        uint copiedSequence = SharedMemoryLayout.ReadUInt32(_drivingBuffer, SharedMemoryLayout.SequenceNumber);
                        if (bytesRead == _drivingBuffer.Length && SnapshotValidator.IsConsistent(before, copiedSequence, after))
                        {
                            return ParseDrivingBlock(_drivingBuffer, localIndex, generation, gameState, sessionState, DateTimeOffset.UtcNow);
                        }

                        Thread.SpinWait(32);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        return null; // The independent recording loop owns attach/error recovery.
                    }
                }
            }

            return null;
        }

        // Bytes from the start of the mapping through the last field the display read uses.
        public const int DrivingBlockBytes = SharedMemoryLayout.HandBrake + sizeof(float);

        public static AMS2LeagueClient.Core.Presentation.DrivingTelemetrySample? ParseDrivingBlock(
            byte[] block, int localIndex, int generation, uint gameState, uint sessionState, DateTimeOffset capturedAt)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            if (block.Length < DrivingBlockBytes || localIndex < 0 || localIndex >= SharedMemoryLayout.MaxParticipants) return null;
            if (SharedMemoryLayout.ReadUInt32(block, SharedMemoryLayout.Version) != SharedMemoryLayout.SupportedVersion
                || SharedMemoryLayout.ReadUInt32(block, SharedMemoryLayout.GameState) != gameState
                || SharedMemoryLayout.ReadUInt32(block, SharedMemoryLayout.SessionState) != sessionState
                || SharedMemoryLayout.ReadInt32(block, SharedMemoryLayout.ViewedParticipantIndex) != localIndex) return null;
            int count = SharedMemoryLayout.ReadInt32(block, SharedMemoryLayout.NumParticipants);
            if (count <= localIndex || count > SharedMemoryLayout.MaxParticipants
                || block[SharedMemoryLayout.ParticipantOffset(localIndex) + SharedMemoryLayout.ParticipantIsActive] == 0) return null;
            float brake = SharedMemoryLayout.ReadSingle(block, SharedMemoryLayout.Brake), throttle = SharedMemoryLayout.ReadSingle(block, SharedMemoryLayout.Throttle);
            float clutch = SharedMemoryLayout.ReadSingle(block, SharedMemoryLayout.Clutch), handBrake = SharedMemoryLayout.ReadSingle(block, SharedMemoryLayout.HandBrake);
            float speed = SharedMemoryLayout.ReadSingle(block, SharedMemoryLayout.Speed), steering = SharedMemoryLayout.ReadSingle(block, SharedMemoryLayout.UnfilteredSteering);
            float rpm = SharedMemoryLayout.ReadSingle(block, SharedMemoryLayout.Rpm), maxRpm = SharedMemoryLayout.ReadSingle(block, SharedMemoryLayout.MaxRpm);
            int gear = SharedMemoryLayout.ReadInt32(block, SharedMemoryLayout.Gear);
            bool abs = block[SharedMemoryLayout.AntiLockActive] != 0;
            return new AMS2LeagueClient.Core.Presentation.DrivingTelemetrySample(capturedAt, generation, localIndex,
                brake, throttle, clutch, handBrake, speed, gear, abs, steering, rpm, maxRpm);
        }

        public void Reset()
        {
            lock (_viewGate)
            {
                _view?.Dispose();
                _mapping?.Dispose();
                _view = null;
                _mapping = null;
                _nextAttachAttempt = DateTimeOffset.MinValue;
            }
        }

        private TelemetryReadResult? EnsureAttached()
        {
            if (_view != null)
            {
                return null;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now < _nextAttachAttempt)
            {
                return TelemetryReadResult.Failure(
                    TelemetryReadStatus.MappingUnavailable,
                    "AMS2 Shared Memory mapping is not available.");
            }

            _nextAttachAttempt = now.AddSeconds(1);
            try
            {
                _mapping = MemoryMappedFile.OpenExisting(_mappingName, MemoryMappedFileRights.Read);
                _view = _mapping.CreateViewAccessor(0, SharedMemoryLayout.RequiredBytes, MemoryMappedFileAccess.Read);
                return null;
            }
            catch (FileNotFoundException)
            {
                ResetAfterFailedAttach(now);
                return TelemetryReadResult.Failure(
                    TelemetryReadStatus.MappingUnavailable,
                    "AMS2 Shared Memory is not available. Enable Project CARS 2 Shared Memory in AMS2 Options > System.");
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException || exception is IOException || exception is ArgumentException)
            {
                ResetAfterFailedAttach(now);
                return TelemetryReadResult.Failure(TelemetryReadStatus.Error, exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void ResetAfterFailedAttach(DateTimeOffset now)
        {
            _view?.Dispose();
            _mapping?.Dispose();
            _view = null;
            _mapping = null;
            _nextAttachAttempt = now.AddSeconds(1);
        }

        public void Dispose()
        {
            lock (_viewGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Reset();
            }
        }
    }
}
