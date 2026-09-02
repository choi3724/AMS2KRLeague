using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2LeagueActivity.Tests
{
    internal static class FutureTelemetryArchiveTests
    {
        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Future telemetry fixture inventory", FixedFixtureInventory);
            yield return new TestCase("Future telemetry identity joins all streams and separates attempts", IdentityAndAttempts);
            yield return new TestCase("Future telemetry clock is monotonic and independent from lap time", MonotonicCaptureClock);
            yield return new TestCase("Future telemetry defaults are 30s 5Hz 20Hz 20Hz", DefaultTierPolicy);
            yield return new TestCase("Future telemetry persists all five gzip chunk contracts", AllFiveStreamsPersist);
            yield return new TestCase("Future telemetry cadence gaps and out-of-order input are counted", CadenceAndDropQuality);
            yield return new TestCase("Viewed root telemetry is not assumed to be local driver telemetry", UnresolvedViewedVehicleIsPrivateGate);
            yield return new TestCase("Incident burst uses bounded pre/post roll for related and nearby participants", IncidentBurstIsBoundedAndFiltered);
            yield return new TestCase("Telemetry archive uses thirty second replay chunks", ThirtySecondChunking);
            yield return new TestCase("Telemetry archive duplicate is idempotent and conflict is preserved", DuplicateAndConflict);
            yield return new TestCase("Telemetry archive recovers orphan chunk metadata and interrupted writes", CrashRecovery);
            yield return new TestCase("Telemetry upload queue verifies gzip and persists sent state", UploadQueueVerifiesAndMarksSent);
            yield return new TestCase("Telemetry upload worker retries without losing durable chunk", UploadWorkerRetriesThenSends);
            yield return new TestCase("Private driver upload defaults to LOCAL_PENDING_OWNER while public upload remains available", PrivateUploadDefaultsToDenied);
            yield return new TestCase("Archive fault ledger distinguishes serialization disk and finalize failures", ArchiveFailureStagesAreDistinct);
        }

        private static void FixedFixtureInventory()
        {
            string[] names =
            {
                "60MIN_32CAR_REPLAY.json",
                "LOCAL_TELEMETRY_BRAKING.json",
                "LOCAL_TELEMETRY_THROTTLE.json",
                "LOCAL_TELEMETRY_STEERING.json",
                "DRIVING_LINE_MULTI_LAP.json",
                "INCIDENT_HIGH_RATE.json",
                "CLIENT_CRASH_RECOVERY.json",
                "OFFLINE_FULL_RACE.json",
                "MULTI_WITNESS_REPLAY.json",
                "TIME_ATTACK_COACHING.json"
            };
            string root = Path.Combine(AppContext.BaseDirectory, "fixtures", "future_telemetry");
            foreach (string name in names)
            {
                string path = Path.Combine(root, name);
                AssertEx.True(File.Exists(path), "Missing future telemetry fixture: " + name);
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
                AssertEx.True(document.RootElement.GetProperty("scenario").GetString()!.Length > 0);
            }
        }

        private static void IdentityAndAttempts()
        {
            TelemetryArchiveIdentity first = TelemetryArchiveIdentityFactory.StartSession("fingerprint-identity-0001", "witness-fixed");
            TelemetryArchiveIdentity second = TelemetryArchiveIdentityFactory.NextAttempt(first);
            AssertEx.True(first.SessionId.StartsWith("capture-", StringComparison.Ordinal));
            AssertEx.Equal(first.SessionId, second.SessionId);
            AssertEx.Equal(first.SessionFingerprint, second.SessionFingerprint);
            AssertEx.Equal(first.WitnessId, second.WitnessId);
            AssertEx.NotEqual(first.AttemptId, second.AttemptId);
            AssertEx.Equal(1, first.AttemptNumber);
            AssertEx.Equal(2, second.AttemptNumber);
        }

        private static void MonotonicCaptureClock()
        {
            var source = new MutableMonotonicClock { Frequency = 1000, Timestamp = 10_000 };
            var clock = new TelemetrySessionClock(source);
            source.Timestamp = 10_125;
            AssertEx.Equal(125L, clock.Capture(DateTimeOffset.UnixEpoch).SessionElapsedMs);
            source.Timestamp = 10_100;
            AssertEx.Equal(125L, clock.Capture(DateTimeOffset.UnixEpoch.AddSeconds(1)).SessionElapsedMs);
            source.Timestamp = 10_500;
            TelemetryCaptureStamp stamp = clock.Capture(DateTimeOffset.UnixEpoch.AddSeconds(2));
            AssertEx.Equal(500L, stamp.SessionElapsedMs);
            AssertEx.Equal("MONOTONIC_CAPTURE_CLOCK", stamp.ClockSource);
        }

        private static void DefaultTierPolicy()
        {
            var options = new TelemetryArchiveOptions();
            AssertEx.Equal(30_000, options.ChunkDurationMs);
            AssertEx.Equal(5.0, options.ReplayRateHz);
            AssertEx.Equal(20.0, options.DriverTelemetryRateHz);
            AssertEx.Equal(20.0, options.IncidentRateHz);
            AssertEx.Equal(512, options.InputChannelCapacity);
            AssertEx.Equal(64, options.MaximumParticipantsPerFrame);
        }

        private static void AllFiveStreamsPersist()
        {
            using var directory = new TemporaryDirectory("future-telemetry-five-streams");
            TelemetryArchiveIdentity identity = FixedIdentity("five-streams");
            var options = new TelemetryArchiveOptions
            {
                IncidentPreRollMs = 100,
                IncidentPostRollMs = 100,
                IncidentRingDurationMs = 500
            };
            var archive = new LocalDurableTelemetryArchive(directory.Root, identity, options);
            try
            {
                AssertEx.True(archive.TryCaptureSessionMetadata(Metadata(0)));
                AssertEx.True(archive.TryCaptureRaceStory(new RaceStoryEventSample
                {
                    EventId = "event-start",
                    EventType = "SESSION_START",
                    CapturedAtUtc = At(0),
                    SessionElapsedMs = 0
                }));

                for (int elapsed = 0; elapsed <= 250; elapsed += 50)
                {
                    TelemetryFrameSample frame = Frame(elapsed, 2, true);
                    if (elapsed == 100)
                    {
                        frame.IncidentCandidate = new IncidentCandidateSample
                        {
                            CandidateId = "incident-001",
                            TriggerCode = "RAW_PROXIMITY_AND_POSITION_CHANGE",
                            RelatedParticipantRefs = new[] { 1 }
                        };
                    }
                    AssertEx.True(archive.TryCaptureFrame(frame));
                }
                archive.FlushAsync().GetAwaiter().GetResult();
                AssertEx.False(archive.CompletionReport.FinalizeAcknowledged,
                    "A maintenance flush is not an attempt-close acknowledgement.");

                IReadOnlyList<TelemetryPendingUploadMetadata> pending = archive.ScanPending();
                AssertEx.Equal(5, pending.Count);
                AssertEx.Equal(5, pending.Select(value => value.StreamType).Distinct().Count());
                AssertEx.Equal(1, pending.Count(value => value.Visibility == TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS));
                AssertEx.True(pending.All(value => value.Status == TelemetryUploadStatus.PENDING));
                AssertEx.True(pending.All(value => value.Endpoint == "v1/telemetry/chunks"));
                AssertEx.True(pending.All(value => value.SessionId == identity.SessionId));
                AssertEx.True(pending.All(value => value.SessionFingerprint == identity.SessionFingerprint));
                AssertEx.True(pending.All(value => value.WitnessId == identity.WitnessId));
                AssertEx.True(pending.All(value => value.AttemptId == identity.AttemptId));

                foreach (TelemetryPendingUploadMetadata metadata in pending)
                {
                    string chunkPath = Path.Combine(directory.Root, metadata.RelativeChunkPath);
                    byte[] compressed = File.ReadAllBytes(chunkPath);
                    AssertEx.True(compressed.Length > 2 && compressed[0] == 0x1f && compressed[1] == 0x8b);
                    AssertEx.Equal(metadata.CompressedSha256, TelemetryChunkSerializer.Sha256(compressed));
                    byte[] payload;
                    using (var stream = File.OpenRead(chunkPath)) payload = TelemetryChunkSerializer.Gunzip(stream);
                    AssertEx.Equal(metadata.PayloadSha256, TelemetryChunkSerializer.Sha256(payload));
                    using (JsonDocument raw = JsonDocument.Parse(payload))
                    {
                        AssertEx.Equal("ams2-telemetry-chunk-v1", raw.RootElement.GetProperty("schema").GetString());
                        AssertEx.Equal(metadata.StreamType.ToString(), raw.RootElement.GetProperty("streamType").GetString());
                        AssertEx.Equal(metadata.Visibility.ToString(), raw.RootElement.GetProperty("visibility").GetString());
                        AssertEx.True(raw.RootElement.TryGetProperty("quality", out _));
                        AssertEx.True(raw.RootElement.TryGetProperty("data", out _));
                    }
                    TelemetryChunkEnvelope envelope = TelemetryChunkSerializer.Deserialize(payload);
                    AssertEx.Equal("ams2-telemetry-chunk-v1", envelope.Schema);
                    AssertEx.Equal(metadata.StreamType, envelope.StreamType);
                    AssertEx.Equal(metadata.ChunkId, envelope.ChunkId);
                    AssertEx.Equal("MONOTONIC_CAPTURE_CLOCK", envelope.Quality.ClockSource);
                }

                TelemetryPendingUploadMetadata replayMetadata =
                    pending.Single(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY);
                TelemetryChunkEnvelope replay = Read(directory.Root, replayMetadata);
                AssertEx.Equal(4, replay.Data.Rows.Count);
                AssertEx.Equal(2, replay.Data.Dictionaries["names"].Length);
                AssertEx.Equal("sessionElapsedMs", replay.Data.Fields[0]);
                AssertEx.True(replay.Data.Fields.Contains("headingRadians"));
                AssertEx.True(replay.Data.Fields.Contains("speedMetersPerSecond"));
                AssertEx.True(replay.Data.Rows.All(row => row.Length == replay.Data.Fields.Length));
                AssertEx.True(replayMetadata.CompressedBytes < replayMetadata.UncompressedBytes);
                AssertEx.Equal(0, Directory.GetFiles(directory.Root, "*.tmp-*", SearchOption.AllDirectories).Length);

                TelemetryChunkEnvelope driver = Read(
                    directory.Root,
                    pending.Single(value => value.StreamType == TelemetryStreamType.DRIVER_TELEMETRY));
                AssertEx.Equal(6, driver.Data.Rows.Count);
                AssertEx.Equal(TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS, driver.Visibility);
                AssertEx.True(driver.Data.Fields.Contains("unfilteredBrake"));
                AssertEx.True(driver.Data.Fields.Contains("tyrePressureFrontLeftKpa"));
                string driverJson = System.Text.Encoding.UTF8.GetString(TelemetryChunkSerializer.Serialize(driver));
                AssertEx.False(driverJson.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
                AssertEx.False(driverJson.Contains("WindowsUsername", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            AssertEx.True(archive.CompletionReport.FinalizeAcknowledged);
        }

        private static void CadenceAndDropQuality()
        {
            using var directory = new TemporaryDirectory("future-telemetry-quality");
            var archive = new LocalDurableTelemetryArchive(directory.Root, FixedIdentity("quality"));
            try
            {
                AssertEx.True(archive.TryCaptureFrame(Frame(0, 1, true)));
                AssertEx.True(archive.TryCaptureFrame(Frame(500, 1, true)));
                AssertEx.True(archive.TryCaptureFrame(Frame(400, 1, true)));
                archive.FlushAsync().GetAwaiter().GetResult();
                IReadOnlyList<TelemetryPendingUploadMetadata> pending = archive.ScanPending();
                TelemetryPendingUploadMetadata replay = pending.Single(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY);
                AssertEx.Equal(3, replay.Quality.ExpectedSampleCount);
                AssertEx.Equal(2, replay.Quality.ActualSampleCount);
                AssertEx.Equal(1, replay.Quality.MissingSamples);
                AssertEx.Equal(1, replay.Quality.DroppedInputMessages);
                TelemetryPendingUploadMetadata driver = pending.Single(value => value.StreamType == TelemetryStreamType.DRIVER_TELEMETRY);
                AssertEx.Equal(11, driver.Quality.ExpectedSampleCount);
                AssertEx.Equal(2, driver.Quality.ActualSampleCount);
                AssertEx.Equal(9, driver.Quality.MissingSamples);
                AssertEx.Equal("PARTIAL", driver.Quality.CaptureCompleteness);
                AssertEx.True(archive.Counters.DroppedMessages >= 1);
                TelemetryArchiveStreamCompletion replayCompletion = archive.CompletionReport.Streams
                    .Single(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY);
                AssertEx.True(replayCompletion.ArchiveInputLosses >= 1);
                AssertEx.True(replayCompletion.CadenceMissedSamples >= 1);
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private static void UnresolvedViewedVehicleIsPrivateGate()
        {
            using var directory = new TemporaryDirectory("future-telemetry-local-gate");
            var archive = new LocalDurableTelemetryArchive(directory.Root, FixedIdentity("local-gate"));
            try
            {
                TelemetryFrameSample frame = Frame(0, 1, false);
                AssertEx.NotNull(frame.LocalDriver);
                AssertEx.False(frame.LocalDriver!.LocalParticipantResolved);
                AssertEx.True(archive.TryCaptureFrame(frame));
                archive.FlushAsync().GetAwaiter().GetResult();
                IReadOnlyList<TelemetryPendingUploadMetadata> pending = archive.ScanPending();
                AssertEx.True(pending.Any(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY));
                AssertEx.False(pending.Any(value => value.StreamType == TelemetryStreamType.DRIVER_TELEMETRY));
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private static void IncidentBurstIsBoundedAndFiltered()
        {
            using var directory = new TemporaryDirectory("future-telemetry-incident");
            var options = new TelemetryArchiveOptions
            {
                IncidentPreRollMs = 100,
                IncidentPostRollMs = 100,
                IncidentRingDurationMs = 200
            };
            var archive = new LocalDurableTelemetryArchive(directory.Root, FixedIdentity("incident"), options);
            try
            {
                int[] elapsedValues = { 0, 50, 100, 125, 150, 200, 250 };
                foreach (int elapsed in elapsedValues)
                {
                    TelemetryFrameSample frame = Frame(elapsed, 8, false);
                    frame.Participants[2].WorldX += 1_000;
                    frame.Participants[2].WorldZ += 1_000;
                    if (elapsed == 125)
                    {
                        frame.IncidentCandidate = new IncidentCandidateSample
                        {
                            CandidateId = "incident-filtered",
                            TriggerCode = "RAW_FLAG_AND_DISAPPEARANCE",
                            RelatedParticipantRefs = new[] { 1 }
                        };
                    }
                    archive.TryCaptureFrame(frame);
                }
                archive.FlushAsync().GetAwaiter().GetResult();
                TelemetryPendingUploadMetadata metadata = archive.ScanPending()
                    .Single(value => value.StreamType == TelemetryStreamType.INCIDENT_TRACE);
                TelemetryChunkEnvelope incident = Read(directory.Root, metadata);
                AssertEx.Equal(25, incident.Data.Rows.Count);
                int relativeIndex = Array.IndexOf(incident.Data.Fields, "relativeTimeMs");
                int participantIndex = Array.IndexOf(incident.Data.Fields, "participantRef");
                AssertEx.Equal(-75.0, incident.Data.Rows.First()[relativeIndex]!.Value);
                AssertEx.Equal(75.0, incident.Data.Rows.Last()[relativeIndex]!.Value);
                AssertEx.True(incident.Data.Rows.All(row =>
                    row[participantIndex] == 1 || row[participantIndex] == 2 ||
                    row[participantIndex] == 4 || row[participantIndex] == 5 ||
                    row[participantIndex] == 6));
                AssertEx.False(incident.Data.Rows.Any(row => row[participantIndex] == 3));
                AssertEx.False(incident.Data.Rows.Any(row => row[participantIndex] == 7 || row[participantIndex] == 8));
                AssertEx.Equal(TelemetryVisibility.PUBLIC_REPLAY, incident.Visibility);
                AssertEx.False(incident.Data.Fields.Any(value => value.Contains("fault", StringComparison.OrdinalIgnoreCase)));
                AssertEx.False(incident.Data.Fields.Any(value => value.Contains("blame", StringComparison.OrdinalIgnoreCase)));
                AssertEx.True(incident.Data.Fields.Contains("headingRadians"));
                AssertEx.True(incident.Data.Fields.Contains("speedMetersPerSecond"));
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private static void ThirtySecondChunking()
        {
            using var directory = new TemporaryDirectory("future-telemetry-chunking");
            var archive = new LocalDurableTelemetryArchive(
                directory.Root,
                FixedIdentity("chunking"),
                new TelemetryArchiveOptions { InputChannelCapacity = 8_192 });
            try
            {
                for (int elapsed = 0; elapsed <= 90_000; elapsed += 200)
                {
                    AssertEx.True(archive.TryCaptureFrame(Frame(elapsed, 32, false)));
                }
                archive.FlushAsync().GetAwaiter().GetResult();
                TelemetryPendingUploadMetadata[] replay = archive.ScanPending()
                    .Where(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY)
                    .OrderBy(value => value.ChunkIndex)
                    .ToArray();
                AssertEx.Equal(4, replay.Length);
                AssertEx.Equal(0, replay[0].ChunkIndex);
                AssertEx.Equal(3, replay[3].ChunkIndex);
                AssertEx.Equal(4_800, replay[0].Quality.ActualSampleCount);
                AssertEx.Equal(32, replay[3].Quality.ActualSampleCount);
                AssertEx.True(replay.All(value => value.CompressedBytes < value.UncompressedBytes));
                AssertEx.Equal(0L, archive.Counters.DroppedMessages);
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private static void CrashRecovery()
        {
            using var directory = new TemporaryDirectory("future-telemetry-recovery");
            TelemetryArchiveIdentity identity = FixedIdentity("recovery");
            var archive = new LocalDurableTelemetryArchive(directory.Root, identity);
            TelemetryPendingUploadMetadata pending;
            try
            {
                archive.TryCaptureFrame(Frame(0, 1, false));
                archive.FlushAsync().GetAwaiter().GetResult();
                pending = archive.ScanPending().Single(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY);
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            string chunkPath = Path.Combine(directory.Root, pending.RelativeChunkPath);
            string metadataPath = chunkPath.Substring(0, chunkPath.Length - ".json.gz".Length) + ".upload.json";
            File.Delete(metadataPath);
            string interrupted = chunkPath + ".tmp-interrupted-fixture";
            File.WriteAllText(interrupted, "partial");

            var store = new TelemetryChunkStore(directory.Root, identity);
            TelemetryArchiveRecoveryReport report = store.Recover();
            AssertEx.Equal(1, report.ValidChunks);
            AssertEx.Equal(1, report.RebuiltPendingMetadata);
            AssertEx.Equal(1, report.PreservedTemporaryFiles);
            AssertEx.True(File.Exists(metadataPath));
            AssertEx.False(File.Exists(interrupted));
            AssertEx.Equal(1, store.ScanPending().Count);
            AssertEx.Equal(0, report.Issues.Count);
        }

        private static void DuplicateAndConflict()
        {
            using var directory = new TemporaryDirectory("future-telemetry-conflict");
            TelemetryArchiveIdentity identity = FixedIdentity("conflict");
            var archive = new LocalDurableTelemetryArchive(directory.Root, identity);
            TelemetryPendingUploadMetadata metadata;
            try
            {
                archive.TryCaptureFrame(Frame(0, 1, false));
                archive.FlushAsync().GetAwaiter().GetResult();
                metadata = archive.ScanPending().Single(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY);
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            var store = new TelemetryChunkStore(directory.Root, identity);
            TelemetryChunkEnvelope envelope = Read(directory.Root, metadata);
            AssertEx.Equal(TelemetryChunkCommitDisposition.DUPLICATE, store.Commit(envelope).Disposition);
            envelope.Data.Rows[0][7] = envelope.Data.Rows[0][7]!.Value + 1;
            TelemetryChunkCommitOutcome conflict = store.Commit(envelope);
            AssertEx.Equal(TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED, conflict.Disposition);
            AssertEx.True(File.Exists(conflict.ChunkPath));
            AssertEx.Equal(1, store.ScanPending().Count);
        }

        private static void UploadQueueVerifiesAndMarksSent()
        {
            using var directory = new TemporaryDirectory("future-telemetry-upload-queue");
            var archive = new LocalDurableTelemetryArchive(directory.Root, FixedIdentity("upload-queue"));
            try
            {
                AssertEx.True(archive.TryCaptureFrame(Frame(0, 2, false)));
                archive.FlushAsync().GetAwaiter().GetResult();
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            var queue = new TelemetryChunkUploadQueue(directory.Root);
            TelemetryChunkUploadItem item = queue.GetDueBatch(4, At(1_000)).Single();
            AssertEx.True(item.CompressedPayload.Length > 2);
            AssertEx.Equal(0x1f, item.CompressedPayload.Span[0]);
            AssertEx.Equal(0x8b, item.CompressedPayload.Span[1]);
            queue.MarkSent(item, At(1_000), false);
            AssertEx.Equal(0, queue.GetDueBatch(4, At(2_000)).Count);
            TelemetryPendingUploadMetadata stored = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(item.MetadataPath));
            AssertEx.Equal(TelemetryUploadStatus.SENT, stored.Status);
            AssertEx.Equal(1, stored.AttemptCount);
            AssertEx.True(File.Exists(item.ChunkPath));
        }

        private static void UploadWorkerRetriesThenSends()
        {
            using var directory = new TemporaryDirectory("future-telemetry-upload-retry");
            var archive = new LocalDurableTelemetryArchive(directory.Root, FixedIdentity("upload-retry"));
            try
            {
                AssertEx.True(archive.TryCaptureFrame(Frame(0, 1, false)));
                archive.FlushAsync().GetAwaiter().GetResult();
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            var queue = new TelemetryChunkUploadQueue(directory.Root);
            var transport = new SequenceTelemetryTransport(
                TelemetryChunkUploadTransportResult.Failure(null, "NETWORK_UNAVAILABLE", true),
                TelemetryChunkUploadTransportResult.Stored(200, true));
            var worker = new TelemetryChunkUploadWorker(queue, transport);
            TelemetryChunkUploadBatchResult first = worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
            AssertEx.Equal(1, first.Retryable);
            TelemetryChunkUploadItem pending = LoadOnlyUploadItem(directory.Root);
            TelemetryPendingUploadMetadata retryState = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(pending.MetadataPath));
            AssertEx.Equal(TelemetryUploadStatus.FAILED_RETRYABLE, retryState.Status);
            AssertEx.NotNull(retryState.NextAttemptAtUtc);
            retryState.NextAttemptAtUtc = DateTimeOffset.UnixEpoch;
            File.WriteAllBytes(pending.MetadataPath, TelemetryChunkSerializer.SerializeMetadata(retryState));
            TelemetryChunkUploadBatchResult second = worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
            AssertEx.Equal(1, second.Sent);
            TelemetryPendingUploadMetadata sentState = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(pending.MetadataPath));
            AssertEx.Equal(TelemetryUploadStatus.SENT, sentState.Status);
            AssertEx.Equal(2, sentState.AttemptCount);
            AssertEx.Equal(2, transport.Calls);
        }

        private static void PrivateUploadDefaultsToDenied()
        {
            using var directory = new TemporaryDirectory("future-telemetry-private-deny");
            var archive = new LocalDurableTelemetryArchive(directory.Root, FixedIdentity("private-deny"));
            try
            {
                AssertEx.True(archive.TryCaptureFrame(Frame(0, 1, true)));
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            string[] sidecars = Directory.EnumerateFiles(
                directory.Root,
                "*.upload.json",
                SearchOption.AllDirectories).ToArray();
            foreach (string path in sidecars)
            {
                TelemetryPendingUploadMetadata metadata =
                    TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(path));
                if (metadata.Visibility == TelemetryVisibility.PUBLIC_REPLAY)
                {
                    metadata.Status = TelemetryUploadStatus.SENT;
                    File.WriteAllBytes(path, TelemetryChunkSerializer.SerializeMetadata(metadata));
                }
            }

            var transport = new SequenceTelemetryTransport(TelemetryChunkUploadTransportResult.Stored(201, false));
            var deniedWorker = new TelemetryChunkUploadWorker(
                new TelemetryChunkUploadQueue(directory.Root),
                transport);
            TelemetryChunkUploadBatchResult denied = deniedWorker
                .ProcessDueAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            AssertEx.Equal(0, denied.Attempted);
            AssertEx.Equal(0, transport.Calls);

            TelemetryPendingUploadMetadata privateMetadata = sidecars
                .Select(path => TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(path)))
                .Single(value => value.Visibility == TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS);
            AssertEx.Equal(TelemetryUploadStatus.LOCAL_PENDING_OWNER, privateMetadata.Status);
            AssertEx.Equal("PRIVATE_OWNER_AUTHORITY_REQUIRED", privateMetadata.LastError);

            var authorizedQueue = new TelemetryChunkUploadQueue(directory.Root, new ExplicitTestAuthority());
            TelemetryChunkUploadItem authorized = authorizedQueue
                .GetDueBatch(1, DateTimeOffset.UtcNow)
                .Single();
            AssertEx.Equal(TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS, authorized.Metadata.Visibility);
        }

        private static void ArchiveFailureStagesAreDistinct()
        {
            using var serializationDirectory = new TemporaryDirectory("future-telemetry-serialization-failure");
            var serialization = new LocalDurableTelemetryArchive(
                serializationDirectory.Root,
                FixedIdentity("serialization-failure"),
                null,
                _ => throw new JsonException("fixture serialization failure"),
                null);
            AssertEx.True(serialization.TryCaptureFrame(Frame(0, 1, false)));
            AssertDisposeFails(serialization);
            TelemetryArchiveStreamCompletion serializationReport = serialization.CompletionReport.Streams
                .Single(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY);
            AssertEx.True(serializationReport.SerializationFailures > 0);
            AssertEx.True(serializationReport.FinalizeFailures > 0);
            AssertEx.False(serialization.CompletionReport.FinalizeAcknowledged);

            using var diskDirectory = new TemporaryDirectory("future-telemetry-disk-failure");
            var disk = new LocalDurableTelemetryArchive(
                diskDirectory.Root,
                FixedIdentity("disk-failure"),
                null,
                _ => throw new IOException("fixture disk failure"),
                null);
            AssertEx.True(disk.TryCaptureFrame(Frame(0, 1, false)));
            AssertDisposeFails(disk);
            TelemetryArchiveStreamCompletion diskReport = disk.CompletionReport.Streams
                .Single(value => value.StreamType == TelemetryStreamType.PARTICIPANT_REPLAY);
            AssertEx.True(diskReport.DiskWriteFailures > 0);
            AssertEx.True(diskReport.FinalizeFailures > 0);

            using var finalizeDirectory = new TemporaryDirectory("future-telemetry-finalize-failure");
            var finalize = new LocalDurableTelemetryArchive(
                finalizeDirectory.Root,
                FixedIdentity("finalize-failure"),
                null,
                null,
                () => throw new InvalidOperationException("fixture finalize failure"));
            AssertEx.True(finalize.TryCaptureFrame(Frame(0, 1, false)));
            AssertDisposeFails(finalize);
            AssertEx.True(finalize.CompletionReport.Streams.Sum(value => value.FinalizeFailures) > 0);
            AssertEx.False(finalize.CompletionReport.FinalizeAcknowledged);

            using var conflictDirectory = new TemporaryDirectory("future-telemetry-commit-conflict");
            var conflict = new LocalDurableTelemetryArchive(
                conflictDirectory.Root,
                FixedIdentity("commit-conflict"),
                null,
                envelope => new TelemetryChunkCommitOutcome
                {
                    Disposition = TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED
                },
                null);
            AssertEx.True(conflict.TryCaptureFrame(Frame(0, 1, false)));
            conflict.DisposeAsync().AsTask().GetAwaiter().GetResult();
            AssertEx.True(conflict.CompletionReport.Streams.Sum(value => value.CommitConflicts) > 0);
            AssertEx.False(conflict.CompletionReport.Streams.Any(value => value.DurableCommitAcks > 0));
        }

        private static void AssertDisposeFails(LocalDurableTelemetryArchive archive)
        {
            bool failed = false;
            try
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                failed = true;
            }
            AssertEx.True(failed, "Injected archive finalize failure must be acknowledged as a failure.");
        }

        private static TelemetryChunkUploadItem LoadOnlyUploadItem(string root)
        {
            var queue = new TelemetryChunkUploadQueue(root);
            TelemetryPendingUploadMetadata metadata = Directory.EnumerateFiles(root, "*.upload.json", SearchOption.AllDirectories)
                .Select(path => TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(path)))
                .Single();
            metadata.NextAttemptAtUtc = DateTimeOffset.UnixEpoch;
            string metadataPath = Directory.EnumerateFiles(root, "*.upload.json", SearchOption.AllDirectories).Single();
            File.WriteAllBytes(metadataPath, TelemetryChunkSerializer.SerializeMetadata(metadata));
            return queue.GetDueBatch(1, DateTimeOffset.UtcNow).Single();
        }

        private static SessionMetadataSample Metadata(long elapsedMs)
        {
            var sample = new SessionMetadataSample
            {
                CapturedAtUtc = At(elapsedMs),
                SessionElapsedMs = elapsedMs,
                GameBuild = 3398,
                SharedMemoryVersion = 14,
                ClientVersion = "0.2.2",
                ParserVersion = "fixture-v14",
                Track = "Monza",
                Layout = "Monza_2020",
                TrackLengthMeters = 5793,
                SessionType = "RACE",
                ClockSource = "MONOTONIC_CAPTURE_CLOCK",
                JoinedMidSession = true,
                SessionStartOffsetStatus = TelemetryCapabilityState.UNKNOWN,
                ObservedParticipants = 2,
                CaptureStarted = true,
                CaptureCompleteness = "PARTIAL_START_OFFSET_UNKNOWN"
            };
            sample.Fields["weather.rainDensity"] = new TelemetryCapabilityValue
            {
                State = TelemetryCapabilityState.OBSERVED_ONLY,
                NumericValue = 0,
                Unit = "ratio"
            };
            return sample;
        }

        private static TelemetryFrameSample Frame(int elapsedMs, int participants, bool localResolved)
        {
            var rows = new List<ReplayParticipantSample>();
            for (int index = 0; index < participants; index++)
            {
                rows.Add(new ReplayParticipantSample
                {
                    ParticipantRef = index + 1,
                    Slot = index,
                    Generation = 1,
                    NameSnapshot = "Driver " + (index + 1),
                    VehicleRef = "vehicle-gt3",
                    VehicleClassRef = "GT3",
                    Lap = 2,
                    LapDistanceMeters = 1000 + elapsedMs * 0.05 + index,
                    RacePosition = index + 1,
                    WorldX = elapsedMs * 0.01 + index,
                    WorldY = 1.5,
                    WorldZ = elapsedMs * 0.02 - index,
                    RaceStateRaw = 2,
                    PitStateRaw = 0,
                    HeadingRadians = 0.25 + index * 0.01,
                    SpeedMetersPerSecond = 60 - index * 0.5
                });
            }
            return new TelemetryFrameSample
            {
                CapturedAtUtc = At(elapsedMs),
                SessionElapsedMs = elapsedMs,
                Participants = rows,
                RaceStateRaw = 2,
                FlagColourRaw = elapsedMs == 100 ? 2 : 0,
                FlagReasonRaw = elapsedMs == 100 ? 7 : 0,
                PositionChangeMagnitude = elapsedMs == 100 ? 2 : 0,
                LocalDriver = new DriverTelemetrySample
                {
                    LocalParticipantResolved = localResolved,
                    SourceParticipantRef = localResolved ? 1 : (int?)null,
                    DriverRef = 1,
                    Lap = 2,
                    Sector = 1,
                    LapDistanceMeters = 1000 + elapsedMs * 0.05,
                    WorldX = elapsedMs * 0.01,
                    WorldY = 1.5,
                    WorldZ = elapsedMs * 0.02,
                    SpeedMetersPerSecond = 60,
                    Rpm = 6500,
                    GearRaw = 4,
                    Throttle = 0.7,
                    Brake = elapsedMs == 100 ? 0.8 : 0,
                    Steering = -0.1,
                    Clutch = 0,
                    UnfilteredThrottle = 0.72,
                    UnfilteredBrake = elapsedMs == 100 ? 0.82 : 0,
                    UnfilteredSteering = -0.12,
                    UnfilteredClutch = 0,
                    LongitudinalAccelerationMetersPerSecondSquared = -2.1,
                    LateralAccelerationMetersPerSecondSquared = 4.2,
                    VerticalAccelerationMetersPerSecondSquared = 0.1,
                    TyreTemperaturesCelsius = new double?[] { 84, 85, 82, 83 },
                    TyrePressuresKpa = new double?[] { 180, 181, 178, 179 },
                    TyreWear = new double?[] { 0.98, 0.98, 0.99, 0.99 },
                    TrackTemperatureCelsius = 32,
                    AmbientTemperatureCelsius = 24,
                    RainDensity = 0,
                    PitStateRaw = 0,
                    LapValid = true,
                    CurrentLapTimeMs = elapsedMs
                }
            };
        }

        private static TelemetryArchiveIdentity FixedIdentity(string suffix)
            => new TelemetryArchiveIdentity
            {
                SessionId = "capture-session-" + suffix,
                SessionFingerprint = "session-fingerprint-" + suffix,
                WitnessId = "witness-" + suffix,
                AttemptId = "attempt-" + suffix,
                AttemptNumber = 1
            };

        private static DateTimeOffset At(long elapsedMs)
            => new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(elapsedMs);

        private static TelemetryChunkEnvelope Read(string root, TelemetryPendingUploadMetadata metadata)
        {
            using FileStream stream = File.OpenRead(Path.Combine(root, metadata.RelativeChunkPath));
            return TelemetryChunkSerializer.Deserialize(TelemetryChunkSerializer.Gunzip(stream));
        }

        private sealed class MutableMonotonicClock : ITelemetryMonotonicClock
        {
            public long Timestamp { get; set; }
            public long Frequency { get; set; }
        }

        private sealed class SequenceTelemetryTransport : ITelemetryChunkUploadTransport
        {
            private readonly Queue<TelemetryChunkUploadTransportResult> _results;

            public SequenceTelemetryTransport(params TelemetryChunkUploadTransportResult[] results)
            {
                _results = new Queue<TelemetryChunkUploadTransportResult>(results);
            }

            public int Calls { get; private set; }

            public Task<TelemetryChunkUploadTransportResult> SendTelemetryChunkAsync(
                TelemetryChunkUploadItem item,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                AssertEx.True(item.CompressedPayload.Length > 0);
                if (_results.Count == 0) throw new InvalidOperationException("No fake telemetry transport result remains.");
                return Task.FromResult(_results.Dequeue());
            }
        }

        private sealed class ExplicitTestAuthority : IPrivateTelemetryUploadAuthority
        {
            public bool IsUploadAuthorized(TelemetryPendingUploadMetadata metadata)
                => metadata.Visibility == TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS;
        }
    }
}
