using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AMS2LeagueClient.Core.CompactTelemetry;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    /// <summary>
    /// Production conversion boundary from in-memory P023 catalog rows to A2CT.
    /// High-rate rows are never serialized as JSON. Session metadata remains a
    /// low-rate JSON compatibility record until every metadata string has a V1
    /// compact schema home.
    /// </summary>
    internal sealed class CompactTelemetryChunkStore
    {
        internal const string CompactContentType = "application/vnd.ams2.compact-telemetry-v1";
        private const int SequenceStride = 32;
        private readonly string _root;
        private readonly string _sessionRoot;
        private readonly TelemetryArchiveIdentity _identity;
        private readonly TelemetryChunkStore _legacyStore;
        private readonly uint _sessionLocalId;
        private readonly uint _attemptLocalId;
        private readonly Dictionary<int, ushort> _participantRefs = new Dictionary<int, ushort>();
        private readonly List<CompactParticipantDictionaryEntry> _participants =
            new List<CompactParticipantDictionaryEntry>();
        private readonly Dictionary<int, int?> _lastReplayPosition = new Dictionary<int, int?>();
        private readonly Dictionary<int, int?> _lastReplayPit = new Dictionary<int, int?>();
        private readonly Dictionary<int, double?> _lastDriverChangeValues = new Dictionary<int, double?>();
        private readonly HashSet<int> _geometryBins = new HashSet<int>();
        private long? _nextReplayBaseMs;
        private long? _nextReplayWorldMs;
        private long? _nextReplayExtensionMs;
        private long? _nextReplayBattleMs;
        private int _participantDictionaryRevision;
        private int _participantDictionaryEmittedRevision;

        public CompactTelemetryChunkStore(string root, TelemetryArchiveIdentity identity)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Archive root is required.", nameof(root));
            _root = Path.GetFullPath(root);
            _identity = (identity ?? throw new ArgumentNullException(nameof(identity))).ValidatedCopy();
            _legacyStore = new TelemetryChunkStore(_root, _identity);
            _sessionRoot = _legacyStore.SessionDirectory;
            _sessionLocalId = StableUInt32(_identity.SessionFingerprint);
            _attemptLocalId = StableUInt32(_identity.AttemptId);
        }

        public TelemetryChunkCommitOutcome Commit(TelemetryChunkEnvelope source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var outcomes = new List<TelemetryChunkCommitOutcome>();
            if (source.StreamType == TelemetryStreamType.SESSION_METADATA)
            {
                // Metadata records contain low-frequency text/capability state that
                // has no immutable V1 numeric field yet. Preserve it losslessly.
                outcomes.Add(_legacyStore.Commit(source));
            }

            int artifactIndex = 0;
            foreach (CompactArtifact artifact in BuildArtifacts(source))
            {
                uint sequence = checked((uint)(source.ChunkIndex * SequenceStride
                    + ((int)source.StreamType * 6) + artifactIndex));
                outcomes.Add(CommitArtifact(source, artifact, sequence));
                artifactIndex++;
            }

            if (outcomes.Count == 0)
            {
                throw new InvalidDataException("Telemetry source chunk produced no durable output.");
            }
            return Aggregate(outcomes);
        }

        /// <summary>
        /// Persists the acknowledged attempt-close contract after every regular
        /// stream chunk has reached durable storage. Offsets 30 and 31 are
        /// reserved inside the final 32-sequence bucket so these facts cannot
        /// collide with the regular stream artifacts (whose highest offset is 24).
        /// ATTEMPT_FINALIZE_V1 is deliberately written last: its presence is the
        /// durable acknowledgement that the preceding ledger was also committed.
        /// </summary>
        internal IReadOnlyList<TelemetryChunkCommitOutcome> CommitAttemptIntegrity(
            TelemetryAttemptLossLedger ledger,
            long endElapsedMs,
            DateTimeOffset capturedAtUtc,
            int chunkDurationMs)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (endElapsedMs < 0) throw new ArgumentOutOfRangeException(nameof(endElapsedMs));
            if (chunkDurationMs <= 0) throw new ArgumentOutOfRangeException(nameof(chunkDurationMs));
            if (!ledger.CloseRequested || !ledger.FinalizeAcknowledged)
            {
                throw new InvalidDataException("Compact attempt integrity requires an acknowledged close.");
            }
            if (!string.Equals(ledger.SessionId, _identity.SessionId, StringComparison.Ordinal)
                || !string.Equals(ledger.SessionFingerprint, _identity.SessionFingerprint, StringComparison.Ordinal)
                || !string.Equals(ledger.WitnessId, _identity.WitnessId, StringComparison.Ordinal)
                || !string.Equals(ledger.AttemptId, _identity.AttemptId, StringComparison.Ordinal)
                || ledger.AttemptNumber != _identity.AttemptNumber)
            {
                throw new InvalidDataException("Compact attempt integrity identity does not match the archive.");
            }

            long bucket = endElapsedMs / chunkDurationMs;
            uint sequenceBase = checked((uint)bucket * SequenceStride);
            uint ledgerSequence = checked(sequenceBase + 30U);
            uint finalizeSequence = checked(sequenceBase + 31U);
            var source = new TelemetryChunkEnvelope
            {
                ChunkId = "compact-attempt-integrity",
                StreamType = TelemetryStreamType.SESSION_METADATA,
                Visibility = TelemetryVisibility.PUBLIC_REPLAY,
                SessionId = _identity.SessionId,
                SessionFingerprint = _identity.SessionFingerprint,
                WitnessId = _identity.WitnessId,
                AttemptId = _identity.AttemptId,
                AttemptNumber = _identity.AttemptNumber,
                ScheduledEventHint = _identity.ScheduledEventHint,
                ChunkIndex = checked((int)ledgerSequence),
                StartElapsedMs = endElapsedMs,
                EndElapsedMs = endElapsedMs,
                FirstCapturedAtUtc = capturedAtUtc,
                LastCapturedAtUtc = capturedAtUtc,
                Quality = new TelemetryChunkQuality
                {
                    TargetSampleRateHz = 0,
                    ExpectedSampleCount = 1,
                    ActualSampleCount = 1,
                    CaptureCompleteness = ledger.Completeness.ToString(),
                    SourceWitnessCount = 1
                }
            };

            CompactTelemetrySample[] lossRows = BuildLossRows(ledger, endElapsedMs);
            var lossArtifact = new CompactArtifact(
                "integrity",
                CompactTelemetrySchemaId.LossLedgerV1,
                lossRows,
                0,
                null,
                null);
            TelemetryChunkCommitOutcome lossOutcome = CommitArtifact(source, lossArtifact, ledgerSequence);
            var outcomes = new List<TelemetryChunkCommitOutcome> { lossOutcome };
            if (lossOutcome.Disposition == TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED)
            {
                return outcomes;
            }

            long acceptedWork = ledger.Streams.Sum(value => value.AcceptedWorkUnits);
            long durableWork = ledger.Streams
                .Where(value => value.DurableProcessingAcknowledged)
                .Sum(value => value.AcceptedWorkUnits);
            long knownLoss = ledger.KnownLossCount;
            var finalizeArtifact = new CompactArtifact(
                "integrity",
                CompactTelemetrySchemaId.AttemptFinalizeV1,
                new[]
                {
                    new CompactTelemetrySample(endElapsedMs, new double?[]
                    {
                        ProtocolCount(acceptedWork, "accepted work"),
                        ProtocolCount(durableWork, "durable work"),
                        ProtocolCount(knownLoss, "known loss"),
                        (byte)CompletenessCode(ledger.Completeness)
                    })
                },
                0,
                null,
                null);
            outcomes.Add(CommitArtifact(source, finalizeArtifact, finalizeSequence));
            return outcomes;
        }

        private static CompactTelemetrySample[] BuildLossRows(
            TelemetryAttemptLossLedger ledger,
            long elapsedMs)
        {
            var rows = new List<CompactTelemetrySample>();
            foreach (TelemetryStreamLossLedger stream in ledger.Streams.OrderBy(value => value.StreamType))
            {
                CompactTelemetryLossReasonCode reason = LossReasonCode(stream.StreamType);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.OuterQueueDrop,
                    stream.OuterQueueLosses, reason);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.ArchiveInputDrop,
                    stream.ArchiveInputLosses, reason);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.CadenceMissed,
                    stream.CadenceMissedSamples, reason);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.SerializationFailure,
                    stream.SerializationFailures, reason);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.DiskWriteFailure,
                    stream.DiskWriteFailures, reason);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.WorkerException,
                    stream.WorkerExceptions, reason);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.UploadFailure,
                    stream.UploadFailures, reason);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.FinalizeFailure,
                    stream.FinalizeFailures, reason);
                AddLoss(rows, elapsedMs, CompactTelemetryLossSourceCode.CommitConflict,
                    stream.CommitConflicts, reason);
            }
            if (rows.Count == 0)
            {
                rows.Add(new CompactTelemetrySample(elapsedMs, new double?[]
                {
                    (byte)CompactTelemetryLossSourceCode.None,
                    0,
                    (ushort)CompactTelemetryLossReasonCode.None
                }));
            }
            return rows.ToArray();
        }

        private static void AddLoss(
            ICollection<CompactTelemetrySample> rows,
            long elapsedMs,
            CompactTelemetryLossSourceCode source,
            long count,
            CompactTelemetryLossReasonCode reason)
        {
            if (count <= 0) return;
            rows.Add(new CompactTelemetrySample(elapsedMs, new double?[]
            {
                (byte)source,
                ProtocolCount(count, source.ToString()),
                (ushort)reason
            }));
        }

        private static int ProtocolCount(long value, string name)
        {
            if (value < 0 || value > int.MaxValue)
            {
                throw new InvalidDataException("Compact " + name + " is outside the V1 count range.");
            }
            return checked((int)value);
        }

        private static CompactTelemetryLossReasonCode LossReasonCode(TelemetryStreamType stream)
            => stream switch
            {
                TelemetryStreamType.SESSION_METADATA => CompactTelemetryLossReasonCode.SessionMetadata,
                TelemetryStreamType.RACE_STORY => CompactTelemetryLossReasonCode.RaceStory,
                TelemetryStreamType.PARTICIPANT_REPLAY => CompactTelemetryLossReasonCode.ParticipantReplay,
                TelemetryStreamType.DRIVER_TELEMETRY => CompactTelemetryLossReasonCode.DriverTelemetry,
                TelemetryStreamType.INCIDENT_TRACE => CompactTelemetryLossReasonCode.IncidentTrace,
                _ => throw new ArgumentOutOfRangeException(nameof(stream))
            };

        private static CompactTelemetryCompletenessCode CompletenessCode(
            TelemetryAttemptCompleteness completeness)
            => completeness switch
            {
                TelemetryAttemptCompleteness.IN_PROGRESS => CompactTelemetryCompletenessCode.InProgress,
                TelemetryAttemptCompleteness.PARTIAL => CompactTelemetryCompletenessCode.Partial,
                TelemetryAttemptCompleteness.COMPLETE => CompactTelemetryCompletenessCode.Complete,
                _ => throw new ArgumentOutOfRangeException(nameof(completeness))
            };

        private IEnumerable<CompactArtifact> BuildArtifacts(TelemetryChunkEnvelope source)
        {
            switch (source.StreamType)
            {
                case TelemetryStreamType.SESSION_METADATA:
                    return BuildSessionArtifacts(source);
                case TelemetryStreamType.RACE_STORY:
                    return BuildStoryArtifacts(source);
                case TelemetryStreamType.PARTICIPANT_REPLAY:
                    return BuildReplayArtifacts(source);
                case TelemetryStreamType.DRIVER_TELEMETRY:
                    return BuildDriverArtifacts(source);
                case TelemetryStreamType.INCIDENT_TRACE:
                    return BuildIncidentArtifacts(source);
                default:
                    throw new ArgumentOutOfRangeException(nameof(source.StreamType));
            }
        }

        private IReadOnlyList<CompactArtifact> BuildSessionArtifacts(TelemetryChunkEnvelope source)
        {
            SessionMetadataSample? metadata = source.Data.Records?.LastOrDefault();
            if (metadata == null) return Array.Empty<CompactArtifact>();
            EnsureParticipants(metadata.Participants);
            double? maxRpm = CapabilityNumber(metadata, "maxRpm");
            var sample = new CompactTelemetrySample(metadata.SessionElapsedMs, new double?[]
            {
                metadata.TrackLengthMeters,
                maxRpm
            });
            return new[]
            {
                new CompactArtifact(
                    "session",
                    CompactTelemetrySchemaId.SessionStaticV1,
                    new[] { sample },
                    0,
                    TakeParticipantDictionary(),
                    null)
            };
        }

        private IReadOnlyList<CompactArtifact> BuildStoryArtifacts(TelemetryChunkEnvelope source)
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.RaceEventV1);
            Dictionary<string, int> fields = FieldMap(source.Data.Fields);
            CompactTelemetrySample[] samples = source.Data.Rows
                .Select(row => new CompactTelemetrySample(
                    Elapsed(row, fields),
                    Project(schema, row, fields, null)))
                .OrderBy(value => value.ElapsedMs)
                .ToArray();
            if (samples.Length == 0) return Array.Empty<CompactArtifact>();
            var strings = new List<CompactStringDictionaryEntry>();
            AddStrings(strings, source, "eventTypes", CompactStringDictionaryId.EventType);
            AddStrings(strings, source, "eventIds", CompactStringDictionaryId.EventId);
            AddStrings(strings, source, "factCodes", CompactStringDictionaryId.FactCode);
            return new[]
            {
                new CompactArtifact("story", schema.Id, samples, 0, null, strings)
            };
        }

        private IReadOnlyList<CompactArtifact> BuildReplayArtifacts(TelemetryChunkEnvelope source)
        {
            Dictionary<string, int> fields = FieldMap(source.Data.Fields);
            EnsureParticipants(source, fields);
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.ParticipantReplayV1);
            ISet<string> progressFields = Set(
                "participantRef", "lap", "lapDistanceMeters", "racePosition",
                "raceStateRaw", "pitStateRaw", "isActive");
            ISet<string> worldFields = Set(
                "participantRef", "lap", "lapDistanceMeters", "worldX", "worldY", "worldZ",
                "headingRadians", "speedMetersPerSecond");
            var extensionFields = new HashSet<string>(StringComparer.Ordinal) { "participantRef", "lap" };
            foreach (CompactTelemetryField field in schema.Fields)
            {
                if (!progressFields.Contains(field.Name) && !worldFields.Contains(field.Name))
                {
                    extensionFields.Add(field.Name);
                }
            }

            double?[][] orderedRows = source.Data.Rows
                .OrderBy(row => Elapsed(row, fields))
                .ThenBy(row => Number(row, fields, "participantRef") ?? double.MaxValue)
                .ToArray();
            var progress = new List<CompactTelemetrySample>();
            var world = new List<CompactTelemetrySample>();
            var extension = new List<CompactTelemetrySample>();
            var geometry = new List<CompactTelemetrySample>();

            foreach (IGrouping<long, double?[]> group in orderedRows.GroupBy(row => Elapsed(row, fields)))
            {
                double?[][] rows = group.ToArray();
                long elapsed = group.Key;
                bool baseDue = TakeDue(ref _nextReplayBaseMs, elapsed, 2_000);
                bool worldDue = TakeDue(ref _nextReplayWorldMs, elapsed, 5_000);
                bool extensionDue = TakeDue(ref _nextReplayExtensionMs, elapsed, 20_000);
                bool battleDue = TakeDue(ref _nextReplayBattleMs, elapsed, 500);
                bool startBurst = elapsed < 10_000;
                var changed = new HashSet<int>();
                foreach (double?[] row in rows)
                {
                    int sourceRef = Int(row, fields, "participantRef") ?? -1;
                    int? position = Int(row, fields, "racePosition");
                    int? pit = Int(row, fields, "pitStateRaw");
                    if ((_lastReplayPosition.TryGetValue(sourceRef, out int? priorPosition) && priorPosition != position)
                        || (_lastReplayPit.TryGetValue(sourceRef, out int? priorPit) && priorPit != pit))
                    {
                        changed.Add(sourceRef);
                    }
                    _lastReplayPosition[sourceRef] = position;
                    _lastReplayPit[sourceRef] = pit;
                }
                HashSet<int> battle = battleDue ? CloseBattleParticipants(rows, fields) : new HashSet<int>();

                foreach (double?[] row in rows)
                {
                    int sourceRef = Int(row, fields, "participantRef") ?? -1;
                    if (baseDue || startBurst || changed.Contains(sourceRef) || battle.Contains(sourceRef))
                    {
                        progress.Add(new CompactTelemetrySample(elapsed, Project(schema, row, fields, progressFields)));
                    }
                    if (worldDue || startBurst)
                    {
                        world.Add(new CompactTelemetrySample(elapsed, Project(schema, row, fields, worldFields)));
                    }
                    if (extensionDue)
                    {
                        extension.Add(new CompactTelemetrySample(elapsed, Project(schema, row, fields, extensionFields)));
                    }
                }
                AddGeometry(rows, fields, geometry);
            }

            var artifacts = new List<CompactArtifact>();
            CompactTelemetrySample[] replay = MergeReplaySamples(schema, progress, world, extension);
            AddArtifact(artifacts, "replay", schema.Id, replay, 0, TakeParticipantDictionary(), null);
            AddArtifact(artifacts, "track-geometry", CompactTelemetrySchemaId.TrackGeometryV1, geometry, 0, null, null);
            return artifacts;
        }

        private static CompactTelemetrySample[] MergeReplaySamples(
            CompactTelemetrySchema schema,
            params IReadOnlyList<CompactTelemetrySample>[] sources)
        {
            int participantOrdinal = schema.Fields
                .Single(value => string.Equals(value.Name, "participantRef", StringComparison.Ordinal))
                .Ordinal;
            var rows = new Dictionary<(long ElapsedMs, int ParticipantRef), double?[]>();
            foreach (IReadOnlyList<CompactTelemetrySample> source in sources)
            {
                foreach (CompactTelemetrySample sample in source)
                {
                    double? rawParticipant = sample.Values[participantOrdinal];
                    if (!rawParticipant.HasValue)
                    {
                        throw new InvalidDataException("Replay sample has no participant reference.");
                    }
                    var key = (sample.ElapsedMs, checked((int)rawParticipant.Value));
                    if (!rows.TryGetValue(key, out double?[]? target))
                    {
                        target = new double?[schema.Fields.Count];
                        rows.Add(key, target);
                    }
                    for (int index = 0; index < sample.Values.Count; index++)
                    {
                        double? value = sample.Values[index];
                        if (!value.HasValue) continue;
                        if (target[index].HasValue && !SameValue(target[index], value))
                        {
                            throw new InvalidDataException(
                                "Conflicting replay value for " + schema.Fields[index].Name + ".");
                        }
                        target[index] = value;
                    }
                }
            }
            return rows
                .OrderBy(value => value.Key.ElapsedMs)
                .ThenBy(value => value.Key.ParticipantRef)
                .Select(value => new CompactTelemetrySample(value.Key.ElapsedMs, value.Value))
                .ToArray();
        }

        private IReadOnlyList<CompactArtifact> BuildDriverArtifacts(TelemetryChunkEnvelope source)
        {
            Dictionary<string, int> fields = FieldMap(source.Data.Fields);
            double?[][] rows = source.Data.Rows.OrderBy(row => Elapsed(row, fields)).ToArray();
            if (rows.Length == 0) return Array.Empty<CompactArtifact>();
            var artifacts = new List<CompactArtifact>();

            CompactTelemetrySchema fastSchema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverFastV1);
            CompactTelemetrySample[] fast = rows.Select(row => new CompactTelemetrySample(
                Elapsed(row, fields),
                new double?[]
                {
                    Number(row, fields, "unfilteredThrottle") ?? Number(row, fields, "throttle"),
                    Number(row, fields, "unfilteredBrake") ?? Number(row, fields, "brake"),
                    Number(row, fields, "unfilteredSteering") ?? Number(row, fields, "steering"),
                    Number(row, fields, "speedMetersPerSecond"),
                    Number(row, fields, "lapDistanceMeters"),
                    Number(row, fields, "longitudinalAccelerationMetersPerSecondSquared"),
                    Number(row, fields, "lateralAccelerationMetersPerSecondSquared")
                })).ToArray();
            AddArtifact(artifacts, "driver-fast", fastSchema.Id, fast, CadenceOrZero(fast, 50), null, null);

            CompactTelemetrySchema motionSchema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverMotionV1);
            CompactTelemetrySample[] motion = SelectAtCadence(rows, fields, 200)
                .Select(row => new CompactTelemetrySample(Elapsed(row, fields), new double?[]
                {
                    Number(row, fields, "worldX"), Number(row, fields, "worldY"), Number(row, fields, "worldZ"),
                    Number(row, fields, "headingRadians"), Number(row, fields, "rpm")
                })).ToArray();
            AddArtifact(artifacts, "driver-motion", motionSchema.Id, motion, CadenceOrZero(motion, 200), null, null);

            CompactTelemetrySchema slowSchema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverSlowV1);
            CompactTelemetrySample[] slow = SelectAtCadence(rows, fields, 1_000)
                .Select(row => new CompactTelemetrySample(Elapsed(row, fields), new double?[]
                {
                    Number(row, fields, "fuelLiters"), Number(row, fields, "engineDamage"),
                    Number(row, fields, "aeroDamage"), Number(row, fields, "trackTemperatureCelsius")
                })).ToArray();
            AddArtifact(artifacts, "driver-slow", slowSchema.Id, slow, CadenceOrZero(slow, 1_000), null, null);

            var dedicated = Set(
                "sessionElapsedMs", "capturedAtUnixMs", "throttle", "brake", "steering",
                "unfilteredThrottle", "unfilteredBrake", "unfilteredSteering",
                "speedMetersPerSecond", "lapDistanceMeters",
                "longitudinalAccelerationMetersPerSecondSquared", "lateralAccelerationMetersPerSecondSquared",
                "worldX", "worldY", "worldZ", "headingRadians", "rpm", "fuelLiters",
                "engineDamage", "aeroDamage", "trackTemperatureCelsius");
            var changeOnly = Set(
                "driverRef", "lap", "sector", "gearRaw", "clutch", "unfilteredClutch",
                "pitStateRaw", "lapValid", "rootLapInvalidated", "participantLapInvalidated",
                "rootPitModeRaw", "rootPitScheduleRaw", "participantPitScheduleRaw",
                "highestFlagColourRaw", "highestFlagReasonRaw", "participantHighestFlagColourRaw",
                "participantHighestFlagReasonRaw", "carFlagsRaw", "antiLockActive",
                "lastOpponentCollisionIndex", "boostActive", "drsStateRaw", "antiLockSetting",
                "tractionControlSetting", "ersDeploymentModeRaw", "ersAutoModeEnabled",
                "clutchOverheated", "clutchSlipping", "launchStageRaw", "handBrake", "crashStateRaw");
            var changes = new List<CompactTelemetrySample>();

            // Discrete controls and game states are inspected at the input cadence and
            // written only on transition. This keeps a short gear/flag/pit transition
            // from disappearing between the 20-second generic snapshots.
            foreach (double?[] row in rows)
            {
                long elapsed = Elapsed(row, fields);
                for (int ordinal = 0; ordinal < source.Data.Fields.Length; ordinal++)
                {
                    string name = source.Data.Fields[ordinal];
                    if (!changeOnly.Contains(name)) continue;
                    double? value = row[ordinal];
                    if (_lastDriverChangeValues.TryGetValue(ordinal, out double? previous)
                        && SameValue(previous, value)) continue;
                    _lastDriverChangeValues[ordinal] = value;
                    changes.Add(new CompactTelemetrySample(elapsed, new double?[] { ordinal, value }));
                }
            }

            // Remaining low-information-rate fields retain the complete P023 catalog
            // at 0.05 Hz. They are not expanded into the 20 Hz fast row.
            foreach (double?[] row in SelectAtCadence(rows, fields, 20_000))
            {
                long elapsed = Elapsed(row, fields);
                for (int ordinal = 0; ordinal < source.Data.Fields.Length; ordinal++)
                {
                    string name = source.Data.Fields[ordinal];
                    if (dedicated.Contains(name) || changeOnly.Contains(name)) continue;
                    changes.Add(new CompactTelemetrySample(elapsed, new double?[] { ordinal, row[ordinal] }));
                }
            }
            var driverStrings = new List<CompactStringDictionaryEntry>();
            AddStrings(driverStrings, source, "tyreCompounds", CompactStringDictionaryId.DriverText);
            AddArtifact(
                artifacts,
                "driver-change",
                CompactTelemetrySchemaId.DriverChangeV1,
                changes.OrderBy(value => value.ElapsedMs)
                    .ThenBy(value => value.Values[0] ?? double.MaxValue)
                    .ToArray(),
                0,
                null,
                driverStrings);
            return artifacts;
        }

        private IReadOnlyList<CompactArtifact> BuildIncidentArtifacts(TelemetryChunkEnvelope source)
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.IncidentV1);
            Dictionary<string, int> fields = FieldMap(source.Data.Fields);
            CompactTelemetrySample[] samples = source.Data.Rows
                .Select(row => new CompactTelemetrySample(Elapsed(row, fields), Project(schema, row, fields, null)))
                .OrderBy(value => value.ElapsedMs)
                .ToArray();
            if (samples.Length == 0) return Array.Empty<CompactArtifact>();
            var strings = new List<CompactStringDictionaryEntry>();
            AddStrings(strings, source, "candidates", CompactStringDictionaryId.IncidentCandidate);
            AddStrings(strings, source, "triggerCodes", CompactStringDictionaryId.IncidentTriggerCode);
            return new[]
            {
                new CompactArtifact("incident", schema.Id, samples, 0, null, strings)
            };
        }

        private TelemetryChunkCommitOutcome CommitArtifact(
            TelemetryChunkEnvelope source,
            CompactArtifact artifact,
            uint sequence)
        {
            var block = new CompactTelemetryBlock(
                artifact.SchemaId,
                artifact.Samples[0].ElapsedMs,
                artifact.CadenceMs,
                artifact.Samples);
            var envelope = new CompactTelemetryEnvelope(
                _sessionLocalId,
                _attemptLocalId,
                sequence,
                block,
                artifact.Participants,
                artifact.Strings);
            byte[] payload = CompactTelemetryCodec.Encode(envelope);
            // Decode before durable ACK so corrupt encoder output can never become source of truth.
            CompactTelemetryEnvelope verified = CompactTelemetryCodec.Decode(payload);
            if (verified.ChunkSequence != sequence || verified.Block.SchemaId != artifact.SchemaId)
            {
                throw new InvalidDataException("Compact telemetry self-verification failed.");
            }
            byte[] compressed = TelemetryChunkSerializer.Gzip(payload);
            string payloadSha = TelemetryChunkSerializer.Sha256(payload);
            string compressedSha = TelemetryChunkSerializer.Sha256(compressed);
            string directory = Path.Combine(_sessionRoot, "chunks", "compact", artifact.Family);
            Directory.CreateDirectory(directory);
            string baseName = sequence.ToString("D8", CultureInfo.InvariantCulture)
                + "-" + ((ushort)artifact.SchemaId).ToString("X4", CultureInfo.InvariantCulture);
            string chunkPath = Path.Combine(directory, baseName + ".a2ct.gz");
            string metadataPath = Path.Combine(directory, baseName + ".upload.json");
            string chunkId = "a2ct-" + TelemetryChunkSerializer.StableId(
                _identity.SessionFingerprint,
                _identity.WitnessId,
                _identity.AttemptId,
                sequence.ToString(CultureInfo.InvariantCulture),
                ((ushort)artifact.SchemaId).ToString(CultureInfo.InvariantCulture)).Substring(0, 48);

            if (File.Exists(chunkPath))
            {
                byte[] existing;
                using (FileStream stream = File.OpenRead(chunkPath)) existing = TelemetryChunkSerializer.Gunzip(stream);
                if (string.Equals(TelemetryChunkSerializer.Sha256(existing), payloadSha, StringComparison.Ordinal))
                {
                    if (!File.Exists(metadataPath))
                    {
                        AtomicWrite(metadataPath, TelemetryChunkSerializer.SerializeMetadata(CreateMetadata(
                            source, artifact, sequence, chunkId, chunkPath, payloadSha, compressedSha,
                            payload.Length, compressed.Length)));
                    }
                    return Outcome(TelemetryChunkCommitDisposition.DUPLICATE, chunkPath, metadataPath,
                        payloadSha, compressedSha, payload.Length, compressed.Length);
                }
                string conflicts = Path.Combine(_sessionRoot, "conflicts");
                Directory.CreateDirectory(conflicts);
                string conflictPath = Path.Combine(conflicts, baseName + "-" + compressedSha.Substring(0, 16) + ".a2ct.gz");
                AtomicWrite(conflictPath, compressed);
                return Outcome(TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED, conflictPath, string.Empty,
                    payloadSha, compressedSha, payload.Length, compressed.Length);
            }

            AtomicWrite(chunkPath, compressed);
            AtomicWrite(metadataPath, TelemetryChunkSerializer.SerializeMetadata(CreateMetadata(
                source, artifact, sequence, chunkId, chunkPath, payloadSha, compressedSha,
                payload.Length, compressed.Length)));
            return Outcome(TelemetryChunkCommitDisposition.STORED, chunkPath, metadataPath,
                payloadSha, compressedSha, payload.Length, compressed.Length);
        }

        private TelemetryPendingUploadMetadata CreateMetadata(
            TelemetryChunkEnvelope source,
            CompactArtifact artifact,
            uint sequence,
            string chunkId,
            string chunkPath,
            string payloadSha,
            string compressedSha,
            long payloadBytes,
            long compressedBytes)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool isPrivate = artifact.SchemaId == CompactTelemetrySchemaId.DriverFastV1
                || artifact.SchemaId == CompactTelemetrySchemaId.DriverMotionV1
                || artifact.SchemaId == CompactTelemetrySchemaId.DriverSlowV1
                || artifact.SchemaId == CompactTelemetrySchemaId.DriverChangeV1;
            return new TelemetryPendingUploadMetadata
            {
                Schema = "ams2-compact-upload-metadata-v1",
                Endpoint = "v1/telemetry/chunks",
                Protocol = "AMS2_COMPACT_TELEMETRY_V1",
                CompactSchemaId = (ushort)artifact.SchemaId,
                SessionLocalId = _sessionLocalId,
                AttemptLocalId = _attemptLocalId,
                ChunkId = chunkId,
                StreamType = source.StreamType,
                Visibility = isPrivate ? TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS : source.Visibility,
                SessionId = source.SessionId,
                SessionFingerprint = source.SessionFingerprint,
                WitnessId = source.WitnessId,
                AttemptId = source.AttemptId,
                AttemptNumber = source.AttemptNumber,
                ChunkIndex = checked((int)sequence),
                StartElapsedMs = artifact.Samples[0].ElapsedMs,
                EndElapsedMs = artifact.Samples[artifact.Samples.Count - 1].ElapsedMs,
                StartLap = source.StartLap,
                EndLap = source.EndLap,
                FirstCapturedAtUtc = source.FirstCapturedAtUtc,
                LastCapturedAtUtc = source.LastCapturedAtUtc,
                RelativeChunkPath = Path.GetRelativePath(_root, chunkPath),
                ContentType = CompactContentType,
                ContentEncoding = "gzip",
                PayloadSha256 = payloadSha,
                CompressedSha256 = compressedSha,
                UncompressedBytes = payloadBytes,
                CompressedBytes = compressedBytes,
                Quality = CloneQuality(source.Quality, artifact.Samples.Count),
                Status = isPrivate ? TelemetryUploadStatus.LOCAL_PENDING_OWNER : TelemetryUploadStatus.PENDING,
                AttemptCount = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LastError = isPrivate ? "PRIVATE_OWNER_AUTHORITY_REQUIRED" : null
            };
        }

        private void EnsureParticipants(IReadOnlyList<TelemetryParticipantDictionaryEntry> source)
        {
            foreach (TelemetryParticipantDictionaryEntry participant in source.OrderBy(value => value.ParticipantRef))
            {
                UpsertParticipant(
                    participant.ParticipantRef,
                    participant.NameSnapshot,
                    participant.VehicleRef,
                    participant.VehicleClassRef);
            }
        }

        private void EnsureParticipants(TelemetryChunkEnvelope source, IReadOnlyDictionary<string, int> fields)
        {
            foreach (double?[] row in source.Data.Rows
                .OrderBy(value => Number(value, fields, "participantRef") ?? double.MaxValue))
            {
                int? sourceRef = Int(row, fields, "participantRef");
                if (!sourceRef.HasValue) continue;
                UpsertParticipant(
                    sourceRef.Value,
                    DictionaryValue(source, "names", Int(row, fields, "nameRef")),
                    DictionaryValue(source, "vehicles", Int(row, fields, "vehicleRef")),
                    DictionaryValue(source, "vehicleClasses", Int(row, fields, "vehicleClassRef")));
            }
        }

        private void UpsertParticipant(int sourceRef, string? name, string? vehicle, string? vehicleClass)
        {
            string resolvedName = name ?? "UNKNOWN-" + sourceRef.ToString(CultureInfo.InvariantCulture);
            string resolvedVehicle = vehicle ?? "UNKNOWN";
            string resolvedClass = vehicleClass ?? "UNKNOWN";
            if (_participantRefs.TryGetValue(sourceRef, out ushort existingRef))
            {
                CompactParticipantDictionaryEntry existing = _participants[existingRef];
                if (string.Equals(existing.DisplayName, resolvedName, StringComparison.Ordinal)
                    && string.Equals(existing.VehicleName, resolvedVehicle, StringComparison.Ordinal)
                    && string.Equals(existing.ClassName, resolvedClass, StringComparison.Ordinal))
                {
                    return;
                }
                _participants[existingRef] = new CompactParticipantDictionaryEntry(
                    existingRef, resolvedName, resolvedVehicle, resolvedClass);
                _participantDictionaryRevision++;
                return;
            }
            ushort compactRef = checked((ushort)_participants.Count);
            _participantRefs.Add(sourceRef, compactRef);
            _participants.Add(new CompactParticipantDictionaryEntry(
                compactRef,
                resolvedName,
                resolvedVehicle,
                resolvedClass));
            _participantDictionaryRevision++;
        }

        private IReadOnlyList<CompactParticipantDictionaryEntry>? TakeParticipantDictionary()
        {
            if (_participants.Count == 0 || _participantDictionaryEmittedRevision == _participantDictionaryRevision)
            {
                return null;
            }
            _participantDictionaryEmittedRevision = _participantDictionaryRevision;
            return _participants.ToArray();
        }

        private double?[] Project(
            CompactTelemetrySchema schema,
            double?[] source,
            IReadOnlyDictionary<string, int> fields,
            ISet<string>? included)
        {
            var result = new double?[schema.Fields.Count];
            for (int index = 0; index < schema.Fields.Count; index++)
            {
                CompactTelemetryField field = schema.Fields[index];
                if (included != null && !included.Contains(field.Name)) continue;
                double? value = Number(source, fields, field.Name);
                if (value.HasValue && IsParticipantReference(field.Name))
                {
                    int sourceRef = checked((int)value.Value);
                    value = _participantRefs.TryGetValue(sourceRef, out ushort mapped) ? mapped : (double?)null;
                }
                if (value.HasValue && (!double.IsFinite(value.Value)
                    || (value.Value == -1 && field.QuantizedMinimum >= 0)))
                {
                    value = null;
                }
                result[index] = value;
            }
            return result;
        }

        private void AddGeometry(
            IReadOnlyList<double?[]> rows,
            IReadOnlyDictionary<string, int> fields,
            ICollection<CompactTelemetrySample> output)
        {
            int? minimumRef = rows.Select(row => Int(row, fields, "participantRef")).Where(value => value.HasValue)
                .Select(value => value!.Value).DefaultIfEmpty(-1).Min();
            if (!minimumRef.HasValue || minimumRef.Value < 0) return;
            foreach (double?[] row in rows)
            {
                if (Int(row, fields, "participantRef") != minimumRef) continue;
                double? distance = Number(row, fields, "lapDistanceMeters");
                double? x = Number(row, fields, "worldX");
                double? y = Number(row, fields, "worldY");
                double? z = Number(row, fields, "worldZ");
                if (!distance.HasValue || !x.HasValue || !y.HasValue || !z.HasValue || distance.Value < 0) continue;
                int bin = checked((int)Math.Round(distance.Value / 20.0, MidpointRounding.AwayFromZero));
                if (!_geometryBins.Add(bin)) continue;
                output.Add(new CompactTelemetrySample(Elapsed(row, fields), new double?[]
                {
                    distance, x, y, z
                }));
            }
        }

        private static HashSet<int> CloseBattleParticipants(
            IReadOnlyList<double?[]> rows,
            IReadOnlyDictionary<string, int> fields)
        {
            var result = new HashSet<int>();
            for (int left = 0; left < rows.Count; left++)
            {
                for (int right = left + 1; right < rows.Count; right++)
                {
                    int? leftPosition = Int(rows[left], fields, "racePosition");
                    int? rightPosition = Int(rows[right], fields, "racePosition");
                    if (!leftPosition.HasValue || !rightPosition.HasValue
                        || Math.Abs(leftPosition.Value - rightPosition.Value) != 1) continue;
                    double? lx = Number(rows[left], fields, "worldX");
                    double? ly = Number(rows[left], fields, "worldY");
                    double? lz = Number(rows[left], fields, "worldZ");
                    double? rx = Number(rows[right], fields, "worldX");
                    double? ry = Number(rows[right], fields, "worldY");
                    double? rz = Number(rows[right], fields, "worldZ");
                    if (!lx.HasValue || !ly.HasValue || !lz.HasValue
                        || !rx.HasValue || !ry.HasValue || !rz.HasValue) continue;
                    double dx = lx.Value - rx.Value;
                    double dy = ly.Value - ry.Value;
                    double dz = lz.Value - rz.Value;
                    if ((dx * dx) + (dy * dy) + (dz * dz) > 400.0) continue;
                    int? leftRef = Int(rows[left], fields, "participantRef");
                    int? rightRef = Int(rows[right], fields, "participantRef");
                    if (leftRef.HasValue) result.Add(leftRef.Value);
                    if (rightRef.HasValue) result.Add(rightRef.Value);
                }
            }
            return result;
        }

        private static IEnumerable<double?[]> SelectAtCadence(
            IReadOnlyList<double?[]> rows,
            IReadOnlyDictionary<string, int> fields,
            long intervalMs)
        {
            long? next = null;
            foreach (double?[] row in rows)
            {
                long elapsed = Elapsed(row, fields);
                if (!next.HasValue || elapsed >= next.Value)
                {
                    yield return row;
                    next = checked(elapsed + intervalMs);
                }
            }
        }

        private static bool TakeDue(ref long? next, long elapsed, long interval)
        {
            if (!next.HasValue) next = elapsed;
            if (elapsed < next.Value) return false;
            do next = checked(next.Value + interval); while (next.Value <= elapsed);
            return true;
        }

        private static bool SameValue(double? left, double? right)
        {
            if (!left.HasValue || !right.HasValue) return left.HasValue == right.HasValue;
            if (double.IsNaN(left.Value) || double.IsNaN(right.Value))
            {
                return double.IsNaN(left.Value) && double.IsNaN(right.Value);
            }
            return left.Value.Equals(right.Value);
        }

        private static uint CadenceOrZero(IReadOnlyList<CompactTelemetrySample> samples, uint expected)
        {
            for (int index = 1; index < samples.Count; index++)
            {
                if (samples[index].ElapsedMs - samples[index - 1].ElapsedMs != expected) return 0;
            }
            return samples.Count > 1 ? expected : 0;
        }

        private static void AddArtifact(
            ICollection<CompactArtifact> target,
            string family,
            CompactTelemetrySchemaId schemaId,
            IReadOnlyList<CompactTelemetrySample> samples,
            uint cadence,
            IReadOnlyList<CompactParticipantDictionaryEntry>? participants,
            IReadOnlyList<CompactStringDictionaryEntry>? strings)
        {
            if (samples.Count == 0) return;
            target.Add(new CompactArtifact(family, schemaId, samples, cadence, participants, strings));
        }

        private static void AddStrings(
            ICollection<CompactStringDictionaryEntry> target,
            TelemetryChunkEnvelope source,
            string sourceName,
            CompactStringDictionaryId dictionaryId)
        {
            if (!source.Data.Dictionaries.TryGetValue(sourceName, out string[]? values)) return;
            for (uint index = 0; index < values.Length; index++)
            {
                target.Add(new CompactStringDictionaryEntry(dictionaryId, index, values[index]));
            }
        }

        private static double? CapabilityNumber(SessionMetadataSample metadata, string key)
            => metadata.Fields.TryGetValue(key, out TelemetryCapabilityValue? value) ? value.NumericValue : null;

        private static Dictionary<string, int> FieldMap(IReadOnlyList<string> fields)
            => fields.Select((name, index) => new { name, index })
                .ToDictionary(value => value.name, value => value.index, StringComparer.Ordinal);

        private static long Elapsed(double?[] row, IReadOnlyDictionary<string, int> fields)
            => checked((long)(Number(row, fields, "sessionElapsedMs")
                ?? throw new InvalidDataException("Compact source row has no elapsed time.")));

        private static double? Number(double?[] row, IReadOnlyDictionary<string, int> fields, string name)
            => fields.TryGetValue(name, out int index) && index >= 0 && index < row.Length ? row[index] : null;

        private static int? Int(double?[] row, IReadOnlyDictionary<string, int> fields, string name)
        {
            double? value = Number(row, fields, name);
            return value.HasValue ? checked((int)value.Value) : (int?)null;
        }

        private static string? DictionaryValue(TelemetryChunkEnvelope source, string name, int? index)
            => index.HasValue && source.Data.Dictionaries.TryGetValue(name, out string[]? values)
                && index.Value >= 0 && index.Value < values.Length
                    ? values[index.Value]
                    : null;

        private static bool IsParticipantReference(string name)
            => string.Equals(name, "participantRef", StringComparison.Ordinal)
                || string.Equals(name, "viewedParticipantRef", StringComparison.Ordinal)
                || string.Equals(name, "collisionOpponentRef", StringComparison.Ordinal);

        private static HashSet<string> Set(params string[] values)
            => new HashSet<string>(values, StringComparer.Ordinal);

        private static TelemetryChunkQuality CloneQuality(TelemetryChunkQuality source, int compactSamples)
            => new TelemetryChunkQuality
            {
                ClockSource = source.ClockSource,
                TargetSampleRateHz = source.TargetSampleRateHz,
                ExpectedSampleCount = compactSamples,
                ActualSampleCount = compactSamples,
                MissingSamples = source.MissingSamples,
                DroppedSamples = source.DroppedSamples,
                DroppedInputMessages = source.DroppedInputMessages,
                CaptureCompleteness = source.CaptureCompleteness,
                SourceWitnessCount = source.SourceWitnessCount
            };

        private static uint StableUInt32(string value)
        {
            string hash = TelemetryChunkSerializer.StableId(value);
            uint result = uint.Parse(hash.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return result == 0 ? 1U : result;
        }

        private static void AtomicWrite(string targetPath, byte[] bytes)
        {
            string? directory = Path.GetDirectoryName(targetPath);
            if (directory == null) throw new InvalidDataException("Compact archive target directory is missing.");
            Directory.CreateDirectory(directory);
            string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                65_536,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(temporaryPath, targetPath);
        }

        private static TelemetryChunkCommitOutcome Aggregate(IReadOnlyList<TelemetryChunkCommitOutcome> values)
        {
            TelemetryChunkCommitOutcome first = values[0];
            return new TelemetryChunkCommitOutcome
            {
                Disposition = values.Any(value => value.Disposition == TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED)
                    ? TelemetryChunkCommitDisposition.CONFLICT_QUARANTINED
                    : values.All(value => value.Disposition == TelemetryChunkCommitDisposition.DUPLICATE)
                        ? TelemetryChunkCommitDisposition.DUPLICATE
                        : TelemetryChunkCommitDisposition.STORED,
                ChunkPath = first.ChunkPath,
                MetadataPath = first.MetadataPath,
                PayloadSha256 = first.PayloadSha256,
                CompressedSha256 = first.CompressedSha256,
                UncompressedBytes = values.Sum(value => value.UncompressedBytes),
                CompressedBytes = values.Sum(value => value.CompressedBytes)
            };
        }

        private static TelemetryChunkCommitOutcome Outcome(
            TelemetryChunkCommitDisposition disposition,
            string chunkPath,
            string metadataPath,
            string payloadSha,
            string compressedSha,
            long payloadBytes,
            long compressedBytes)
            => new TelemetryChunkCommitOutcome
            {
                Disposition = disposition,
                ChunkPath = chunkPath,
                MetadataPath = metadataPath,
                PayloadSha256 = payloadSha,
                CompressedSha256 = compressedSha,
                UncompressedBytes = payloadBytes,
                CompressedBytes = compressedBytes
            };

        private sealed class CompactArtifact
        {
            public CompactArtifact(
                string family,
                CompactTelemetrySchemaId schemaId,
                IReadOnlyList<CompactTelemetrySample> samples,
                uint cadenceMs,
                IReadOnlyList<CompactParticipantDictionaryEntry>? participants,
                IReadOnlyList<CompactStringDictionaryEntry>? strings)
            {
                Family = family;
                SchemaId = schemaId;
                Samples = samples;
                CadenceMs = cadenceMs;
                Participants = participants;
                Strings = strings;
            }

            public string Family { get; }
            public CompactTelemetrySchemaId SchemaId { get; }
            public IReadOnlyList<CompactTelemetrySample> Samples { get; }
            public uint CadenceMs { get; }
            public IReadOnlyList<CompactParticipantDictionaryEntry>? Participants { get; }
            public IReadOnlyList<CompactStringDictionaryEntry>? Strings { get; }
        }
    }
}
