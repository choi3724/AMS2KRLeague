using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Session;

namespace AMS2LeagueClient.Core.ActivityCapture.Upload
{
    // Low-rate capture candidates are not wire envelopes. A final envelope is
    // written once, before enqueue, and reused byte-for-byte after a restart.
    public sealed class ProvisionalActivityStore
    {
        private readonly string _root;
        private readonly object _gate = new object();
        public ProvisionalActivityStore(string root) => _root = Path.GetFullPath(root);

        public void Stage(string id, string endpoint, string key, byte[] payload)
        {
            using var document = JsonDocument.Parse(payload);
            ReadRange(document.RootElement, out _, out _);
            var candidate = new Candidate { Id = id, Endpoint = endpoint, Key = key,
                Payload = payload, Sha256 = ActivityCanonicalSerializer.Sha256(payload) };
            ValidateIdentity(candidate, document.RootElement);
            string directory = Path.Combine(_root, TelemetryChunkSerializer.StableId(endpoint, key));
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "capture.json");
                if (File.Exists(path))
                {
                    Candidate prior = Read(path);
                    if (prior.Sha256 != candidate.Sha256 || prior.Id != id || prior.Key != key || prior.Endpoint != endpoint)
                        throw new InvalidDataException("PROVISIONAL_IDENTITY_CONFLICT");
                    return;
                }
                WriteOnce(path, JsonSerializer.SerializeToUtf8Bytes(candidate));
            }
        }

        public void Reconcile(ActivityUploadQueue queue, Func<DateTimeOffset, DateTimeOffset, SessionPlayMode> classify,
            Func<DateTimeOffset, DateTimeOffset, bool> canUpload, Action<string>? diagnostic = null)
        {
            lock (_gate)
            {
                if (!Directory.Exists(_root)) return;
                foreach (string path in Directory.EnumerateFiles(_root, "capture.json", SearchOption.AllDirectories))
                {
                    string directory = Path.GetDirectoryName(path)!;
                    if (File.Exists(Path.Combine(directory, "queued"))) continue;
                    try
                    {
                        Candidate capture = Read(path);
                        using var document = JsonDocument.Parse(capture.Payload);
                        ReadRange(document.RootElement, out var first, out var last);
                        if (document.RootElement.GetProperty("raceMode").GetString() == "SINGLE_PLAYER") continue;
                        string finalPath = Path.Combine(directory, "envelope.json");
                        Candidate final;
                        if (File.Exists(finalPath)) final = Read(finalPath);
                        else
                        {
                            SessionPlayMode mode = classify(first, last);
                            if (mode != SessionPlayMode.Multiplayer || !canUpload(first, last))
                            {
                                diagnostic?.Invoke("id=" + capture.Id + " eligibility=" + SessionPlayModeDetector.WireValue(mode));
                                continue;
                            }
                            JsonNode body = JsonNode.Parse(capture.Payload)!;
                            body["raceMode"] = "MULTIPLAYER";
                            final = new Candidate { Id = capture.Id, Endpoint = capture.Endpoint, Key = capture.Key,
                                Payload = JsonSerializer.SerializeToUtf8Bytes(body) };
                            final.Sha256 = ActivityCanonicalSerializer.Sha256(final.Payload);
                            WriteOnce(finalPath, JsonSerializer.SerializeToUtf8Bytes(final));
                        }
                        if (final.Id != capture.Id || final.Key != capture.Key || final.Endpoint != capture.Endpoint)
                            throw new InvalidDataException("PROVISIONAL_ENVELOPE_IDENTITY_MISMATCH");
                        JsonNode expected = JsonNode.Parse(capture.Payload)!;
                        expected["raceMode"] = "MULTIPLAYER";
                        if (ActivityCanonicalSerializer.Sha256(JsonSerializer.SerializeToUtf8Bytes(expected)) != final.Sha256)
                            throw new InvalidDataException("PROVISIONAL_ENVELOPE_CONTENT_MISMATCH");
                        // Evidence is checked again by the upload queue before any HTTP call.
                        ActivityEnqueueOutcome result = queue.Enqueue(final.Id, final.Endpoint, final.Key, final.Payload);
                        if (result.Disposition == ActivityEnqueueDisposition.Conflict)
                            throw new InvalidDataException("PROVISIONAL_QUEUE_CONFLICT");
                        WriteOnce(Path.Combine(directory, "queued"), System.Text.Encoding.ASCII.GetBytes(final.Sha256));
                        diagnostic?.Invoke("id=" + final.Id + " eligibility=MULTIPLAYER envelope=IMMUTABLE queued=true");
                    }
                    catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is JsonException
                        || e is InvalidOperationException || e is ArgumentException || e is FormatException || e is System.Collections.Generic.KeyNotFoundException)
                    { diagnostic?.Invoke("candidate=" + Path.GetFileName(directory) + " error=" + e.GetType().Name); }
                }
            }
        }

        private static Candidate Read(string path)
        {
            Candidate value = JsonSerializer.Deserialize<Candidate>(File.ReadAllBytes(path))
                ?? throw new InvalidDataException("PROVISIONAL_INVALID");
            if (ActivityCanonicalSerializer.Sha256(value.Payload) != value.Sha256)
                throw new InvalidDataException("PROVISIONAL_HASH_MISMATCH");
            using var document = JsonDocument.Parse(value.Payload);
            ValidateIdentity(value, document.RootElement);
            return value;
        }
        private static void ValidateIdentity(Candidate value, JsonElement root)
        {
            bool witness = root.TryGetProperty("schema", out var schema) && schema.GetString() == "ams2-session-witness-v1";
            string endpoint = witness ? "v1/session/witness" : "v1/player/activities";
            if (string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.Key)
                || value.Endpoint != endpoint || !root.TryGetProperty(witness ? "witnessId" : "activityId", out var id)
                || id.ValueKind != JsonValueKind.String || id.GetString() != value.Id)
                throw new InvalidDataException("PROVISIONAL_IDENTITY_MISMATCH");
        }
        private static void ReadRange(JsonElement root, out DateTimeOffset first, out DateTimeOffset last)
        {
            string schema = root.GetProperty("schema").GetString() ?? "";
            bool witness = schema == "ams2-session-witness-v1";
            if (!witness && schema != "ams2-player-activity-v2") throw new InvalidDataException("PROVISIONAL_SCHEMA_UNKNOWN");
            first = root.GetProperty(witness ? "captureStartedAtUtc" : "startedAtUtc").GetDateTimeOffset();
            last = root.GetProperty(witness ? "captureEndedAtUtc" : "endedAtUtc").GetDateTimeOffset();
            if (first == default || last < first) throw new InvalidDataException("PROVISIONAL_TIME_RANGE");
        }
        private static void WriteOnce(string path, byte[] bytes)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path);
        }
        public sealed class Candidate
        {
            public string Id { get; set; } = "";
            public string Endpoint { get; set; } = "";
            public string Key { get; set; } = "";
            public byte[] Payload { get; set; } = Array.Empty<byte>();
            public string Sha256 { get; set; } = "";
        }
    }
}
