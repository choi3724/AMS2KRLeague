using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.SessionWitness;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueActivity.Tests
{
    internal static class SessionWitnessTests
    {
        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Single witness is retained and uploadable", SingleWitnessIsRetained);
            yield return new TestCase("Three clients share session fingerprint and keep witnesses", ThreeClientsGroupWithoutDeduplication);
            yield return new TestCase("Race restart creates separate immutable attempts", RestartCreatesSeparateAttempts);
            yield return new TestCase("Mid-session join is classified without rejection", MidSessionCompletenessIsPreserved);
            yield return new TestCase("Mid-session witness matches a full multi-stage session", MidSessionMatchesFullSessionFingerprint);
            yield return new TestCase("Witness timeline is event driven and bounded", TimelineIsEventDrivenAndBounded);
            yield return new TestCase("Witness survives offline queue restart", WitnessSurvivesOfflineQueueRestart);
            yield return new TestCase("Witness store is immutable and quarantines conflict", WitnessStoreIsImmutable);
            yield return new TestCase("Legacy witness payload omits archive identity", LegacyPayloadOmitsArchiveIdentity);
            yield return new TestCase("Witness and all five telemetry streams share archive identity", ArchiveIdentityJoinsWitnessAndAllStreams);
            yield return new TestCase("Witness restart advances only the archive attempt", ArchiveRestartAdvancesAttempt);
            yield return new TestCase("Witness rejects archive identity changes during capture", ArchiveIdentityChangeFailsClosed);
        }

        private static void SingleWitnessIsRetained()
        {
            SessionWitnessRecord witness = CaptureFinished("install-single", TimeSpan.Zero);
            byte[] payload = SessionWitnessUploadPayloadBuilder.Build(witness);

            AssertEx.True(witness.WitnessId.StartsWith("witness-", StringComparison.Ordinal));
            AssertEx.Equal(SessionWitnessCompleteness.FullSession, witness.CaptureCompleteness);
            AssertEx.Equal(2, witness.Session.RaceResult?.Participants.Count ?? 0);
            AssertEx.True(payload.Length > 0);
            using JsonDocument document = JsonDocument.Parse(payload);
            AssertEx.Equal("ams2-session-witness-v1", document.RootElement.GetProperty("schema").GetString());
            AssertEx.Equal("FULL_SESSION", document.RootElement.GetProperty("captureCompleteness").GetString());
            AssertEx.Equal("UNCLASSIFIED", document.RootElement.GetProperty("activity").GetProperty("recordScopeHint").GetString());
        }

        private static void ThreeClientsGroupWithoutDeduplication()
        {
            SessionWitnessRecord[] witnesses = Enumerable.Range(1, 3)
                .Select(index => CaptureFinished("install-client-" + index, TimeSpan.FromSeconds((index - 1) * 20)))
                .ToArray();

            AssertEx.Equal(1, witnesses.Select(value => value.SessionFingerprint).Distinct(StringComparer.Ordinal).Count());
            AssertEx.Equal(3, witnesses.Select(value => value.WitnessId).Distinct(StringComparer.Ordinal).Count());
            AssertEx.Equal(3, witnesses.Select(value => value.SourceClientId).Distinct(StringComparer.Ordinal).Count());
            AssertEx.Equal(1, witnesses.Select(value => value.RosterSignature).Distinct(StringComparer.Ordinal).Count());
        }

        private static void RestartCreatesSeparateAttempts()
        {
            var engine = new SessionWitnessCaptureEngine("install-restart", "0.2.1");
            DateTimeOffset start = FixedTime();
            engine.Observe(Snapshot(start, 0, RaceState.Racing, 2, 1));
            SessionWitnessRecord first = Required(engine.Observe(Snapshot(start.AddSeconds(20), 0, RaceState.NotStarted, 0, 1)).FinalizedWitness);
            engine.Observe(Snapshot(start.AddSeconds(21), 1, RaceState.NotStarted, 0, 1));
            engine.Observe(Snapshot(start.AddSeconds(25), 2, RaceState.Finished, 2, 2));
            SessionWitnessRecord second = Required(engine.Close(start.AddSeconds(27), "TEST_END").FinalizedWitness);

            AssertEx.NotEqual(first.WitnessId, second.WitnessId);
            AssertEx.Equal(first.SessionFingerprint, second.SessionFingerprint);
            AssertEx.Equal("RESTARTED", first.Session.AttemptStatus);
            AssertEx.Equal(2, second.Session.Activity?.AttemptNumber ?? 0);
        }

        private static void MidSessionCompletenessIsPreserved()
        {
            var engine = new SessionWitnessCaptureEngine("install-mid", "0.2.1");
            DateTimeOffset start = FixedTime();
            engine.Observe(Snapshot(start, 0, RaceState.Racing, 5, 300));
            engine.Observe(Snapshot(start.AddSeconds(5), 1, RaceState.Racing, 5, 305));
            SessionWitnessRecord witness = Required(engine.Close(start.AddSeconds(6), "CLIENT_EXIT").FinalizedWitness);

            AssertEx.Equal(SessionWitnessCompleteness.MidSession, witness.CaptureCompleteness);
            AssertEx.NotNull(witness.Session.RaceResult);
            AssertEx.True(witness.QualityScore > 0);
        }

        private static void MidSessionMatchesFullSessionFingerprint()
        {
            DateTimeOffset start = FixedTime();
            var fullEngine = new SessionWitnessCaptureEngine("install-full-stages", "0.2.1");
            fullEngine.Observe(SessionSnapshot(start, 0, SessionState.Qualify, RaceState.Racing, 0, 0, 15));
            fullEngine.Observe(SessionSnapshot(start.AddMinutes(15), 1, SessionState.Race, RaceState.NotStarted, 0, 0, 40));
            fullEngine.Observe(SessionSnapshot(start.AddMinutes(15).AddSeconds(10), 2, SessionState.Race, RaceState.Finished, 3, 10, 40));
            SessionWitnessRecord full = Required(fullEngine.Close(start.AddMinutes(15).AddSeconds(12), "TEST_END").FinalizedWitness);

            var midEngine = new SessionWitnessCaptureEngine("install-mid-stages", "0.2.1");
            midEngine.Observe(SessionSnapshot(start.AddMinutes(20), 0, SessionState.Race, RaceState.Racing, 2, 300, 40));
            // Cross the recorder's bounded 30-second evidence interval so this is
            // genuinely a mid-session observation, not an end-only two-snapshot capture.
            midEngine.Observe(SessionSnapshot(start.AddMinutes(20).AddSeconds(31), 1, SessionState.Race, RaceState.Racing, 3, 331, 40));
            midEngine.Observe(SessionSnapshot(start.AddMinutes(20).AddSeconds(40), 2, SessionState.Race, RaceState.Finished, 4, 340, 40));
            SessionWitnessRecord mid = Required(midEngine.Close(start.AddMinutes(20).AddSeconds(41), "TEST_END").FinalizedWitness);

            AssertEx.Equal(full.SessionFingerprint, mid.SessionFingerprint);
            AssertEx.Equal(SessionWitnessCompleteness.MidSession, mid.CaptureCompleteness);
        }

        private static void TimelineIsEventDrivenAndBounded()
        {
            var engine = new SessionWitnessCaptureEngine("install-bandwidth", "0.2.1");
            DateTimeOffset start = FixedTime();
            const int frames = 3600;
            for (int index = 0; index < frames; index++)
            {
                engine.Observe(Snapshot(start.AddMilliseconds(index * 100), (uint)index, RaceState.Racing, 1, index / 10.0f));
            }
            SessionWitnessRecord witness = Required(engine.Close(start.AddMinutes(6), "TEST_END").FinalizedWitness);
            byte[] payload = SessionWitnessUploadPayloadBuilder.Build(witness);

            AssertEx.True(witness.Events.Count < 20, "Stable telemetry unexpectedly produced " + witness.Events.Count + " timeline events.");
            AssertEx.True(witness.Weather.Count <= 7, "Weather sampling was not bounded.");
            AssertEx.True(payload.Length < 512 * 1024, "Witness payload exceeded 512 KiB: " + payload.Length + ".");
            AssertEx.True(witness.Events.Count < frames / 100, "Witness appears to be streaming per-frame state.");
        }

        private static void WitnessSurvivesOfflineQueueRestart()
        {
            using var scope = new TemporaryDirectory("witness-queue-restart");
            SessionWitnessRecord witness = CaptureFinished("install-offline", TimeSpan.Zero);
            byte[] payload = SessionWitnessUploadPayloadBuilder.Build(witness);
            var clock = new MutableClock(FixedTime());
            var queue = new ActivityUploadQueue(scope.Root, new ActivityUploadQueueOptions(), clock);
            queue.Enqueue(
                witness.WitnessId,
                "v1/session/witness",
                SessionWitnessUploadPayloadBuilder.CreateIdempotencyKey(witness),
                payload);

            var restarted = new ActivityUploadQueue(scope.Root, new ActivityUploadQueueOptions(), clock);
            ActivityUploadItem item = AssertEx.Single(restarted.GetDueBatch());
            AssertEx.Equal("v1/session/witness", item.Metadata.Endpoint);
            AssertEx.Equal(witness.WitnessId, item.Metadata.ActivityId);
            AssertEx.Equal(SessionWitnessUploadPayloadBuilder.Sha256Hex(payload), item.Metadata.BodySha256);
        }

        private static void WitnessStoreIsImmutable()
        {
            using var scope = new TemporaryDirectory("witness-store");
            SessionWitnessRecord witness = CaptureFinished("install-store", TimeSpan.Zero);
            byte[] payload = SessionWitnessUploadPayloadBuilder.Build(witness);
            var store = new SessionWitnessStore(scope.Root);

            SessionWitnessStoreOutcome stored = store.Commit(witness, payload);
            SessionWitnessStoreOutcome duplicate = store.Commit(witness, payload);
            byte[] conflictPayload = payload.Concat(new byte[] { (byte)' ' }).ToArray();
            SessionWitnessStoreOutcome conflict = store.Commit(witness, conflictPayload);

            AssertEx.Equal(SessionWitnessStoreDisposition.Stored, stored.Disposition);
            AssertEx.Equal(SessionWitnessStoreDisposition.Duplicate, duplicate.Disposition);
            AssertEx.Equal(SessionWitnessStoreDisposition.ConflictQuarantined, conflict.Disposition);
            AssertEx.True(File.Exists(Path.Combine(stored.WitnessPath, "source-evidence.json.gz")));
            AssertEx.True(File.Exists(Path.Combine(stored.WitnessPath, "upload-payload.json")));
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stored.WitnessPath, "manifest.json")));
            AssertEx.Equal("FULL_SESSION", manifest.RootElement.GetProperty("captureCompleteness").GetString());
        }

        private static void LegacyPayloadOmitsArchiveIdentity()
        {
            SessionWitnessRecord witness = CaptureFinished("install-legacy-shape", TimeSpan.Zero);
            byte[] payload = SessionWitnessUploadPayloadBuilder.Build(witness);
            using JsonDocument document = JsonDocument.Parse(payload);

            AssertEx.False(document.RootElement.TryGetProperty("captureSessionId", out _));
            AssertEx.False(document.RootElement.TryGetProperty("attemptId", out _));
            AssertEx.False(document.RootElement.TryGetProperty("attemptNumber", out _));
            string expectedKey = "witness:" + SessionWitnessUploadPayloadBuilder.Sha256Hex(
                System.Text.Encoding.UTF8.GetBytes(witness.WitnessId));
            AssertEx.Equal(expectedKey, SessionWitnessUploadPayloadBuilder.CreateIdempotencyKey(witness));

            using var scope = new TemporaryDirectory("witness-legacy-storage-shape");
            SessionWitnessStoreOutcome stored = new SessionWitnessStore(scope.Root).Commit(witness, payload);
            AssertEx.Equal(witness.WitnessId, Path.GetFileName(stored.WitnessPath));
        }

        private static void ArchiveIdentityJoinsWitnessAndAllStreams()
        {
            TelemetryArchiveIdentity identity = FixedArchiveIdentity("join", 1);
            var engine = new SessionWitnessCaptureEngine("install-archive-join", "0.2.2");
            engine.BeginArchiveIdentity(identity);
            // Repeating the exact binding is deliberately idempotent.
            engine.BeginArchiveIdentity(FixedArchiveIdentity("join", 1));
            DateTimeOffset start = FixedTime();
            engine.Observe(Snapshot(start, 0, RaceState.NotStarted, 0, 0));
            engine.Observe(Snapshot(start.AddSeconds(2), 1, RaceState.Racing, 1, 2));
            SessionWitnessRecord witness = Required(
                engine.Close(start.AddSeconds(3), "TEST_END").FinalizedWitness);

            AssertArchiveIdentity(identity, witness);
            foreach (TelemetryStreamType stream in Enum.GetValues<TelemetryStreamType>())
            {
                var chunk = new TelemetryChunkEnvelope
                {
                    StreamType = stream,
                    SessionId = identity.SessionId,
                    SessionFingerprint = identity.SessionFingerprint,
                    WitnessId = identity.WitnessId,
                    AttemptId = identity.AttemptId,
                    AttemptNumber = identity.AttemptNumber
                };
                AssertEx.Equal(witness.CaptureSessionId, chunk.SessionId);
                AssertEx.Equal(witness.SessionFingerprint, chunk.SessionFingerprint);
                AssertEx.Equal(witness.WitnessId, chunk.WitnessId);
                AssertEx.Equal(witness.AttemptId, chunk.AttemptId);
                AssertEx.Equal(witness.AttemptNumber, (int?)chunk.AttemptNumber);
            }

            using JsonDocument document = JsonDocument.Parse(SessionWitnessUploadPayloadBuilder.Build(witness));
            AssertEx.Equal(identity.SessionId, document.RootElement.GetProperty("captureSessionId").GetString());
            AssertEx.Equal(identity.AttemptId, document.RootElement.GetProperty("attemptId").GetString());
            AssertEx.Equal(identity.AttemptNumber, document.RootElement.GetProperty("attemptNumber").GetInt32());
            AssertEx.Null(engine.CurrentArchiveIdentity);
        }

        private static void ArchiveRestartAdvancesAttempt()
        {
            TelemetryArchiveIdentity firstIdentity = FixedArchiveIdentity("restart", 1);
            var engine = new SessionWitnessCaptureEngine("install-archive-restart", "0.2.2");
            engine.BeginArchiveIdentity(firstIdentity);
            DateTimeOffset start = FixedTime();
            engine.Observe(Snapshot(start, 0, RaceState.Racing, 2, 1));
            SessionWitnessRecord first = Required(
                engine.Observe(Snapshot(start.AddSeconds(20), 0, RaceState.NotStarted, 0, 1)).FinalizedWitness);

            TelemetryArchiveIdentity secondIdentity = engine.CurrentArchiveIdentity
                ?? throw new InvalidOperationException("Restart did not reserve the next archive attempt.");
            AssertArchiveIdentity(firstIdentity, first);
            AssertEx.Equal(firstIdentity.SessionId, secondIdentity.SessionId);
            AssertEx.Equal(firstIdentity.SessionFingerprint, secondIdentity.SessionFingerprint);
            AssertEx.Equal(firstIdentity.WitnessId, secondIdentity.WitnessId);
            AssertEx.NotEqual(firstIdentity.AttemptId, secondIdentity.AttemptId);
            AssertEx.Equal(2, secondIdentity.AttemptNumber);

            engine.Observe(Snapshot(start.AddSeconds(21), 1, RaceState.NotStarted, 0, 1));
            engine.Observe(Snapshot(start.AddSeconds(25), 2, RaceState.Finished, 2, 2));
            SessionWitnessRecord second = Required(
                engine.Close(start.AddSeconds(27), "TEST_END").FinalizedWitness);
            AssertArchiveIdentity(secondIdentity, second);
            AssertEx.Null(engine.CurrentArchiveIdentity);

            byte[] firstPayload = SessionWitnessUploadPayloadBuilder.Build(first);
            byte[] secondPayload = SessionWitnessUploadPayloadBuilder.Build(second);
            AssertEx.NotEqual(
                SessionWitnessUploadPayloadBuilder.CreateIdempotencyKey(first),
                SessionWitnessUploadPayloadBuilder.CreateIdempotencyKey(second));
            using var scope = new TemporaryDirectory("witness-archive-restart-storage");
            var store = new SessionWitnessStore(scope.Root);
            SessionWitnessStoreOutcome firstStored = store.Commit(first, firstPayload);
            SessionWitnessStoreOutcome secondStored = store.Commit(second, secondPayload);
            AssertEx.Equal(SessionWitnessStoreDisposition.Stored, firstStored.Disposition);
            AssertEx.Equal(SessionWitnessStoreDisposition.Stored, secondStored.Disposition);
            AssertEx.NotEqual(firstStored.WitnessPath, secondStored.WitnessPath);
        }

        private static void ArchiveIdentityChangeFailsClosed()
        {
            TelemetryArchiveIdentity identity = FixedArchiveIdentity("locked", 1);
            var engine = new SessionWitnessCaptureEngine("install-archive-locked", "0.2.2");
            engine.BeginArchiveIdentity(identity);
            engine.Observe(Snapshot(FixedTime(), 0, RaceState.Racing, 1, 1));

            bool rejected = false;
            try
            {
                engine.BeginArchiveIdentity(FixedArchiveIdentity("different", 1));
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            AssertEx.True(rejected, "A mid-session archive identity change was accepted.");
            TelemetryArchiveIdentity current = engine.CurrentArchiveIdentity
                ?? throw new InvalidOperationException("The original archive identity was lost.");
            AssertEx.Equal(identity.AttemptId, current.AttemptId);
        }

        private static SessionWitnessRecord CaptureFinished(string installationId, TimeSpan clockOffset)
        {
            var engine = new SessionWitnessCaptureEngine(installationId, "0.2.1");
            DateTimeOffset start = FixedTime().Add(clockOffset);
            engine.Observe(Snapshot(start, 0, RaceState.NotStarted, 0, 0));
            engine.Observe(Snapshot(start.AddSeconds(2), 1, RaceState.Racing, 1, 2));
            engine.Observe(Snapshot(start.AddSeconds(10), 2, RaceState.Finished, 3, 10));
            engine.Observe(Snapshot(start.AddSeconds(12), 3, RaceState.Finished, 3, 12));
            return Required(engine.Close(start.AddSeconds(13), "SESSION_RESET").FinalizedWitness);
        }

        private static TelemetrySnapshot Snapshot(
            DateTimeOffset at,
            uint sequence,
            RaceState raceState,
            uint lapsCompleted,
            float currentTime)
            => SessionSnapshot(at, sequence, SessionState.Race, raceState, lapsCompleted, currentTime, 40);

        private static TelemetrySnapshot SessionSnapshot(
            DateTimeOffset at,
            uint sequence,
            SessionState sessionState,
            RaceState raceState,
            uint lapsCompleted,
            float currentTime,
            float sessionDuration)
        {
            var fixture = new ActivityFixtureSnapshot
            {
                CapturedAtUtc = at,
                GameState = raceState == RaceState.NotStarted ? "InGameMenuTimeTicking" : "InGamePlaying",
                SessionState = sessionState.ToString(),
                RaceState = raceState.ToString(),
                CurrentTime = currentTime,
                SessionDuration = sessionDuration,
                Track = "Monza",
                Layout = "GP",
                Participants = new List<ActivityFixtureParticipant>
                {
                    new ActivityFixtureParticipant
                    {
                        Name = "Driver Alpha", Position = 1, LapsCompleted = lapsCompleted,
                        CurrentLap = lapsCompleted + 1, RaceState = raceState.ToString(),
                        Vehicle = "GT3 Alpha", VehicleClass = "GT3"
                    },
                    new ActivityFixtureParticipant
                    {
                        Name = "Driver Beta", Position = 2, LapsCompleted = lapsCompleted,
                        CurrentLap = lapsCompleted + 1, RaceState = raceState.ToString(),
                        Vehicle = "GT3 Beta", VehicleClass = "GT3"
                    }
                }
            };
            return fixture.ToSnapshot(1000 + (sequence * 2));
        }

        private static SessionWitnessRecord Required(SessionWitnessRecord? witness)
            => witness ?? throw new InvalidOperationException("A finalized session witness was expected.");

        private static TelemetryArchiveIdentity FixedArchiveIdentity(string suffix, int attemptNumber)
            => new TelemetryArchiveIdentity
            {
                SessionId = "capture-session-" + suffix,
                SessionFingerprint = "session-fingerprint-" + suffix,
                WitnessId = "witness-shared-" + suffix,
                AttemptId = "attempt-" + attemptNumber + "-" + suffix,
                AttemptNumber = attemptNumber
            };

        private static void AssertArchiveIdentity(
            TelemetryArchiveIdentity expected,
            SessionWitnessRecord actual)
        {
            AssertEx.Equal(expected.SessionId, actual.CaptureSessionId);
            AssertEx.Equal(expected.SessionFingerprint, actual.SessionFingerprint);
            AssertEx.Equal(expected.WitnessId, actual.WitnessId);
            AssertEx.Equal(expected.AttemptId, actual.AttemptId);
            AssertEx.Equal((int?)expected.AttemptNumber, actual.AttemptNumber);
        }

        private static DateTimeOffset FixedTime()
            => new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    }
}
