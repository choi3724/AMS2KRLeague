using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.CompactTelemetry;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public static class CompactArchiveEvidence
    {
        // This reader never changes an archive or authorizes an upload.
        public static CompactTelemetryEnvelope ReadValidated(string root, TelemetryPendingUploadMetadata m)
        {
            new TelemetryArchiveIdentity { SessionId = m.SessionId, SessionFingerprint = m.SessionFingerprint,
                WitnessId = m.WitnessId, AttemptId = m.AttemptId, AttemptNumber = m.AttemptNumber }.ValidatedCopy();
            string sessionKey = TelemetryChunkSerializer.StableId(m.SessionFingerprint, m.WitnessId, m.AttemptId).Substring(0, 32);
            string sessionRoot = Path.Combine(Path.GetFullPath(root), "sessions", sessionKey, "chunks", "compact") + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, m.RelativeChunkPath));
            if (!path.StartsWith(sessionRoot, StringComparison.OrdinalIgnoreCase)
                || m.ChunkIndex < 0 || !m.CompactSchemaId.HasValue
                || Path.GetFileName(path) != m.ChunkIndex.ToString("D8", CultureInfo.InvariantCulture) + "-" + m.CompactSchemaId.Value.ToString("X4", CultureInfo.InvariantCulture) + ".a2ct.gz"
                || m.ContentType != CompactTelemetryChunkStore.CompactContentType || m.ContentEncoding != "gzip"
                || m.Endpoint != "v1/telemetry/chunks" || m.Protocol != "AMS2_COMPACT_TELEMETRY_V1")
                throw new InvalidDataException("COMPACT_PROVENANCE_INVALID");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 1 || info.Length > 67_108_864 || info.Length != m.CompressedBytes)
                throw new InvalidDataException("COMPACT_FILE_SIZE_INVALID");
            byte[] compressed = File.ReadAllBytes(path);
            if (TelemetryChunkSerializer.Sha256(compressed) != m.CompressedSha256)
                throw new InvalidDataException("COMPACT_COMPRESSED_HASH_MISMATCH");
            byte[] payload;
            using (var stream = new MemoryStream(compressed, false)) payload = TelemetryChunkSerializer.Gunzip(stream);
            if (payload.LongLength != m.UncompressedBytes || TelemetryChunkSerializer.Sha256(payload) != m.PayloadSha256)
                throw new InvalidDataException("COMPACT_PAYLOAD_HASH_MISMATCH");
            var block = CompactTelemetryCodec.Decode(payload);
            string id = "a2ct-" + TelemetryChunkSerializer.StableId(m.SessionFingerprint, m.WitnessId, m.AttemptId,
                m.ChunkIndex.ToString(CultureInfo.InvariantCulture), m.CompactSchemaId.Value.ToString(CultureInfo.InvariantCulture)).Substring(0, 48);
            bool driver = block.Block.SchemaId == CompactTelemetrySchemaId.DriverFastV1 || block.Block.SchemaId == CompactTelemetrySchemaId.DriverFastV2
                || block.Block.SchemaId == CompactTelemetrySchemaId.DriverMotionV1 || block.Block.SchemaId == CompactTelemetrySchemaId.DriverSlowV1
                || block.Block.SchemaId == CompactTelemetrySchemaId.DriverChangeV1;
            if (id != m.ChunkId || block.ChunkSequence != m.ChunkIndex || (ushort)block.Block.SchemaId != m.CompactSchemaId
                || block.SessionLocalId != LocalId(m.SessionFingerprint) || block.AttemptLocalId != LocalId(m.AttemptId)
                || block.SessionLocalId != m.SessionLocalId || block.AttemptLocalId != m.AttemptLocalId
                || m.Visibility != (driver ? TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS : TelemetryVisibility.PUBLIC_REPLAY)
                || m.StartElapsedMs != block.Block.Samples.First().ElapsedMs || m.EndElapsedMs != block.Block.Samples.Last().ElapsedMs
                || !m.FirstCapturedAtUtc.HasValue || !m.LastCapturedAtUtc.HasValue || m.LastCapturedAtUtc < m.FirstCapturedAtUtc)
                throw new InvalidDataException("COMPACT_IDENTITY_OR_PRIVACY_MISMATCH");
            return block;
        }

        // Only a write-ahead record from the new writer can be applied. Older
        // orphan files have insufficient global identity and are reported only.
        public static TelemetryArchiveRecoveryReport Recover(string root, bool apply = false)
        {
            var report = new TelemetryArchiveRecoveryReport();
            string sessions = Path.Combine(root, "sessions");
            if (!Directory.Exists(sessions)) return report;
            foreach (string chunk in Directory.EnumerateFiles(sessions, "*.a2ct.gz", SearchOption.AllDirectories))
            {
                if (!chunk.Contains(Path.DirectorySeparatorChar + "compact" + Path.DirectorySeparatorChar)) continue;
                string metadataPath = chunk.Substring(0, chunk.Length - ".a2ct.gz".Length) + ".upload.json";
                if (File.Exists(metadataPath)) continue; // Never reset an existing delivery state.
                string journal = metadataPath + ".commit";
                try
                {
                    if (!File.Exists(journal)) throw new InvalidDataException("COMPACT_RECOVERY_PROVENANCE_MISSING");
                    if (new FileInfo(journal).Length > 1_048_576) throw new InvalidDataException("COMPACT_RECOVERY_MANIFEST_OVERSIZE");
                    byte[] bytes = File.ReadAllBytes(journal);
                    var metadata = TelemetryChunkSerializer.DeserializeMetadata(bytes);
                    if (Path.GetFullPath(Path.Combine(root, metadata.RelativeChunkPath)) != Path.GetFullPath(chunk)
                        || metadata.AttemptCount != 0 || metadata.NextAttemptAtUtc.HasValue
                        || (metadata.Status != TelemetryUploadStatus.PENDING && metadata.Status != TelemetryUploadStatus.LOCAL_PENDING_OWNER))
                        throw new InvalidDataException("COMPACT_RECOVERY_MANIFEST_INVALID");
                    ReadValidated(root, metadata);
                    report.ValidChunks++;
                    if (apply) WriteOnce(metadataPath, bytes);
                    report.RebuiltPendingMetadata++;
                }
                catch (Exception e) when (IsEvidenceFailure(e))
                { report.Issues.Add(new TelemetryArchiveRecoveryIssue { Path = chunk, Code = "COMPACT_RECOVERY_BLOCKED", Detail = e.GetType().Name }); }
            }
            return report;
        }

        public static bool HasDurableFinalize(string root, string sessionId, string fingerprint, string witnessId, string attemptId)
        {
            string key = TelemetryChunkSerializer.StableId(fingerprint, witnessId, attemptId).Substring(0, 32);
            string directory = Path.Combine(root, "sessions", key, "chunks", "compact", "integrity");
            if (!Directory.Exists(directory)) return false;
            try
            {
                var finalPaths = Directory.GetFiles(directory, "*-0051.a2ct.gz");
                if (finalPaths.Length != 1) return false;
                TelemetryPendingUploadMetadata Metadata(string chunk)
                {
                    string path = chunk.Substring(0, chunk.Length - ".a2ct.gz".Length) + ".upload.json";
                    // Delivery/auth errors are not local loss, but an explicit
                    // integrity conflict must never be hidden by the journal.
                    if (File.Exists(path))
                    {
                        var delivery = ReadMetadata(path);
                        if (delivery.Status == TelemetryUploadStatus.CONFLICT)
                            throw new InvalidDataException("COMPACT_FINALIZE_DELIVERY_CONFLICT");
                    }
                    // Prefer immutable local evidence over mutable delivery fields.
                    if (File.Exists(path + ".commit")) path += ".commit";
                    var m = ReadMetadata(path);
                    if (m.SessionId != sessionId || m.SessionFingerprint != fingerprint || m.WitnessId != witnessId || m.AttemptId != attemptId
                        || m.Status == TelemetryUploadStatus.CONFLICT)
                        throw new InvalidDataException("COMPACT_FINALIZE_IDENTITY_MISMATCH");
                    return m;
                }
                var finalMetadata = Metadata(finalPaths[0]);
                var final = ReadValidated(root, finalMetadata);
                if (final.Block.SchemaId != CompactTelemetrySchemaId.AttemptFinalizeV1 || final.Block.Samples.Count != 1 || final.ChunkSequence % 32 != 31) return false;
                string lossPath = Path.Combine(directory, (final.ChunkSequence - 1).ToString("D8", CultureInfo.InvariantCulture) + "-0050.a2ct.gz");
                var lossMetadata = Metadata(lossPath);
                var loss = ReadValidated(root, lossMetadata);
                if (loss.Block.SchemaId != CompactTelemetrySchemaId.LossLedgerV1 || lossMetadata.AttemptNumber != finalMetadata.AttemptNumber
                    || loss.Block.Samples.Any(s => s.ElapsedMs != final.Block.Samples[0].ElapsedMs)) return false;
                var values = final.Block.Samples[0].Values;
                if (values.Any(v => !v.HasValue) || values[0] < 1 || values[0] != values[1]) return false;
                if (loss.Block.Samples.Any(s => !s.Values[1].HasValue)) return false;
                double knownLoss = loss.Block.Samples.Sum(s => s.Values[1]!.Value);
                if (knownLoss < 0 || knownLoss != values[2]) return false;
                // Completion of durable processing is separate from observational
                // completeness. Preserve known losses; never upgrade PARTIAL.
                return values[3] == (byte)CompactTelemetryCompletenessCode.Complete ? knownLoss == 0
                    : values[3] == (byte)CompactTelemetryCompletenessCode.Partial && knownLoss > 0;
            }
            catch (Exception e) when (IsEvidenceFailure(e)) { return false; }
        }

        private static TelemetryPendingUploadMetadata ReadMetadata(string path)
        {
            // Queue transitions atomically replace this file. Share deletion
            // and writing so a concurrent replacement does not look like lost proof.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 1_048_576) throw new InvalidDataException("COMPACT_METADATA_OVERSIZE");
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return TelemetryChunkSerializer.DeserializeMetadata(bytes);
        }

        private static uint LocalId(string value)
        {
            uint id = uint.Parse(TelemetryChunkSerializer.StableId(value).Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return id == 0 ? 1 : id;
        }
        private static bool IsEvidenceFailure(Exception e) => e is IOException || e is InvalidDataException || e is UnauthorizedAccessException
            || e is JsonException || e is CompactTelemetryFormatException || e is ArgumentException
            || e is InvalidOperationException || e is OverflowException;
        internal static void WriteOnce(string path, byte[] bytes)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path);
        }
    }
}
