using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS2LeagueClient.Core.ActivityCapture.Upload
{
    /// <summary>
    /// Converts the immutable local evidence model into the deliberately smaller
    /// Player API contract.  Server identity is never asserted by the client: the
    /// bearer installation is the sole authoritative identity at ingestion time.
    /// </summary>
    public static class PlayerActivityUploadPayloadBuilder
    {
        private static readonly JsonSerializerOptions Json = CreateOptions();

        public static bool TryBuild(ActivityRecord record, out byte[] payloadUtf8, out string reason)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            payloadUtf8 = Array.Empty<byte>();
            reason = string.Empty;

            if (record.Authority != ActivityAuthority.PlayerPersonal)
            {
                reason = "NOT_PLAYER_PERSONAL";
                return false;
            }

            object? personalResult = null;
            object[]? laps = null;
            string activityType;
            if (record.ActivityType == ActivityType.Race)
            {
                PersonalRaceSummary? summary = record.PersonalRaceSummary;
                if (summary == null || !summary.FinishPosition.HasValue || !summary.FieldSize.HasValue)
                {
                    reason = "PERSONAL_RACE_RESULT_INCOMPLETE";
                    return false;
                }

                personalResult = new
                {
                    position = summary.FinishPosition.Value,
                    participantCount = summary.FieldSize.Value,
                    lapsCompleted = summary.CompletedLaps,
                    bestLapSeconds = MillisecondsToSeconds(summary.BestLapMilliseconds),
                    resultState = summary.ResultState
                };
                activityType = "RACE";
            }
            else if (record.ActivityType == ActivityType.TimeAttack)
            {
                TimeAttackLapRecord? lap = record.TimeAttackLap;
                if (lap == null || !lap.LapTimeMilliseconds.HasValue)
                {
                    reason = "TIME_ATTACK_LAP_INCOMPLETE";
                    return false;
                }

                laps = new object[]
                {
                    new
                    {
                        lapId = lap.LapUid,
                        lapNumber = lap.LapOrdinal,
                        completedAtUtc = lap.CompletedAtUtc,
                        lapTimeSeconds = MillisecondsToSeconds(lap.LapTimeMilliseconds),
                        sector1Seconds = MillisecondsToSeconds(lap.Sector1Milliseconds),
                        sector2Seconds = MillisecondsToSeconds(lap.Sector2Milliseconds),
                        sector3Seconds = MillisecondsToSeconds(lap.Sector3Milliseconds),
                        valid = lap.IsValid,
                        invalidReason = lap.IsValid ? null : lap.InvalidReason,
                        personalBestHint = lap.ClientPersonalBest,
                        sessionBestHint = lap.ClientSessionBest
                    }
                };
                activityType = "TIME_ATTACK";
            }
            else
            {
                reason = "ACTIVITY_TYPE_UNSUPPORTED";
                return false;
            }

            var payload = new
            {
                schema = "ams2-player-activity-v1",
                record.ActivityId,
                activityType,
                recordScope = "GENERAL",
                sessionId = record.SessionFingerprint,
                record.SessionFingerprint,
                evidenceSha256 = record.Evidence.EvidenceSha256,
                observedName = record.Identity.ObservedAms2Name,
                record.Track,
                record.Layout,
                record.Vehicle,
                record.VehicleClass,
                raceMode = NormalizeRaceMode(record.ObservedConditions.RaceMode),
                record.StartedAtUtc,
                record.EndedAtUtc,
                record.ConfiguredSettings,
                record.ObservedConditions,
                captureVersion = record.Evidence.CaptureVersion,
                gameVersion = record.Evidence.Ams2Build.ToString(System.Globalization.CultureInfo.InvariantCulture),
                record.ClientVersion,
                personalResult,
                laps
            };
            payloadUtf8 = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            return true;
        }

        public static string CreateIdempotencyKey(ActivityRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.ActivityId)) throw new ArgumentException("Activity ID is required.", nameof(record));
            return "player:" + ActivityIds.Hash(record.ActivityId);
        }

        private static double? MillisecondsToSeconds(int? milliseconds)
            => milliseconds.HasValue ? milliseconds.Value / 1000.0 : (double?)null;

        private static string NormalizeRaceMode(string? value)
        {
            string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            return normalized == "MULTIPLAYER" || normalized == "SINGLE_PLAYER" ? normalized : "UNKNOWN";
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return options;
        }
    }

}
