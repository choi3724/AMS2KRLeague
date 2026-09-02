using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.CompactTelemetry;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public sealed class TelemetryChunkUploadItem
    {
        internal TelemetryChunkUploadItem(
            string metadataPath,
            string chunkPath,
            TelemetryPendingUploadMetadata metadata,
            byte[] compressedPayload)
        {
            MetadataPath = metadataPath;
            ChunkPath = chunkPath;
            Metadata = metadata;
            CompressedPayload = new ReadOnlyMemory<byte>(compressedPayload);
        }

        public string MetadataPath { get; }
        public string ChunkPath { get; }
        public TelemetryPendingUploadMetadata Metadata { get; }
        public ReadOnlyMemory<byte> CompressedPayload { get; }
    }

    public sealed class TelemetryChunkUploadTransportResult
    {
        private TelemetryChunkUploadTransportResult()
        {
        }

        public bool Success { get; private set; }
        public bool Duplicate { get; private set; }
        public bool Retryable { get; private set; }
        public int? HttpStatus { get; private set; }
        public string ResultCode { get; private set; } = string.Empty;

        public static TelemetryChunkUploadTransportResult Stored(int httpStatus, bool duplicate)
            => new TelemetryChunkUploadTransportResult
            {
                Success = true,
                Duplicate = duplicate,
                HttpStatus = httpStatus,
                ResultCode = duplicate ? "DUPLICATE" : "STORED"
            };

        public static TelemetryChunkUploadTransportResult Failure(
            int? httpStatus,
            string resultCode,
            bool retryable)
            => new TelemetryChunkUploadTransportResult
            {
                HttpStatus = httpStatus,
                ResultCode = NormalizeResultCode(resultCode),
                Retryable = retryable
            };

        private static string NormalizeResultCode(string value)
        {
            string normalized = new string((value ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Take(96)
                .Select(character => char.IsLetterOrDigit(character)
                    || character == '_'
                    || character == '-'
                    || character == '.'
                    || character == ':'
                        ? character
                        : '_')
                .ToArray());
            return normalized.Length == 0 ? "UNKNOWN" : normalized;
        }
    }

    public interface ITelemetryChunkUploadTransport
    {
        Task<TelemetryChunkUploadTransportResult> SendTelemetryChunkAsync(
            TelemetryChunkUploadItem item,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Scans every completed session below one telemetry root. It never reads
    /// Shared Memory and only exposes chunks whose gzip and decoded JSON hashes
    /// still match their durable sidecar.
    /// </summary>
    public sealed class TelemetryChunkUploadQueue
    {
        private const int MaximumCompressedBytes = 67_108_864;
        private const int MaximumDecodedBytes = 268_435_456;
        private readonly object _gate = new object();
        private readonly string _root;
        private readonly IPrivateTelemetryUploadAuthority? _privateUploadAuthority;

        public TelemetryChunkUploadQueue(
            string root,
            IPrivateTelemetryUploadAuthority? privateUploadAuthority = null)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Telemetry upload root is required.", nameof(root));
            _root = Path.GetFullPath(root);
            _privateUploadAuthority = privateUploadAuthority;
            Directory.CreateDirectory(_root);
        }

        public string Root => _root;

        public IReadOnlyList<TelemetryChunkUploadItem> GetDueBatch(int maximumItems, DateTimeOffset nowUtc)
        {
            if (maximumItems < 1 || maximumItems > 64) throw new ArgumentOutOfRangeException(nameof(maximumItems));
            lock (_gate)
            {
                var result = new List<TelemetryChunkUploadItem>();
                foreach (string metadataPath in Directory.EnumerateFiles(_root, "*.upload.json", SearchOption.AllDirectories)
                    .OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (result.Count >= maximumItems) break;
                    TelemetryPendingUploadMetadata metadata;
                    try
                    {
                        metadata = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(metadataPath));
                        if (metadata.Status != TelemetryUploadStatus.PENDING
                            && metadata.Status != TelemetryUploadStatus.LOCAL_PENDING_OWNER
                            && metadata.Status != TelemetryUploadStatus.FAILED_RETRYABLE)
                        {
                            continue;
                        }
                        if (metadata.Visibility == TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS
                            && !IsPrivateUploadAuthorized(metadata))
                        {
                            MarkPendingOwner(metadataPath, metadata, nowUtc);
                            continue;
                        }
                        if (metadata.NextAttemptAtUtc.HasValue && metadata.NextAttemptAtUtc.Value > nowUtc)
                        {
                            continue;
                        }
                        string chunkPath = ResolveChunkPath(metadata.RelativeChunkPath);
                        var info = new FileInfo(chunkPath);
                        if (!info.Exists || info.Length < 1 || info.Length > MaximumCompressedBytes)
                        {
                            Quarantine(metadataPath, metadata, nowUtc, "LOCAL_CHUNK_MISSING_OR_SIZE_INVALID");
                            continue;
                        }
                        byte[] compressed = File.ReadAllBytes(chunkPath);
                        string compressedSha = TelemetryChunkSerializer.Sha256(compressed);
                        if (!string.Equals(compressedSha, metadata.CompressedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            Quarantine(metadataPath, metadata, nowUtc, "LOCAL_COMPRESSED_HASH_MISMATCH");
                            continue;
                        }
                        byte[] decoded;
                        using (var stream = new MemoryStream(compressed, false))
                        {
                            decoded = TelemetryChunkSerializer.Gunzip(stream, MaximumDecodedBytes);
                        }
                        string payloadSha = TelemetryChunkSerializer.Sha256(decoded);
                        if (!string.Equals(payloadSha, metadata.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            Quarantine(metadataPath, metadata, nowUtc, "LOCAL_PAYLOAD_HASH_MISMATCH");
                            continue;
                        }
                        if (string.Equals(metadata.ContentType, CompactTelemetryChunkStore.CompactContentType,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            CompactTelemetryEnvelope compact = CompactTelemetryCodec.Decode(decoded);
                            if (!metadata.CompactSchemaId.HasValue
                                || metadata.CompactSchemaId.Value != (ushort)compact.Block.SchemaId
                                || metadata.SessionLocalId != compact.SessionLocalId
                                || metadata.AttemptLocalId != compact.AttemptLocalId
                                || metadata.ChunkIndex < 0
                                || (uint)metadata.ChunkIndex != compact.ChunkSequence)
                            {
                                Quarantine(metadataPath, metadata, nowUtc, "LOCAL_COMPACT_METADATA_MISMATCH");
                                continue;
                            }
                        }
                        else
                        {
                            TelemetryChunkEnvelope envelope = TelemetryChunkSerializer.Deserialize(decoded);
                            if (!string.Equals(envelope.ChunkId, metadata.ChunkId, StringComparison.Ordinal)
                                || envelope.StreamType != metadata.StreamType
                                || envelope.Visibility != metadata.Visibility)
                            {
                                Quarantine(metadataPath, metadata, nowUtc, "LOCAL_ENVELOPE_METADATA_MISMATCH");
                                continue;
                            }
                        }
                        result.Add(new TelemetryChunkUploadItem(metadataPath, chunkPath, metadata, compressed));
                    }
                    catch (Exception exception) when (exception is IOException
                        || exception is UnauthorizedAccessException
                        || exception is InvalidDataException
                        || exception is CompactTelemetryFormatException
                        || exception is System.Text.Json.JsonException)
                    {
                        // A malformed sidecar cannot be trusted enough to mutate.
                        // Local archive recovery keeps the source chunk available
                        // and can rebuild the sidecar on the next session scan.
                    }
                }
                return result;
            }
        }

        public void MarkSent(TelemetryChunkUploadItem item, DateTimeOffset attemptedAtUtc, bool duplicate)
            => Transition(item, TelemetryUploadStatus.SENT, attemptedAtUtc, null, duplicate ? "DUPLICATE" : "STORED", null);

        public void MarkRetryable(
            TelemetryChunkUploadItem item,
            DateTimeOffset attemptedAtUtc,
            string resultCode,
            DateTimeOffset nextAttemptAtUtc)
            => Transition(item, TelemetryUploadStatus.FAILED_RETRYABLE, attemptedAtUtc, null, resultCode, nextAttemptAtUtc);

        public void MarkConflict(TelemetryChunkUploadItem item, DateTimeOffset attemptedAtUtc, string resultCode)
            => Transition(item, TelemetryUploadStatus.CONFLICT, attemptedAtUtc, null, resultCode, null);

        public void MarkQuarantined(TelemetryChunkUploadItem item, DateTimeOffset attemptedAtUtc, string resultCode)
            => Transition(item, TelemetryUploadStatus.QUARANTINED, attemptedAtUtc, null, resultCode, null);

        private void Transition(
            TelemetryChunkUploadItem item,
            TelemetryUploadStatus status,
            DateTimeOffset attemptedAtUtc,
            int? httpStatus,
            string resultCode,
            DateTimeOffset? nextAttemptAtUtc)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            lock (_gate)
            {
                TelemetryPendingUploadMetadata metadata =
                    TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(item.MetadataPath));
                if (!string.Equals(metadata.ChunkId, item.Metadata.ChunkId, StringComparison.Ordinal)
                    || !string.Equals(metadata.PayloadSha256, item.Metadata.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Telemetry upload sidecar changed during delivery.");
                }
                metadata.Status = status;
                metadata.AttemptCount = checked(metadata.AttemptCount + 1);
                metadata.LastAttemptAtUtc = attemptedAtUtc;
                metadata.UpdatedAtUtc = attemptedAtUtc;
                metadata.NextAttemptAtUtc = nextAttemptAtUtc;
                metadata.LastError = status == TelemetryUploadStatus.SENT ? null : resultCode;
                AtomicWrite(item.MetadataPath, TelemetryChunkSerializer.SerializeMetadata(metadata));
            }
        }

        private void Quarantine(
            string metadataPath,
            TelemetryPendingUploadMetadata metadata,
            DateTimeOffset atUtc,
            string reason)
        {
            metadata.Status = TelemetryUploadStatus.QUARANTINED;
            metadata.UpdatedAtUtc = atUtc;
            metadata.LastError = reason;
            metadata.NextAttemptAtUtc = null;
            AtomicWrite(metadataPath, TelemetryChunkSerializer.SerializeMetadata(metadata));
        }

        private bool IsPrivateUploadAuthorized(TelemetryPendingUploadMetadata metadata)
        {
            if (_privateUploadAuthority == null) return false;
            try
            {
                return _privateUploadAuthority.IsUploadAuthorized(metadata);
            }
            catch
            {
                // Authority evaluation fails closed. A verifier failure must
                // never turn an unverified viewed vehicle into an uploadable
                // private driver stream.
                return false;
            }
        }

        private static void MarkPendingOwner(
            string metadataPath,
            TelemetryPendingUploadMetadata metadata,
            DateTimeOffset atUtc)
        {
            if (metadata.Status == TelemetryUploadStatus.LOCAL_PENDING_OWNER
                && string.Equals(metadata.LastError, "PRIVATE_OWNER_AUTHORITY_REQUIRED", StringComparison.Ordinal))
            {
                return;
            }
            metadata.Status = TelemetryUploadStatus.LOCAL_PENDING_OWNER;
            metadata.UpdatedAtUtc = atUtc;
            metadata.LastError = "PRIVATE_OWNER_AUTHORITY_REQUIRED";
            metadata.NextAttemptAtUtc = null;
            AtomicWrite(metadataPath, TelemetryChunkSerializer.SerializeMetadata(metadata));
        }

        private string ResolveChunkPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidDataException("Telemetry chunk path is missing.");
            string path = Path.GetFullPath(Path.Combine(_root, relativePath));
            string prefix = _root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? _root
                : _root + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Telemetry chunk path escaped the archive root.");
            }
            return path;
        }

        private static void AtomicWrite(string targetPath, byte[] bytes)
        {
            string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16_384,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Replace(temporaryPath, targetPath, null, true);
        }
    }

    public sealed class TelemetryChunkUploadWorker
    {
        private readonly TelemetryChunkUploadQueue _queue;
        private readonly ITelemetryChunkUploadTransport _transport;

        public TelemetryChunkUploadWorker(
            TelemetryChunkUploadQueue queue,
            ITelemetryChunkUploadTransport transport)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<TelemetryChunkUploadBatchResult> ProcessDueAsync(CancellationToken cancellationToken)
        {
            var result = new TelemetryChunkUploadBatchResult();
            foreach (TelemetryChunkUploadItem item in _queue.GetDueBatch(4, DateTimeOffset.UtcNow))
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Attempted++;
                TelemetryChunkUploadTransportResult sent =
                    await _transport.SendTelemetryChunkAsync(item, cancellationToken).ConfigureAwait(false);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (sent.Success)
                {
                    _queue.MarkSent(item, now, sent.Duplicate);
                    result.Sent++;
                    continue;
                }
                if (sent.Retryable)
                {
                    int exponent = Math.Min(8, item.Metadata.AttemptCount);
                    TimeSpan delay = TimeSpan.FromSeconds(Math.Min(900, 5 * Math.Pow(2, exponent)));
                    _queue.MarkRetryable(item, now, sent.ResultCode, now.Add(delay));
                    result.Retryable++;
                    continue;
                }
                if (sent.HttpStatus == 409)
                {
                    _queue.MarkConflict(item, now, sent.ResultCode);
                    result.Conflicts++;
                }
                else
                {
                    _queue.MarkQuarantined(item, now, sent.ResultCode);
                    result.Quarantined++;
                }
            }
            return result;
        }
    }

    public sealed class TelemetryChunkUploadBatchResult
    {
        public int Attempted { get; internal set; }
        public int Sent { get; internal set; }
        public int Retryable { get; internal set; }
        public int Conflicts { get; internal set; }
        public int Quarantined { get; internal set; }
    }
}
