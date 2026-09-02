using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public sealed class TelemetryChunkStore
    {
        private const int MaximumCompressedChunkBytes = 67_108_864;
        private const int MaximumUncompressedChunkBytes = 268_435_456;
        private readonly string _root;
        private readonly string _sessionRoot;

        public TelemetryChunkStore(string root, TelemetryArchiveIdentity identity)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Archive root is required.", nameof(root));
            Identity = (identity ?? throw new ArgumentNullException(nameof(identity))).ValidatedCopy();
            _root = Path.GetFullPath(root);
            string sessionKey = TelemetryChunkSerializer.StableId(
                Identity.SessionFingerprint,
                Identity.WitnessId,
                Identity.AttemptId).Substring(0, 32);
            _sessionRoot = Path.Combine(_root, "sessions", sessionKey);
            Directory.CreateDirectory(_sessionRoot);
        }

        public TelemetryArchiveIdentity Identity { get; }
        public string SessionDirectory => _sessionRoot;

        public TelemetryChunkCommitOutcome Commit(TelemetryChunkEnvelope envelope)
        {
            ValidateEnvelope(envelope);
            byte[] payload = TelemetryChunkSerializer.Serialize(envelope);
            byte[] compressed = TelemetryChunkSerializer.Gzip(payload);
            if (compressed.Length > MaximumCompressedChunkBytes)
            {
                throw new InvalidDataException("Compressed telemetry chunk exceeds the local safety limit.");
            }

            string payloadSha = TelemetryChunkSerializer.Sha256(payload);
            string compressedSha = TelemetryChunkSerializer.Sha256(compressed);
            string streamDirectory = Path.Combine(_sessionRoot, "chunks", envelope.StreamType.ToString().ToLowerInvariant());
            Directory.CreateDirectory(streamDirectory);
            string chunkPath = Path.Combine(streamDirectory, envelope.ChunkIndex.ToString("D8") + ".json.gz");
            string metadataPath = MetadataPathFor(chunkPath);

            if (File.Exists(chunkPath))
            {
                byte[] existingPayload = ReadPayload(chunkPath);
                string existingPayloadSha = TelemetryChunkSerializer.Sha256(existingPayload);
                if (string.Equals(existingPayloadSha, payloadSha, StringComparison.Ordinal))
                {
                    if (!File.Exists(metadataPath))
                    {
                        AtomicWrite(metadataPath, TelemetryChunkSerializer.SerializeMetadata(
                            CreateMetadata(envelope, chunkPath, payloadSha, compressedSha, payload.Length, compressed.Length)));
                    }
                    return Outcome(TelemetryChunkCommitDisposition.DUPLICATE, chunkPath, metadataPath, payloadSha, compressedSha, payload.Length, compressed.Length);
                }

                string conflictDirectory = Path.Combine(_sessionRoot, "conflicts");
                Directory.CreateDirectory(conflictDirectory);
                string conflictPath = Path.Combine(
                    conflictDirectory,
                    envelope.StreamType.ToString().ToLowerInvariant() + "-" + envelope.ChunkIndex.ToString("D8") + "-" +
                    compressedSha.Substring(0, 16) + ".json.gz");
                AtomicWrite(conflictPath, compressed);
                return Outcome(TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED, conflictPath, string.Empty, payloadSha, compressedSha, payload.Length, compressed.Length);
            }

            AtomicWrite(chunkPath, compressed);
            TelemetryPendingUploadMetadata metadata =
                CreateMetadata(envelope, chunkPath, payloadSha, compressedSha, payload.Length, compressed.Length);
            AtomicWrite(metadataPath, TelemetryChunkSerializer.SerializeMetadata(metadata));
            return Outcome(TelemetryChunkCommitDisposition.STORED, chunkPath, metadataPath, payloadSha, compressedSha, payload.Length, compressed.Length);
        }

        public TelemetryArchiveRecoveryReport Recover()
        {
            var report = new TelemetryArchiveRecoveryReport();
            PreserveTemporaryFiles(report);
            string chunksRoot = Path.Combine(_sessionRoot, "chunks");
            if (!Directory.Exists(chunksRoot)) return report;

            foreach (string chunkPath in Directory.EnumerateFiles(chunksRoot, "*.json.gz", SearchOption.AllDirectories))
            {
                try
                {
                    byte[] payload = ReadPayload(chunkPath);
                    TelemetryChunkEnvelope envelope = TelemetryChunkSerializer.Deserialize(payload);
                    ValidateEnvelope(envelope);
                    report.ValidChunks++;
                    string compressedSha = HashFile(chunkPath);
                    string payloadSha = TelemetryChunkSerializer.Sha256(payload);
                    string metadataPath = MetadataPathFor(chunkPath);
                    bool rebuild = !File.Exists(metadataPath);
                    if (!rebuild)
                    {
                        try
                        {
                            TelemetryPendingUploadMetadata metadata =
                                TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(metadataPath));
                            rebuild = !string.Equals(metadata.PayloadSha256, payloadSha, StringComparison.Ordinal) ||
                                !string.Equals(metadata.CompressedSha256, compressedSha, StringComparison.Ordinal) ||
                                !string.Equals(metadata.ChunkId, envelope.ChunkId, StringComparison.Ordinal);
                            if (rebuild) PreserveForRecovery(metadataPath, "metadata-mismatch");
                        }
                        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                            exception is InvalidDataException || exception is System.Text.Json.JsonException)
                        {
                            PreserveForRecovery(metadataPath, "metadata-invalid");
                            rebuild = true;
                        }
                    }

                    if (rebuild)
                    {
                        var metadata = CreateMetadata(
                            envelope,
                            chunkPath,
                            payloadSha,
                            compressedSha,
                            payload.Length,
                            new FileInfo(chunkPath).Length);
                        AtomicWrite(metadataPath, TelemetryChunkSerializer.SerializeMetadata(metadata));
                        report.RebuiltPendingMetadata++;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                    exception is InvalidDataException || exception is System.Text.Json.JsonException)
                {
                    report.Issues.Add(new TelemetryArchiveRecoveryIssue
                    {
                        Path = chunkPath,
                        Code = "CHUNK_INVALID",
                        Detail = exception.Message
                    });
                }
            }
            return report;
        }

        public IReadOnlyList<TelemetryPendingUploadMetadata> ScanPending()
        {
            string chunksRoot = Path.Combine(_sessionRoot, "chunks");
            if (!Directory.Exists(chunksRoot)) return Array.Empty<TelemetryPendingUploadMetadata>();
            var result = new List<TelemetryPendingUploadMetadata>();
            foreach (string path in Directory.EnumerateFiles(chunksRoot, "*.upload.json", SearchOption.AllDirectories))
            {
                try
                {
                    TelemetryPendingUploadMetadata metadata =
                        TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(path));
                    if (metadata.Status == TelemetryUploadStatus.PENDING ||
                        metadata.Status == TelemetryUploadStatus.LOCAL_PENDING_OWNER ||
                        metadata.Status == TelemetryUploadStatus.FAILED_RETRYABLE)
                    {
                        string chunkPath = Path.GetFullPath(Path.Combine(_root, metadata.RelativeChunkPath));
                        if (IsUnderRoot(chunkPath) && File.Exists(chunkPath)) result.Add(metadata);
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                    exception is InvalidDataException || exception is System.Text.Json.JsonException)
                {
                    // Recovery reports malformed metadata. A scan never mutates it or blocks healthy items.
                }
            }
            return result.OrderBy(value => value.StreamType).ThenBy(value => value.ChunkIndex).ToArray();
        }

        public TelemetryChunkEnvelope ReadChunk(string path)
            => TelemetryChunkSerializer.Deserialize(ReadPayload(path));

        private void ValidateEnvelope(TelemetryChunkEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (!string.Equals(envelope.Schema, "ams2-telemetry-chunk-v1", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unknown telemetry chunk schema.");
            }
            if (!string.Equals(envelope.SessionId, Identity.SessionId, StringComparison.Ordinal) ||
                !string.Equals(envelope.SessionFingerprint, Identity.SessionFingerprint, StringComparison.Ordinal) ||
                !string.Equals(envelope.WitnessId, Identity.WitnessId, StringComparison.Ordinal) ||
                !string.Equals(envelope.AttemptId, Identity.AttemptId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Telemetry chunk join keys do not match the archive session.");
            }
            if (envelope.ChunkIndex < 0 || envelope.StartElapsedMs < 0 || envelope.EndElapsedMs < envelope.StartElapsedMs)
            {
                throw new InvalidDataException("Telemetry chunk range is invalid.");
            }
            if (string.IsNullOrWhiteSpace(envelope.ChunkId)) throw new InvalidDataException("Telemetry chunk ID is missing.");
        }

        private TelemetryPendingUploadMetadata CreateMetadata(
            TelemetryChunkEnvelope envelope,
            string chunkPath,
            string payloadSha,
            string compressedSha,
            long payloadBytes,
            long compressedBytes)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new TelemetryPendingUploadMetadata
            {
                ChunkId = envelope.ChunkId,
                StreamType = envelope.StreamType,
                Visibility = envelope.Visibility,
                SessionId = envelope.SessionId,
                SessionFingerprint = envelope.SessionFingerprint,
                WitnessId = envelope.WitnessId,
                AttemptId = envelope.AttemptId,
                AttemptNumber = envelope.AttemptNumber,
                ChunkIndex = envelope.ChunkIndex,
                StartElapsedMs = envelope.StartElapsedMs,
                EndElapsedMs = envelope.EndElapsedMs,
                StartLap = envelope.StartLap,
                EndLap = envelope.EndLap,
                FirstCapturedAtUtc = envelope.FirstCapturedAtUtc,
                LastCapturedAtUtc = envelope.LastCapturedAtUtc,
                RelativeChunkPath = Path.GetRelativePath(_root, chunkPath),
                PayloadSha256 = payloadSha,
                CompressedSha256 = compressedSha,
                UncompressedBytes = payloadBytes,
                CompressedBytes = compressedBytes,
                Quality = envelope.Quality,
                Status = TelemetryUploadStatus.PENDING,
                AttemptCount = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }

        private void PreserveTemporaryFiles(TelemetryArchiveRecoveryReport report)
        {
            foreach (string temporaryPath in Directory.EnumerateFiles(_sessionRoot, "*.tmp-*", SearchOption.AllDirectories).ToArray())
            {
                try
                {
                    PreserveForRecovery(temporaryPath, "interrupted-write");
                    report.PreservedTemporaryFiles++;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    report.Issues.Add(new TelemetryArchiveRecoveryIssue
                    {
                        Path = temporaryPath,
                        Code = "TEMP_PRESERVE_FAILED",
                        Detail = exception.Message
                    });
                }
            }
        }

        private void PreserveForRecovery(string sourcePath, string reason)
        {
            string directory = Path.Combine(_sessionRoot, "recovery");
            Directory.CreateDirectory(directory);
            string fileName = Path.GetFileName(sourcePath) + "." + reason + "." + Guid.NewGuid().ToString("N");
            File.Move(sourcePath, Path.Combine(directory, fileName));
        }

        private byte[] ReadPayload(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumCompressedChunkBytes)
            {
                throw new InvalidDataException("Compressed telemetry chunk exceeds the local safety limit.");
            }
            using FileStream stream = File.OpenRead(path);
            return TelemetryChunkSerializer.Gunzip(stream, MaximumUncompressedChunkBytes);
        }

        private static string MetadataPathFor(string chunkPath)
            => chunkPath.Substring(0, chunkPath.Length - ".json.gz".Length) + ".upload.json";

        private static void AtomicWrite(string targetPath, byte[] bytes)
        {
            string? directory = Path.GetDirectoryName(targetPath);
            if (directory == null) throw new InvalidDataException("Telemetry archive target directory is missing.");
            Directory.CreateDirectory(directory);
            string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                65_536,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(temporaryPath, targetPath);
        }

        private static string HashFile(string path)
            => TelemetryChunkSerializer.Sha256(File.ReadAllBytes(path));

        private bool IsUnderRoot(string path)
        {
            string normalizedRoot = _root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? _root
                : _root + Path.DirectorySeparatorChar;
            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static TelemetryChunkCommitOutcome Outcome(
            TelemetryChunkCommitDisposition disposition,
            string chunkPath,
            string metadataPath,
            string payloadSha,
            string compressedSha,
            long payloadBytes,
            long compressedBytes)
            => new TelemetryChunkCommitOutcome
            {
                Disposition = disposition,
                ChunkPath = chunkPath,
                MetadataPath = metadataPath,
                PayloadSha256 = payloadSha,
                CompressedSha256 = compressedSha,
                UncompressedBytes = payloadBytes,
                CompressedBytes = compressedBytes
            };
    }
}
