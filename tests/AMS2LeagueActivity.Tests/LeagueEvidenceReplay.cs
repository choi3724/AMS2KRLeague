using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMS2LeagueClient.Core.HostRecording;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.SessionWitness;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueActivity.Tests
{
    /// <summary>Read actual stored evidence without starting the product or its upload queue.</summary>
    internal static class LeagueEvidenceReplay
    {
        public static int Run(string input, string output)
        {
            byte[] raw = File.ReadAllBytes(input);
            string before = Convert.ToHexString(SHA256.HashData(raw));
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            using var compressed = new MemoryStream(raw, false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            SessionWitnessRecord source = JsonSerializer.Deserialize<SessionWitnessRecord>(gzip, options)
                ?? throw new InvalidDataException("No witness source.");
            var engine = new HostRecorderEngine("offline-evidence-reader");
            foreach (HostEvidenceSnapshot snapshot in source.Session.Evidence.OrderBy(x => x.CapturedAtUtc))
                engine.Observe(ToSnapshot(snapshot, source.Session));
            HostRaceResult? final = source.Session.RaceResult;
            if (final != null && source.Session.Evidence.Count > 0
                && final.CapturedAtUtc >= source.Session.Evidence.Max(x => x.CapturedAtUtc))
            {
                HostEvidenceSnapshot last = source.Session.Evidence.Last();
                // These rows and their timestamp come from the original result.
                // No extra stable frames or terminal states are manufactured.
                engine.Observe(ToSnapshot(new HostEvidenceSnapshot
                {
                    CapturedAtUtc = final.CapturedAtUtc, Participants = final.Participants,
                    GameStateRaw = last.GameStateRaw, SessionStateRaw = (uint)SessionState.Race,
                    RaceStateRaw = last.RaceStateRaw, Track = source.Session.Track, Layout = source.Session.Layout,
                    ConfiguredLaps = last.ConfiguredLaps, SessionDurationMinutes = last.SessionDurationMinutes
                }, source.Session));
            }
            HostSessionResult result = engine.Close(source.Session.EndedAtUtc, "OFFLINE_EVIDENCE_REPLAY").FinalizedSession
                ?? throw new InvalidDataException("Evidence did not create a session.");
            var witness = new SessionWitnessRecord
            {
                WitnessId = "offline-proof-" + before.Substring(0, 24).ToLowerInvariant(),
                SessionFingerprint = source.SessionFingerprint, EventFingerprint = source.EventFingerprint,
                RosterSignature = source.RosterSignature, RosterNames = source.RosterNames,
                SourceClientId = "offline-evidence-reader", ClientVersion = "audit",
                CaptureStartedAtUtc = result.StartedAtUtc, CaptureEndedAtUtc = result.EndedAtUtc,
                CaptureCompleteness = SessionWitnessCompleteness.Unknown, RaceMode = source.RaceMode,
                VehicleClass = source.VehicleClass, Session = result,
                Events = source.Events, Weather = source.Weather
            };
            byte[] payload = SessionWitnessUploadPayloadBuilder.Build(witness);
            using var wire = new MemoryStream();
            using (var zipped = new GZipStream(wire, CompressionLevel.Optimal, true)) zipped.Write(payload);
            var roles = new ParticipantRoleClassifier();
            HostParticipantEvidence[] observations = (result.RaceParticipantObservations ?? new List<HostParticipantEvidence>())
                .Where(x => roles.IsLeagueDriver(x.Vehicle, x.VehicleClass)).ToArray();
            var proof = new
            {
                inputSha256 = before.ToLowerInvariant(),
                sourceSnapshots = source.Session.Evidence.Count,
                originalFinalHumans = source.Session.RaceResult?.Participants.Count(x => roles.IsLeagueDriver(x.Vehicle, x.VehicleClass)) ?? 0,
                observedHumans = observations.Select(x => x.NameSnapshot + "\u001f" + x.Vehicle).Distinct().Count(),
                vanished = observations.Where(x => x.Disappeared).Select(x => new
                {
                    x.NameSnapshot, x.LapsCompleted, x.ResultStateRaw, x.LastSeenUtc
                }).ToArray(),
                payloadBytes = payload.Length, gzipBytes = wire.Length,
                sourceUnchanged = before == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
                uploadAttempted = false
            };
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "replayed-witness.json"), payload);
            File.WriteAllText(Path.Combine(output, "proof.json"), JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(proof));
            return proof.sourceUnchanged ? 0 : 1;
        }

        private static TelemetrySnapshot ToSnapshot(HostEvidenceSnapshot raw, HostSessionResult session)
        {
            int count = raw.Participants.Count == 0 ? 0 : raw.Participants.Max(x => x.Slot) + 1;
            var participants = Enumerable.Range(0, count).Select(i => new ParticipantSnapshot(i, false, "", 0, 0, 0, 0, 0, 0, -1, -1)).ToArray();
            foreach (HostParticipantEvidence row in raw.Participants)
                participants[row.Slot] = new ParticipantSnapshot(row.Slot, row.Active, row.NameSnapshot, row.Position,
                    row.LapsCompleted, row.CurrentLap, row.CurrentSector, row.ResultStateRaw, row.PitStateRaw,
                    row.BestLapSeconds, row.LastLapSeconds, row.Vehicle, row.VehicleClass);
            return new TelemetrySnapshot(raw.CapturedAtUtc, session.SharedMemoryVersion, session.Ams2Build,
                raw.SequenceNumber, raw.GameStateRaw, raw.SessionStateRaw, raw.RaceStateRaw, -1, count,
                raw.ConfiguredLaps, -1, -1, -1, -1, participants, raw.Track, raw.Layout,
                currentTime: raw.CurrentTimeSeconds, eventTimeRemaining: raw.EventTimeRemainingSeconds,
                sessionDuration: raw.SessionDurationMinutes);
        }
    }
}
