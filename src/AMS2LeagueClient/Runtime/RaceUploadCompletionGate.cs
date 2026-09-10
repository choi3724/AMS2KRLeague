using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2LeagueClient.Runtime
{
    /// <summary>Release a captured weekend only after a durable, stable race result exists.</summary>
    public sealed class RaceUploadCompletionGate
    {
        private readonly string _archiveRoot;
        private Completion[] _ready = Array.Empty<Completion>();
        public RaceUploadCompletionGate(string archiveRoot) => _archiveRoot = archiveRoot;

        public void Refresh(IReadOnlyList<ActivityUploadItem> items)
        {
            var witnesses = new List<Completion>();
            foreach (ActivityUploadItem item in items)
            {
                if (item.State.Status == ActivityUploadStatus.CONFLICT
                    || (item.State.Status == ActivityUploadStatus.QUARANTINED && item.State.LastHttpStatus != 401)) continue;
                // A legacy 401 quarantine describes delivery credentials, not
                // local evidence integrity. Do not migrate or resend that item.
                try
                {
                    using var document = JsonDocument.Parse(item.PayloadUtf8);
                    JsonElement root = document.RootElement;
                    if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != "ams2-session-witness-v1"
                        || !root.TryGetProperty("captureSessionId", out var id) || string.IsNullOrWhiteSpace(id.GetString())
                        || !root.TryGetProperty("captureStartedAtUtc", out var start) || !start.TryGetDateTimeOffset(out var first)
                        || !root.TryGetProperty("captureEndedAtUtc", out var end) || !end.TryGetDateTimeOffset(out var last) || last < first) continue;
                    bool finished = root.TryGetProperty("session", out var session)
                        && session.TryGetProperty("raceResult", out var result) && result.ValueKind == JsonValueKind.Object
                        && result.TryGetProperty("stable", out var stable) && stable.ValueKind == JsonValueKind.True
                        && FinalizeIsDurable(root, id.GetString()!);
                    witnesses.Add(new Completion(id.GetString()!, first, last, finished));
                }
                catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException)
                {
                    // Malformed proof never unlocks an upload; the immutable queue item is retained.
                    continue;
                }
            }
            _ready = witnesses.GroupBy(value => value.SessionId)
                .Where(group => group.Any(value => value.Finished))
                .Select(group => new Completion(group.Key, group.Min(value => value.Start),
                    group.Where(value => value.Finished).Max(value => value.End), true)).ToArray();
        }

        public bool Allows(string sessionId) => _ready.Any(value => value.SessionId == sessionId);

        public bool Allows(ActivityUploadItem item)
        {
            try
            {
                using var document = JsonDocument.Parse(item.PayloadUtf8);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("captureSessionId", out var id)) return Allows(id.GetString() ?? string.Empty);
                return root.TryGetProperty("startedAtUtc", out var start) && start.TryGetDateTimeOffset(out var first)
                    && root.TryGetProperty("endedAtUtc", out var end) && end.TryGetDateTimeOffset(out var last)
                    && first <= last && _ready.Any(value => first >= value.Start && last <= value.End);
            }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException) { return false; }
        }

        private bool FinalizeIsDurable(JsonElement witness, string sessionId)
        {
            if (!witness.TryGetProperty("sessionFingerprint", out var fingerprint)
                || !witness.TryGetProperty("witnessId", out var witnessId)
                || !witness.TryGetProperty("attemptId", out var attemptId)) return false;
            string key = TelemetryChunkSerializer.StableId(fingerprint.GetString() ?? "",
                witnessId.GetString() ?? "", attemptId.GetString() ?? "").Substring(0, 32);
            // Compact is authoritative. A stale mirror must neither block a valid
            // final ACK nor override a corrupt or absent Compact final ACK.
            if (Directory.Exists(Path.Combine(_archiveRoot, "sessions", key, "chunks", "compact")))
                return CompactArchiveEvidence.HasDurableFinalize(_archiveRoot, sessionId,
                    fingerprint.GetString() ?? "", witnessId.GetString() ?? "", attemptId.GetString() ?? "");
            string path = Path.Combine(_archiveRoot, "attempt-ledgers", key + ".attempt-loss.json");
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 1_048_576) return false;
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                JsonElement ledger = document.RootElement;
                return ledger.GetProperty("sessionId").GetString() == sessionId
                    && ledger.GetProperty("attemptId").GetString() == attemptId.GetString()
                    && ledger.GetProperty("closeRequested").ValueKind == JsonValueKind.True
                    && ledger.GetProperty("finalizeAcknowledged").ValueKind == JsonValueKind.True;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException
                || e is InvalidOperationException || e is KeyNotFoundException) { return false; }
        }

        private sealed class Completion
        {
            public Completion(string id, DateTimeOffset start, DateTimeOffset end, bool finished)
            { SessionId = id; Start = start; End = end; Finished = finished; }
            public string SessionId { get; }
            public DateTimeOffset Start { get; }
            public DateTimeOffset End { get; }
            public bool Finished { get; }
        }
    }
}
