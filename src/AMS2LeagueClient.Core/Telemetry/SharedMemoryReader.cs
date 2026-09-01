using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace AMS2LeagueClient.Core.Telemetry
{
    public sealed class SharedMemoryReader : IDisposable
    {
        private const int MaxSequenceAttempts = 3;
        private readonly SharedMemoryParser _parser = new SharedMemoryParser();
        private readonly byte[] _buffer = new byte[SharedMemoryLayout.RequiredBytes];
        private MemoryMappedFile? _mapping;
        private MemoryMappedViewAccessor? _view;
        private DateTimeOffset _nextAttachAttempt = DateTimeOffset.MinValue;
        private bool _disposed;

        public long SuccessfulSnapshots { get; private set; }
        public long SequenceRetries { get; private set; }
        public long SequenceDrops { get; private set; }

        public TelemetryReadResult TryRead()
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

            for (int attempt = 0; attempt < MaxSequenceAttempts; attempt++)
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
                    uint copied = SharedMemoryLayout.ReadUInt32(_buffer, SharedMemoryLayout.SequenceNumber);

                    if (bytesRead == _buffer.Length && SnapshotValidator.IsConsistent(before, copied, after))
                    {
                        TelemetryReadResult parsed = _parser.Parse(_buffer, DateTimeOffset.UtcNow, attempt);
                        if (parsed.Status == TelemetryReadStatus.Success)
                        {
                            SuccessfulSnapshots++;
                        }

                        return parsed;
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

            SequenceDrops++;
            return TelemetryReadResult.Failure(
                TelemetryReadStatus.InconsistentSnapshot,
                "Sequence changed or remained odd across three bounded attempts.",
                MaxSequenceAttempts);
        }

        public void Reset()
        {
            _view?.Dispose();
            _mapping?.Dispose();
            _view = null;
            _mapping = null;
            _nextAttachAttempt = DateTimeOffset.MinValue;
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
                _mapping = MemoryMappedFile.OpenExisting(SharedMemoryLayout.MappingName, MemoryMappedFileRights.Read);
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Reset();
        }
    }
}
