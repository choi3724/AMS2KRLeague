using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMS2LeagueClient.Core.HostRecording;

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
                    rawParticipantCount = summary.FieldSize.Value,
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
                        invalidReason = lap.IsValid ? null : lap.InvalidReason
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
                schema = "ams2-player-activity-v2",
                record.ActivityId,
                activityType,
                recordScope = "UNCLASSIFIED",
                scheduledEventHint = record.ScheduledEventHint,
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

    /// <summary>
    /// Builds the backward-compatible recorder envelope.  Raw sampling evidence
    /// remains in the local immutable store; the upload contains only the result
    /// artifacts required by the central service.
    /// </summary>
    public static class HostResultUploadPayloadBuilder
    {
        private static readonly JsonSerializerOptions Json = CreateOptions();

        public static byte[] Build(string? eventId, HostSessionResult session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            object? activity = null;
            if (session.Activity != null)
            {
                if (session.Activity.ActivityType != ActivityType.Race)
                {
                    throw new InvalidOperationException("Recorder results can only upload RACE activity metadata.");
                }
                activity = new
                {
                    session.Activity.ActivityId,
                    activityType = "RACE",
                    recordScopeHint = "UNCLASSIFIED",
                    session.Activity.SessionFingerprint,
                    session.Activity.CaptureChainId,
                    session.Activity.ScheduledEventHint,
                    session.Activity.AttemptNumber,
                    attemptStatus = NormalizeAttemptStatus(session.Activity.AttemptStatus),
                    raceMode = NormalizeRaceMode(session.Activity.RaceMode),
                    configuredSettings = session.Activity.ConfiguredSettings.Count == 0 ? null : session.Activity.ConfiguredSettings,
                    observedConditions = session.Activity.ObservedConditions.Count == 0 ? null : session.Activity.ObservedConditions
                };
            }

            var payload = new
            {
                eventId = string.IsNullOrWhiteSpace(eventId) ? null : eventId.Trim(),
                activity,
                session = new
                {
                    session.Schema,
                    session.ParserVersion,
                    session.SessionId,
                    session.HostInstallationId,
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
                }
            };
            return JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        }

        public static string CreateIdempotencyKey(HostSessionResult session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            string identity = session.Activity?.ActivityId ?? session.SessionId;
            if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Session activity identity is required.", nameof(session));
            return "recorder:" + ActivityIds.Hash(identity);
        }

        private static string NormalizeAttemptStatus(string? value)
        {
            string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            return normalized == "FINISHED" || normalized == "ABORTED" || normalized == "RESTARTED"
                ? normalized
                : "UNKNOWN";
        }

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
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
