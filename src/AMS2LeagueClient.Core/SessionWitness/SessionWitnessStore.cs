using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS2LeagueClient.Core.SessionWitness
{
    public sealed class SessionWitnessStore
    {
        private readonly string _root;
        private readonly JsonSerializerOptions _json;

        public SessionWitnessStore(string root)
        {
            _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
            _json = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            _json.Converters.Add(new JsonStringEnumConverter());
        }

        public SessionWitnessStoreOutcome Commit(SessionWitnessRecord witness, byte[] uploadPayload)
        {
            if (witness == null) throw new ArgumentNullException(nameof(witness));
            if (!SafeId(witness.WitnessId)) throw new InvalidDataException("Witness ID is missing or unsafe.");
            if (uploadPayload == null || uploadPayload.Length == 0) throw new InvalidDataException("Witness upload payload is empty.");

            string payloadHash = SessionWitnessUploadPayloadBuilder.Sha256Hex(uploadPayload);
            string sessionsRoot = Path.Combine(_root, "sessions");
            Directory.CreateDirectory(sessionsRoot);
            string storageId = StorageId(witness);
            string target = Path.Combine(sessionsRoot, storageId);
            if (Directory.Exists(target))
            {
                string manifestPath = Path.Combine(target, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                    string existing = manifest.RootElement.GetProperty("payloadSha256").GetString() ?? string.Empty;
                    if (string.Equals(existing, payloadHash, StringComparison.Ordinal))
                    {
                        return new SessionWitnessStoreOutcome
                        {
                            Disposition = SessionWitnessStoreDisposition.Duplicate,
                            WitnessPath = target,
                            PayloadSha256 = payloadHash
                        };
                    }
                }

                string quarantineRoot = Path.Combine(_root, "quarantine");
                Directory.CreateDirectory(quarantineRoot);
                string conflict = Path.Combine(quarantineRoot, "witness-conflict-" + storageId + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
                Directory.CreateDirectory(conflict);
                WriteJson(Path.Combine(conflict, "incoming-witness.json"), witness);
                WriteBytes(Path.Combine(conflict, "upload-payload.json"), uploadPayload);
                return new SessionWitnessStoreOutcome
                {
                    Disposition = SessionWitnessStoreDisposition.ConflictQuarantined,
                    WitnessPath = conflict,
                    PayloadSha256 = payloadHash
                };
            }

            string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                WriteSummary(Path.Combine(temporary, "witness.json"), witness);
                WriteGzip(Path.Combine(temporary, "source-evidence.json.gz"), witness);
                WriteBytes(Path.Combine(temporary, "upload-payload.json"), uploadPayload);
                var files = Directory.GetFiles(temporary)
                    .Select(path => new
                    {
                        name = Path.GetFileName(path),
                        lengthBytes = new FileInfo(path).Length,
                        sha256 = FileHash(path)
                    })
                    .OrderBy(value => value.name, StringComparer.Ordinal)
                    .ToArray();
                WriteJson(Path.Combine(temporary, "manifest.json"), new
                {
                    witness.WitnessId,
                    witness.SessionFingerprint,
                    witness.SourceClientId,
                    sourceRole = witness.SourceRole.ToString().ToUpperInvariant(),
                    captureCompleteness = CompletenessName(witness.CaptureCompleteness),
                    payloadSha256 = payloadHash,
                    files
                });
                Directory.Move(temporary, target);
                return new SessionWitnessStoreOutcome
                {
                    Disposition = SessionWitnessStoreDisposition.Stored,
                    WitnessPath = target,
                    PayloadSha256 = payloadHash
                };
            }
            catch
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                throw;
            }
        }

        private void WriteSummary(string path, SessionWitnessRecord witness)
            => WriteJson(path, new
            {
                witness.Schema,
                witness.WitnessId,
                witness.SessionFingerprint,
                witness.EventFingerprint,
                witness.RosterSignature,
                witness.SourceClientId,
                witness.SourceRole,
                witness.CaptureStartedAtUtc,
                witness.CaptureEndedAtUtc,
                witness.EstimatedSessionStartedAtUtc,
                witness.CaptureCompleteness,
                witness.QualityScore,
                witness.ScheduledEventHint,
                witness.VehicleClass,
                witness.ClientVersion,
                sourceSessionId = witness.Session.SessionId,
                witness.Session.Track,
                witness.Session.Layout,
                witness.Session.AttemptStatus
            });

        private void WriteJson<T>(string path, T value)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, _json);
            WriteBytes(path, bytes);
        }

        private void WriteGzip<T>(string path, T value)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var gzip = new GZipStream(file, CompressionLevel.Optimal, false);
            JsonSerializer.Serialize(gzip, value, _json);
        }

        private static void WriteBytes(string path, byte[] value)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(value, 0, value.Length);
            stream.Flush(true);
        }

        private static bool SafeId(string value)
            => value.Length >= 8 && value.Length <= 128
                && value.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.');

        private static string StorageId(SessionWitnessRecord witness)
        {
            if (string.IsNullOrWhiteSpace(witness.AttemptId)) return witness.WitnessId;
            if (!SafeId(witness.AttemptId)) throw new InvalidDataException("Witness attempt ID is unsafe.");
            string value = witness.WitnessId + "--" + witness.AttemptId;
            if (!SafeId(value)) throw new InvalidDataException("Combined witness attempt identity is unsafe.");
            return value;
        }

        private static string CompletenessName(SessionWitnessCompleteness value)
            => value switch
            {
                SessionWitnessCompleteness.FullSession => "FULL_SESSION",
                SessionWitnessCompleteness.MidSession => "MID_SESSION",
                SessionWitnessCompleteness.EndOnly => "END_ONLY",
                _ => "UNKNOWN"
            };

        private static string FileHash(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
