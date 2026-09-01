using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS2LeagueClient.Core.ActivityCapture
{
    public enum ActivityStoreDisposition
    {
        Stored,
        Duplicate,
        ConflictQuarantined
    }

    public sealed class ActivityStoreOutcome
    {
        public ActivityStoreDisposition Disposition { get; set; }
        public string ActivityPath { get; set; } = string.Empty;
        public string PayloadSha256 { get; set; } = string.Empty;
        public byte[] PayloadUtf8 { get; set; } = Array.Empty<byte>();
    }

    public static class ActivityCanonicalSerializer
    {
        private static readonly JsonSerializerOptions CanonicalOptions = CreateOptions(false);
        private static readonly JsonSerializerOptions PrettyOptions = CreateOptions(true);

        public static void Seal(ActivityRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.Evidence.EvidenceSha256 = string.Empty;
            byte[] source = JsonSerializer.SerializeToUtf8Bytes(record, CanonicalOptions);
            record.Evidence.EvidenceSha256 = Sha256(source);
        }

        public static byte[] Serialize(ActivityRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.Evidence.EvidenceSha256)) Seal(record);
            return JsonSerializer.SerializeToUtf8Bytes(record, CanonicalOptions);
        }

        public static byte[] SerializePretty(ActivityRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.Evidence.EvidenceSha256)) Seal(record);
            return JsonSerializer.SerializeToUtf8Bytes(record, PrettyOptions);
        }

        public static string Sha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes ?? throw new ArgumentNullException(nameof(bytes)))).ToLowerInvariant();
        }

        private static JsonSerializerOptions CreateOptions(bool indented)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return options;
        }
    }

    public sealed class ActivityRecordStore
    {
        private readonly string _root;

        public ActivityRecordStore(string root)
        {
            _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
        }

        public ActivityStoreOutcome Commit(ActivityRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!IsSafeId(record.ActivityId)) throw new InvalidDataException("Activity ID is missing or unsafe.");

            byte[] canonical = ActivityCanonicalSerializer.Serialize(record);
            string payloadSha = ActivityCanonicalSerializer.Sha256(canonical);
            string activitiesRoot = Path.Combine(_root, "activities");
            Directory.CreateDirectory(activitiesRoot);
            string target = Path.Combine(activitiesRoot, record.ActivityId);
            if (Directory.Exists(target))
            {
                string manifestPath = Path.Combine(target, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                    string existing = document.RootElement.TryGetProperty("payloadSha256", out JsonElement value)
                        ? value.GetString() ?? string.Empty
                        : string.Empty;
                    if (string.Equals(existing, payloadSha, StringComparison.Ordinal))
                    {
                        return new ActivityStoreOutcome
                        {
                            Disposition = ActivityStoreDisposition.Duplicate,
                            ActivityPath = target,
                            PayloadSha256 = payloadSha,
                            PayloadUtf8 = canonical
                        };
                    }
                }

                return CommitConflict(record, canonical, payloadSha);
            }

            string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                WriteNew(Path.Combine(temporary, "activity.json"), ActivityCanonicalSerializer.SerializePretty(record));
                WriteManifest(Path.Combine(temporary, "manifest.json"), record, payloadSha);
                Directory.Move(temporary, target);
                return new ActivityStoreOutcome
                {
                    Disposition = ActivityStoreDisposition.Stored,
                    ActivityPath = target,
                    PayloadSha256 = payloadSha,
                    PayloadUtf8 = canonical
                };
            }
            catch
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                throw;
            }
        }

        private ActivityStoreOutcome CommitConflict(ActivityRecord record, byte[] canonical, string payloadSha)
        {
            string quarantineRoot = Path.Combine(_root, "quarantine");
            Directory.CreateDirectory(quarantineRoot);
            string target = Path.Combine(
                quarantineRoot,
                "activity-conflict-" + record.ActivityId + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
            Directory.CreateDirectory(target);
            WriteNew(Path.Combine(target, "incoming-activity.json"), ActivityCanonicalSerializer.SerializePretty(record));
            WriteManifest(Path.Combine(target, "manifest.json"), record, payloadSha);
            return new ActivityStoreOutcome
            {
                Disposition = ActivityStoreDisposition.ConflictQuarantined,
                ActivityPath = target,
                PayloadSha256 = payloadSha,
                PayloadUtf8 = canonical
            };
        }

        private static void WriteManifest(string path, ActivityRecord record, string payloadSha)
        {
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "ams2-league-activity-manifest-v1",
                record.ActivityId,
                payloadSha256 = payloadSha,
                evidenceSha256 = record.Evidence.EvidenceSha256,
                record.ActivityType,
                record.RecordScopeHint,
                record.Authority,
                capturedAtUtc = record.EndedAtUtc
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            WriteNew(path, manifest);
        }

        private static void WriteNew(string path, byte[] bytes)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        private static bool IsSafeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
            foreach (char character in value)
            {
                if (!(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.')) return false;
            }
            return true;
        }
    }
}
