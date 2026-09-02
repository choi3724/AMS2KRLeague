using System;
using System.Collections.Generic;
using System.Linq;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    internal sealed class TelemetryChunkAccumulator
    {
        private readonly TelemetryArchiveIdentity _identity;
        private readonly Dictionary<string, List<string>> _dictionaries =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, int>> _dictionaryIndexes =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        private readonly List<double?[]> _rows = new List<double?[]>();
        private readonly List<SessionMetadataSample> _metadata = new List<SessionMetadataSample>();
        private int _expected;
        private int _actual;
        private int _dropped;
        private int _droppedInputMessages;
        private int? _startLap;
        private int? _endLap;
        private long? _firstElapsed;
        private long? _lastElapsed;
        private DateTimeOffset? _firstCapturedAtUtc;
        private DateTimeOffset? _lastCapturedAtUtc;

        public TelemetryChunkAccumulator(
            TelemetryArchiveIdentity identity,
            TelemetryStreamType streamType,
            int chunkIndex,
            double targetRateHz)
        {
            _identity = identity;
            StreamType = streamType;
            ChunkIndex = chunkIndex;
            TargetRateHz = targetRateHz;
        }

        public TelemetryStreamType StreamType { get; }
        public int ChunkIndex { get; }
        public double TargetRateHz { get; }
        public bool HasData => _actual > 0;

        public void AddMetadata(SessionMetadataSample sample, int maximumRecords)
        {
            _expected++;
            if (_metadata.Count >= maximumRecords)
            {
                _dropped++;
                return;
            }
            _metadata.Add(sample);
            _actual++;
            Touch(sample.SessionElapsedMs, sample.CapturedAtUtc, null);
        }

        public void AddStory(RaceStoryEventSample sample, int maximumEvents)
        {
            _expected++;
            if (_rows.Count >= maximumEvents)
            {
                _dropped++;
                return;
            }
            int eventTypeRef = DictionaryRef("eventTypes", sample.EventType);
            int eventIdRef = DictionaryRef("eventIds", sample.EventId);
            int? factCodeRef = string.IsNullOrWhiteSpace(sample.FactCode)
                ? (int?)null
                : DictionaryRef("factCodes", sample.FactCode!);
            _rows.Add(new double?[]
            {
                sample.SessionElapsedMs,
                sample.CapturedAtUtc.ToUnixTimeMilliseconds(),
                eventTypeRef,
                eventIdRef,
                factCodeRef,
                sample.ParticipantRef,
                sample.Lap,
                sample.Sector,
                sample.LapDistanceMeters,
                sample.WorldX,
                sample.WorldY,
                sample.WorldZ,
                sample.PositionBefore,
                sample.PositionAfter,
                sample.LapTimeMs,
                sample.RaceStateRaw,
                sample.PitStateRaw,
                sample.FlagColourRaw,
                sample.FlagReasonRaw,
                sample.PenaltyTypeRaw,
                sample.ResultStateRaw,
                sample.YellowFlagStateRaw,
                sample.ParticipantIsActiveRaw.HasValue
                    ? (sample.ParticipantIsActiveRaw.Value ? 1 : 0)
                    : (int?)null
            });
            _actual++;
            Touch(sample.SessionElapsedMs, sample.CapturedAtUtc, sample.Lap);
        }

        public void AddReplay(TelemetryFrameSample frame, int expectedSlots, int maximumParticipants)
        {
            int sourceCount = frame.Participants.Count;
            int observed = Math.Min(sourceCount, maximumParticipants);
            _expected = checked(_expected + Math.Max(1, expectedSlots) * observed);
            int take = observed;
            for (int index = 0; index < take; index++)
            {
                ReplayParticipantSample participant = frame.Participants[index];
                _rows.Add(new double?[]
                {
                    frame.SessionElapsedMs,
                    participant.ParticipantRef,
                    participant.Slot,
                    participant.Generation,
                    participant.Lap,
                    participant.LapDistanceMeters,
                    participant.RacePosition,
                    participant.WorldX,
                    participant.WorldY,
                    participant.WorldZ,
                    participant.RaceStateRaw,
                    participant.PitStateRaw,
                    DictionaryRefNullable("names", participant.NameSnapshot),
                    DictionaryRefNullable("vehicles", participant.VehicleRef),
                    DictionaryRefNullable("vehicleClasses", participant.VehicleClassRef),
                    participant.HeadingRadians,
                    participant.SpeedMetersPerSecond,
                    participant.LapsCompleted,
                    participant.SectorRaw,
                    participant.CurrentSector1TimeSeconds,
                    participant.CurrentSector2TimeSeconds,
                    participant.CurrentSector3TimeSeconds,
                    participant.LapInvalidated ? 1 : 0,
                    participant.OrientationRawX,
                    participant.OrientationRawY,
                    participant.OrientationRawZ,
                    participant.NationalityRaw,
                    participant.PitScheduleRaw,
                    participant.HighestFlagColourRaw,
                    participant.HighestFlagReasonRaw,
                    participant.BestLapTimeSeconds,
                    participant.LastLapTimeSeconds,
                    participant.FastestSector1TimeSeconds,
                    participant.FastestSector2TimeSeconds,
                    participant.FastestSector3TimeSeconds,
                    participant.IsActive ? 1 : 0
                });
                _actual++;
                Touch(frame.SessionElapsedMs, frame.CapturedAtUtc, participant.Lap);
            }
            _dropped = checked(_dropped + Math.Max(0, sourceCount - take));
        }

        public void AddDriver(TelemetryFrameSample frame, int expectedSlots)
        {
            DriverTelemetrySample sample = frame.LocalDriver ?? throw new InvalidOperationException("Local driver sample is missing.");
            _expected = checked(_expected + Math.Max(1, expectedSlots));
            var row = new List<double?>(TelemetryFieldCatalog.DriverTelemetryFields.Count)
            {
                frame.SessionElapsedMs,
                frame.CapturedAtUtc.ToUnixTimeMilliseconds(),
                sample.DriverRef,
                sample.Lap,
                sample.Sector,
                sample.LapDistanceMeters,
                sample.WorldX,
                sample.WorldY,
                sample.WorldZ,
                sample.SpeedMetersPerSecond,
                sample.Rpm,
                sample.GearRaw,
                sample.Throttle,
                sample.Brake,
                sample.Steering,
                sample.Clutch,
                sample.UnfilteredThrottle,
                sample.UnfilteredBrake,
                sample.UnfilteredSteering,
                sample.UnfilteredClutch,
                sample.LongitudinalAccelerationMetersPerSecondSquared,
                sample.LateralAccelerationMetersPerSecondSquared,
                sample.VerticalAccelerationMetersPerSecondSquared,
                sample.HeadingRadians,
                sample.VelocityX,
                sample.VelocityY,
                sample.VelocityZ,
                sample.FuelLevelRatio,
                sample.FuelCapacityLiters,
                sample.FuelLiters,
                sample.BrakeBias,
                sample.EngineDamage,
                sample.AeroDamage,
                sample.SuspensionDamage,
                Wheel(sample.TyreTemperaturesCelsius, 0),
                Wheel(sample.TyreTemperaturesCelsius, 1),
                Wheel(sample.TyreTemperaturesCelsius, 2),
                Wheel(sample.TyreTemperaturesCelsius, 3),
                Wheel(sample.TyrePressuresKpa, 0),
                Wheel(sample.TyrePressuresKpa, 1),
                Wheel(sample.TyrePressuresKpa, 2),
                Wheel(sample.TyrePressuresKpa, 3),
                Wheel(sample.TyreWear, 0),
                Wheel(sample.TyreWear, 1),
                Wheel(sample.TyreWear, 2),
                Wheel(sample.TyreWear, 3),
                sample.TrackTemperatureCelsius,
                sample.AmbientTemperatureCelsius,
                sample.RainDensity,
                sample.PitStateRaw,
                sample.LapValid.HasValue ? (sample.LapValid.Value ? 1 : 0) : (int?)null,
                sample.CurrentLapTimeMs
            };
            foreach (string field in TelemetryFieldCatalog.DriverAdditionalScalarFields)
            {
                row.Add(Raw(sample, field));
            }
            foreach (string field in TelemetryFieldCatalog.DriverAdditionalWheelFields)
            {
                if (field.StartsWith("tyreCompound", StringComparison.Ordinal))
                {
                    sample.AdditionalTextValues.TryGetValue(field, out string? text);
                    row.Add(DictionaryRefNullable("tyreCompounds", text));
                }
                else
                {
                    row.Add(Raw(sample, field));
                }
            }
            _rows.Add(row.ToArray());
            _actual++;
            Touch(frame.SessionElapsedMs, frame.CapturedAtUtc, sample.Lap);
        }

        public void AddIncident(
            IncidentCandidateSample candidate,
            long triggerElapsedMs,
            IncidentFrame frame,
            IReadOnlyCollection<int> relatedParticipantRefs)
        {
            int candidateRef = DictionaryRef("candidates", candidate.CandidateId);
            int triggerRef = DictionaryRef("triggerCodes", candidate.TriggerCode);
            _expected = checked(_expected + Math.Max(1, frame.ExpectedSlots) * relatedParticipantRefs.Count);
            int added = 0;
            foreach (ReplayParticipantSample participant in frame.Participants)
            {
                if (!relatedParticipantRefs.Contains(participant.ParticipantRef)) continue;
                _rows.Add(new double?[]
                {
                    frame.SessionElapsedMs - triggerElapsedMs,
                    frame.SessionElapsedMs,
                    frame.CapturedAtUtc.ToUnixTimeMilliseconds(),
                    candidateRef,
                    triggerRef,
                    participant.ParticipantRef,
                    participant.Slot,
                    participant.Generation,
                    participant.Lap,
                    participant.LapDistanceMeters,
                    participant.RacePosition,
                    participant.WorldX,
                    participant.WorldY,
                    participant.WorldZ,
                    participant.RaceStateRaw,
                    participant.PitStateRaw,
                    frame.FlagColourRaw,
                    frame.FlagReasonRaw,
                    frame.ParticipantDisappeared ? 1 : 0,
                    frame.PositionChangeMagnitude,
                    participant.HeadingRadians,
                    participant.SpeedMetersPerSecond,
                    participant.LapsCompleted,
                    participant.SectorRaw,
                    participant.CurrentSector1TimeSeconds,
                    participant.CurrentSector2TimeSeconds,
                    participant.CurrentSector3TimeSeconds,
                    participant.LapInvalidated ? 1 : 0,
                    participant.OrientationRawX,
                    participant.OrientationRawY,
                    participant.OrientationRawZ,
                    participant.NationalityRaw,
                    participant.PitScheduleRaw,
                    participant.HighestFlagColourRaw,
                    participant.HighestFlagReasonRaw,
                    participant.BestLapTimeSeconds,
                    participant.LastLapTimeSeconds,
                    participant.FastestSector1TimeSeconds,
                    participant.FastestSector2TimeSeconds,
                    participant.FastestSector3TimeSeconds,
                    participant.IsActive ? 1 : 0,
                    frame.YellowFlagStateRaw,
                    frame.ViewedParticipantRef,
                    frame.CollisionOpponentSlotRaw,
                    frame.CollisionOpponentRef,
                    frame.CollisionMagnitude,
                    frame.CrashStateRaw
                });
                added++;
                _actual++;
                Touch(frame.SessionElapsedMs, frame.CapturedAtUtc, participant.Lap);
            }
            _dropped = checked(_dropped + Math.Max(0, relatedParticipantRefs.Count - added));
        }

        public void AddDroppedInputMessages(int count)
            => _droppedInputMessages = checked(_droppedInputMessages + Math.Max(0, count));

        public void AddKnownDroppedSamples(int count)
        {
            int value = Math.Max(0, count);
            _expected = checked(_expected + value);
            _dropped = checked(_dropped + value);
        }

        public TelemetryChunkEnvelope Build()
        {
            if (!HasData) throw new InvalidOperationException("Cannot build an empty telemetry chunk.");
            int missing = Math.Max(0, _expected - _actual);
            int dropped = Math.Max(_dropped, missing);
            string chunkId = "chunk-" + TelemetryChunkSerializer.StableId(
                _identity.SessionFingerprint,
                _identity.WitnessId,
                _identity.AttemptId,
                StreamType.ToString(),
                ChunkIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)).Substring(0, 40);
            return new TelemetryChunkEnvelope
            {
                ChunkId = chunkId,
                StreamType = StreamType,
                Visibility = StreamType == TelemetryStreamType.DRIVER_TELEMETRY
                    ? TelemetryVisibility.PRIVATE_DRIVER_ANALYTICS
                    : TelemetryVisibility.PUBLIC_REPLAY,
                SessionId = _identity.SessionId,
                SessionFingerprint = _identity.SessionFingerprint,
                WitnessId = _identity.WitnessId,
                AttemptId = _identity.AttemptId,
                AttemptNumber = _identity.AttemptNumber,
                ScheduledEventHint = _identity.ScheduledEventHint,
                ChunkIndex = ChunkIndex,
                StartElapsedMs = _firstElapsed!.Value,
                EndElapsedMs = _lastElapsed!.Value,
                StartLap = _startLap,
                EndLap = _endLap,
                FirstCapturedAtUtc = _firstCapturedAtUtc!.Value,
                LastCapturedAtUtc = _lastCapturedAtUtc!.Value,
                Quality = new TelemetryChunkQuality
                {
                    TargetSampleRateHz = TargetRateHz,
                    ExpectedSampleCount = _expected,
                    ActualSampleCount = _actual,
                    MissingSamples = missing,
                    DroppedSamples = dropped,
                    DroppedInputMessages = _droppedInputMessages,
                    CaptureCompleteness = dropped == 0 && _droppedInputMessages == 0 ? "COMPLETE" : "PARTIAL",
                    SourceWitnessCount = 1
                },
                Data = new TelemetryChunkData
                {
                    Fields = FieldsFor(StreamType),
                    Dictionaries = _dictionaries.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.ToArray(),
                        StringComparer.Ordinal),
                    Rows = _rows,
                    Records = StreamType == TelemetryStreamType.SESSION_METADATA ? _metadata : null
                }
            };
        }

        private void Touch(long elapsedMs, DateTimeOffset capturedAtUtc, int? lap)
        {
            if (!_firstElapsed.HasValue || elapsedMs < _firstElapsed.Value) _firstElapsed = elapsedMs;
            if (!_lastElapsed.HasValue || elapsedMs > _lastElapsed.Value) _lastElapsed = elapsedMs;
            if (!_firstCapturedAtUtc.HasValue || capturedAtUtc < _firstCapturedAtUtc.Value) _firstCapturedAtUtc = capturedAtUtc;
            if (!_lastCapturedAtUtc.HasValue || capturedAtUtc > _lastCapturedAtUtc.Value) _lastCapturedAtUtc = capturedAtUtc;
            if (lap.HasValue)
            {
                if (!_startLap.HasValue || lap.Value < _startLap.Value) _startLap = lap.Value;
                if (!_endLap.HasValue || lap.Value > _endLap.Value) _endLap = lap.Value;
            }
        }

        private int DictionaryRef(string dictionary, string value)
        {
            if (!_dictionaryIndexes.TryGetValue(dictionary, out Dictionary<string, int>? indexes))
            {
                indexes = new Dictionary<string, int>(StringComparer.Ordinal);
                _dictionaryIndexes.Add(dictionary, indexes);
                _dictionaries.Add(dictionary, new List<string>());
            }
            if (indexes.TryGetValue(value, out int existing)) return existing;
            int index = indexes.Count;
            indexes.Add(value, index);
            _dictionaries[dictionary].Add(value);
            return index;
        }

        private int? DictionaryRefNullable(string dictionary, string? value)
            => string.IsNullOrWhiteSpace(value) ? (int?)null : DictionaryRef(dictionary, value!);

        private static double? Wheel(double?[] values, int index)
            => values != null && index >= 0 && index < values.Length ? values[index] : null;

        private static double? Raw(DriverTelemetrySample sample, string field)
            => sample.AdditionalRawValues.TryGetValue(field, out double? value) ? value : null;

        private static string[] FieldsFor(TelemetryStreamType streamType)
        {
            switch (streamType)
            {
                case TelemetryStreamType.SESSION_METADATA: return Array.Empty<string>();
                case TelemetryStreamType.RACE_STORY: return TelemetryFieldCatalog.RaceStoryFields.ToArray();
                case TelemetryStreamType.PARTICIPANT_REPLAY: return TelemetryFieldCatalog.ParticipantReplayFields.ToArray();
                case TelemetryStreamType.DRIVER_TELEMETRY: return TelemetryFieldCatalog.DriverTelemetryFields.ToArray();
                case TelemetryStreamType.INCIDENT_TRACE: return TelemetryFieldCatalog.IncidentTraceFields.ToArray();
                default: throw new ArgumentOutOfRangeException(nameof(streamType));
            }
        }
    }

    internal sealed class IncidentFrame
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public long SessionElapsedMs { get; set; }
        public int ExpectedSlots { get; set; } = 1;
        public IReadOnlyList<ReplayParticipantSample> Participants { get; set; } = Array.Empty<ReplayParticipantSample>();
        public int FlagColourRaw { get; set; }
        public int FlagReasonRaw { get; set; }
        public bool ParticipantDisappeared { get; set; }
        public int PositionChangeMagnitude { get; set; }
        public int YellowFlagStateRaw { get; set; }
        public int? ViewedParticipantRef { get; set; }
        public int? CollisionOpponentSlotRaw { get; set; }
        public int? CollisionOpponentRef { get; set; }
        public double? CollisionMagnitude { get; set; }
        public int? CrashStateRaw { get; set; }
    }
}
