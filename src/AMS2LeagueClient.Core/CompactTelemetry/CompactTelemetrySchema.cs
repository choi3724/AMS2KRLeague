using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AMS2LeagueClient.Core.CompactTelemetry
{
    public sealed class CompactTelemetryField
    {
        public CompactTelemetryField(
            int ordinal,
            string name,
            CompactFieldEncoding encoding,
            int fixedWidth,
            double scale,
            double offset,
            long quantizedMinimum,
            long quantizedMaximum)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A field name is required.", nameof(name));
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                throw new ArgumentOutOfRangeException(nameof(scale));
            }
            if (quantizedMinimum > quantizedMaximum) throw new ArgumentOutOfRangeException(nameof(quantizedMinimum));
            if ((encoding == CompactFieldEncoding.FixedUnsigned || encoding == CompactFieldEncoding.FixedSigned) &&
                fixedWidth != 1 && fixedWidth != 2 && fixedWidth != 4 && fixedWidth != 8)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedWidth));
            }
            if (encoding != CompactFieldEncoding.FixedUnsigned && encoding != CompactFieldEncoding.FixedSigned && fixedWidth != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedWidth));
            }

            Ordinal = ordinal;
            Name = name;
            Encoding = encoding;
            FixedWidth = fixedWidth;
            Scale = scale;
            Offset = offset;
            QuantizedMinimum = quantizedMinimum;
            QuantizedMaximum = quantizedMaximum;
        }

        public int Ordinal { get; }
        public string Name { get; }
        public CompactFieldEncoding Encoding { get; }
        public int FixedWidth { get; }
        public double Scale { get; }
        public double Offset { get; }
        public long QuantizedMinimum { get; }
        public long QuantizedMaximum { get; }
        public double MaximumQuantizationError => Scale / 2.0;

        public long Quantize(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new CompactTelemetryFormatException("Field " + Name + " is not finite.");
            }

            double normalized = (value - Offset) / Scale;
            double rounded = Math.Round(normalized, MidpointRounding.AwayFromZero);
            long quantized;
            try
            {
                quantized = checked((long)rounded);
            }
            catch (OverflowException exception)
            {
                throw new CompactTelemetryFormatException("Field " + Name + " is outside its quantized range.", exception);
            }

            ValidateQuantized(quantized);
            return quantized;
        }

        public double Dequantize(long value)
        {
            ValidateQuantized(value);
            return Offset + (value * Scale);
        }

        internal void ValidateQuantized(long value)
        {
            if (value < QuantizedMinimum || value > QuantizedMaximum)
            {
                throw new CompactTelemetryFormatException(
                    "Field " + Name + " quantized value " + value + " is outside [" +
                    QuantizedMinimum + ", " + QuantizedMaximum + "].");
            }
        }
    }

    public sealed class CompactTelemetrySchema
    {
        internal CompactTelemetrySchema(CompactTelemetrySchemaId id, string name, params CompactTelemetryField[] fields)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A schema name is required.", nameof(name));
            if (fields == null || fields.Length == 0) throw new ArgumentException("At least one field is required.", nameof(fields));

            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < fields.Length; index++)
            {
                CompactTelemetryField field = fields[index] ?? throw new ArgumentException("A schema field cannot be null.", nameof(fields));
                if (field.Ordinal != index) throw new ArgumentException("Schema ordinals must be contiguous and immutable.", nameof(fields));
                if (!names.Add(field.Name)) throw new ArgumentException("Schema field names must be unique.", nameof(fields));
            }

            Id = id;
            Name = name;
            Fields = Array.AsReadOnly((CompactTelemetryField[])fields.Clone());
        }

        public CompactTelemetrySchemaId Id { get; }
        public string Name { get; }
        public IReadOnlyList<CompactTelemetryField> Fields { get; }
    }

    public static class CompactTelemetrySchemaRegistry
    {
        private static readonly IReadOnlyDictionary<CompactTelemetrySchemaId, CompactTelemetrySchema> Registry =
            BuildRegistry();

        public static IReadOnlyDictionary<CompactTelemetrySchemaId, CompactTelemetrySchema> Schemas => Registry;

        public static CompactTelemetrySchema Get(CompactTelemetrySchemaId id)
        {
            if (!Registry.TryGetValue(id, out CompactTelemetrySchema? schema))
            {
                throw new CompactTelemetryFormatException("Unknown compact telemetry schema ID 0x" + ((ushort)id).ToString("X4") + ".");
            }
            return schema;
        }

        private static IReadOnlyDictionary<CompactTelemetrySchemaId, CompactTelemetrySchema> BuildRegistry()
        {
            var schemas = new Dictionary<CompactTelemetrySchemaId, CompactTelemetrySchema>
            {
                [CompactTelemetrySchemaId.SessionStaticV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.SessionStaticV1,
                    "SESSION_STATIC_V1",
                    UnsignedFixed(0, "trackLengthMeters", 4, 0.01, 0, 2_000_000),
                    UnsignedFixed(1, "maxRpm", 2, 1.0, 0, 65_535)),

                // The full Int64 range is intentional: it also makes overflow rejection testable.
                [CompactTelemetrySchemaId.SessionChangeV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.SessionChangeV1,
                    "SESSION_CHANGE_V1",
                    Field(0, "fieldOrdinal", CompactFieldEncoding.RleUnsigned, 1.0, 0, 65_535),
                    Field(1, "rawValue", CompactFieldEncoding.ZigZag, 0.001, long.MinValue, long.MaxValue)),

                [CompactTelemetrySchemaId.RaceEventV1] = BuildRaceEventSchema(),

                [CompactTelemetrySchemaId.ParticipantReplayV1] = BuildParticipantReplaySchema(),

                [CompactTelemetrySchemaId.TrackGeometryV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.TrackGeometryV1,
                    "TRACK_GEOMETRY_V1",
                    Field(0, "lapDistanceMeters", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 2_000_000),
                    Field(1, "worldX", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue),
                    Field(2, "worldY", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue),
                    Field(3, "worldZ", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue)),

                [CompactTelemetrySchemaId.DriverFastV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.DriverFastV1,
                    "DRIVER_FAST_V1",
                    UnsignedFixed(0, "throttle", 1, 1.0 / 255.0, 0, 255),
                    UnsignedFixed(1, "brake", 1, 1.0 / 255.0, 0, 255),
                    SignedFixed(2, "steering", 2, 1.0 / 32767.0, -32_767, 32_767),
                    Field(3, "speedMetersPerSecond", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 65_535),
                    Field(4, "lapDistanceMeters", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 2_000_000),
                    Field(5, "longitudinalAccelerationMetersPerSecondSquared", CompactFieldEncoding.ZigZag, 0.01, -32_768, 32_767),
                    Field(6, "lateralAccelerationMetersPerSecondSquared", CompactFieldEncoding.ZigZag, 0.01, -32_768, 32_767)),

                [CompactTelemetrySchemaId.DriverMotionV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.DriverMotionV1,
                    "DRIVER_MOTION_V1",
                    Field(0, "worldX", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue),
                    Field(1, "worldY", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue),
                    Field(2, "worldZ", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue),
                    Field(3, "headingRadians", CompactFieldEncoding.DeltaZigZag, 0.0001, -62_832, 62_832),
                    Field(4, "rpm", CompactFieldEncoding.DeltaZigZag, 1.0, 0, 65_535)),

                [CompactTelemetrySchemaId.DriverSlowV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.DriverSlowV1,
                    "DRIVER_SLOW_V1",
                    Field(0, "fuelLiters", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 20_000),
                    Field(1, "engineDamage", CompactFieldEncoding.RleUnsigned, 1.0 / 255.0, 0, 255),
                    Field(2, "aeroDamage", CompactFieldEncoding.RleUnsigned, 1.0 / 255.0, 0, 255),
                    Field(3, "trackTemperatureCelsius", CompactFieldEncoding.DeltaZigZag, 0.1, -500, 1_500)),

                [CompactTelemetrySchemaId.DriverChangeV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.DriverChangeV1,
                    "DRIVER_CHANGE_V1",
                    Field(0, "fieldOrdinal", CompactFieldEncoding.RleUnsigned, 1.0, 0, 65_535),
                    Field(1, "rawValue", CompactFieldEncoding.ZigZag, 0.001, long.MinValue, long.MaxValue)),

                [CompactTelemetrySchemaId.IncidentV1] = BuildIncidentSchema(),

                [CompactTelemetrySchemaId.LossLedgerV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.LossLedgerV1,
                    "LOSS_LEDGER_V1",
                    Field(0, "lossSourceCode", CompactFieldEncoding.RleUnsigned, 1.0, 0, 255),
                    Field(1, "lossCount", CompactFieldEncoding.VarUInt, 1.0, 0, int.MaxValue),
                    Field(2, "reasonCode", CompactFieldEncoding.RleUnsigned, 1.0, 0, 65_535)),

                [CompactTelemetrySchemaId.AttemptFinalizeV1] = new CompactTelemetrySchema(
                    CompactTelemetrySchemaId.AttemptFinalizeV1,
                    "ATTEMPT_FINALIZE_V1",
                    Field(0, "acceptedWork", CompactFieldEncoding.VarUInt, 1.0, 0, int.MaxValue),
                    Field(1, "durableCommitAck", CompactFieldEncoding.VarUInt, 1.0, 0, int.MaxValue),
                    Field(2, "knownLoss", CompactFieldEncoding.VarUInt, 1.0, 0, int.MaxValue),
                    Field(3, "completenessCode", CompactFieldEncoding.RleUnsigned, 1.0, 0, 255))
            };

            return new ReadOnlyDictionary<CompactTelemetrySchemaId, CompactTelemetrySchema>(schemas);
        }

        private static CompactTelemetrySchema BuildRaceEventSchema()
        {
            var fields = new List<CompactTelemetryField>();
            Add(fields, "eventTypeRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "eventIdRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "factCodeRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "participantRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, 4_095);
            Add(fields, "lap", CompactFieldEncoding.RleZigZag, 1.0, -1, 65_535);
            Add(fields, "sector", CompactFieldEncoding.RleZigZag, 1.0, -1, 3);
            Add(fields, "lapDistanceMeters", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 2_000_000);
            AddWorld(fields);
            Add(fields, "positionBefore", CompactFieldEncoding.RleZigZag, 1.0, -1, 4_096);
            Add(fields, "positionAfter", CompactFieldEncoding.RleZigZag, 1.0, -1, 4_096);
            Add(fields, "lapTimeMs", CompactFieldEncoding.DeltaZigZag, 1.0, 0, int.MaxValue);
            AddRawCode(fields, "raceStateRaw");
            AddRawCode(fields, "pitStateRaw");
            AddRawCode(fields, "flagColourRaw");
            AddRawCode(fields, "flagReasonRaw");
            AddRawCode(fields, "penaltyTypeRaw");
            AddRawCode(fields, "resultStateRaw");
            AddRawCode(fields, "yellowFlagStateRaw");
            AddRawCode(fields, "participantIsActiveRaw");
            return new CompactTelemetrySchema(CompactTelemetrySchemaId.RaceEventV1, "RACE_EVENT_V1", fields.ToArray());
        }

        private static CompactTelemetrySchema BuildParticipantReplaySchema()
        {
            var fields = new List<CompactTelemetryField>();
            Add(fields, "participantRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, 4_095);
            Add(fields, "slot", CompactFieldEncoding.RleUnsigned, 1.0, 0, 4_095);
            Add(fields, "generation", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "lap", CompactFieldEncoding.RleZigZag, 1.0, -1, 65_535);
            Add(fields, "lapDistanceMeters", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 2_000_000);
            Add(fields, "racePosition", CompactFieldEncoding.RleZigZag, 1.0, -1, 4_096);
            AddWorld(fields);
            AddRawCode(fields, "raceStateRaw");
            AddRawCode(fields, "pitStateRaw");
            Add(fields, "nameRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "vehicleRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "vehicleClassRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "headingRadians", CompactFieldEncoding.DeltaZigZag, 0.0001, -62_832, 62_832);
            Add(fields, "speedMetersPerSecond", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 65_535);
            Add(fields, "lapsCompleted", CompactFieldEncoding.RleZigZag, 1.0, -1, 65_535);
            AddRawCode(fields, "sectorRaw");
            AddSeconds(fields, "currentSector1TimeSeconds");
            AddSeconds(fields, "currentSector2TimeSeconds");
            AddSeconds(fields, "currentSector3TimeSeconds");
            AddRawCode(fields, "lapInvalidated");
            AddOrientationAngle(fields, "orientationRawX");
            AddOrientationAngle(fields, "orientationRawY");
            AddOrientationAngle(fields, "orientationRawZ");
            AddRawCode(fields, "nationalityRaw");
            AddRawCode(fields, "pitScheduleRaw");
            AddRawCode(fields, "highestFlagColourRaw");
            AddRawCode(fields, "highestFlagReasonRaw");
            AddSeconds(fields, "bestLapTimeSeconds");
            AddSeconds(fields, "lastLapTimeSeconds");
            AddSeconds(fields, "fastestSector1TimeSeconds");
            AddSeconds(fields, "fastestSector2TimeSeconds");
            AddSeconds(fields, "fastestSector3TimeSeconds");
            AddRawCode(fields, "isActive");
            Rescale(fields, "lapDistanceMeters", 0.1, 0, 200_000);
            Rescale(fields, "worldX", 0.1, int.MinValue, int.MaxValue);
            Rescale(fields, "worldY", 0.1, int.MinValue, int.MaxValue);
            Rescale(fields, "worldZ", 0.1, int.MinValue, int.MaxValue);
            Rescale(fields, "headingRadians", 0.002, -3_142, 3_142);
            Rescale(fields, "speedMetersPerSecond", 0.1, 0, 6_554);
            return new CompactTelemetrySchema(
                CompactTelemetrySchemaId.ParticipantReplayV1, "PARTICIPANT_REPLAY_V1", fields.ToArray());
        }

        private static void Rescale(
            IList<CompactTelemetryField> fields,
            string name,
            double scale,
            long minimum,
            long maximum)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                CompactTelemetryField source = fields[index];
                if (!string.Equals(source.Name, name, StringComparison.Ordinal)) continue;
                fields[index] = new CompactTelemetryField(
                    source.Ordinal,
                    source.Name,
                    source.Encoding,
                    source.FixedWidth,
                    scale,
                    source.Offset,
                    minimum,
                    maximum);
                return;
            }
            throw new InvalidOperationException("Compact schema field not found: " + name);
        }

        private static CompactTelemetrySchema BuildIncidentSchema()
        {
            var fields = new List<CompactTelemetryField>();
            Add(fields, "relativeTimeMs", CompactFieldEncoding.DeltaZigZag, 1.0, -60_000, 60_000);
            Add(fields, "candidateRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "triggerCodeRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "participantRef", CompactFieldEncoding.RleUnsigned, 1.0, 0, 4_095);
            Add(fields, "slot", CompactFieldEncoding.RleUnsigned, 1.0, 0, 4_095);
            Add(fields, "generation", CompactFieldEncoding.RleUnsigned, 1.0, 0, int.MaxValue);
            Add(fields, "lap", CompactFieldEncoding.RleZigZag, 1.0, -1, 65_535);
            Add(fields, "lapDistanceMeters", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 2_000_000);
            Add(fields, "racePosition", CompactFieldEncoding.RleZigZag, 1.0, -1, 4_096);
            AddWorld(fields);
            AddRawCode(fields, "raceStateRaw");
            AddRawCode(fields, "pitStateRaw");
            AddRawCode(fields, "flagColourRaw");
            AddRawCode(fields, "flagReasonRaw");
            AddRawCode(fields, "participantDisappeared");
            Add(fields, "positionChangeMagnitude", CompactFieldEncoding.ZigZag, 0.01, 0, int.MaxValue);
            Add(fields, "headingRadians", CompactFieldEncoding.DeltaZigZag, 0.0001, -62_832, 62_832);
            Add(fields, "speedMetersPerSecond", CompactFieldEncoding.DeltaZigZag, 0.01, 0, 65_535);
            Add(fields, "lapsCompleted", CompactFieldEncoding.RleZigZag, 1.0, -1, 65_535);
            AddRawCode(fields, "sectorRaw");
            AddSeconds(fields, "currentSector1TimeSeconds");
            AddSeconds(fields, "currentSector2TimeSeconds");
            AddSeconds(fields, "currentSector3TimeSeconds");
            AddRawCode(fields, "lapInvalidated");
            AddOrientationAngle(fields, "orientationRawX");
            AddOrientationAngle(fields, "orientationRawY");
            AddOrientationAngle(fields, "orientationRawZ");
            AddRawCode(fields, "nationalityRaw");
            AddRawCode(fields, "pitScheduleRaw");
            AddRawCode(fields, "highestParticipantFlagColourRaw");
            AddRawCode(fields, "highestParticipantFlagReasonRaw");
            AddSeconds(fields, "bestLapTimeSeconds");
            AddSeconds(fields, "lastLapTimeSeconds");
            AddSeconds(fields, "fastestSector1TimeSeconds");
            AddSeconds(fields, "fastestSector2TimeSeconds");
            AddSeconds(fields, "fastestSector3TimeSeconds");
            AddRawCode(fields, "isActive");
            AddRawCode(fields, "yellowFlagStateRaw");
            Add(fields, "viewedParticipantRef", CompactFieldEncoding.RleZigZag, 1.0, -1, 4_095);
            Add(fields, "collisionOpponentSlotRaw", CompactFieldEncoding.RleZigZag, 1.0, -1, 4_095);
            Add(fields, "collisionOpponentRef", CompactFieldEncoding.RleZigZag, 1.0, -1, 4_095);
            Add(fields, "collisionMagnitude", CompactFieldEncoding.ZigZag, 0.001, 0, int.MaxValue);
            AddRawCode(fields, "crashStateRaw");
            return new CompactTelemetrySchema(CompactTelemetrySchemaId.IncidentV1, "INCIDENT_V1", fields.ToArray());
        }

        private static void AddWorld(List<CompactTelemetryField> fields)
        {
            Add(fields, "worldX", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue);
            Add(fields, "worldY", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue);
            Add(fields, "worldZ", CompactFieldEncoding.DeltaZigZag, 0.01, int.MinValue, int.MaxValue);
        }

        private static void AddSeconds(List<CompactTelemetryField> fields, string name)
        {
            Add(fields, name, CompactFieldEncoding.DeltaZigZag, 0.001, -1_000, int.MaxValue);
        }

        private static void AddOrientationAngle(List<CompactTelemetryField> fields, string name)
        {
            // AMS2 names these values "orientation", but they are Euler-angle components,
            // not normalized unit-vector coordinates. Keep a defensive +/-1000 rad range.
            Add(fields, name, CompactFieldEncoding.DeltaZigZag, 0.00001, -100_000_000, 100_000_000);
        }

        private static void AddRawCode(List<CompactTelemetryField> fields, string name)
        {
            Add(fields, name, CompactFieldEncoding.RleZigZag, 1.0, int.MinValue, int.MaxValue);
        }

        private static void Add(
            List<CompactTelemetryField> fields,
            string name,
            CompactFieldEncoding encoding,
            double scale,
            long minimum,
            long maximum)
        {
            fields.Add(Field(fields.Count, name, encoding, scale, minimum, maximum));
        }

        private static CompactTelemetryField Field(
            int ordinal,
            string name,
            CompactFieldEncoding encoding,
            double scale,
            long minimum,
            long maximum)
        {
            return new CompactTelemetryField(ordinal, name, encoding, 0, scale, 0, minimum, maximum);
        }

        private static CompactTelemetryField UnsignedFixed(
            int ordinal,
            string name,
            int width,
            double scale,
            long minimum,
            long maximum)
        {
            return new CompactTelemetryField(
                ordinal, name, CompactFieldEncoding.FixedUnsigned, width, scale, 0, minimum, maximum);
        }

        private static CompactTelemetryField SignedFixed(
            int ordinal,
            string name,
            int width,
            double scale,
            long minimum,
            long maximum)
        {
            return new CompactTelemetryField(
                ordinal, name, CompactFieldEncoding.FixedSigned, width, scale, 0, minimum, maximum);
        }
    }
}
