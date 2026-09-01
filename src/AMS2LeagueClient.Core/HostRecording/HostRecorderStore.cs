using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS2LeagueClient.Core.HostRecording
{
    public sealed class HostRecorderStore
    {
        private readonly string _root;
        private readonly JsonSerializerOptions _json;

        public HostRecorderStore(string root)
        {
            _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
            _json = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            _json.Converters.Add(new JsonStringEnumConverter());
        }

        public HostStoreOutcome Commit(HostSessionResult session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            Seal(session);
            if (session.Reliability == HostResultReliability.Quarantined)
            {
                return CommitValidationQuarantine(session);
            }

            string sessionsRoot = Path.Combine(_root, "sessions");
            Directory.CreateDirectory(sessionsRoot);
            string target = Path.Combine(sessionsRoot, session.SessionId);
            if (Directory.Exists(target))
            {
                string manifestPath = Path.Combine(target, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                    string existingHash = manifest.RootElement.GetProperty("evidenceSha256").GetString() ?? string.Empty;
                    if (string.Equals(existingHash, session.EvidenceSha256, StringComparison.Ordinal))
                    {
                        return new HostStoreOutcome { Disposition = HostStoreDisposition.Duplicate, SessionPath = target, Message = "same session/evidence already stored" };
                    }
                }

                string quarantineRoot = Path.Combine(_root, "quarantine");
                Directory.CreateDirectory(quarantineRoot);
                string conflict = Path.Combine(quarantineRoot, "session-conflict-" + session.SessionId + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
                Directory.CreateDirectory(conflict);
                WriteJson(Path.Combine(conflict, "incoming-session.json"), session);
                return new HostStoreOutcome { Disposition = HostStoreDisposition.ConflictQuarantined, SessionPath = conflict, Message = "same session id has different immutable evidence" };
            }

            string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                WriteJson(Path.Combine(temporary, "session.json"), new
                {
                    session.Schema, session.ParserVersion, session.SessionId, session.HostInstallationId,
                    session.StartedAtUtc, session.EndedAtUtc, session.Ams2Build, session.SharedMemoryVersion,
                    session.Track, session.Layout, session.SessionTypesObserved, session.Reliability,
                    session.EvidenceSha256, session.ClosingReason, session.AttemptStatus, session.Activity
                });
                WriteOptional(Path.Combine(temporary, "qualifying.json"), session.Qualifying);
                WriteOptional(Path.Combine(temporary, "starting-grid.json"), session.StartingGrid);
                WriteOptional(Path.Combine(temporary, "race-result.json"), session.RaceResult);
                WriteJson(Path.Combine(temporary, "validation.json"), MetadataEnvelope(session, new { session.Reliability, issues = session.Issues }));
                WriteEvidenceGzip(Path.Combine(temporary, "evidence-samples.json.gz"), MetadataEnvelope(session, new { samples = session.Evidence }));
                var files = Directory.GetFiles(temporary)
                    .Select(path => new
                    {
                        name = Path.GetFileName(path),
                        lengthBytes = new FileInfo(path).Length,
                        sha256 = ComputeFileSha256(path)
                    })
                    .OrderBy(file => file.name, StringComparer.Ordinal)
                    .ToArray();
                WriteJson(Path.Combine(temporary, "manifest.json"), new
                {
                    session.SessionId,
                    session.EvidenceSha256,
                    parserVersion = session.ParserVersion,
                    sharedMemoryVersion = session.SharedMemoryVersion,
                    ams2Build = session.Ams2Build,
                    capturedAtUtc = session.EndedAtUtc,
                    files
                });
                Directory.Move(temporary, target);
                return new HostStoreOutcome { Disposition = HostStoreDisposition.Stored, SessionPath = target, Message = "immutable host session evidence stored" };
            }
            catch
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                throw;
            }
        }

        private static void Seal(HostSessionResult session)
        {
            if (string.IsNullOrWhiteSpace(session.EvidenceSha256))
            {
                session.EvidenceSha256 = HostEvidenceHasher.Compute(session.Evidence);
            }
            if (session.Qualifying != null) Stamp(session.Qualifying, session);
            if (session.StartingGrid != null) Stamp(session.StartingGrid, session);
            if (session.RaceResult != null)
            {
                session.RaceResult.SessionId = session.SessionId;
                session.RaceResult.ParserVersion = session.ParserVersion;
                session.RaceResult.Ams2Build = session.Ams2Build;
                session.RaceResult.SharedMemoryVersion = session.SharedMemoryVersion;
                session.RaceResult.EvidenceSha256 = session.EvidenceSha256;
            }
        }

        private static void Stamp(HostClassification classification, HostSessionResult session)
        {
            classification.SessionId = session.SessionId;
            classification.ParserVersion = session.ParserVersion;
            classification.Ams2Build = session.Ams2Build;
            classification.SharedMemoryVersion = session.SharedMemoryVersion;
            classification.EvidenceSha256 = session.EvidenceSha256;
        }

        private HostStoreOutcome CommitValidationQuarantine(HostSessionResult session)
        {
            string quarantineRoot = Path.Combine(_root, "quarantine");
            Directory.CreateDirectory(quarantineRoot);
            string target = Path.Combine(quarantineRoot, "validation-" + session.SessionId);
            if (Directory.Exists(target))
            {
                string manifestPath = Path.Combine(target, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                    string existingHash = manifest.RootElement.GetProperty("evidenceSha256").GetString() ?? string.Empty;
                    if (string.Equals(existingHash, session.EvidenceSha256, StringComparison.Ordinal))
                    {
                        return new HostStoreOutcome { Disposition = HostStoreDisposition.Duplicate, SessionPath = target, Message = "same quarantined evidence already stored" };
                    }
                }

                target += "-conflict-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            }

            string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                WriteJson(Path.Combine(temporary, "incoming-session.json"), session);
                WriteJson(Path.Combine(temporary, "validation.json"), MetadataEnvelope(session, new { session.Reliability, issues = session.Issues }));
                WriteEvidenceGzip(Path.Combine(temporary, "evidence-samples.json.gz"), MetadataEnvelope(session, new { samples = session.Evidence }));
                var files = Directory.GetFiles(temporary)
                    .Select(path => new
                    {
                        name = Path.GetFileName(path),
                        lengthBytes = new FileInfo(path).Length,
                        sha256 = ComputeFileSha256(path)
                    })
                    .OrderBy(file => file.name, StringComparer.Ordinal)
                    .ToArray();
                WriteJson(Path.Combine(temporary, "manifest.json"), new
                {
                    session.SessionId,
                    session.EvidenceSha256,
                    parserVersion = session.ParserVersion,
                    sharedMemoryVersion = session.SharedMemoryVersion,
                    ams2Build = session.Ams2Build,
                    capturedAtUtc = session.EndedAtUtc,
                    disposition = "QUARANTINED",
                    files
                });
                Directory.Move(temporary, target);
                return new HostStoreOutcome { Disposition = HostStoreDisposition.Quarantined, SessionPath = target, Message = "validation errors quarantined without result fabrication" };
            }
            catch
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                throw;
            }
        }

        private void WriteOptional<T>(string path, T? value) where T : class
        {
            if (value != null) WriteJson(path, value);
        }

        private void WriteJson<T>(string path, T value)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, _json);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        private void WriteEvidenceGzip(string path, object value)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var gzip = new GZipStream(file, CompressionLevel.Optimal, false);
            JsonSerializer.Serialize(gzip, value, _json);
        }

        private static object MetadataEnvelope(HostSessionResult session, object payload)
            => new
            {
                schema = "ams2-league-host-artifact-envelope-v1",
                session.SessionId,
                session.ParserVersion,
                session.Ams2Build,
                session.SharedMemoryVersion,
                capturedAtUtc = session.EndedAtUtc,
                session.EvidenceSha256,
                payload
            };

        private static string ComputeFileSha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
