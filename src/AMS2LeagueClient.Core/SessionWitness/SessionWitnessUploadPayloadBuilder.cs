using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMS2LeagueClient.Core.ActivityCapture;

namespace AMS2LeagueClient.Core.SessionWitness
{
    public static class SessionWitnessUploadPayloadBuilder
    {
        private static readonly JsonSerializerOptions Json = CreateOptions();

        public static byte[] Build(SessionWitnessRecord witness)
        {
            if (witness == null) throw new ArgumentNullException(nameof(witness));
            if (string.IsNullOrWhiteSpace(witness.WitnessId)) throw new ArgumentException("Witness ID is required.", nameof(witness));
            if (string.IsNullOrWhiteSpace(witness.SessionFingerprint)) throw new ArgumentException("Session fingerprint is required.", nameof(witness));

            var session = witness.Session;
            var activity = session.Activity;
            var payload = new
            {
                schema = "ams2-session-witness-v1",
                payloadVersion = 1,
                witness.WitnessId,
                witness.SessionFingerprint,
                witness.EventFingerprint,
                witness.RosterSignature,
                witness.RosterNames,
                witness.SourceClientId,
                sourceRole = witness.SourceRole.ToString().ToUpperInvariant(),
                witness.CaptureStartedAtUtc,
                witness.CaptureEndedAtUtc,
                witness.EstimatedSessionStartedAtUtc,
                captureCompleteness = CompletenessName(witness.CaptureCompleteness),
                witness.QualityScore,
                witness.ScheduledEventHint,
                witness.VehicleClass,
                witness.ClientVersion,
                activity = activity == null ? null : new
                {
                    activity.ActivityId,
                    activityType = "RACE",
                    recordScopeHint = "UNCLASSIFIED",
                    sessionFingerprint = witness.SessionFingerprint,
                    activity.CaptureChainId,
                    activity.ScheduledEventHint,
                    activity.AttemptNumber,
                    activity.AttemptStatus,
                    activity.RaceMode,
                    activity.ConfiguredSettings,
                    activity.ObservedConditions
                },
                session = new
                {
                    sourceSessionId = session.SessionId,
                    session.ParserVersion,
                    session.StartedAtUtc,
                    session.EndedAtUtc,
                    session.Ams2Build,
                    session.SharedMemoryVersion,
                    session.Track,
                    session.Layout,
                    session.SessionTypesObserved,
                    session.Reliability,
                    session.EvidenceSha256,
                    session.ClosingReason,
                    session.AttemptStatus,
                    session.Qualifying,
                    session.StartingGrid,
                    session.RaceResult,
                    session.Issues
                },
                events = witness.Events,
                weather = witness.Weather
            };
            return JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        }

        public static string CreateIdempotencyKey(SessionWitnessRecord witness)
        {
            if (witness == null || string.IsNullOrWhiteSpace(witness.WitnessId))
            {
                throw new ArgumentException("Witness identity is required.", nameof(witness));
            }
            return "witness:" + Sha256Hex(Encoding.UTF8.GetBytes(witness.WitnessId));
        }

        public static string Sha256Hex(byte[] value)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(value ?? Array.Empty<byte>())).ToLowerInvariant();
        }

        private static string CompletenessName(SessionWitnessCompleteness value)
            => value switch
            {
                SessionWitnessCompleteness.FullSession => "FULL_SESSION",
                SessionWitnessCompleteness.MidSession => "MID_SESSION",
                SessionWitnessCompleteness.EndOnly => "END_ONLY",
                _ => "UNKNOWN"
            };

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
