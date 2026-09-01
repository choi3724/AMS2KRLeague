using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.ActivityCapture.Upload;

namespace AMS2LeagueActivity.Tests
{
    internal static class PayloadContractTests
    {
        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Player race payload matches Cafe24 contract", PlayerRacePayloadMatches);
            yield return new TestCase("Player Time Attack valid and invalid payloads match Cafe24 contract", PlayerTimeAttackPayloadsMatch);
            yield return new TestCase("Activity idempotency keys are stable and bounded", IdempotencyKeysAreStable);
        }

        private static void PlayerRacePayloadMatches()
        {
            ActivityRecord record = PlayerRecord(ActivityType.Race, "player-race-contract-0001");
            record.PersonalRaceSummary = new PersonalRaceSummary
            {
                FinishPosition = 4,
                FieldSize = 20,
                CompletedLaps = 12,
                BestLapMilliseconds = 92345,
                ResultState = "FINISHED"
            };

            AssertEx.True(PlayerActivityUploadPayloadBuilder.TryBuild(record, out byte[] payload, out string reason), reason);
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            AssertEx.Equal("ams2-player-activity-v1", root.GetProperty("schema").GetString());
            AssertEx.Equal("RACE", root.GetProperty("activityType").GetString());
            AssertEx.Equal("GENERAL", root.GetProperty("recordScope").GetString());
            AssertEx.Equal(record.SessionFingerprint, root.GetProperty("sessionId").GetString());
            AssertEx.Equal(4, root.GetProperty("personalResult").GetProperty("position").GetInt32());
            AssertEx.False(root.TryGetProperty("laps", out _));
            AssertNoAuthorityClaims(root);
        }

        private static void PlayerTimeAttackPayloadsMatch()
        {
            ActivityRecord valid = PlayerRecord(ActivityType.TimeAttack, "player-ta-valid-contract-0001");
            valid.TimeAttackLap = new TimeAttackLapRecord
            {
                LapUid = "player-ta-lap-valid-0001",
                LapOrdinal = 3,
                CompletedAtUtc = valid.EndedAtUtc,
                LapTimeMilliseconds = 90123,
                Sector1Milliseconds = 30000,
                Sector2Milliseconds = null,
                Sector3Milliseconds = 30123,
                IsValid = true,
                ClientPersonalBest = true,
                ClientSessionBest = true
            };
            AssertEx.True(PlayerActivityUploadPayloadBuilder.TryBuild(valid, out byte[] validPayload, out string validReason), validReason);
            using (JsonDocument document = JsonDocument.Parse(validPayload))
            {
                JsonElement root = document.RootElement;
                JsonElement lap = root.GetProperty("laps")[0];
                AssertEx.Equal("TIME_ATTACK", root.GetProperty("activityType").GetString());
                AssertEx.True(lap.GetProperty("valid").GetBoolean());
                AssertEx.False(lap.TryGetProperty("invalidReason", out _));
                AssertEx.False(lap.TryGetProperty("sector2Seconds", out _), "Unsupported sector value must remain absent/null.");
                AssertEx.Equal(90.123, lap.GetProperty("lapTimeSeconds").GetDouble());
                AssertEx.False(root.TryGetProperty("personalResult", out _));
                AssertNoAuthorityClaims(root);
            }

            ActivityRecord invalid = PlayerRecord(ActivityType.TimeAttack, "player-ta-invalid-contract-0001");
            invalid.TimeAttackLap = new TimeAttackLapRecord
            {
                LapUid = "player-ta-lap-invalid-0001",
                LapOrdinal = 4,
                CompletedAtUtc = invalid.EndedAtUtc,
                LapTimeMilliseconds = 88000,
                IsValid = false,
                InvalidReason = "AMS2_LAP_INVALIDATED"
            };
            AssertEx.True(PlayerActivityUploadPayloadBuilder.TryBuild(invalid, out byte[] invalidPayload, out string invalidReason), invalidReason);
            using (JsonDocument document = JsonDocument.Parse(invalidPayload))
            {
                JsonElement lap = document.RootElement.GetProperty("laps")[0];
                AssertEx.False(lap.GetProperty("valid").GetBoolean());
                AssertEx.Equal("AMS2_LAP_INVALIDATED", lap.GetProperty("invalidReason").GetString());
                AssertEx.False(lap.GetProperty("personalBestHint").GetBoolean());
            }
        }

        private static void IdempotencyKeysAreStable()
        {
            ActivityRecord player = PlayerRecord(ActivityType.Race, new string('x', 128));
            string firstPlayer = PlayerActivityUploadPayloadBuilder.CreateIdempotencyKey(player);
            string secondPlayer = PlayerActivityUploadPayloadBuilder.CreateIdempotencyKey(player);
            AssertEx.Equal(firstPlayer, secondPlayer);
            AssertEx.True(Regex.IsMatch(firstPlayer, "^[A-Za-z0-9._:-]{8,128}$"));
            AssertEx.True(firstPlayer.Length <= 128);

            player.ActivityId = "player-contract-activity-0002";
            AssertEx.NotEqual(firstPlayer, PlayerActivityUploadPayloadBuilder.CreateIdempotencyKey(player));
        }

        private static ActivityRecord PlayerRecord(ActivityType type, string activityId)
            => new ActivityRecord
            {
                ActivityId = activityId,
                ActivityType = type,
                RecordScopeHint = ActivityRecordScope.General,
                Authority = ActivityAuthority.PlayerPersonal,
                CompletionStatus = ActivityCompletionStatus.Finished,
                SessionFingerprint = "contract-session-fingerprint-0001",
                StartedAtUtc = new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero),
                EndedAtUtc = new DateTimeOffset(2026, 9, 1, 1, 5, 0, TimeSpan.Zero),
                Track = "Bathurst",
                Layout = "2020",
                Vehicle = "GT3 Contract",
                VehicleClass = "GT3",
                Identity = new ActivityIdentitySnapshot { ObservedAms2Name = "Fixture Driver" },
                ConfiguredSettings = new ConfiguredSessionSettings(),
                ObservedConditions = new ObservedSessionConditions
                {
                    Observed = true,
                    SessionType = type == ActivityType.Race ? "RACE" : "TIME_ATTACK",
                    RaceMode = type == ActivityType.Race ? "MULTIPLAYER" : "SINGLE_PLAYER"
                },
                Evidence = new ActivitySourceEvidence
                {
                    EvidenceSha256 = new string('a', 64),
                    Ams2Build = 3398,
                    CaptureVersion = "phase1d3-contract-v1"
                },
                ClientVersion = "0.1.1"
            };

        private static void AssertNoAuthorityClaims(JsonElement root)
        {
            foreach (string forbidden in new[] { "driverId", "driverPublicId", "userId", "participants", "official", "approved", "approvalState", "classification" })
            {
                AssertEx.False(root.TryGetProperty(forbidden, out _), "Forbidden Player authority field was emitted: " + forbidden);
            }
        }

    }
}
