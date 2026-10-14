using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2LeagueClient.Runtime
{
    /// <summary>Deliver new observer evidence independently; keep replay and legacy completion safeguards.</summary>
    public sealed class RaceUploadCompletionGate
    {
        private readonly string _archiveRoot;
        private readonly DateTimeOffset _phaseLinkActivatedAtUtc;
        private readonly DateTimeOffset _observerUploadActivatedAtUtc;
        private Completion[] _ready = Array.Empty<Completion>();
        private Completion[] _observerCaptures = Array.Empty<Completion>();
        private HashSet<string> _observerWitnessIds = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _observerCompactIds = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _linkedWitnessIds = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _linkedCompactIds = new HashSet<string>(StringComparer.Ordinal);
        public string PhaseLinkPolicyStatus { get; }
        public string ObserverUploadPolicyStatus { get; }

        public RaceUploadCompletionGate(string archiveRoot, DateTimeOffset? phaseLinkActivatedAtUtc = null,
            DateTimeOffset? observerUploadActivatedAtUtc = null)
        {
            _archiveRoot = archiveRoot;
            if (phaseLinkActivatedAtUtc.HasValue)
            {
                _phaseLinkActivatedAtUtc = phaseLinkActivatedAtUtc.Value.ToUniversalTime();
                PhaseLinkPolicyStatus = "TEST_ACTIVATION";
            }
            else
            {
                (_phaseLinkActivatedAtUtc, PhaseLinkPolicyStatus) = LoadOrCreatePhaseLinkActivation(archiveRoot);
            }
            if (observerUploadActivatedAtUtc.HasValue)
            {
                _observerUploadActivatedAtUtc = observerUploadActivatedAtUtc.Value.ToUniversalTime();
                ObserverUploadPolicyStatus = "TEST_ACTIVATION";
            }
            else
            {
                (_observerUploadActivatedAtUtc, ObserverUploadPolicyStatus) = LoadOrCreatePhaseLinkActivation(
                    archiveRoot, "observer-upload-policy-v2.json", "ams2-observer-upload-policy-v2");
            }
        }

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
                        || !root.TryGetProperty("captureStartedAtUtc", out var start) || !start.TryGetDateTimeOffset(out var first)
                        || !root.TryGetProperty("captureEndedAtUtc", out var end) || !end.TryGetDateTimeOffset(out var last) || last < first) continue;
                    bool independent = item.Metadata.PhaseLinkPolicyVersion >= 2 && first >= _observerUploadActivatedAtUtc;
                    string captureId = Text(root, "captureSessionId");
                    bool archiveBound = !string.IsNullOrWhiteSpace(captureId);
                    if (!archiveBound && (!independent || string.IsNullOrWhiteSpace(Text(root, "witnessId")))) continue;
                    // A failed replay recorder must not prevent the independently
                    // durable result witness from being delivered.
                    string evidenceId = archiveBound ? captureId : "witness-only:" + Text(root, "witnessId");
                    bool durable = archiveBound && FinalizeIsDurable(root, captureId);
                    bool finished = root.TryGetProperty("session", out var session)
                        && session.TryGetProperty("raceResult", out var result) && result.ValueKind == JsonValueKind.Object
                        && result.TryGetProperty("stable", out var stable) && stable.ValueKind == JsonValueKind.True
                        && durable;
                    string[] roster = root.TryGetProperty("rosterNames", out var names) && names.ValueKind == JsonValueKind.Array
                        ? names.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                            .Select(value => value.GetString() ?? "").Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : Array.Empty<string>();
                    string phase = session.TryGetProperty("qualifying", out var qualifying)
                        && qualifying.ValueKind == JsonValueKind.Object ? "QUALIFYING" : string.Empty;
                    if (phase.Length == 0 && session.TryGetProperty("sessionTypesObserved", out var types)
                        && types.ValueKind == JsonValueKind.Array
                        && types.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String
                            && value.GetString() == "PRACTICE")) phase = "PRACTICE";
                    witnesses.Add(new Completion(evidenceId, first, last, finished, durable,
                        Text(root, "eventFingerprint"), Text(root, "sourceClientId"),
                        Text(session, "track"), Text(session, "layout"), Text(root, "vehicleClass"), roster, phase,
                        item.Metadata.PhaseLinkPolicyVersion >= 1));
                    if (independent)
                        witnesses[witnesses.Count - 1].IndependentObserverUpload = true;
                }
                catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException)
                {
                    // Malformed proof never unlocks an upload; the immutable queue item is retained.
                    continue;
                }
            }
            _ready = witnesses.GroupBy(value => value.SessionId)
                .Where(group => group.Any(value => value.Finished))
                .Select(group => group.First(value => value.Finished)).ToArray();
            // Result/phase witnesses have their own immutable durable queue.
            // Their delivery must not depend on replay finalization, or on this
            // observer remaining in the room until everyone else finishes.
            _observerCaptures = witnesses.Where(value => value.IndependentObserverUpload).ToArray();
            _observerWitnessIds = _observerCaptures.Select(value => value.SessionId).ToHashSet(StringComparer.Ordinal);
            _observerCompactIds = _observerCaptures.Where(value => value.Durable)
                .Select(value => value.SessionId).ToHashSet(StringComparer.Ordinal);
            // Phase captures often receive a new archive identity at the AMS2
            // qualifying-to-race transition. Only the witness JSON is released
            // across that boundary; compact chunks still require their own final ACK.
            var linked = new HashSet<string>(StringComparer.Ordinal);
            var compact = new HashSet<string>(StringComparer.Ordinal);
            // Legacy pending witnesses remain untouched for a separately reviewed
            // recovery. A software update must not silently transmit old phases.
            foreach (Completion phase in witnesses.Where(value => !value.Finished
                && value.Phase.Length > 0 && value.NewPhaseLinkPolicy
                && value.Start >= _phaseLinkActivatedAtUtc))
            {
                if (witnesses.Any(next => !next.Finished && next.SessionId != phase.SessionId
                    && next.Phase == phase.Phase && next.EventFingerprint == phase.EventFingerprint
                    && next.SourceClientId == phase.SourceClientId && next.Start >= phase.End
                    && next.Start - phase.End <= TimeSpan.FromMinutes(2))) continue;
                Completion[] races = _ready.Where(value => SameMeeting(phase, value)).ToArray();
                if (races.Length == 1)
                {
                    linked.Add(phase.SessionId);
                    if (phase.Durable) compact.Add(phase.SessionId);
                }
            }
            _linkedWitnessIds = linked;
            _linkedCompactIds = compact;
        }

        public bool Allows(string sessionId) => _ready.Any(value => value.SessionId == sessionId)
            || _linkedCompactIds.Contains(sessionId) || _observerCompactIds.Contains(sessionId);

        public bool Allows(ActivityUploadItem item)
        {
            try
            {
                using var document = JsonDocument.Parse(item.PayloadUtf8);
                JsonElement root = document.RootElement;
                if (Text(root, "schema") == "ams2-session-witness-v1")
                {
                    string captureId = Text(root, "captureSessionId");
                    string evidenceId = !string.IsNullOrWhiteSpace(captureId)
                        ? captureId : "witness-only:" + Text(root, "witnessId");
                    return Allows(evidenceId) || _linkedWitnessIds.Contains(evidenceId) || _observerWitnessIds.Contains(evidenceId);
                }
                if (root.TryGetProperty("captureSessionId", out var id))
                {
                    string sessionId = id.GetString() ?? string.Empty;
                    return Allows(sessionId) || (Text(root, "schema") == "ams2-session-witness-v1"
                        && (_linkedWitnessIds.Contains(sessionId) || _observerWitnessIds.Contains(sessionId)));
                }
                return root.TryGetProperty("startedAtUtc", out var start) && start.TryGetDateTimeOffset(out var first)
                    && root.TryGetProperty("endedAtUtc", out var end) && end.TryGetDateTimeOffset(out var last)
                    && first <= last && _ready.Concat(_observerCaptures.Where(value => value.Durable))
                        .Any(value => first >= value.Start && last <= value.End);
            }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException) { return false; }
        }

        private static string Text(JsonElement root, string property)
            => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty : string.Empty;

        private static (DateTimeOffset ActivatedAtUtc, string Status) LoadOrCreatePhaseLinkActivation(string archiveRoot,
            string filename = "phase-link-policy.json", string schema = "ams2-phase-link-policy-v1")
        {
            string path = Path.Combine(archiveRoot, filename);
            try
            {
                Directory.CreateDirectory(archiveRoot);
                if (!File.Exists(path))
                {
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        schema,
                        activatedAtUtc = DateTimeOffset.UtcNow
                    });
                    try
                    {
                        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    catch (IOException) when (File.Exists(path)) { }
                }
                if (new FileInfo(path).Length > 4096) return (DateTimeOffset.MaxValue, "INVALID_ACTIVATION");
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                JsonElement marker = document.RootElement;
                if (Text(marker, "schema") != schema
                    || !marker.TryGetProperty("activatedAtUtc", out var time)
                    || !time.TryGetDateTimeOffset(out var activated)
                    || activated == default) return (DateTimeOffset.MaxValue, "INVALID_ACTIVATION");
                return (activated.ToUniversalTime(), "ACTIVE");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException
                || e is JsonException || e is InvalidOperationException || e is FormatException)
            {
                // Keep ordinary same-capture uploads working, but never unlock
                // historical cross-phase records when the activation is uncertain.
                return (DateTimeOffset.MaxValue, "UNAVAILABLE_ACTIVATION");
            }
        }

        private static bool SameMeeting(Completion phase, Completion race)
        {
            if (phase.EventFingerprint.Length == 0 || phase.EventFingerprint != race.EventFingerprint
                || phase.SourceClientId.Length == 0 || phase.SourceClientId != race.SourceClientId
                || phase.Track.Length == 0 || !string.Equals(phase.Track, race.Track, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(phase.Layout, race.Layout, StringComparison.OrdinalIgnoreCase)
                || phase.VehicleClass.Length == 0
                || !string.Equals(phase.VehicleClass, race.VehicleClass, StringComparison.OrdinalIgnoreCase)
                || phase.Start >= race.Start || phase.End > race.Start.AddSeconds(5)
                || race.Start - phase.End > TimeSpan.FromHours(2)) return false;
            // Entrants may leave or join between phases. The caller requires
            // one race matching observer, course, settings and bounded time.
            // Repeated identical meetings remain ambiguous instead of guessing.
            return true;
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
            public Completion(string id, DateTimeOffset start, DateTimeOffset end, bool finished, bool durable,
                string eventFingerprint, string sourceClientId, string track, string layout,
                string vehicleClass, string[] roster, string phase, bool newPhaseLinkPolicy)
            {
                SessionId = id; Start = start; End = end; Finished = finished; Durable = durable;
                EventFingerprint = eventFingerprint; SourceClientId = sourceClientId;
                Track = track; Layout = layout; VehicleClass = vehicleClass; Roster = roster; Phase = phase;
                NewPhaseLinkPolicy = newPhaseLinkPolicy;
            }
            public string SessionId { get; }
            public DateTimeOffset Start { get; }
            public DateTimeOffset End { get; }
            public bool Finished { get; }
            public bool Durable { get; }
            public string EventFingerprint { get; }
            public string SourceClientId { get; }
            public string Track { get; }
            public string Layout { get; }
            public string VehicleClass { get; }
            public string[] Roster { get; }
            public string Phase { get; }
            public bool NewPhaseLinkPolicy { get; }
            public bool IndependentObserverUpload { get; set; }
        }
    }
}
