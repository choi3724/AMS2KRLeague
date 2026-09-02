using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AMS2LeagueClient.Core.CompactTelemetry;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2LeagueActivity.Tests
{
    internal static class CompactTelemetryCodecTests
    {
        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Compact protocol registry IDs and ordinals are immutable", RegistryIsExplicitAndImmutable);
            yield return new TestCase("Compact columns round-trip within quantization bounds", RoundTripAndQuantizationBounds);
            yield return new TestCase("Compact catalog projection omits high-rate JSON field names", CatalogProjectionHasNoFieldNamesOnWire);
            yield return new TestCase("Compact presence bitmap distinguishes null from zero", PresenceBitmapDistinguishesNullFromZero);
            yield return new TestCase("Compact varint and ZigZag boundaries round-trip", VarIntAndZigZagBoundaries);
            yield return new TestCase("Compact delta keyframes and RLE columns round-trip", DeltaAndRleRoundTrip);
            yield return new TestCase("Compact irregular timestamp deltas reconstruct exactly", IrregularTimestampsRoundTripExactly);
            yield return new TestCase("Compact irregular timestamps reject malformed order and overflow", IrregularTimestampFailuresAreRejected);
            yield return new TestCase("Compact decoder rejects an unknown schema", WrongSchemaIsRejected);
            yield return new TestCase("Compact decoder rejects a wrong protocol version", WrongVersionIsRejected);
            yield return new TestCase("Compact decoder rejects truncation", TruncationIsRejected);
            yield return new TestCase("Compact decoder rejects a body hash mismatch", HashMismatchIsRejected);
            yield return new TestCase("Compact decoder rejects delta overflow", DeltaOverflowIsRejected);
            yield return new TestCase("Compact encoder rejects out-of-range quantization", OutOfRangeQuantizationIsRejected);
            yield return new TestCase("Compact typed string dictionaries round-trip without field names", TypedStringDictionariesRoundTrip);
        }

        private static void RegistryIsExplicitAndImmutable()
        {
            AssertEx.Equal((ushort)0x0001, (ushort)CompactTelemetrySchemaId.SessionStaticV1);
            AssertEx.Equal((ushort)0x0020, (ushort)CompactTelemetrySchemaId.ParticipantReplayV1);
            AssertEx.Equal((ushort)0x0030, (ushort)CompactTelemetrySchemaId.DriverFastV1);
            AssertEx.Equal((ushort)0x0040, (ushort)CompactTelemetrySchemaId.IncidentV1);
            AssertEx.Equal((ushort)0x0050, (ushort)CompactTelemetrySchemaId.LossLedgerV1);
            AssertEx.Equal((ushort)0x0051, (ushort)CompactTelemetrySchemaId.AttemptFinalizeV1);
            AssertEx.Equal((byte)0, (byte)CompactTelemetryLossSourceCode.None);
            AssertEx.Equal((byte)1, (byte)CompactTelemetryLossSourceCode.ShmSourceGap);
            AssertEx.Equal((byte)10, (byte)CompactTelemetryLossSourceCode.CommitConflict);
            AssertEx.Equal((ushort)1, (ushort)CompactTelemetryLossReasonCode.SessionMetadata);
            AssertEx.Equal((ushort)5, (ushort)CompactTelemetryLossReasonCode.IncidentTrace);
            AssertEx.Equal((byte)0, (byte)CompactTelemetryCompletenessCode.InProgress);
            AssertEx.Equal((byte)1, (byte)CompactTelemetryCompletenessCode.Partial);
            AssertEx.Equal((byte)2, (byte)CompactTelemetryCompletenessCode.Complete);
            AssertEx.Equal(12, CompactTelemetrySchemaRegistry.Schemas.Count);

            CompactTelemetrySchema driver = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverFastV1);
            AssertEx.Equal("DRIVER_FAST_V1", driver.Name);
            for (int index = 0; index < driver.Fields.Count; index++) AssertEx.Equal(index, driver.Fields[index].Ordinal);
            AssertEx.Equal("throttle", driver.Fields[0].Name);
            AssertEx.Equal("brake", driver.Fields[1].Name);
            AssertEx.Equal("steering", driver.Fields[2].Name);

            CompactTelemetrySchema replay = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.ParticipantReplayV1);
            AssertEx.Equal(0.1, replay.Fields.Single(value => value.Name == "lapDistanceMeters").Scale);
            AssertEx.Equal(0.1, replay.Fields.Single(value => value.Name == "worldX").Scale);
            AssertEx.Equal(0.002, replay.Fields.Single(value => value.Name == "headingRadians").Scale);

            AssertSchemaMatchesCatalogWithoutImplicitTime(
                CompactTelemetrySchemaId.RaceEventV1,
                TelemetryFieldCatalog.RaceStoryFields);
            AssertSchemaMatchesCatalogWithoutImplicitTime(
                CompactTelemetrySchemaId.ParticipantReplayV1,
                TelemetryFieldCatalog.ParticipantReplayFields);
            AssertSchemaMatchesCatalogWithoutImplicitTime(
                CompactTelemetrySchemaId.IncidentV1,
                TelemetryFieldCatalog.IncidentTraceFields);

            var mutable = (IDictionary<CompactTelemetrySchemaId, CompactTelemetrySchema>)CompactTelemetrySchemaRegistry.Schemas;
            bool rejected = false;
            try
            {
                mutable.Remove(CompactTelemetrySchemaId.DriverFastV1);
            }
            catch (NotSupportedException)
            {
                rejected = true;
            }
            AssertEx.True(rejected, "The schema registry must be read-only.");
        }

        private static void RoundTripAndQuantizationBounds()
        {
            const long baseElapsed = 987_654_321L;
            const uint cadence = 50;
            var rows = Rows(
                new double?[] { 0.0, 0.25, -0.5, 54.321, 1_234.567, -1.234, 2.345 },
                new double?[] { 0.501, 1.0, 0.12345, 55.009, 1_237.999, null, -2.345 },
                new double?[] { 1.0, 0.0, 1.0, 56.001, 1_240.001, 0.0, 0.0 });
            CompactTelemetryBlock block = CompactTelemetryBlock.FromRows(
                CompactTelemetrySchemaId.DriverFastV1, baseElapsed, cadence, rows);
            var envelope = new CompactTelemetryEnvelope(0x01020304, 0x11223344, 0x55667788, block);

            byte[] wire = CompactTelemetryCodec.Encode(envelope);
            AssertEx.Equal((byte)'A', wire[0]);
            AssertEx.Equal((byte)'2', wire[1]);
            AssertEx.Equal((byte)'C', wire[2]);
            AssertEx.Equal((byte)'T', wire[3]);
            AssertEx.Equal((byte)0x04, wire[12]);
            AssertEx.Equal((byte)0x03, wire[13]);
            AssertEx.Equal((byte)0x02, wire[14]);
            AssertEx.Equal((byte)0x01, wire[15]);
            AssertEx.Equal((byte)0x88, wire[20]);
            AssertEx.Equal((byte)0x77, wire[21]);
            int dictionaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(44, 4)));
            int presenceLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(48, 4)));
            AssertEx.Equal(3, presenceLength); // 2-byte column states + 1 bitmap for the sole mixed column
            int payloadOffset = CompactTelemetryProtocol.HeaderSize + dictionaryLength + presenceLength;
            AssertEx.Equal((byte)0x00, wire[payloadOffset + 6]); // steering -0.5 -> Int16 -16384
            AssertEx.Equal((byte)0xC0, wire[payloadOffset + 7]); // explicit little-endian column value

            CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(wire);
            AssertEx.Equal(envelope.SessionLocalId, decoded.SessionLocalId);
            AssertEx.Equal(envelope.AttemptLocalId, decoded.AttemptLocalId);
            AssertEx.Equal(envelope.ChunkSequence, decoded.ChunkSequence);
            AssertEx.Equal(3, decoded.Block.Samples.Count);

            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverFastV1);
            for (int sampleIndex = 0; sampleIndex < rows.Count; sampleIndex++)
            {
                AssertEx.Equal(baseElapsed + (sampleIndex * cadence), decoded.Block.Samples[sampleIndex].ElapsedMs);
                for (int fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
                {
                    double? expected = rows[sampleIndex][fieldIndex];
                    double? actual = decoded.Block.Samples[sampleIndex].Values[fieldIndex];
                    if (!expected.HasValue)
                    {
                        AssertEx.Null(actual);
                        continue;
                    }
                    AssertEx.True(actual.HasValue);
                    double error = Math.Abs(expected.Value - actual.GetValueOrDefault());
                    AssertEx.True(
                        error <= schema.Fields[fieldIndex].MaximumQuantizationError + 1e-12,
                        schema.Fields[fieldIndex].Name + " error " + error + " exceeded its bound.");
                }
            }
        }

        private static void CatalogProjectionHasNoFieldNamesOnWire()
        {
            IReadOnlyList<string> sourceFields = TelemetryFieldCatalog.DriverTelemetryFields;
            var sourceRow = new double?[sourceFields.Count];
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverFastV1);
            double[] values = { 0.75, 0.1, -0.25, 42.42, 1_000.25, -3.1, 4.2 };
            for (int index = 0; index < schema.Fields.Count; index++)
            {
                int sourceIndex = IndexOf(sourceFields, schema.Fields[index].Name);
                AssertEx.True(sourceIndex >= 0, "P023 catalog field missing: " + schema.Fields[index].Name);
                sourceRow[sourceIndex] = values[index];
            }

            CompactTelemetryBlock block = CompactTelemetryBlock.FromCatalogRows(
                CompactTelemetrySchemaId.DriverFastV1,
                10_000,
                50,
                sourceFields,
                Rows(sourceRow));
            var participants = new[]
            {
                new CompactParticipantDictionaryEntry(0, "ENG-IceBlasT", "Lancer", "GT3")
            };
            byte[] wire = CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(7, 8, 9, block, participants));
            CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(wire);

            AssertEx.Equal("ENG-IceBlasT", decoded.Participants[0].DisplayName);
            AssertEx.Equal(schema.Fields.Count, decoded.Block.Samples[0].Values.Count);
            foreach (CompactTelemetryField field in schema.Fields)
            {
                AssertEx.False(ContainsAscii(wire, field.Name), "Field name leaked onto the binary wire: " + field.Name);
            }
            AssertEx.False(ContainsAscii(wire, "sessionElapsedMs"));
            AssertEx.False(ContainsAscii(wire, "worldX"));
        }

        private static void TypedStringDictionariesRoundTrip()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.RaceEventV1);
            var row = new double?[schema.Fields.Count];
            Set(row, schema, "eventTypeRef", 0);
            Set(row, schema, "eventIdRef", 0);
            Set(row, schema, "factCodeRef", 0);
            var strings = new[]
            {
                new CompactStringDictionaryEntry(CompactStringDictionaryId.EventType, 0, "LAP_COMPLETED"),
                new CompactStringDictionaryEntry(CompactStringDictionaryId.EventId, 0, "event-0001"),
                new CompactStringDictionaryEntry(CompactStringDictionaryId.FactCode, 0, "LAP_TIME")
            };
            var block = new CompactTelemetryBlock(
                CompactTelemetrySchemaId.RaceEventV1,
                123,
                0,
                new[] { new CompactTelemetrySample(123, row) });
            byte[] wire = CompactTelemetryCodec.Encode(
                new CompactTelemetryEnvelope(1, 2, 3, block, null, strings));
            AssertEx.Equal((byte)3, wire[10]);
            AssertEx.Equal((byte)0, wire[11]);
            CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(wire);
            AssertEx.Equal(3, decoded.Strings.Count);
            AssertEx.Equal("LAP_COMPLETED", decoded.Strings[0].Value);
            AssertEx.Equal("event-0001", decoded.Strings[1].Value);
            AssertEx.Equal("LAP_TIME", decoded.Strings[2].Value);
            foreach (CompactTelemetryField field in schema.Fields)
            {
                AssertEx.False(ContainsAscii(wire, field.Name));
            }
        }

        private static void PresenceBitmapDistinguishesNullFromZero()
        {
            var rows = Rows(
                new double?[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 },
                new double?[] { null, 0.0, null, 0.0, null, 0.0, null });
            var block = CompactTelemetryBlock.FromRows(CompactTelemetrySchemaId.DriverFastV1, 1_000, 50, rows);
            CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(
                CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(1, 1, 0, block)));

            AssertEx.Equal(0.0, decoded.Block.Samples[0].Values[0]);
            AssertEx.Null(decoded.Block.Samples[1].Values[0]);
            AssertEx.Equal(0.0, decoded.Block.Samples[1].Values[1]);
            AssertEx.Null(decoded.Block.Samples[1].Values[2]);
            AssertEx.Equal(0.0, decoded.Block.Samples[1].Values[3]);
        }

        private static void VarIntAndZigZagBoundaries()
        {
            ulong[] unsignedValues = { 0, 1, 127, 128, 16_383, 16_384, uint.MaxValue, ulong.MaxValue };
            foreach (ulong expected in unsignedValues)
            {
                byte[] encoded = CompactVarInt.EncodeUInt64(expected);
                ulong actual = CompactVarInt.DecodeUInt64(encoded, out int bytesRead);
                AssertEx.Equal(expected, actual);
                AssertEx.Equal(encoded.Length, bytesRead);
            }

            long[] signedValues = { long.MinValue, int.MinValue, -65, -1, 0, 1, 64, int.MaxValue, long.MaxValue };
            foreach (long expected in signedValues)
            {
                byte[] encoded = CompactVarInt.EncodeInt64ZigZag(expected);
                long actual = CompactVarInt.DecodeInt64ZigZag(encoded, out int bytesRead);
                AssertEx.Equal(expected, actual);
                AssertEx.Equal(encoded.Length, bytesRead);
            }

            ExpectFormat(
                () => CompactVarInt.DecodeUInt64(new byte[] { 0x80 }, out _),
                "Truncated");
            ExpectFormat(
                () => CompactVarInt.DecodeUInt64(Enumerable.Repeat((byte)0xff, 10).ToArray(), out _),
                "exceeds UInt64");
        }

        private static void DeltaAndRleRoundTrip()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(
                CompactTelemetrySchemaId.ParticipantReplayV1);
            double?[] row0 = BlankRow(schema);
            double?[] row1 = BlankRow(schema);
            double?[] row2 = BlankRow(schema);
            double?[] row3 = BlankRow(schema);
            SetReplay(row0, schema, 100.00, 5, 1_000.00, -500.00, 0);
            SetReplay(row1, schema, 100.02, 5, 1_000.01, -499.98, 0);
            SetReplay(row2, schema, 100.07, 4, 1_000.04, -499.95, 0);
            SetReplay(row3, schema, 100.11, 4, 1_000.08, -499.91, 1);
            var rows = Rows(row0, row1, row2, row3);
            var participants = new[] { new CompactParticipantDictionaryEntry(0, "Driver", "Car", "Class") };
            CompactTelemetryBlock block = CompactTelemetryBlock.FromRows(
                CompactTelemetrySchemaId.ParticipantReplayV1, 2_000, 200, rows);
            byte[] wire = CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(2, 3, 4, block, participants));
            CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(wire);

            AssertEx.Equal(4, decoded.Block.Samples.Count);
            AssertEx.Equal(2_600L, decoded.Block.Samples[3].ElapsedMs);
            AssertEx.Equal(100.0, Value(decoded, 0, schema, "lapDistanceMeters")); // first delta is absolute
            AssertEx.True(Math.Abs(100.1 - Value(decoded, 3, schema, "lapDistanceMeters")) < 1e-9);
            AssertEx.Equal(4.0, Value(decoded, 3, schema, "racePosition"));
            AssertEx.Equal(1.0, Value(decoded, 3, schema, "pitStateRaw"));
        }

        private static void IrregularTimestampsRoundTripExactly()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(
                CompactTelemetrySchemaId.DriverChangeV1);
            long[] elapsed = { 10_000, 10_152, 13_653, 13_697 };
            var samples = new List<CompactTelemetrySample>();
            for (int index = 0; index < elapsed.Length; index++)
            {
                samples.Add(new CompactTelemetrySample(elapsed[index], new double?[] { 12, index * 0.125 }));
            }
            var block = new CompactTelemetryBlock(CompactTelemetrySchemaId.DriverChangeV1, 10_000, 0, samples);
            byte[] wire = CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(4, 5, 6, block));

            AssertEx.Equal((byte)CompactTelemetryProtocol.IrregularDeltaTimeFlags, wire[8]);
            AssertEx.Equal((byte)0, wire[9]);
            CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(wire);
            for (int index = 0; index < elapsed.Length; index++)
            {
                AssertEx.Equal(elapsed[index], decoded.Block.Samples[index].ElapsedMs);
                double? actual = decoded.Block.Samples[index].Values[1];
                AssertEx.True(actual.HasValue);
                AssertEx.True(Math.Abs((index * 0.125) - actual.GetValueOrDefault()) <= schema.Fields[1].MaximumQuantizationError);
            }
        }

        private static void IrregularTimestampFailuresAreRejected()
        {
            var nonMonotonic = new CompactTelemetryBlock(
                CompactTelemetrySchemaId.DriverChangeV1,
                100,
                0,
                new[]
                {
                    new CompactTelemetrySample(101, new double?[] { 1, 1 }),
                    new CompactTelemetrySample(100, new double?[] { 1, 2 })
                });
            ExpectFormat(
                () => CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(1, 1, 1, nonMonotonic)),
                "monotonic");

            var overflowBlock = new CompactTelemetryBlock(
                CompactTelemetrySchemaId.SessionChangeV1,
                long.MaxValue,
                0,
                new[] { new CompactTelemetrySample(long.MaxValue, new double?[] { 0, 0 }) });
            byte[] wire = CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(1, 1, 1, overflowBlock));
            int presenceLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(48, 4)));
            int dictionaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(44, 4)));
            int timestampOffset = CompactTelemetryProtocol.HeaderSize + dictionaryLength + presenceLength;
            wire[timestampOffset] = 1; // base + 1 overflows Int64
            RewriteBodyHash(wire);
            ExpectFormat(() => CompactTelemetryCodec.Decode(wire), "timestamp reconstruction overflow");
        }

        private static void WrongSchemaIsRejected()
        {
            byte[] wire = ValidSessionChangeWire();
            BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(6, 2), 0x7fff);
            ExpectFormat(() => CompactTelemetryCodec.Decode(wire), "Unknown compact telemetry schema");
        }

        private static void WrongVersionIsRejected()
        {
            byte[] wire = ValidSessionChangeWire();
            wire[4] = 2;
            ExpectFormat(() => CompactTelemetryCodec.Decode(wire), "Unsupported compact telemetry protocol version");
        }

        private static void TruncationIsRejected()
        {
            byte[] wire = ValidSessionChangeWire();
            ExpectFormat(() => CompactTelemetryCodec.Decode(wire.Take(wire.Length - 1).ToArray()), "Truncated compact telemetry body");
            ExpectFormat(() => CompactTelemetryCodec.Decode(wire.Take(40).ToArray()), "Truncated compact telemetry header");
        }

        private static void HashMismatchIsRejected()
        {
            byte[] wire = ValidSessionChangeWire();
            wire[wire.Length - 1] ^= 0x01;
            ExpectFormat(() => CompactTelemetryCodec.Decode(wire), "hash mismatch");
        }

        private static void DeltaOverflowIsRejected()
        {
            var sourceBlock = new CompactTelemetryBlock(
                CompactTelemetrySchemaId.TrackGeometryV1,
                0,
                1,
                new[]
                {
                    new CompactTelemetrySample(0, new double?[] { 1, null, null, null }),
                    new CompactTelemetrySample(1, new double?[] { 2, null, null, null })
                });
            byte[] original = CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(1, 1, 1, sourceBlock));
            byte[] absolute = CompactVarInt.EncodeInt64ZigZag(long.MaxValue);
            byte[] delta = CompactVarInt.EncodeInt64ZigZag(1);
            byte[] wire = new byte[CompactTelemetryProtocol.HeaderSize + 1 + absolute.Length + delta.Length];
            Buffer.BlockCopy(original, 0, wire, 0, CompactTelemetryProtocol.HeaderSize);
            wire[CompactTelemetryProtocol.HeaderSize] = 0x01; // field 0 all-present; fields 1..3 absent
            Buffer.BlockCopy(absolute, 0, wire, CompactTelemetryProtocol.HeaderSize + 1, absolute.Length);
            Buffer.BlockCopy(delta, 0, wire, CompactTelemetryProtocol.HeaderSize + 1 + absolute.Length, delta.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(52, 4), checked((uint)(absolute.Length + delta.Length)));
            RewriteBodyHash(wire);

            ExpectFormat(() => CompactTelemetryCodec.Decode(wire), "Delta decoding overflow");
        }

        private static void OutOfRangeQuantizationIsRejected()
        {
            var rows = Rows(new double?[] { 1.1, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 });
            CompactTelemetryBlock block = CompactTelemetryBlock.FromRows(
                CompactTelemetrySchemaId.DriverFastV1, 0, 50, rows);
            ExpectFormat(
                () => CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(1, 1, 1, block)),
                "outside");
        }

        private static byte[] ValidSessionChangeWire()
        {
            CompactTelemetryBlock block = CompactTelemetryBlock.FromRows(
                CompactTelemetrySchemaId.SessionChangeV1,
                100,
                10,
                Rows(new double?[] { 0, 0 }, new double?[] { 1, 1 }));
            return CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(1, 2, 3, block));
        }

        private static void RewriteBodyHash(byte[] wire)
        {
            byte[] hash = SHA256.HashData(wire.AsSpan(CompactTelemetryProtocol.HeaderSize));
            hash.CopyTo(wire.AsSpan(CompactTelemetryProtocol.HashOffset, CompactTelemetryProtocol.Sha256Length));
        }

        private static IReadOnlyList<IReadOnlyList<double?>> Rows(params double?[][] rows)
        {
            return new ReadOnlyCollection<IReadOnlyList<double?>>(
                rows.Select(row => (IReadOnlyList<double?>)row).ToArray());
        }

        private static int IndexOf(IReadOnlyList<string> values, string expected)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], expected, StringComparison.Ordinal)) return index;
            }
            return -1;
        }

        private static void AssertSchemaMatchesCatalogWithoutImplicitTime(
            CompactTelemetrySchemaId schemaId,
            IReadOnlyList<string> catalog)
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(schemaId);
            string[] expected = catalog
                .Where(name => name != "sessionElapsedMs" && name != "capturedAtUnixMs")
                .ToArray();
            AssertEx.Equal(expected.Length, schema.Fields.Count);
            for (int index = 0; index < expected.Length; index++) AssertEx.Equal(expected[index], schema.Fields[index].Name);
            AssertEx.False(schema.Fields.Any(field => field.Name == "sessionElapsedMs"));
            AssertEx.False(schema.Fields.Any(field => field.Name == "capturedAtUnixMs"));
        }

        private static double?[] BlankRow(CompactTelemetrySchema schema)
        {
            return new double?[schema.Fields.Count];
        }

        private static void SetReplay(
            double?[] row,
            CompactTelemetrySchema schema,
            double lapDistance,
            int position,
            double worldX,
            double worldZ,
            int pitState)
        {
            Set(row, schema, "participantRef", 0);
            Set(row, schema, "slot", 0);
            Set(row, schema, "generation", 1);
            Set(row, schema, "lap", 1);
            Set(row, schema, "lapDistanceMeters", lapDistance);
            Set(row, schema, "racePosition", position);
            Set(row, schema, "worldX", worldX);
            Set(row, schema, "worldY", 5.0);
            Set(row, schema, "worldZ", worldZ);
            Set(row, schema, "pitStateRaw", pitState);
        }

        private static void Set(double?[] row, CompactTelemetrySchema schema, string name, double value)
        {
            int ordinal = schema.Fields.Select(field => field.Name).ToList().IndexOf(name);
            if (ordinal < 0) throw new InvalidOperationException("Schema field missing: " + name);
            row[ordinal] = value;
        }

        private static double Value(
            CompactTelemetryEnvelope envelope,
            int sampleIndex,
            CompactTelemetrySchema schema,
            string name)
        {
            int ordinal = schema.Fields.Select(field => field.Name).ToList().IndexOf(name);
            double? value = envelope.Block.Samples[sampleIndex].Values[ordinal];
            AssertEx.True(value.HasValue);
            return value.GetValueOrDefault();
        }

        private static bool ContainsAscii(byte[] bytes, string text)
        {
            byte[] expected = Encoding.ASCII.GetBytes(text);
            for (int offset = 0; offset <= bytes.Length - expected.Length; offset++)
            {
                bool matches = true;
                for (int index = 0; index < expected.Length; index++)
                {
                    if (bytes[offset + index] == expected[index]) continue;
                    matches = false;
                    break;
                }
                if (matches) return true;
            }
            return false;
        }

        private static void ExpectFormat(Action action, string messageFragment)
        {
            try
            {
                action();
            }
            catch (CompactTelemetryFormatException exception)
            {
                AssertEx.True(
                    exception.Message.IndexOf(messageFragment, StringComparison.OrdinalIgnoreCase) >= 0,
                    "Expected error containing '" + messageFragment + "', got '" + exception.Message + "'.");
                return;
            }
            throw new InvalidOperationException("Expected CompactTelemetryFormatException.");
        }
    }
}
