using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using AMS2LeagueClient.Core.CompactTelemetry;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.SessionWitness;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueActivity.Tests
{
    internal static class FutureTelemetryRuntimeAdapterTests
    {
        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Future adapter requires viewed/root consistency for private driver data", PrivateDriverGate);
            yield return new TestCase("Future adapter records discrete facts outside UI suppression", DiscreteFacts);
            yield return new TestCase("Future adapter emits bounded raw incident candidates without blame", IncidentCandidates);
            yield return new TestCase("Future metadata tracks observed stream availability", StreamCapabilityMetadata);
            yield return new TestCase("Future compact rows append durable raw coverage without breaking prefixes", DurableRawCoverageRows);
            yield return new TestCase("Future runtime exposes one common identity and commits off hot path", RuntimeIdentityAndDurability);
            yield return new TestCase("Shipping compact runtime removes high-rate JSON and keeps private upload locked", CompactRuntimePersistsA2ctOnlyForHighRate);
            yield return new TestCase("Shipping compact runtime durably emits loss ledger and finalize acknowledgement", CompactRuntimeEmitsAttemptIntegrity);
            yield return new TestCase("Shipping compact runtime marks cadence loss PARTIAL on the wire", CompactRuntimeIntegrityMarksCadenceLossPartial);
            yield return new TestCase("Compact integrity commit failure rolls the durable ledger back to PARTIAL", CompactIntegrityFailureIsPartial);
            yield return new TestCase("Compact loss-ledger conflict prevents finalize artifact creation", CompactLossConflictStopsFinalize);
            yield return new TestCase("Shipping compact runtime normalizes AMS2 negative time sentinels", CompactRuntimeNormalizesNegativeTimeSentinels);
            yield return new TestCase("Shipping compact runtime normalizes unavailable DriverFast domains", CompactRuntimeNormalizesUnavailableDriverFastDomains);
            yield return new TestCase("Future runtime identity reaches witness and all five persisted streams", RuntimeWitnessAndFiveStreams);
            yield return new TestCase("Future runtime fingerprint joins independent witnesses", IndependentWitnessFingerprintsJoin);
            yield return new TestCase("Future runtime separates restart attempts while retaining join identity", RestartAttemptIdentity);
            yield return new TestCase("Future runtime close uses durable acknowledgement before COMPLETE", CloseRequiresDurableAcknowledgement);
            yield return new TestCase("Future runtime propagates deterministic outer queue loss and isolates attempts", OuterQueueLossIsAttemptScoped);
            yield return new TestCase("Future runtime worker exception cannot produce COMPLETE", WorkerExceptionIsPartial);
            yield return new TestCase("Future runtime disk and finalize failure cannot produce COMPLETE", DiskFinalizeFailureIsPartial);
        }

        private static void PrivateDriverGate()
        {
            var adapter = new FutureTelemetrySnapshotAdapter("0.2.2-test");
            TelemetrySnapshot valid = Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, vehicle: "GT3 Car", vehicleClass: "GT3") },
                vehicle: Vehicle(speed: 62.5f, collisionMagnitude: 0));
            FutureTelemetryCaptureBatch first = adapter.Observe(valid, Stamp(0));
            AssertEx.NotNull(first.Frame);
            AssertEx.Equal(1, first.Frame!.Participants.Count);
            AssertEx.NotNull(first.Frame.LocalDriver);
            AssertEx.True(first.Frame.LocalDriver!.LocalParticipantResolved);
            AssertEx.Equal(first.Frame.LocalDriver.DriverRef, first.Frame.LocalDriver.SourceParticipantRef!.Value);
            AssertEx.Equal(62.5, first.Frame.LocalDriver.SpeedMetersPerSecond!.Value);
            AssertEx.True(Math.Abs(0.6 - first.Frame.LocalDriver.FuelLevelRatio!.Value) < 0.0001);
            AssertEx.True(Math.Abs(70.0 - first.Frame.LocalDriver.FuelCapacityLiters!.Value) < 0.0001);
            AssertEx.True(Math.Abs(42.0 - first.Frame.LocalDriver.FuelLiters!.Value) < 0.0001);
            AssertEx.Equal(TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS, VisibilityFor(TelemetryStreamType.DRIVER_TELEMETRY));

            var mismatchAdapter = new FutureTelemetrySnapshotAdapter("0.2.2-test");
            TelemetrySnapshot mismatch = Snapshot(
                At(1),
                new[] { Participant(0, "LOCAL", 1, vehicle: "Different Car", vehicleClass: "GT3") },
                vehicle: Vehicle(speed: 70, collisionMagnitude: 0),
                rootCar: "GT3 Car");
            FutureTelemetryCaptureBatch mismatchBatch = mismatchAdapter.Observe(mismatch, Stamp(0));
            AssertEx.NotNull(mismatchBatch.Frame);
            AssertEx.Null(mismatchBatch.Frame!.LocalDriver, "A root/viewed vehicle mismatch must not leak private controls.");
            AssertEx.Equal(1, mismatchBatch.Frame.Participants.Count);

            var pausedAdapter = new FutureTelemetrySnapshotAdapter("0.2.2-test");
            TelemetrySnapshot paused = Snapshot(
                At(2),
                new[] { Participant(0, "LOCAL", 1, vehicle: "GT3 Car", vehicleClass: "GT3") },
                vehicle: Vehicle(speed: 20, collisionMagnitude: 0),
                gameState: GameState.InGamePaused);
            AssertEx.Null(pausedAdapter.Observe(paused, Stamp(0)).Frame!.LocalDriver);
        }

        private static void DiscreteFacts()
        {
            var adapter = new FutureTelemetrySnapshotAdapter("0.2.2-test");
            TelemetrySnapshot baseline = Snapshot(
                At(0),
                new[]
                {
                    Participant(0, "A", 1, laps: 1, bestLap: 100, worldX: 10),
                    Participant(1, "B", 2, laps: 1, bestLap: 101, worldX: 20)
                },
                vehicle: null);
            FutureTelemetryCaptureBatch start = adapter.Observe(baseline, Stamp(0));
            AssertEx.True(start.StoryEvents.Any(value => value.EventType == "SESSION_START"));
            AssertEx.True(start.StoryEvents.Any(value => value.EventType == "RACE_START"));
            RaceStoryEventSample[] activeBaseline = start.StoryEvents
                .Where(value => value.EventType == "PARTICIPANT_ACTIVE_STATE")
                .ToArray();
            AssertEx.Equal(2, activeBaseline.Length);
            AssertEx.True(activeBaseline.All(value => value.ParticipantIsActiveRaw == true));
            AssertEx.True(activeBaseline.All(value => value.ParticipantRef.HasValue));

            TelemetrySnapshot changed = Snapshot(
                At(1),
                new[]
                {
                    Participant(0, "A", 2, laps: 2, bestLap: 98, lastLap: 98,
                        pitMode: PitMode.DrivingIntoPits, pitSchedule: PitSchedule.DriveThrough, worldX: 12),
                    Participant(1, "B", 1, laps: 1, bestLap: 101, worldX: 22)
                },
                vehicle: null,
                flag: FlagColour.DoubleYellow,
                yellow: YellowFlagState.Pending);
            FutureTelemetryCaptureBatch update = adapter.Observe(changed, Stamp(50));
            string[] types = update.StoryEvents.Select(value => value.EventType).ToArray();
            AssertEx.True(types.Contains("POSITION_CHANGE"));
            AssertEx.True(types.Contains("LEADER_CHANGE"));
            AssertEx.True(types.Contains("LAP_COMPLETE"));
            AssertEx.True(types.Contains("PERSONAL_BEST"));
            AssertEx.True(types.Contains("SESSION_FASTEST_LAP"));
            AssertEx.True(types.Contains("PIT_ENTRY"));
            AssertEx.True(types.Contains("DRIVE_THROUGH"));
            AssertEx.True(types.Contains("DOUBLE_YELLOW"));
            AssertEx.True(types.Contains("FULL_COURSE_YELLOW"));
            AssertEx.True(update.StoryEvents.All(value => value.CapturedAtUtc != default));
            AssertEx.True(update.StoryEvents.All(value => value.SessionElapsedMs == 50));

            FutureTelemetryCaptureBatch fcyEnded = adapter.Observe(
                Snapshot(At(2), changed.Participants.ToArray(), vehicle: null, yellow: YellowFlagState.None),
                Stamp(100));
            RaceStoryEventSample endFact = AssertEx.Single(
                fcyEnded.StoryEvents.Where(value => value.EventType == "FULL_COURSE_YELLOW_END"));
            AssertEx.Equal((int?)YellowFlagState.None, endFact.YellowFlagStateRaw);
            AssertEx.Null(endFact.ResultStateRaw, "A global race state must not be fabricated as participant result state.");
        }

        private static void IncidentCandidates()
        {
            var adapter = new FutureTelemetrySnapshotAdapter("0.2.2-test");
            ParticipantSnapshot[] participants =
            {
                Participant(0, "LOCAL", 1, worldX: 10, vehicle: "GT3 Car", vehicleClass: "GT3"),
                Participant(1, "OTHER", 2, worldX: 20, vehicle: "GT3 Car", vehicleClass: "GT3")
            };
            adapter.Observe(Snapshot(At(0), participants, Vehicle(50, 0)), Stamp(0));
            FutureTelemetryCaptureBatch collision = adapter.Observe(
                Snapshot(At(1), participants, Vehicle(45, 8, collisionIndex: 1)),
                Stamp(50));
            AssertEx.NotNull(collision.Frame!.IncidentCandidate);
            AssertEx.Equal("COLLISION_MAGNITUDE_CHANGE", collision.Frame.IncidentCandidate!.TriggerCode);
            AssertEx.Equal(2, collision.Frame.IncidentCandidate.RelatedParticipantRefs.Length);
            AssertEx.False(collision.Frame.IncidentCandidate.TriggerCode.Contains("FAULT", StringComparison.OrdinalIgnoreCase));

            // The same persistent collision value is not repeatedly promoted.
            FutureTelemetryCaptureBatch duplicate = adapter.Observe(
                Snapshot(At(2), participants, Vehicle(44, 8, collisionIndex: 1)),
                Stamp(100));
            AssertEx.Null(duplicate.Frame!.IncidentCandidate);

            var disappearanceAdapter = new FutureTelemetrySnapshotAdapter("0.2.2-test");
            disappearanceAdapter.Observe(Snapshot(At(0), participants, null), Stamp(0));
            FutureTelemetryCaptureBatch disappeared = disappearanceAdapter.Observe(
                Snapshot(At(1), new[] { participants[0] }, null),
                Stamp(50));
            AssertEx.True(disappeared.Frame!.ParticipantDisappeared);
            AssertEx.NotNull(disappeared.Frame.IncidentCandidate);
            AssertEx.Equal("PARTICIPANT_DISAPPEARANCE", disappeared.Frame.IncidentCandidate!.TriggerCode);
            RaceStoryEventSample tombstone = AssertEx.Single(disappeared.StoryEvents.Where(value =>
                value.EventType == "PARTICIPANT_ACTIVE_STATE" && value.ParticipantIsActiveRaw == false));
            AssertEx.Equal(SharedMemoryLayout.MaxParticipants + 1, tombstone.ParticipantRef!.Value);
        }

        private static void StreamCapabilityMetadata()
        {
            var adapter = new FutureTelemetrySnapshotAdapter("0.2.2-test");
            ParticipantSnapshot[] participants =
            {
                Participant(0, "LOCAL", 1, worldX: 10, vehicle: "GT3 Car", vehicleClass: "GT3"),
                Participant(1, "OTHER", 2, worldX: 20, vehicle: "GT3 Car", vehicleClass: "GT3")
            };
            FutureTelemetryCaptureBatch first = adapter.Observe(
                Snapshot(At(0), participants, Vehicle(50, 0)),
                Stamp(0));
            AssertEx.NotNull(first.Metadata);
            AssertEx.Equal(true, first.Metadata!.Fields["raceStory"].BooleanValue!.Value);
            AssertEx.Equal(true, first.Metadata.Fields["replay"].BooleanValue!.Value);
            AssertEx.Equal(true, first.Metadata.Fields["driverTelemetry"].BooleanValue!.Value);
            AssertEx.Equal(false, first.Metadata.Fields["incidentHighRate"].BooleanValue!.Value);

            FutureTelemetryCaptureBatch collision = adapter.Observe(
                Snapshot(At(1), participants, Vehicle(45, 8, collisionIndex: 1)),
                Stamp(50));
            AssertEx.NotNull(collision.Frame!.IncidentCandidate);
            AssertEx.NotNull(collision.Metadata, "The first observed incident stream must emit structural metadata.");
            AssertEx.Equal(true, collision.Metadata!.Fields["incidentHighRate"].BooleanValue!.Value);
        }

        private static void DurableRawCoverageRows()
        {
            IReadOnlyList<string>[] catalogs =
            {
                TelemetryFieldCatalog.RaceStoryFields,
                TelemetryFieldCatalog.ParticipantReplayFields,
                TelemetryFieldCatalog.DriverTelemetryFields,
                TelemetryFieldCatalog.IncidentTraceFields
            };
            foreach (IReadOnlyList<string> fields in catalogs)
            {
                AssertEx.Equal(fields.Count, fields.Distinct(StringComparer.Ordinal).Count(), "Compact fields must be unique.");
            }
            AssertEx.Equal(23, TelemetryFieldCatalog.RaceStoryFields.Count);
            AssertEx.Equal(36, TelemetryFieldCatalog.ParticipantReplayFields.Count);
            AssertEx.Equal(222, TelemetryFieldCatalog.DriverTelemetryFields.Count);
            AssertEx.Equal(47, TelemetryFieldCatalog.IncidentTraceFields.Count);
            AssertEx.Equal("resultStateRaw", TelemetryFieldCatalog.RaceStoryFields[TelemetryFieldCatalog.RaceStoryBaseFieldCount - 1]);
            AssertEx.Equal("yellowFlagStateRaw", TelemetryFieldCatalog.RaceStoryFields[TelemetryFieldCatalog.RaceStoryBaseFieldCount]);
            AssertEx.Equal("participantIsActiveRaw", TelemetryFieldCatalog.RaceStoryFields[TelemetryFieldCatalog.RaceStoryBaseFieldCount + 1]);
            AssertEx.Equal("speedMetersPerSecond", TelemetryFieldCatalog.ParticipantReplayFields[TelemetryFieldCatalog.ParticipantReplayBaseFieldCount - 1]);
            AssertEx.Equal("lapsCompleted", TelemetryFieldCatalog.ParticipantReplayFields[TelemetryFieldCatalog.ParticipantReplayBaseFieldCount]);
            AssertEx.Equal("currentLapTimeMs", TelemetryFieldCatalog.DriverTelemetryFields[TelemetryFieldCatalog.DriverTelemetryBaseFieldCount - 1]);
            AssertEx.Equal("rootLapInvalidated", TelemetryFieldCatalog.DriverTelemetryFields[TelemetryFieldCatalog.DriverTelemetryBaseFieldCount]);
            AssertEx.Equal("speedMetersPerSecond", TelemetryFieldCatalog.IncidentTraceFields[TelemetryFieldCatalog.IncidentTraceBaseFieldCount - 1]);
            AssertEx.Equal("lapsCompleted", TelemetryFieldCatalog.IncidentTraceFields[TelemetryFieldCatalog.IncidentTraceBaseFieldCount]);

            string[] forbiddenPublicKeys = { "throttle", "brake", "clutch", "steering", "gear", "fuel", "tire", "tyre", "suspension" };
            foreach (string field in TelemetryFieldCatalog.ParticipantReplayFields.Concat(TelemetryFieldCatalog.IncidentTraceFields))
            {
                string lower = field.ToLowerInvariant();
                AssertEx.False(forbiddenPublicKeys.Any(lower.Contains), "Private key leaked to public schema: " + field);
            }

            using var temporary = new TemporaryDirectory("future-durable-raw-coverage");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-durable",
                "0.2.2-test",
                options: new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 1_000,
                    ReplayIntervalMs = 200,
                    DriverTelemetryIntervalMs = 50,
                    IncidentIntervalMs = 50,
                    IncidentPreRollMs = 100,
                    IncidentPostRollMs = 100
                },
                clockFactory: () => new TelemetrySessionClock(monotonic));
            ParticipantSnapshot local = ExtendedParticipant(0, "LOCAL", 1, nationality: 44, orientationX: 0.11f);
            ParticipantSnapshot opponent = ExtendedParticipant(1, "OTHER", 2, nationality: 55, orientationX: 0.22f);
            runtime.Observe(Snapshot(
                At(0),
                new[] { local, opponent },
                ExtendedVehicle(0, -1),
                trackLocation: "BathurstRaw",
                trackVariation: "RawLayout",
                translatedTrackLocation: "배서스트",
                translatedTrackVariation: "번역 레이아웃"));
            monotonic.Timestamp = 50;
            runtime.Observe(Snapshot(
                At(1),
                new[] { local, opponent },
                ExtendedVehicle(8, 1),
                flag: FlagColour.DoubleYellow,
                yellow: YellowFlagState.Pending,
                trackLocation: "BathurstRaw",
                trackVariation: "RawLayout",
                translatedTrackLocation: "배서스트",
                translatedTrackVariation: "번역 레이아웃"));
            monotonic.Timestamp = 100;
            runtime.Observe(Snapshot(
                At(2),
                new[] { local },
                ExtendedVehicle(8, 1),
                trackLocation: "BathurstRaw",
                trackVariation: "RawLayout",
                translatedTrackLocation: "배서스트",
                translatedTrackVariation: "번역 레이아웃"));
            runtime.GameDetached();
            runtime.Dispose();

            TelemetryChunkEnvelope[] chunks = Directory.GetFiles(temporary.Root, "*.json.gz", SearchOption.AllDirectories)
                .Select(ReadChunk)
                .ToArray();
            foreach (TelemetryChunkEnvelope chunk in chunks.Where(value => value.StreamType != TelemetryStreamType.SESSION_METADATA))
            {
                AssertEx.True(chunk.Data.Rows.All(row => row.Length == chunk.Data.Fields.Length),
                    chunk.StreamType + " compact row width mismatch.");
                AssertEx.Equal(chunk.Data.Fields.Length, chunk.Data.Fields.Distinct(StringComparer.Ordinal).Count());
            }

            TelemetryChunkEnvelope metadataChunk = chunks.First(value => value.StreamType == TelemetryStreamType.SESSION_METADATA);
            SessionMetadataSample metadata = metadataChunk.Data.Records!.First();
            AssertEx.Equal("BathurstRaw", metadata.Track);
            AssertEx.Equal("BathurstRaw", metadata.RawTrack);
            AssertEx.Equal("배서스트", metadata.TranslatedTrack);
            AssertEx.Equal(44, metadata.Participants.First(value => value.Slot == 0).NationalityRaw);
            AssertEx.True(metadata.Fields.ContainsKey("eventTimeRemainingRaw"));
            AssertEx.True(metadata.Fields.ContainsKey("windDirectionXRaw"));

            TelemetryChunkEnvelope story = chunks.First(value => value.StreamType == TelemetryStreamType.RACE_STORY);
            int activeField = Array.IndexOf(story.Data.Fields, "participantIsActiveRaw");
            double[] activeRawValues = story.Data.Rows
                .Where(row => row[activeField].HasValue)
                .Select(row => row[activeField]!.Value)
                .ToArray();
            AssertEx.True(activeRawValues.Contains(1));
            AssertEx.True(activeRawValues.Contains(0));

            TelemetryChunkEnvelope replay = chunks.First(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY);
            double?[] replayRow = replay.Data.Rows.First();
            AssertNear(7, Field(replay, replayRow, "lapsCompleted"));
            AssertNear(2, Field(replay, replayRow, "sectorRaw"));
            AssertNear(0.11, Field(replay, replayRow, "orientationRawX"));
            AssertNear(44, Field(replay, replayRow, "nationalityRaw"));

            TelemetryChunkEnvelope driver = chunks.First(value => value.StreamType == TelemetryStreamType.DRIVER_TELEMETRY);
            double?[] driverRow = driver.Data.Rows.Last();
            AssertNear(321.5, Field(driver, driverRow, "oilPressureKPa"));
            AssertNear(9.25, Field(driver, driverRow, "worldAccelerationRawZ"));
            AssertNear(27.75, Field(driver, driverRow, "tyreAirPressurePsiFrontLeft"));
            int compoundRef = (int)Field(driver, driverRow, "tyreCompoundFrontLeftRef");
            AssertEx.Equal("SOFT-0", driver.Data.Dictionaries["tyreCompounds"][compoundRef]);
            AssertEx.True(driverRow.Skip(TelemetryFieldCatalog.DriverTelemetryBaseFieldCount).All(value => value.HasValue),
                "Every parsed durable extension source should serialize in the complete test fixture.");

            TelemetryChunkEnvelope incident = chunks.First(value => value.StreamType == TelemetryStreamType.INCIDENT_TRACE);
            double?[] incidentRow = incident.Data.Rows.First(row =>
                Math.Abs(Field(incident, row, "collisionMagnitude") - 8) < 0.0001);
            AssertNear(7, Field(incident, incidentRow, "lapsCompleted"));
            AssertNear(1, Field(incident, incidentRow, "collisionOpponentSlotRaw"));
            AssertNear(8, Field(incident, incidentRow, "collisionMagnitude"));
            AssertNear((int)YellowFlagState.Pending, Field(incident, incidentRow, "yellowFlagStateRaw"));
        }

        private static double Field(TelemetryChunkEnvelope chunk, double?[] row, string field)
        {
            int index = Array.IndexOf(chunk.Data.Fields, field);
            if (index < 0 || index >= row.Length || !row[index].HasValue)
            {
                throw new InvalidOperationException("Missing compact value " + field + ".");
            }
            return row[index]!.Value;
        }

        private static void AssertNear(double expected, double actual)
            => AssertEx.True(Math.Abs(expected - actual) < 0.0001, "Expected " + expected + ", got " + actual + ".");

        private static void RuntimeIdentityAndDurability()
        {
            using var temporary = new TemporaryDirectory("future-runtime");
            var monotonic = new TestMonotonicClock();
            var identities = new List<TelemetryArchiveIdentity>();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-test",
                "0.2.2-test",
                options: new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 1_000,
                    InputChannelCapacity = 64,
                    ReplayIntervalMs = 200,
                    DriverTelemetryIntervalMs = 50,
                    IncidentIntervalMs = 50
                },
                clockFactory: () => new TelemetrySessionClock(monotonic));
            runtime.IdentityStarted += identity => identities.Add(identity);
            TelemetrySnapshot snapshot = Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 10, vehicle: "GT3 Car", vehicleClass: "GT3") },
                Vehicle(50, 0));

            AssertEx.True(runtime.Observe(snapshot));
            TelemetryArchiveIdentity? current = runtime.CurrentIdentity;
            AssertEx.NotNull(current);
            AssertEx.Equal(1, identities.Count);
            AssertEx.Equal(current!.WitnessId, identities[0].WitnessId);
            AssertEx.Equal(current.SessionId, identities[0].SessionId);
            AssertEx.Equal(current.AttemptId, identities[0].AttemptId);
            monotonic.Timestamp = 50;
            runtime.Observe(Snapshot(At(1), snapshot.Participants.ToArray(), Vehicle(51, 0)));
            runtime.GameDetached();
            runtime.Dispose();

            string[] chunks = Directory.GetFiles(temporary.Root, "*.json.gz", SearchOption.AllDirectories);
            AssertEx.True(chunks.Length >= 4, "Expected metadata, story, replay and private driver chunks.");
            TelemetryChunkEnvelope[] envelopes = chunks.Select(ReadChunk).ToArray();
            AssertEx.True(envelopes.All(value => value.SessionId == current.SessionId));
            AssertEx.True(envelopes.All(value => value.WitnessId == current.WitnessId));
            AssertEx.True(envelopes.All(value => value.AttemptId == current.AttemptId));
            AssertEx.True(envelopes.Any(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY
                && value.Visibility == TelemetryVisibility.PUBLIC_REPLAY));
            AssertEx.True(envelopes.Any(value => value.StreamType == TelemetryStreamType.DRIVER_TELEMETRY
                && value.Visibility == TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS));
            AssertEx.True(runtime.Counters.CommittedChunks >= 4);
        }

        private static void CompactRuntimePersistsA2ctOnlyForHighRate()
        {
            using var temporary = new TemporaryDirectory("future-runtime-compact-a2ct");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-compact",
                "0.2.2-test",
                options: new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 1_000,
                    ReplayIntervalMs = 200,
                    DriverTelemetryIntervalMs = 50,
                    IncidentIntervalMs = 50,
                    IncidentPreRollMs = 100,
                    IncidentPostRollMs = 100
                },
                clockFactory: () => new TelemetrySessionClock(monotonic),
                archiveFormat: TelemetryArchiveFormat.COMPACT_A2CT_V1);
            ParticipantSnapshot[] participants =
            {
                Participant(0, "LOCAL", 1, worldX: 10),
                Participant(1, "OTHER", 2, worldX: 18)
            };
            ViewedVehicleTelemetrySnapshot initialVehicle = Vehicle(50, 0);
            ViewedVehicleTelemetrySnapshot shiftedVehicle = Vehicle(48, 9, collisionIndex: 1);
            Set(shiftedVehicle, nameof(ViewedVehicleTelemetrySnapshot.Gear), 5);
            ViewedVehicleTelemetrySnapshot restoredVehicle = Vehicle(47, 9, collisionIndex: 1);
            AssertEx.True(runtime.Observe(Snapshot(At(0), participants, initialVehicle)));
            monotonic.Timestamp = 50;
            AssertEx.True(runtime.Observe(Snapshot(At(1), participants, shiftedVehicle)));
            monotonic.Timestamp = 100;
            AssertEx.True(runtime.Observe(Snapshot(At(2), participants, restoredVehicle)));
            runtime.GameDetached();
            runtime.Dispose();

            string[] compactPaths = Directory.GetFiles(temporary.Root, "*.a2ct.gz", SearchOption.AllDirectories);
            AssertEx.True(compactPaths.Length >= 6, "Expected compact session/story/replay/driver/incident families.");
            CompactTelemetryEnvelope[] compact = compactPaths.Select(path =>
            {
                using FileStream stream = File.OpenRead(path);
                return CompactTelemetryCodec.Decode(TelemetryChunkSerializer.Gunzip(stream));
            }).ToArray();
            AssertEx.True(compact.Any(value => value.Block.SchemaId == CompactTelemetrySchemaId.RaceEventV1));
            AssertEx.True(compact.Any(value => value.Block.SchemaId == CompactTelemetrySchemaId.ParticipantReplayV1));
            AssertEx.True(compact.Any(value => value.Block.SchemaId == CompactTelemetrySchemaId.DriverFastV1));
            AssertEx.True(
                compact.Any(value => value.Block.SchemaId == CompactTelemetrySchemaId.IncidentV1),
                "Missing incident schema. Persisted: " + string.Join(",", compact.Select(value => value.Block.SchemaId))
                    + " losses=" + string.Join(";", runtime.AttemptLossLedgers.SelectMany(value => value.Streams)
                        .Select(value => value.StreamType + ":worker=" + value.WorkerExceptions
                            + ",serialize=" + value.SerializationFailures + ",disk=" + value.DiskWriteFailures)));
            AssertEx.True(compact.Any(value => value.Strings.Any(entry =>
                entry.DictionaryId == CompactStringDictionaryId.EventType)));
            int gearOrdinal = TelemetryFieldCatalog.DriverTelemetryFields
                .Select((name, ordinal) => new { name, ordinal })
                .Single(value => value.name == "gearRaw")
                .ordinal;
            double[] gearTransitions = compact
                .Where(value => value.Block.SchemaId == CompactTelemetrySchemaId.DriverChangeV1)
                .SelectMany(value => value.Block.Samples)
                .Where(value => value.Values.Count >= 2
                    && value.Values[0].HasValue
                    && checked((int)value.Values[0]!.Value) == gearOrdinal
                    && value.Values[1].HasValue)
                .Select(value => value.Values[1]!.Value)
                .ToArray();
            AssertEx.True(gearTransitions.SequenceEqual(new[] { 4.0, 5.0, 4.0 }),
                "A short gear transition must not be lost between generic 20-second snapshots.");

            TelemetryChunkEnvelope[] json = Directory.GetFiles(temporary.Root, "*.json.gz", SearchOption.AllDirectories)
                .Select(ReadChunk)
                .ToArray();
            AssertEx.True(json.Length > 0, "Low-rate metadata compatibility records must remain durable.");
            AssertEx.True(json.All(value => value.StreamType == TelemetryStreamType.SESSION_METADATA),
                "A high-rate P023 JSON row archive was written by the compact runtime.");

            TelemetryPendingUploadMetadata[] sidecars = Directory
                .GetFiles(temporary.Root, "*.upload.json", SearchOption.AllDirectories)
                .Select(path => TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(path)))
                .Where(value => string.Equals(value.Protocol, "AMS2_COMPACT_TELEMETRY_V1", StringComparison.Ordinal))
                .ToArray();
            AssertEx.True(sidecars.Length >= compactPaths.Length);
            AssertEx.True(sidecars.Where(value => value.Visibility == TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS)
                .All(value => value.Status == TelemetryUploadStatus.LOCAL_PENDING_OWNER));

            var queue = new TelemetryChunkUploadQueue(temporary.Root);
            IReadOnlyList<TelemetryChunkUploadItem> due = queue.GetDueBatch(64, At(100));
            AssertEx.True(due.All(value => value.Metadata.Visibility == TelemetryVisibility.PUBLIC_REPLAY));
            AssertEx.True(sidecars.Any(value => value.Visibility == TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS));
        }

        private static void CompactRuntimeEmitsAttemptIntegrity()
        {
            using var temporary = new TemporaryDirectory("future-runtime-compact-integrity");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-compact-integrity",
                "0.2.2-test",
                options: new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 1_000,
                    ReplayIntervalMs = 200,
                    DriverTelemetryIntervalMs = 50,
                    IncidentIntervalMs = 50
                },
                clockFactory: () => new TelemetrySessionClock(monotonic),
                archiveFormat: TelemetryArchiveFormat.COMPACT_A2CT_V1);
            AssertEx.True(runtime.Observe(Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null)));
            runtime.GameDetached();
            runtime.Dispose();

            CompactTelemetryEnvelope[] frames = ReadCompactFrames(temporary.Root);
            CompactTelemetryEnvelope loss = AssertEx.Single(frames.Where(value =>
                value.Block.SchemaId == CompactTelemetrySchemaId.LossLedgerV1));
            CompactTelemetryEnvelope finalize = AssertEx.Single(frames.Where(value =>
                value.Block.SchemaId == CompactTelemetrySchemaId.AttemptFinalizeV1));
            AssertEx.Equal(30U, loss.ChunkSequence % 32U);
            AssertEx.Equal(31U, finalize.ChunkSequence % 32U);
            AssertEx.Equal(loss.ChunkSequence + 1U, finalize.ChunkSequence);

            CompactTelemetrySample clean = AssertEx.Single(loss.Block.Samples);
            AssertEx.Equal((double)(byte)CompactTelemetryLossSourceCode.None, clean.Values[0]!.Value);
            AssertEx.Equal(0.0, clean.Values[1]!.Value);
            AssertEx.Equal((double)(ushort)CompactTelemetryLossReasonCode.None, clean.Values[2]!.Value);

            TelemetryAttemptLossLedger ledger = AssertEx.Single(runtime.AttemptLossLedgers);
            CompactTelemetrySample acknowledged = AssertEx.Single(finalize.Block.Samples);
            long acceptedWork = ledger.Streams.Sum(value => value.AcceptedWorkUnits);
            AssertEx.Equal((double)acceptedWork, acknowledged.Values[0]!.Value);
            AssertEx.Equal((double)acceptedWork, acknowledged.Values[1]!.Value);
            AssertEx.Equal(0.0, acknowledged.Values[2]!.Value);
            AssertEx.Equal(
                (double)(byte)CompactTelemetryCompletenessCode.Complete,
                acknowledged.Values[3]!.Value);
            AssertEx.True(ledger.FinalizeAcknowledged);
            AssertEx.Equal(TelemetryAttemptCompleteness.COMPLETE, ledger.Completeness);

            TelemetryPendingUploadMetadata[] integritySidecars = Directory
                .GetFiles(temporary.Root, "*.upload.json", SearchOption.AllDirectories)
                .Select(path => TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(path)))
                .Where(value => value.CompactSchemaId == (ushort)CompactTelemetrySchemaId.LossLedgerV1
                    || value.CompactSchemaId == (ushort)CompactTelemetrySchemaId.AttemptFinalizeV1)
                .ToArray();
            AssertEx.Equal(2, integritySidecars.Length);
            AssertEx.True(integritySidecars.All(value =>
                value.Visibility == TelemetryVisibility.PUBLIC_REPLAY
                && value.Status == TelemetryUploadStatus.PENDING));
        }

        private static void CompactRuntimeIntegrityMarksCadenceLossPartial()
        {
            using var temporary = new TemporaryDirectory("future-runtime-compact-integrity-partial");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-compact-integrity-partial",
                "0.2.2-test",
                options: new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 1_000,
                    ReplayIntervalMs = 200,
                    DriverTelemetryIntervalMs = 50,
                    IncidentIntervalMs = 50
                },
                clockFactory: () => new TelemetrySessionClock(monotonic),
                archiveFormat: TelemetryArchiveFormat.COMPACT_A2CT_V1);
            ParticipantSnapshot[] participants =
            {
                Participant(0, "LOCAL", 1, worldX: 10, vehicle: "GT3 Car", vehicleClass: "GT3")
            };
            AssertEx.True(runtime.Observe(Snapshot(At(0), participants, Vehicle(50, 0))));
            monotonic.Timestamp = 250;
            AssertEx.True(runtime.Observe(Snapshot(At(1), participants, Vehicle(51, 0))));
            runtime.GameDetached();
            runtime.Dispose();

            TelemetryAttemptLossLedger ledger = AssertEx.Single(runtime.AttemptLossLedgers);
            long cadenceLoss = ledger.Streams
                .Single(value => value.StreamType == TelemetryStreamType.DRIVER_TELEMETRY)
                .CadenceMissedSamples;
            AssertEx.True(cadenceLoss > 0);
            AssertEx.Equal(TelemetryAttemptCompleteness.PARTIAL, ledger.Completeness);

            CompactTelemetryEnvelope[] frames = ReadCompactFrames(temporary.Root);
            CompactTelemetryEnvelope loss = AssertEx.Single(frames.Where(value =>
                value.Block.SchemaId == CompactTelemetrySchemaId.LossLedgerV1));
            CompactTelemetrySample cadence = AssertEx.Single(loss.Block.Samples.Where(value =>
                value.Values[0] == (double)(byte)CompactTelemetryLossSourceCode.CadenceMissed
                && value.Values[2] == (double)(ushort)CompactTelemetryLossReasonCode.DriverTelemetry));
            AssertEx.Equal((double)cadenceLoss, cadence.Values[1]!.Value);

            CompactTelemetryEnvelope finalize = AssertEx.Single(frames.Where(value =>
                value.Block.SchemaId == CompactTelemetrySchemaId.AttemptFinalizeV1));
            CompactTelemetrySample summary = AssertEx.Single(finalize.Block.Samples);
            AssertEx.Equal((double)ledger.KnownLossCount, summary.Values[2]!.Value);
            AssertEx.Equal(
                (double)(byte)CompactTelemetryCompletenessCode.Partial,
                summary.Values[3]!.Value);
        }

        private static void CompactIntegrityFailureIsPartial()
        {
            using var temporary = new TemporaryDirectory("future-runtime-compact-integrity-failure");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-compact-integrity-failure",
                "0.2.2-test",
                "AMS2_SHM_V14",
                new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 1_000,
                    ReplayIntervalMs = 200,
                    DriverTelemetryIntervalMs = 50,
                    IncidentIntervalMs = 50
                },
                () => new TelemetrySessionClock(monotonic),
                (archiveRoot, identity, archiveOptions) =>
                {
                    var compactStore = new CompactTelemetryChunkStore(archiveRoot, identity);
                    return new LocalDurableTelemetryArchive(
                        archiveRoot,
                        identity,
                        archiveOptions,
                        compactStore.Commit,
                        null);
                },
                null,
                true,
                (identity, ledger, endElapsedMs, capturedAtUtc, chunkDurationMs) =>
                    throw new IOException("fixture compact integrity failure"));
            AssertEx.True(runtime.Observe(Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null)));
            runtime.GameDetached();
            runtime.Dispose();

            TelemetryAttemptLossLedger ledger = AssertEx.Single(runtime.AttemptLossLedgers);
            AssertEx.False(ledger.FinalizeAcknowledged);
            AssertEx.Equal(TelemetryAttemptCompleteness.PARTIAL, ledger.Completeness);
            AssertEx.True(ledger.Streams.Sum(value => value.DiskWriteFailures) > 0);
            AssertEx.True(ledger.Streams.Sum(value => value.FinalizeFailures) > 0);
            AssertEx.Equal(0L, runtime.Counters.CompletedAttempts);
            AssertEx.True(runtime.Counters.BackgroundFailures > 0);
            CompactTelemetryEnvelope[] frames = ReadCompactFrames(temporary.Root);
            AssertEx.False(frames.Any(value =>
                value.Block.SchemaId == CompactTelemetrySchemaId.LossLedgerV1
                || value.Block.SchemaId == CompactTelemetrySchemaId.AttemptFinalizeV1));

            string ledgerPath = AssertEx.Single(Directory.GetFiles(
                Path.Combine(temporary.Root, "attempt-ledgers"),
                "*.attempt-loss.json",
                SearchOption.TopDirectoryOnly));
            using JsonDocument persisted = JsonDocument.Parse(File.ReadAllBytes(ledgerPath));
            AssertEx.False(persisted.RootElement.GetProperty("finalizeAcknowledged").GetBoolean());
            AssertEx.Equal("PARTIAL", persisted.RootElement.GetProperty("completeness").GetString());
        }

        private static void CompactLossConflictStopsFinalize()
        {
            using var temporary = new TemporaryDirectory("future-runtime-compact-integrity-conflict");
            TelemetryArchiveIdentity identity = TelemetryArchiveIdentityFactory.StartSession(
                "compact-integrity-conflict-fixture");
            var compactStore = new CompactTelemetryChunkStore(temporary.Root, identity);
            string sessionDirectory = new TelemetryChunkStore(temporary.Root, identity).SessionDirectory;
            string integrityDirectory = Path.Combine(sessionDirectory, "chunks", "compact", "integrity");
            Directory.CreateDirectory(integrityDirectory);
            string lossPath = Path.Combine(integrityDirectory, "00000030-0050.a2ct.gz");
            string finalizePath = Path.Combine(integrityDirectory, "00000031-0051.a2ct.gz");
            File.WriteAllBytes(lossPath, TelemetryChunkSerializer.Gzip(new byte[] { 1, 2, 3, 4 }));
            var ledger = new TelemetryAttemptLossLedger
            {
                SessionId = identity.SessionId,
                SessionFingerprint = identity.SessionFingerprint,
                WitnessId = identity.WitnessId,
                AttemptId = identity.AttemptId,
                AttemptNumber = identity.AttemptNumber,
                CloseRequested = true,
                FinalizeAcknowledged = true,
                DurableAck = true,
                Completeness = TelemetryAttemptCompleteness.COMPLETE
            };

            IReadOnlyList<TelemetryChunkCommitOutcome> outcomes = compactStore.CommitAttemptIntegrity(
                ledger,
                0,
                At(0),
                1_000);
            TelemetryChunkCommitOutcome conflict = AssertEx.Single(outcomes);
            AssertEx.Equal(TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED, conflict.Disposition);
            AssertEx.False(File.Exists(finalizePath),
                "ATTEMPT_FINALIZE_V1 must not exist when LOSS_LEDGER_V1 conflicts.");
        }

        private static CompactTelemetryEnvelope[] ReadCompactFrames(string root)
            => Directory.GetFiles(root, "*.a2ct.gz", SearchOption.AllDirectories)
                .Select(path =>
                {
                    using FileStream stream = File.OpenRead(path);
                    return CompactTelemetryCodec.Decode(TelemetryChunkSerializer.Gunzip(stream));
                })
                .ToArray();

        private static void CompactRuntimeNormalizesNegativeTimeSentinels()
        {
            using var temporary = new TemporaryDirectory("future-runtime-compact-time-sentinel");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-compact-sentinel",
                "0.2.2-test",
                options: new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 1_000,
                    ReplayIntervalMs = 200,
                    DriverTelemetryIntervalMs = 50,
                    IncidentIntervalMs = 50
                },
                clockFactory: () => new TelemetrySessionClock(monotonic),
                archiveFormat: TelemetryArchiveFormat.COMPACT_A2CT_V1);
            var participant = new ParticipantSnapshot(
                0,
                true,
                "LOCAL",
                1,
                0,
                1,
                0,
                (uint)RaceState.Racing,
                (uint)PitMode.None,
                -123,
                -123,
                "GT3 Car",
                "GT3",
                currentLapDistance: 500,
                currentSector1Time: -123,
                currentSector2Time: -123,
                currentSector3Time: -123,
                fastestSector1Time: -123,
                fastestSector2Time: -123,
                fastestSector3Time: -123,
                worldPosition: new TelemetryVector3(1, 2, 3),
                orientation: new TelemetryVector3(0, 1.2f, 0),
                speedMetresPerSecond: 50);

            FutureTelemetryCaptureBatch adapted = new FutureTelemetrySnapshotAdapter("0.2.2-test")
                .Observe(Snapshot(At(0), new[] { participant }, vehicle: null), Stamp(0));
            ReplayParticipantSample replay = adapted.Frame!.Participants.Single();
            AssertEx.Null(replay.CurrentSector1TimeSeconds);
            AssertEx.Null(replay.CurrentSector2TimeSeconds);
            AssertEx.Null(replay.CurrentSector3TimeSeconds);
            AssertEx.Null(replay.BestLapTimeSeconds);
            AssertEx.Null(replay.LastLapTimeSeconds);
            AssertEx.Null(replay.FastestSector1TimeSeconds);
            AssertEx.Null(replay.FastestSector2TimeSeconds);
            AssertEx.Null(replay.FastestSector3TimeSeconds);

            AssertEx.True(runtime.Observe(Snapshot(At(0), new[] { participant }, vehicle: null)));
            runtime.GameDetached();
            runtime.Dispose();

            CompactTelemetryEnvelope[] compact = Directory
                .GetFiles(temporary.Root, "*.a2ct.gz", SearchOption.AllDirectories)
                .Select(path =>
                {
                    using FileStream stream = File.OpenRead(path);
                    return CompactTelemetryCodec.Decode(TelemetryChunkSerializer.Gunzip(stream));
                })
                .ToArray();
            AssertEx.True(compact.Any(value => value.Block.SchemaId == CompactTelemetrySchemaId.ParticipantReplayV1),
                "The AMS2 -123 time sentinel must not abort replay finalization.");
            AssertEx.True(runtime.AttemptLossLedgers.All(value => value.KnownLossCount == 0),
                "Normalizing an unavailable AMS2 time sentinel must not be recorded as telemetry loss.");
        }

        private static void CompactRuntimeNormalizesUnavailableDriverFastDomains()
        {
            using var temporary = new TemporaryDirectory("future-runtime-compact-driver-domain");
            string runtimeRoot = Path.Combine(temporary.Root, "runtime");
            string directRoot = Path.Combine(temporary.Root, "direct");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                runtimeRoot,
                "installation-compact-driver-domain",
                "0.2.2-test",
                options: new TelemetryArchiveOptions
                {
                    ChunkDurationMs = 1_000,
                    ReplayIntervalMs = 200,
                    DriverTelemetryIntervalMs = 50,
                    IncidentIntervalMs = 50
                },
                clockFactory: () => new TelemetrySessionClock(monotonic),
                archiveFormat: TelemetryArchiveFormat.COMPACT_A2CT_V1);
            var unavailableParticipant = new ParticipantSnapshot(
                0,
                true,
                "LOCAL",
                1,
                0,
                1,
                0,
                (uint)RaceState.Racing,
                (uint)PitMode.None,
                100,
                101,
                "GT3 Car",
                "GT3",
                currentLapDistance: -123,
                worldPosition: new TelemetryVector3(1, 2, 3),
                orientation: new TelemetryVector3(0, 1.2f, 0),
                speedMetresPerSecond: -123);
            ViewedVehicleTelemetrySnapshot unavailableVehicle = Vehicle(-123, 0);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.Rpm), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.Throttle), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.Brake), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.Steering), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.Clutch), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.UnfilteredThrottle), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.UnfilteredBrake), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.UnfilteredSteering), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.UnfilteredClutch), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.LocalAcceleration),
                new TelemetryVector3(1_000, 0, -1_000));
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.Orientation),
                new TelemetryVector3(0, -123, 0));
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.FuelLevel), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.BrakeBias), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.EngineDamage), -123f);
            Set(unavailableVehicle, nameof(ViewedVehicleTelemetrySnapshot.AeroDamage), -123f);

            TelemetrySnapshot unavailable = Snapshot(
                At(0), new[] { unavailableParticipant }, unavailableVehicle);
            var adapter = new FutureTelemetrySnapshotAdapter("0.2.2-test");
            FutureTelemetryCaptureBatch adapted = adapter.Observe(unavailable, Stamp(0));
            DriverTelemetrySample driver = adapted.Frame!.LocalDriver!;
            AssertEx.Null(driver.LapDistanceMeters);
            AssertEx.Null(driver.SpeedMetersPerSecond);
            AssertEx.Null(driver.Rpm);
            AssertEx.Null(driver.Throttle);
            AssertEx.Null(driver.Brake);
            AssertEx.Null(driver.Steering);
            AssertEx.Null(driver.UnfilteredThrottle);
            AssertEx.Null(driver.UnfilteredBrake);
            AssertEx.Null(driver.UnfilteredSteering);
            AssertEx.Null(driver.LongitudinalAccelerationMetersPerSecondSquared);
            AssertEx.Null(driver.LateralAccelerationMetersPerSecondSquared);
            AssertEx.Null(driver.HeadingRadians);
            AssertEx.Null(driver.FuelLevelRatio);
            AssertEx.Null(driver.BrakeBias);
            AssertEx.Null(driver.EngineDamage);
            AssertEx.Null(driver.AeroDamage);

            TelemetrySnapshot available = Snapshot(
                At(1),
                new[] { Participant(0, "LOCAL", 1, worldX: 2) },
                Vehicle(50, 0));
            FutureTelemetryCaptureBatch availableAdapted = adapter.Observe(available, Stamp(50));
            var directIdentity = new TelemetryArchiveIdentity
            {
                SessionId = "capture-driver-domain",
                SessionFingerprint = "fingerprint-driver-domain",
                WitnessId = "witness-driver-domain",
                AttemptId = "attempt-driver-domain",
                AttemptNumber = 1
            };
            var accumulator = new TelemetryChunkAccumulator(
                directIdentity,
                TelemetryStreamType.DRIVER_TELEMETRY,
                0,
                20);
            accumulator.AddDriver(adapted.Frame!, 1);
            accumulator.AddDriver(availableAdapted.Frame!, 1);
            new CompactTelemetryChunkStore(directRoot, directIdentity).Commit(accumulator.Build());

            TelemetrySnapshot runtimeUnavailable = Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 1) },
                unavailableVehicle);
            AssertEx.True(runtime.Observe(runtimeUnavailable));
            monotonic.Timestamp = 50;
            AssertEx.True(runtime.Observe(available));
            runtime.GameDetached();
            runtime.Dispose();

            CompactTelemetryEnvelope[] compact = Directory
                .GetFiles(runtimeRoot, "*.a2ct.gz", SearchOption.AllDirectories)
                .Select(path =>
                {
                    using FileStream stream = File.OpenRead(path);
                    return CompactTelemetryCodec.Decode(TelemetryChunkSerializer.Gunzip(stream));
                })
                .ToArray();
            CompactTelemetryEnvelope driverFast = compact.Single(value =>
                value.Block.SchemaId == CompactTelemetrySchemaId.DriverFastV1);
            double?[] firstFast = driverFast.Block.Samples[0].Values.ToArray();
            AssertEx.True(new[] { 0, 1, 2, 3, 5, 6 }.All(index => !firstFast[index].HasValue),
                "Unavailable controls, speed and accelerations must be null, not clamped DriverFast samples.");
            AssertEx.True(firstFast[4].HasValue,
                "A valid lap distance in the same transition sample must remain available.");
            AssertEx.True(driverFast.Block.Samples[1].Values.Any(value => value.HasValue),
                "The first valid post-transition DriverFast sample must remain available.");
            AssertEx.True(compact.Any(value => value.Block.SchemaId == CompactTelemetrySchemaId.DriverMotionV1));
            AssertEx.True(compact.Any(value => value.Block.SchemaId == CompactTelemetrySchemaId.DriverSlowV1));
            AssertEx.True(compact.Any(value => value.Block.SchemaId == CompactTelemetrySchemaId.DriverChangeV1));
            TelemetryAttemptLossLedger[] ledgers = runtime.AttemptLossLedgers.ToArray();
            AssertEx.True(ledgers.SelectMany(value => value.Streams).All(value =>
                    value.WorkerExceptions == 0 && value.SerializationFailures == 0
                    && value.DiskWriteFailures == 0 && value.FinalizeFailures == 0),
                "Unavailable transition values must not abort compact driver finalization: "
                + string.Join(";", ledgers.SelectMany(value => value.Streams).Select(value =>
                    value.StreamType + ":worker=" + value.WorkerExceptions
                    + ",serialize=" + value.SerializationFailures
                    + ",disk=" + value.DiskWriteFailures
                    + ",finalize=" + value.FinalizeFailures
                    + ",cadence=" + value.CadenceMissedSamples)));
        }

        private static void RestartAttemptIdentity()
        {
            using var temporary = new TemporaryDirectory("future-restart");
            var monotonic = new TestMonotonicClock();
            var identities = new List<TelemetryArchiveIdentity>();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-restart",
                "0.2.2-test",
                clockFactory: () => new TelemetrySessionClock(monotonic));
            runtime.IdentityStarted += identity => identities.Add(identity);
            TelemetrySnapshot racing = Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null);
            runtime.Observe(racing);
            monotonic.Timestamp = 50;
            runtime.Observe(Snapshot(
                At(1),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null,
                gameState: GameState.InGameRestarting));
            monotonic.Timestamp = 100;
            runtime.Observe(Snapshot(
                At(2),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null,
                raceState: RaceState.NotStarted));
            runtime.Dispose();

            AssertEx.Equal(2, identities.Count);
            AssertEx.Equal(identities[0].SessionId, identities[1].SessionId);
            AssertEx.Equal(identities[0].SessionFingerprint, identities[1].SessionFingerprint);
            AssertEx.Equal(identities[0].WitnessId, identities[1].WitnessId);
            AssertEx.NotEqual(identities[0].AttemptId, identities[1].AttemptId);
            AssertEx.Equal(1, identities[0].AttemptNumber);
            AssertEx.Equal(2, identities[1].AttemptNumber);
        }

        private static void RuntimeWitnessAndFiveStreams()
        {
            using var temporary = new TemporaryDirectory("future-runtime-witness-five");
            var monotonic = new TestMonotonicClock();
            var witnessEngine = new SessionWitnessCaptureEngine("installation-joined", "0.2.2-test");
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-joined",
                "0.2.2-test",
                clockFactory: () => new TelemetrySessionClock(monotonic));
            runtime.IdentityStarted += witnessEngine.BeginArchiveIdentity;
            ParticipantSnapshot[] participants =
            {
                Participant(0, "LOCAL", 1, worldX: 10, vehicle: "GT3 Car", vehicleClass: "GT3"),
                Participant(1, "OTHER", 2, worldX: 20, vehicle: "GT3 Car", vehicleClass: "GT3")
            };
            TelemetrySnapshot baseline = Snapshot(At(0), participants, Vehicle(50, 0));
            runtime.Observe(baseline);
            witnessEngine.Observe(baseline);
            TelemetryArchiveIdentity identity = runtime.CurrentIdentity
                ?? throw new InvalidOperationException("Runtime identity was not started.");

            monotonic.Timestamp = 50;
            TelemetrySnapshot collision = Snapshot(At(1), participants, Vehicle(45, 8, collisionIndex: 1));
            runtime.Observe(collision);
            witnessEngine.Observe(collision);
            monotonic.Timestamp = 100;
            TelemetrySnapshot post = Snapshot(At(2), participants, Vehicle(44, 8, collisionIndex: 1));
            runtime.Observe(post);
            witnessEngine.Observe(post);
            runtime.GameDetached();
            SessionWitnessRecord witness = witnessEngine.Close(At(3), "GAME_DETACHED").FinalizedWitness
                ?? throw new InvalidOperationException("Joined witness was not finalized.");
            runtime.Dispose();

            AssertEx.Equal(identity.SessionId, witness.CaptureSessionId);
            AssertEx.Equal(identity.SessionFingerprint, witness.SessionFingerprint);
            AssertEx.Equal(identity.WitnessId, witness.WitnessId);
            AssertEx.Equal(identity.AttemptId, witness.AttemptId);
            AssertEx.Equal((int?)identity.AttemptNumber, witness.AttemptNumber);
            TelemetryChunkEnvelope[] chunks = Directory
                .GetFiles(temporary.Root, "*.json.gz", SearchOption.AllDirectories)
                .Select(ReadChunk)
                .ToArray();
            TelemetryStreamType[] streams = chunks.Select(value => value.StreamType).Distinct().ToArray();
            foreach (TelemetryStreamType stream in Enum.GetValues<TelemetryStreamType>())
            {
                AssertEx.True(streams.Contains(stream), "Missing persisted stream " + stream + ".");
            }
            AssertEx.True(chunks.All(value => value.SessionId == witness.CaptureSessionId));
            AssertEx.True(chunks.All(value => value.SessionFingerprint == witness.SessionFingerprint));
            AssertEx.True(chunks.All(value => value.WitnessId == witness.WitnessId));
            AssertEx.True(chunks.All(value => value.AttemptId == witness.AttemptId));
        }

        private static void IndependentWitnessFingerprintsJoin()
        {
            using var firstRoot = new TemporaryDirectory("future-fingerprint-first");
            using var secondRoot = new TemporaryDirectory("future-fingerprint-second");
            TelemetrySnapshot snapshot = Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null);
            var first = new FutureTelemetryCaptureRuntime(firstRoot.Root, "installation-a", "0.2.2-test");
            var second = new FutureTelemetryCaptureRuntime(secondRoot.Root, "installation-b", "0.2.2-test");
            first.Observe(snapshot);
            second.Observe(snapshot);
            TelemetryArchiveIdentity firstIdentity = first.CurrentIdentity
                ?? throw new InvalidOperationException("First identity was not created.");
            TelemetryArchiveIdentity secondIdentity = second.CurrentIdentity
                ?? throw new InvalidOperationException("Second identity was not created.");
            first.Dispose();
            second.Dispose();
            AssertEx.Equal(firstIdentity.SessionFingerprint, secondIdentity.SessionFingerprint);
            AssertEx.NotEqual(firstIdentity.WitnessId, secondIdentity.WitnessId);
            AssertEx.NotEqual(firstIdentity.SessionId, secondIdentity.SessionId);
        }

        private static void CloseRequiresDurableAcknowledgement()
        {
            using var temporary = new TemporaryDirectory("future-runtime-close-ack");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-close-ack",
                "0.2.2-test",
                clockFactory: () => new TelemetrySessionClock(monotonic));
            AssertEx.True(runtime.Observe(Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null)));
            runtime.GameDetached();
            runtime.Dispose();

            TelemetryAttemptLossLedger ledger = AssertEx.Single(runtime.AttemptLossLedgers);
            AssertEx.True(ledger.CloseRequested);
            AssertEx.True(ledger.FinalizeAcknowledged);
            AssertEx.True(ledger.DurableAck);
            AssertEx.Equal(0L, ledger.KnownLossCount);
            AssertEx.Equal(TelemetryAttemptCompleteness.COMPLETE, ledger.Completeness);
            AssertEx.Equal(1, Directory.GetFiles(
                Path.Combine(temporary.Root, "attempt-ledgers"),
                "*.attempt-loss.json",
                SearchOption.TopDirectoryOnly).Length);
        }

        private static void OuterQueueLossIsAttemptScoped()
        {
            using var temporary = new TemporaryDirectory("future-runtime-outer-loss");
            var monotonic = new TestMonotonicClock();
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            int blocked = 0;
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-outer-loss",
                "0.2.2-test",
                "AMS2_SHM_V14",
                new TelemetryArchiveOptions { InputChannelCapacity = 8 },
                () => new TelemetrySessionClock(monotonic),
                null,
                () =>
                {
                    if (Interlocked.CompareExchange(ref blocked, 1, 0) != 0) return;
                    entered.Set();
                    release.Wait();
                });
            ParticipantSnapshot[] participants = { Participant(0, "LOCAL", 1, worldX: 10) };
            AssertEx.True(runtime.Observe(Snapshot(At(0), participants, null)));
            AssertEx.True(entered.Wait(TimeSpan.FromSeconds(5)), "Runtime worker did not enter deterministic block.");

            for (int index = 1; index <= 8; index++)
            {
                monotonic.Timestamp = index * 50;
                AssertEx.True(runtime.Observe(Snapshot(At(index), participants, null)));
            }
            monotonic.Timestamp = 450;
            AssertEx.False(runtime.Observe(Snapshot(At(9), participants, null)), "The ninth queued batch must hit capacity eight.");
            release.Set();

            monotonic.Timestamp = 500;
            runtime.Observe(Snapshot(
                At(10),
                participants,
                null,
                gameState: GameState.InGameRestarting));
            monotonic.Timestamp = 550;
            AssertEx.True(runtime.Observe(Snapshot(
                At(11),
                participants,
                null,
                raceState: RaceState.NotStarted)));
            runtime.GameDetached();
            runtime.Dispose();

            TelemetryAttemptLossLedger[] ledgers = runtime.AttemptLossLedgers
                .OrderBy(value => value.AttemptNumber)
                .ToArray();
            AssertEx.Equal(2, ledgers.Length);
            AssertEx.True(ledgers[0].Streams.Sum(value => value.OuterQueueLosses) > 0);
            AssertEx.Equal(TelemetryAttemptCompleteness.PARTIAL, ledgers[0].Completeness);
            AssertEx.Equal(0L, ledgers[1].Streams.Sum(value => value.OuterQueueLosses));
            AssertEx.Equal(0L, ledgers[1].KnownLossCount);
            AssertEx.Equal(TelemetryAttemptCompleteness.COMPLETE, ledgers[1].Completeness);

            string[] durableLedgers = Directory.GetFiles(
                Path.Combine(temporary.Root, "attempt-ledgers"),
                "*.attempt-loss.json",
                SearchOption.TopDirectoryOnly);
            AssertEx.Equal(2, durableLedgers.Length);
            bool persistedOuterLoss = false;
            foreach (string path in durableLedgers)
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
                JsonElement root = document.RootElement;
                if (root.GetProperty("attemptNumber").GetInt32() != 1) continue;
                persistedOuterLoss = root.GetProperty("streams")
                    .EnumerateArray()
                    .Sum(value => value.GetProperty("outerQueueLosses").GetInt64()) > 0;
                AssertEx.Equal("PARTIAL", root.GetProperty("completeness").GetString());
            }
            AssertEx.True(persistedOuterLoss, "Outer queue loss must survive in the durable attempt ledger.");
        }

        private static void WorkerExceptionIsPartial()
        {
            using var temporary = new TemporaryDirectory("future-runtime-worker-failure");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-worker-failure",
                "0.2.2-test",
                "AMS2_SHM_V14",
                new TelemetryArchiveOptions(),
                () => new TelemetrySessionClock(monotonic),
                (archiveRoot, identity, archiveOptions) =>
                    throw new InvalidOperationException("fixture worker failure"),
                null);
            AssertEx.True(runtime.Observe(Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null)));
            runtime.GameDetached();
            runtime.Dispose();

            TelemetryAttemptLossLedger ledger = AssertEx.Single(runtime.AttemptLossLedgers);
            AssertEx.True(ledger.Streams.Sum(value => value.WorkerExceptions) > 0);
            AssertEx.True(ledger.Streams.Sum(value => value.FinalizeFailures) > 0);
            AssertEx.False(ledger.FinalizeAcknowledged);
            AssertEx.False(ledger.DurableAck);
            AssertEx.Equal(TelemetryAttemptCompleteness.PARTIAL, ledger.Completeness);
        }

        private static void DiskFinalizeFailureIsPartial()
        {
            using var temporary = new TemporaryDirectory("future-runtime-disk-finalize-failure");
            var monotonic = new TestMonotonicClock();
            var runtime = new FutureTelemetryCaptureRuntime(
                temporary.Root,
                "installation-disk-failure",
                "0.2.2-test",
                "AMS2_SHM_V14",
                new TelemetryArchiveOptions(),
                () => new TelemetrySessionClock(monotonic),
                (archiveRoot, identity, archiveOptions) => new LocalDurableTelemetryArchive(
                    archiveRoot,
                    identity,
                    archiveOptions,
                    envelope => throw new IOException("fixture durable commit failure"),
                    null),
                null);
            AssertEx.True(runtime.Observe(Snapshot(
                At(0),
                new[] { Participant(0, "LOCAL", 1, worldX: 10) },
                null)));
            runtime.GameDetached();
            runtime.Dispose();

            TelemetryAttemptLossLedger ledger = AssertEx.Single(runtime.AttemptLossLedgers);
            AssertEx.True(ledger.Streams.Sum(value => value.DiskWriteFailures) > 0);
            AssertEx.True(ledger.Streams.Sum(value => value.FinalizeFailures) > 0);
            AssertEx.False(ledger.FinalizeAcknowledged);
            AssertEx.Equal(TelemetryAttemptCompleteness.PARTIAL, ledger.Completeness);
        }

        private static TelemetryVisibility VisibilityFor(TelemetryStreamType stream)
            => stream == TelemetryStreamType.DRIVER_TELEMETRY
                ? TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS
                : TelemetryVisibility.PUBLIC_REPLAY;

        private static TelemetryChunkEnvelope ReadChunk(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return TelemetryChunkSerializer.Deserialize(TelemetryChunkSerializer.Gunzip(stream));
        }

        private static TelemetryCaptureStamp Stamp(long elapsedMs)
            => new TelemetryCaptureStamp { CapturedAtUtc = At(elapsedMs / 50), SessionElapsedMs = elapsedMs };

        private static DateTimeOffset At(long seconds)
            => new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

        private static ParticipantSnapshot Participant(
            int index,
            string name,
            uint position,
            uint laps = 1,
            float bestLap = 100,
            float lastLap = 101,
            PitMode pitMode = PitMode.None,
            PitSchedule pitSchedule = PitSchedule.None,
            float worldX = 1,
            string vehicle = "GT3 Car",
            string vehicleClass = "GT3")
            => new ParticipantSnapshot(
                index,
                true,
                name,
                position,
                laps,
                laps + 1,
                1,
                (uint)RaceState.Racing,
                (uint)pitMode,
                bestLap,
                lastLap,
                vehicle,
                vehicleClass,
                currentLapDistance: 500 + worldX,
                pitScheduleRaw: (uint)pitSchedule,
                worldPosition: new TelemetryVector3(worldX, 2, 3),
                orientation: new TelemetryVector3(0, 1.2f, 0),
                speedMetresPerSecond: 50);

        private static ParticipantSnapshot ExtendedParticipant(
            int index,
            string name,
            uint position,
            uint nationality,
            float orientationX)
            => new ParticipantSnapshot(
                index,
                true,
                name,
                position,
                lapsCompleted: 7,
                currentLap: 8,
                currentSector: 2,
                raceStateRaw: (uint)RaceState.Racing,
                pitModeRaw: (uint)PitMode.None,
                bestLapTime: 98.25f,
                lastLapTime: 99.5f,
                vehicleName: "GT3 Car",
                vehicleClass: "GT3",
                currentLapDistance: 1234.5f + index,
                lapInvalidated: index == 0,
                currentSector1Time: 31.1f,
                currentSector2Time: 32.2f,
                currentSector3Time: -1,
                fastestSector1Time: 30.1f,
                fastestSector2Time: 31.2f,
                fastestSector3Time: 32.3f,
                pitScheduleRaw: (uint)PitSchedule.DriveThrough,
                highestFlagColourRaw: (uint)FlagColour.DoubleYellow,
                highestFlagReasonRaw: 7,
                worldPosition: new TelemetryVector3(10 + index, 2, 3),
                orientation: new TelemetryVector3(orientationX, 1.2f, -0.33f),
                speedMetresPerSecond: 50,
                nationalityRaw: nationality);

        private static TelemetrySnapshot Snapshot(
            DateTimeOffset capturedAt,
            ParticipantSnapshot[] participants,
            ViewedVehicleTelemetrySnapshot? vehicle,
            GameState gameState = GameState.InGamePlaying,
            RaceState raceState = RaceState.Racing,
            FlagColour flag = FlagColour.None,
            YellowFlagState yellow = YellowFlagState.None,
            string rootCar = "GT3 Car",
            string trackLocation = "Bathurst",
            string trackVariation = "2020",
            string translatedTrackLocation = "",
            string translatedTrackVariation = "")
            => new TelemetrySnapshot(
                capturedAt,
                SharedMemoryLayout.SupportedVersion,
                12345,
                200,
                (uint)gameState,
                (uint)SessionState.Race,
                (uint)raceState,
                0,
                participants.Length,
                10,
                participants.Length == 0 ? -1 : participants[0].LastLapTime,
                participants.Length == 0 ? -1 : participants[0].BestLapTime,
                -1,
                -1,
                participants,
                trackLocation: trackLocation,
                trackVariation: trackVariation,
                numSectors: 3,
                currentTime: 12,
                trackLength: 6213,
                eventTimeRemaining: 1200,
                highestFlagColourRaw: (uint)flag,
                sessionDuration: 30,
                rootCarName: rootCar,
                rootCarClassName: "GT3",
                yellowFlagStateRaw: (int)yellow,
                translatedTrackLocation: translatedTrackLocation,
                translatedTrackVariation: translatedTrackVariation,
                viewedVehicleTelemetry: vehicle);

        private static ViewedVehicleTelemetrySnapshot Vehicle(
            float speed,
            float collisionMagnitude,
            int collisionIndex = -1)
        {
            var value = (ViewedVehicleTelemetrySnapshot)Activator.CreateInstance(
                typeof(ViewedVehicleTelemetrySnapshot),
                nonPublic: true)!;
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.SpeedMetresPerSecond), speed);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.Rpm), 7000f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.Gear), 4);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.Throttle), 0.7f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.Brake), 0.2f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.Steering), -0.1f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.Clutch), 0f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.UnfilteredThrottle), 0.71f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.UnfilteredBrake), 0.21f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.UnfilteredSteering), -0.11f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.UnfilteredClutch), 0f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.FuelLevel), 0.6f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.FuelCapacityLitres), 70f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.BrakeBias), 0.56f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.LastOpponentCollisionIndex), collisionIndex);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.LastOpponentCollisionMagnitude), collisionMagnitude);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.Orientation), new TelemetryVector3(0, 1.2f, 0));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.WorldVelocity), new TelemetryVector3(1, 2, 3));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.LocalAcceleration), new TelemetryVector3(2, 3, 4));
            return value;
        }

        private static ViewedVehicleTelemetrySnapshot ExtendedVehicle(
            float collisionMagnitude,
            int collisionIndex)
        {
            ViewedVehicleTelemetrySnapshot value = Vehicle(62.5f, collisionMagnitude, collisionIndex);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.CarFlagsRaw), 19u);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.OilTemperatureCelsius), 101.25f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.OilPressureKPa), 321.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.WaterTemperatureCelsius), 92.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.WaterPressureKPa), 210.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.FuelPressureKPa), 300.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.MaxRpm), 8200f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.NumGears), 6);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.OdometerKilometres), 123.4f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.AntiLockActive), true);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.BoostActive), true);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.BoostAmount), 0.75f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.Orientation), new TelemetryVector3(0.1f, 1.2f, 0.3f));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.LocalVelocity), new TelemetryVector3(1.1f, 2.2f, 3.3f));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.WorldVelocity), new TelemetryVector3(4.4f, 5.5f, 6.6f));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.AngularVelocity), new TelemetryVector3(0.4f, 0.5f, 0.6f));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.LocalAcceleration), new TelemetryVector3(2.1f, 3.2f, 4.3f));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.WorldAcceleration), new TelemetryVector3(7.25f, 8.25f, 9.25f));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.ExtentsCentre), new TelemetryVector3(0.7f, 0.8f, 0.9f));
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.EngineSpeedRadiansPerSecond), 712.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.EngineTorqueNewtonMetres), 450.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.FrontWing), 3.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.RearWing), 6.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.HandBrake), 0.1f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.CrashStateRaw), 2u);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.TurboBoostPressure), 1.25f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.DrsStateRaw), 3u);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.AntiLockSetting), 4);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.TractionControlSetting), 5);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.ErsDeploymentModeRaw), 2);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.ErsAutoModeEnabled), true);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.ClutchTemperatureKelvin), 355.5f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.ClutchWear), 0.95f);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.ClutchOverheated), true);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.ClutchSlipping), true);
            Set(value, nameof(ViewedVehicleTelemetrySnapshot.LaunchStageRaw), 3);

            TyreTelemetrySnapshot[] tyres = Enumerable.Range(0, 4).Select(ExtendedTyre).ToArray();
            MethodInfo setTyres = typeof(ViewedVehicleTelemetrySnapshot).GetMethod(
                "SetTyres",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing tyre setter.");
            setTyres.Invoke(value, new object[] { tyres });
            return value;
        }

        private static TyreTelemetrySnapshot ExtendedTyre(int index)
        {
            var tyre = (TyreTelemetrySnapshot)Activator.CreateInstance(typeof(TyreTelemetrySnapshot), nonPublic: true)!;
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.Index), index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.FlagsRaw), (uint)(10 + index));
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.TerrainRaw), (uint)(20 + index));
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.LocalY), 0.1f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.RevolutionsPerSecond), 15.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.TemperatureCelsius), 85.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.HeightAboveGround), 0.2f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.Wear), 0.9f - index * 0.01f);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.BrakeDamage), 0.01f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.SuspensionDamage), 0.02f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.BrakeTemperatureCelsius), 450.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.TreadTemperatureKelvin), 360.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.LayerTemperatureKelvin), 350.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.CarcassTemperatureKelvin), 340.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.RimTemperatureKelvin), 330.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.InternalAirTemperatureKelvin), 320.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.WheelLocalPositionY), 0.3f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.SuspensionTravelMetres), 0.04f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.SuspensionVelocity), 0.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.AirPressurePsi), 27.75f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.Compound), "SOFT-" + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.LeftTemperatureCelsius), 84.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.CenterTemperatureCelsius), 85.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.RightTemperatureCelsius), 86.5f + index);
            SetTyre(tyre, nameof(TyreTelemetrySnapshot.RideHeightCentimetres), 5.5f + index);
            return tyre;
        }

        private static void Set<T>(ViewedVehicleTelemetrySnapshot target, string property, T value)
        {
            PropertyInfo? info = typeof(ViewedVehicleTelemetrySnapshot).GetProperty(
                property,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null) throw new InvalidOperationException("Missing telemetry property " + property + ".");
            info.SetValue(target, value);
        }

        private static void SetTyre<T>(TyreTelemetrySnapshot target, string property, T value)
        {
            PropertyInfo? info = typeof(TyreTelemetrySnapshot).GetProperty(
                property,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null) throw new InvalidOperationException("Missing tyre telemetry property " + property + ".");
            info.SetValue(target, value);
        }

        private sealed class TestMonotonicClock : ITelemetryMonotonicClock
        {
            public long Timestamp { get; set; }
            public long Frequency => 1000;
        }
    }
}
