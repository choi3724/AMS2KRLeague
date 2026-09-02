using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AMS2LeagueClient.Core.CompactTelemetry
{
    public static class CompactTelemetryCodec
    {
        private const int MaximumDictionaryStringBytes = 4_096;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(CompactTelemetryEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(envelope.Block.SchemaId);
            ValidateEnvelope(envelope, schema);

            byte[] dictionary = EncodeDictionary(envelope.Participants, envelope.Strings);
            int bytesPerPresenceColumn = checked((envelope.Block.Samples.Count + 7) / 8);
            byte[] presenceStates = new byte[checked(((schema.Fields.Count * 2) + 7) / 8)];
            var mixedPresence = new List<byte[]>();

            using var payloadStream = new MemoryStream();
            if (envelope.Block.CadenceMs == 0)
            {
                WriteIrregularTimestamps(payloadStream, envelope.Block);
            }
            for (int fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
            {
                CompactTelemetryField field = schema.Fields[fieldIndex];
                var values = new List<long>(envelope.Block.Samples.Count);
                var fieldPresence = new byte[bytesPerPresenceColumn];
                for (int sampleIndex = 0; sampleIndex < envelope.Block.Samples.Count; sampleIndex++)
                {
                    double? source = envelope.Block.Samples[sampleIndex].Values[fieldIndex];
                    if (!source.HasValue) continue;
                    fieldPresence[sampleIndex / 8] |= (byte)(1 << (sampleIndex % 8));
                    values.Add(field.Quantize(source.Value));
                }
                int state = values.Count == 0
                    ? 0
                    : values.Count == envelope.Block.Samples.Count
                        ? 1
                        : 2;
                presenceStates[(fieldIndex * 2) / 8] |= (byte)(state << ((fieldIndex * 2) % 8));
                if (state == 2) mixedPresence.Add(fieldPresence);
                WriteColumn(payloadStream, field, values);
            }

            byte[] presence = CombinePresence(presenceStates, mixedPresence);
            byte[] payload = payloadStream.ToArray();
            int bodyLength = checked(dictionary.Length + presence.Length + payload.Length);
            if (bodyLength > CompactTelemetryProtocol.MaximumBodyBytes)
            {
                throw new CompactTelemetryFormatException("Compact telemetry body exceeds the configured limit.");
            }

            byte[] output = new byte[checked(CompactTelemetryProtocol.HeaderSize + bodyLength)];
            Span<byte> header = output.AsSpan(0, CompactTelemetryProtocol.HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(0, 4), CompactTelemetryProtocol.MagicLittleEndian);
            header[4] = CompactTelemetryProtocol.Version;
            header[5] = CompactTelemetryProtocol.HeaderSize;
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6, 2), (ushort)envelope.Block.SchemaId);
            ushort flags = envelope.Block.CadenceMs == 0
                ? CompactTelemetryProtocol.IrregularDeltaTimeFlags
                : CompactTelemetryProtocol.FixedCadenceFlags;
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(8, 2), flags);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(10, 2), checked((ushort)envelope.Strings.Count));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12, 4), envelope.SessionLocalId);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(16, 4), envelope.AttemptLocalId);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(20, 4), envelope.ChunkSequence);
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(24, 8), envelope.Block.BaseElapsedMs);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(32, 4), envelope.Block.CadenceMs);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(36, 4), checked((uint)envelope.Block.Samples.Count));
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(40, 2), checked((ushort)schema.Fields.Count));
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(42, 2), checked((ushort)envelope.Participants.Count));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(44, 4), checked((uint)dictionary.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(48, 4), checked((uint)presence.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(52, 4), checked((uint)payload.Length));

            int bodyOffset = CompactTelemetryProtocol.HeaderSize;
            Buffer.BlockCopy(dictionary, 0, output, bodyOffset, dictionary.Length);
            Buffer.BlockCopy(presence, 0, output, bodyOffset + dictionary.Length, presence.Length);
            Buffer.BlockCopy(payload, 0, output, bodyOffset + dictionary.Length + presence.Length, payload.Length);
            byte[] hash = SHA256.HashData(output.AsSpan(bodyOffset, bodyLength));
            hash.CopyTo(header.Slice(CompactTelemetryProtocol.HashOffset, CompactTelemetryProtocol.Sha256Length));
            return output;
        }

        public static CompactTelemetryEnvelope Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < CompactTelemetryProtocol.HeaderSize)
            {
                throw new CompactTelemetryFormatException("Truncated compact telemetry header.");
            }

            ReadOnlySpan<byte> header = bytes.AsSpan(0, CompactTelemetryProtocol.HeaderSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(0, 4)) != CompactTelemetryProtocol.MagicLittleEndian)
            {
                throw new CompactTelemetryFormatException("Invalid compact telemetry magic.");
            }
            if (header[4] != CompactTelemetryProtocol.Version)
            {
                throw new CompactTelemetryFormatException("Unsupported compact telemetry protocol version " + header[4] + ".");
            }
            if (header[5] != CompactTelemetryProtocol.HeaderSize)
            {
                throw new CompactTelemetryFormatException("Unsupported compact telemetry header size.");
            }

            var schemaId = (CompactTelemetrySchemaId)BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(schemaId);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8, 2));
            if (flags != CompactTelemetryProtocol.FixedCadenceFlags &&
                flags != CompactTelemetryProtocol.IrregularDeltaTimeFlags)
            {
                throw new CompactTelemetryFormatException("Unsupported compact telemetry flags.");
            }
            bool irregularTimestamps = flags == CompactTelemetryProtocol.IrregularDeltaTimeFlags;
            ushort stringDictionaryCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(10, 2));

            uint sessionLocalId = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4));
            uint attemptLocalId = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(16, 4));
            uint chunkSequence = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(20, 4));
            long baseElapsedMs = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(24, 8));
            uint cadenceMs = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(32, 4));
            uint sampleCountRaw = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(36, 4));
            ushort fieldCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(40, 2));
            ushort dictionaryCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(42, 2));
            uint dictionaryLengthRaw = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(44, 4));
            uint presenceLengthRaw = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(48, 4));
            uint payloadLengthRaw = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(52, 4));

            if (sampleCountRaw > CompactTelemetryProtocol.MaximumSamplesPerBlock)
            {
                throw new CompactTelemetryFormatException("Compact telemetry sample count exceeds the configured limit.");
            }
            if (dictionaryCount > CompactTelemetryProtocol.MaximumParticipants)
            {
                throw new CompactTelemetryFormatException("Participant dictionary exceeds the configured limit.");
            }
            if (stringDictionaryCount > CompactTelemetryProtocol.MaximumStringDictionaryEntries)
            {
                throw new CompactTelemetryFormatException("String dictionary exceeds the configured limit.");
            }
            if (fieldCount != schema.Fields.Count)
            {
                throw new CompactTelemetryFormatException("Schema field count does not match the immutable registry.");
            }

            int sampleCount = checked((int)sampleCountRaw);
            if (!irregularTimestamps && sampleCount > 1 && cadenceMs == 0)
            {
                throw new CompactTelemetryFormatException("A multi-sample fixed-cadence block requires a non-zero cadence.");
            }
            if (irregularTimestamps && cadenceMs != 0)
            {
                throw new CompactTelemetryFormatException("An irregular-time block must set cadence to zero.");
            }
            int dictionaryLength = CheckedLength(dictionaryLengthRaw, "dictionary");
            int presenceLength = CheckedLength(presenceLengthRaw, "presence bitmap");
            int payloadLength = CheckedLength(payloadLengthRaw, "payload");
            int bodyLength;
            try
            {
                bodyLength = checked(dictionaryLength + presenceLength + payloadLength);
            }
            catch (OverflowException exception)
            {
                throw new CompactTelemetryFormatException("Compact telemetry body length overflow.", exception);
            }
            if (bodyLength > CompactTelemetryProtocol.MaximumBodyBytes)
            {
                throw new CompactTelemetryFormatException("Compact telemetry body exceeds the configured limit.");
            }
            if (bytes.Length != CompactTelemetryProtocol.HeaderSize + bodyLength)
            {
                throw new CompactTelemetryFormatException("Truncated compact telemetry body or unexpected trailing bytes.");
            }

            ReadOnlySpan<byte> expectedHash = header.Slice(
                CompactTelemetryProtocol.HashOffset,
                CompactTelemetryProtocol.Sha256Length);
            byte[] actualHash = SHA256.HashData(bytes.AsSpan(CompactTelemetryProtocol.HeaderSize, bodyLength));
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new CompactTelemetryFormatException("Compact telemetry body hash mismatch.");
            }

            int dictionaryOffset = CompactTelemetryProtocol.HeaderSize;
            var dictionaryReader = new ByteCursor(bytes, dictionaryOffset, dictionaryLength);
            IReadOnlyList<CompactParticipantDictionaryEntry> participants = DecodeDictionary(dictionaryReader, dictionaryCount);
            IReadOnlyList<CompactStringDictionaryEntry> strings = DecodeStringDictionary(dictionaryReader, stringDictionaryCount);
            dictionaryReader.RequireEnd("dictionary");

            int presenceOffset = dictionaryOffset + dictionaryLength;
            byte[] presence = new byte[presenceLength];
            Buffer.BlockCopy(bytes, presenceOffset, presence, 0, presenceLength);
            bool[][] decodedPresence = DecodePresence(presence, schema.Fields.Count, sampleCount);

            int payloadOffset = presenceOffset + presenceLength;
            var payloadReader = new ByteCursor(bytes, payloadOffset, payloadLength);
            long[] reconstructedElapsed = irregularTimestamps
                ? ReadIrregularTimestamps(payloadReader, baseElapsedMs, sampleCount)
                : Array.Empty<long>();
            var decodedColumns = new long?[schema.Fields.Count][];
            for (int fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
            {
                bool[] fieldPresence = decodedPresence[fieldIndex];
                int presentCount = 0;
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    if (fieldPresence[sampleIndex]) presentCount++;
                }

                long[] presentValues = ReadColumn(payloadReader, schema.Fields[fieldIndex], presentCount);
                var values = new long?[sampleCount];
                int valueIndex = 0;
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    if (fieldPresence[sampleIndex]) values[sampleIndex] = presentValues[valueIndex++];
                }
                decodedColumns[fieldIndex] = values;
            }
            payloadReader.RequireEnd("column payload");

            var samples = new List<CompactTelemetrySample>(sampleCount);
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                long elapsed;
                if (irregularTimestamps)
                {
                    elapsed = reconstructedElapsed[sampleIndex];
                }
                else try
                {
                    elapsed = checked(baseElapsedMs + ((long)sampleIndex * cadenceMs));
                }
                catch (OverflowException exception)
                {
                    throw new CompactTelemetryFormatException("Reconstructed timestamp overflow.", exception);
                }

                var values = new double?[schema.Fields.Count];
                for (int fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
                {
                    long? quantized = decodedColumns[fieldIndex][sampleIndex];
                    if (quantized.HasValue) values[fieldIndex] = schema.Fields[fieldIndex].Dequantize(quantized.Value);
                }
                samples.Add(new CompactTelemetrySample(elapsed, values));
            }

            var block = new CompactTelemetryBlock(schemaId, baseElapsedMs, cadenceMs, samples);
            return new CompactTelemetryEnvelope(sessionLocalId, attemptLocalId, chunkSequence, block, participants, strings);
        }

        private static void ValidateEnvelope(CompactTelemetryEnvelope envelope, CompactTelemetrySchema schema)
        {
            var participantRefs = new HashSet<ushort>();
            for (int index = 0; index < envelope.Participants.Count; index++)
            {
                CompactParticipantDictionaryEntry entry = envelope.Participants[index];
                if (entry.ParticipantRef != index)
                {
                    throw new CompactTelemetryFormatException("Participant references must be contiguous from zero.");
                }
                if (!participantRefs.Add(entry.ParticipantRef))
                {
                    throw new CompactTelemetryFormatException("Duplicate participant reference.");
                }
            }

            var expectedStringRef = new Dictionary<CompactStringDictionaryId, uint>();
            CompactStringDictionaryId? previousDictionary = null;
            foreach (CompactStringDictionaryEntry entry in envelope.Strings)
            {
                if (previousDictionary.HasValue && entry.DictionaryId < previousDictionary.Value)
                {
                    throw new CompactTelemetryFormatException("String dictionary IDs must be ordered.");
                }
                uint expected = expectedStringRef.TryGetValue(entry.DictionaryId, out uint value) ? value : 0;
                if (entry.ValueRef != expected)
                {
                    throw new CompactTelemetryFormatException(
                        "String dictionary references must be contiguous from zero within each dictionary.");
                }
                expectedStringRef[entry.DictionaryId] = checked(expected + 1);
                previousDictionary = entry.DictionaryId;
            }

            for (int sampleIndex = 0; sampleIndex < envelope.Block.Samples.Count; sampleIndex++)
            {
                CompactTelemetrySample sample = envelope.Block.Samples[sampleIndex];
                if (sample.Values.Count != schema.Fields.Count)
                {
                    throw new CompactTelemetryFormatException("Sample field count does not match the immutable schema.");
                }
                if (envelope.Block.CadenceMs == 0)
                {
                    long minimum = sampleIndex == 0
                        ? envelope.Block.BaseElapsedMs
                        : envelope.Block.Samples[sampleIndex - 1].ElapsedMs;
                    if (sample.ElapsedMs < minimum)
                    {
                        throw new CompactTelemetryFormatException(
                            "Irregular timestamps must be monotonic and not precede the block base.");
                    }
                }
                else
                {
                    long expectedElapsed;
                    try
                    {
                        expectedElapsed = checked(envelope.Block.BaseElapsedMs + ((long)sampleIndex * envelope.Block.CadenceMs));
                    }
                    catch (OverflowException exception)
                    {
                        throw new CompactTelemetryFormatException("Input timestamp overflow.", exception);
                    }
                    if (sample.ElapsedMs != expectedElapsed)
                    {
                        throw new CompactTelemetryFormatException("Fixed-cadence timestamps must be reconstructable from the block header.");
                    }
                }
            }
        }

        private static void WriteIrregularTimestamps(Stream stream, CompactTelemetryBlock block)
        {
            long previous = block.BaseElapsedMs;
            for (int index = 0; index < block.Samples.Count; index++)
            {
                long elapsed = block.Samples[index].ElapsedMs;
                if (elapsed < previous)
                {
                    throw new CompactTelemetryFormatException(
                        "Irregular timestamps must be monotonic and not precede the block base.");
                }
                ulong delta;
                try
                {
                    delta = checked((ulong)checked(elapsed - previous));
                }
                catch (OverflowException exception)
                {
                    throw new CompactTelemetryFormatException("Irregular timestamp delta overflow.", exception);
                }
                WriteVarUInt(stream, delta);
                previous = elapsed;
            }
        }

        private static long[] ReadIrregularTimestamps(ByteCursor reader, long baseElapsedMs, int sampleCount)
        {
            var elapsedValues = new long[sampleCount];
            long previous = baseElapsedMs;
            for (int index = 0; index < sampleCount; index++)
            {
                ulong delta = reader.ReadVarUInt();
                if (delta > long.MaxValue)
                {
                    throw new CompactTelemetryFormatException("Irregular timestamp delta exceeds Int64.");
                }
                try
                {
                    previous = checked(previous + (long)delta);
                }
                catch (OverflowException exception)
                {
                    throw new CompactTelemetryFormatException("Irregular timestamp reconstruction overflow.", exception);
                }
                elapsedValues[index] = previous;
            }
            return elapsedValues;
        }

        private static byte[] EncodeDictionary(
            IReadOnlyList<CompactParticipantDictionaryEntry> participants,
            IReadOnlyList<CompactStringDictionaryEntry> strings)
        {
            using var stream = new MemoryStream();
            foreach (CompactParticipantDictionaryEntry participant in participants)
            {
                WriteVarUInt(stream, participant.ParticipantRef);
                WriteString(stream, participant.DisplayName);
                WriteString(stream, participant.VehicleName);
                WriteString(stream, participant.ClassName);
            }
            foreach (CompactStringDictionaryEntry entry in strings)
            {
                WriteVarUInt(stream, (ushort)entry.DictionaryId);
                WriteVarUInt(stream, entry.ValueRef);
                WriteString(stream, entry.Value);
            }
            return stream.ToArray();
        }

        private static IReadOnlyList<CompactParticipantDictionaryEntry> DecodeDictionary(ByteCursor reader, int count)
        {
            var participants = new List<CompactParticipantDictionaryEntry>(count);
            for (int index = 0; index < count; index++)
            {
                ulong reference = reader.ReadVarUInt();
                if (reference != (ulong)index || reference > ushort.MaxValue)
                {
                    throw new CompactTelemetryFormatException("Participant references must be contiguous from zero.");
                }
                participants.Add(new CompactParticipantDictionaryEntry(
                    (ushort)reference,
                    reader.ReadString(MaximumDictionaryStringBytes, StrictUtf8),
                    reader.ReadString(MaximumDictionaryStringBytes, StrictUtf8),
                    reader.ReadString(MaximumDictionaryStringBytes, StrictUtf8)));
            }
            return participants;
        }

        private static IReadOnlyList<CompactStringDictionaryEntry> DecodeStringDictionary(ByteCursor reader, int count)
        {
            var result = new List<CompactStringDictionaryEntry>(count);
            var expectedReferences = new Dictionary<CompactStringDictionaryId, uint>();
            CompactStringDictionaryId? previousDictionary = null;
            for (int index = 0; index < count; index++)
            {
                ulong rawDictionaryId = reader.ReadVarUInt();
                if (rawDictionaryId > ushort.MaxValue
                    || !Enum.IsDefined(typeof(CompactStringDictionaryId), (ushort)rawDictionaryId))
                {
                    throw new CompactTelemetryFormatException("Unknown compact string dictionary ID.");
                }
                var dictionaryId = (CompactStringDictionaryId)(ushort)rawDictionaryId;
                if (previousDictionary.HasValue && dictionaryId < previousDictionary.Value)
                {
                    throw new CompactTelemetryFormatException("String dictionary IDs must be ordered.");
                }
                ulong rawReference = reader.ReadVarUInt();
                uint expected = expectedReferences.TryGetValue(dictionaryId, out uint value) ? value : 0;
                if (rawReference != expected)
                {
                    throw new CompactTelemetryFormatException(
                        "String dictionary references must be contiguous from zero within each dictionary.");
                }
                result.Add(new CompactStringDictionaryEntry(
                    dictionaryId,
                    expected,
                    reader.ReadString(MaximumDictionaryStringBytes, StrictUtf8)));
                expectedReferences[dictionaryId] = checked(expected + 1);
                previousDictionary = dictionaryId;
            }
            return result;
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = StrictUtf8.GetBytes(value);
            if (bytes.Length > MaximumDictionaryStringBytes)
            {
                throw new CompactTelemetryFormatException("Participant dictionary string exceeds the configured limit.");
            }
            WriteVarUInt(stream, checked((ulong)bytes.Length));
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteColumn(Stream stream, CompactTelemetryField field, IReadOnlyList<long> values)
        {
            switch (field.Encoding)
            {
                case CompactFieldEncoding.FixedUnsigned:
                    foreach (long value in values) WriteFixedUnsigned(stream, value, field.FixedWidth);
                    break;
                case CompactFieldEncoding.FixedSigned:
                    foreach (long value in values) WriteFixedSigned(stream, value, field.FixedWidth);
                    break;
                case CompactFieldEncoding.VarUInt:
                    foreach (long value in values)
                    {
                        if (value < 0) throw new CompactTelemetryFormatException("Unsigned field cannot encode a negative value.");
                        WriteVarUInt(stream, checked((ulong)value));
                    }
                    break;
                case CompactFieldEncoding.ZigZag:
                    foreach (long value in values) WriteZigZag(stream, value);
                    break;
                case CompactFieldEncoding.DeltaZigZag:
                    WriteDeltaColumn(stream, values);
                    break;
                case CompactFieldEncoding.RleUnsigned:
                    WriteRleColumn(stream, values, true);
                    break;
                case CompactFieldEncoding.RleZigZag:
                    WriteRleColumn(stream, values, false);
                    break;
                default:
                    throw new CompactTelemetryFormatException("Unsupported field encoding.");
            }
        }

        private static long[] ReadColumn(ByteCursor reader, CompactTelemetryField field, int valueCount)
        {
            var values = new long[valueCount];
            switch (field.Encoding)
            {
                case CompactFieldEncoding.FixedUnsigned:
                    for (int index = 0; index < valueCount; index++) values[index] = reader.ReadFixedUnsigned(field.FixedWidth);
                    break;
                case CompactFieldEncoding.FixedSigned:
                    for (int index = 0; index < valueCount; index++) values[index] = reader.ReadFixedSigned(field.FixedWidth);
                    break;
                case CompactFieldEncoding.VarUInt:
                    for (int index = 0; index < valueCount; index++) values[index] = CheckedSigned(reader.ReadVarUInt(), field.Name);
                    break;
                case CompactFieldEncoding.ZigZag:
                    for (int index = 0; index < valueCount; index++) values[index] = CompactVarInt.DecodeZigZag(reader.ReadVarUInt());
                    break;
                case CompactFieldEncoding.DeltaZigZag:
                    ReadDeltaColumn(reader, values);
                    break;
                case CompactFieldEncoding.RleUnsigned:
                    ReadRleColumn(reader, values, true, field.Name);
                    break;
                case CompactFieldEncoding.RleZigZag:
                    ReadRleColumn(reader, values, false, field.Name);
                    break;
                default:
                    throw new CompactTelemetryFormatException("Unsupported field encoding.");
            }

            foreach (long value in values) field.ValidateQuantized(value);
            return values;
        }

        private static void WriteDeltaColumn(Stream stream, IReadOnlyList<long> values)
        {
            if (values.Count == 0) return;
            WriteZigZag(stream, values[0]);
            for (int index = 1; index < values.Count; index++)
            {
                long delta;
                try
                {
                    delta = checked(values[index] - values[index - 1]);
                }
                catch (OverflowException exception)
                {
                    throw new CompactTelemetryFormatException("Delta encoding overflow.", exception);
                }
                WriteZigZag(stream, delta);
            }
        }

        private static void ReadDeltaColumn(ByteCursor reader, long[] values)
        {
            if (values.Length == 0) return;
            values[0] = CompactVarInt.DecodeZigZag(reader.ReadVarUInt());
            for (int index = 1; index < values.Length; index++)
            {
                long delta = CompactVarInt.DecodeZigZag(reader.ReadVarUInt());
                try
                {
                    values[index] = checked(values[index - 1] + delta);
                }
                catch (OverflowException exception)
                {
                    throw new CompactTelemetryFormatException("Delta decoding overflow.", exception);
                }
            }
        }

        private static void WriteRleColumn(Stream stream, IReadOnlyList<long> values, bool unsigned)
        {
            int index = 0;
            while (index < values.Count)
            {
                int end = index + 1;
                while (end < values.Count && values[end] == values[index]) end++;
                WriteVarUInt(stream, checked((ulong)(end - index)));
                if (unsigned)
                {
                    if (values[index] < 0) throw new CompactTelemetryFormatException("Unsigned RLE cannot encode a negative value.");
                    WriteVarUInt(stream, checked((ulong)values[index]));
                }
                else
                {
                    WriteZigZag(stream, values[index]);
                }
                index = end;
            }
        }

        private static void ReadRleColumn(ByteCursor reader, long[] values, bool unsigned, string fieldName)
        {
            int offset = 0;
            while (offset < values.Length)
            {
                ulong runLengthRaw = reader.ReadVarUInt();
                if (runLengthRaw == 0 || runLengthRaw > (ulong)(values.Length - offset))
                {
                    throw new CompactTelemetryFormatException("Invalid RLE run length.");
                }
                int runLength = checked((int)runLengthRaw);
                long value = unsigned
                    ? CheckedSigned(reader.ReadVarUInt(), fieldName)
                    : CompactVarInt.DecodeZigZag(reader.ReadVarUInt());
                for (int index = 0; index < runLength; index++) values[offset++] = value;
            }
        }

        private static void WriteFixedUnsigned(Stream stream, long value, int width)
        {
            if (value < 0) throw new CompactTelemetryFormatException("Unsigned fixed field cannot encode a negative value.");
            ulong unsigned = checked((ulong)value);
            ulong maximum = width == 8 ? ulong.MaxValue : ((1UL << (width * 8)) - 1);
            if (unsigned > maximum) throw new CompactTelemetryFormatException("Unsigned fixed value exceeds its wire width.");
            for (int index = 0; index < width; index++) stream.WriteByte((byte)(unsigned >> (index * 8)));
        }

        private static void WriteFixedSigned(Stream stream, long value, int width)
        {
            if (width < 8)
            {
                long minimum = -(1L << ((width * 8) - 1));
                long maximum = (1L << ((width * 8) - 1)) - 1;
                if (value < minimum || value > maximum)
                {
                    throw new CompactTelemetryFormatException("Signed fixed value exceeds its wire width.");
                }
            }
            ulong encoded = unchecked((ulong)value);
            for (int index = 0; index < width; index++) stream.WriteByte((byte)(encoded >> (index * 8)));
        }

        private static void WriteVarUInt(Stream stream, ulong value)
        {
            byte[] bytes = CompactVarInt.EncodeUInt64(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteZigZag(Stream stream, long value)
        {
            byte[] bytes = CompactVarInt.EncodeInt64ZigZag(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static int CheckedLength(uint value, string label)
        {
            if (value > int.MaxValue)
            {
                throw new CompactTelemetryFormatException("Compact telemetry " + label + " length exceeds Int32.");
            }
            return (int)value;
        }

        private static long CheckedSigned(ulong value, string fieldName)
        {
            if (value > long.MaxValue)
            {
                throw new CompactTelemetryFormatException("Field " + fieldName + " exceeds Int64.");
            }
            return (long)value;
        }

        private static byte[] CombinePresence(byte[] states, IReadOnlyList<byte[]> mixedColumns)
        {
            int length = states.Length;
            foreach (byte[] column in mixedColumns) length = checked(length + column.Length);
            var combined = new byte[length];
            Buffer.BlockCopy(states, 0, combined, 0, states.Length);
            int offset = states.Length;
            foreach (byte[] column in mixedColumns)
            {
                Buffer.BlockCopy(column, 0, combined, offset, column.Length);
                offset += column.Length;
            }
            return combined;
        }

        private static bool[][] DecodePresence(byte[] presence, int fieldCount, int sampleCount)
        {
            int stateBytes = checked(((fieldCount * 2) + 7) / 8);
            if (presence.Length < stateBytes)
            {
                throw new CompactTelemetryFormatException("Presence state bitmap is truncated.");
            }
            int usedStateBits = fieldCount * 2;
            if ((usedStateBits % 8) != 0)
            {
                byte invalidStateMask = (byte)(0xff << (usedStateBits % 8));
                if ((presence[stateBytes - 1] & invalidStateMask) != 0)
                {
                    throw new CompactTelemetryFormatException("Presence state bitmap has non-zero padding bits.");
                }
            }

            int bytesPerMixedColumn = (sampleCount + 7) / 8;
            int offset = stateBytes;
            var decoded = new bool[fieldCount][];
            for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                int state = (presence[(fieldIndex * 2) / 8] >> ((fieldIndex * 2) % 8)) & 0x03;
                var values = new bool[sampleCount];
                if (state == 1)
                {
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) values[sampleIndex] = true;
                }
                else if (state == 2)
                {
                    if (offset + bytesPerMixedColumn > presence.Length)
                    {
                        throw new CompactTelemetryFormatException("Mixed presence bitmap is truncated.");
                    }
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        values[sampleIndex] = (presence[offset + (sampleIndex / 8)] &
                            (1 << (sampleIndex % 8))) != 0;
                    }
                    if (sampleCount == 0)
                    {
                        throw new CompactTelemetryFormatException("An empty block cannot use mixed presence.");
                    }
                    if ((sampleCount % 8) != 0)
                    {
                        byte invalidMask = (byte)(0xff << (sampleCount % 8));
                        if ((presence[offset + bytesPerMixedColumn - 1] & invalidMask) != 0)
                        {
                            throw new CompactTelemetryFormatException("Mixed presence bitmap has non-zero padding bits.");
                        }
                    }
                    bool any = false;
                    bool all = true;
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        any |= values[sampleIndex];
                        all &= values[sampleIndex];
                    }
                    if (!any || all)
                    {
                        throw new CompactTelemetryFormatException("Mixed presence must contain both null and present samples.");
                    }
                    offset += bytesPerMixedColumn;
                }
                else if (state == 3)
                {
                    throw new CompactTelemetryFormatException("Reserved presence state.");
                }
                decoded[fieldIndex] = values;
            }
            if (offset != presence.Length)
            {
                throw new CompactTelemetryFormatException("Unexpected trailing bytes in presence bitmap.");
            }
            return decoded;
        }

        private sealed class ByteCursor
        {
            private readonly byte[] _bytes;
            private readonly int _end;
            private int _offset;

            public ByteCursor(byte[] bytes, int offset, int length)
            {
                _bytes = bytes;
                _offset = offset;
                _end = checked(offset + length);
            }

            public byte ReadByte()
            {
                if (_offset >= _end) throw new CompactTelemetryFormatException("Truncated compact telemetry value.");
                return _bytes[_offset++];
            }

            public ulong ReadVarUInt()
            {
                ulong value = 0;
                for (int index = 0; index < 10; index++)
                {
                    byte current = ReadByte();
                    if (index == 9 && (current & 0xfe) != 0)
                    {
                        throw new CompactTelemetryFormatException("Varint exceeds UInt64.");
                    }
                    value |= ((ulong)(current & 0x7f)) << (index * 7);
                    if ((current & 0x80) == 0) return value;
                }
                throw new CompactTelemetryFormatException("Varint exceeds ten bytes.");
            }

            public long ReadFixedUnsigned(int width)
            {
                ulong value = 0;
                for (int index = 0; index < width; index++) value |= ((ulong)ReadByte()) << (index * 8);
                if (value > long.MaxValue) throw new CompactTelemetryFormatException("Unsigned fixed value exceeds Int64.");
                return (long)value;
            }

            public long ReadFixedSigned(int width)
            {
                ulong value = 0;
                for (int index = 0; index < width; index++) value |= ((ulong)ReadByte()) << (index * 8);
                if (width < 8 && (value & (1UL << ((width * 8) - 1))) != 0)
                {
                    value |= ulong.MaxValue << (width * 8);
                }
                return unchecked((long)value);
            }

            public string ReadString(int maximumBytes, Encoding encoding)
            {
                ulong lengthRaw = ReadVarUInt();
                if (lengthRaw > (ulong)maximumBytes || lengthRaw > (ulong)(_end - _offset))
                {
                    throw new CompactTelemetryFormatException("Invalid participant dictionary string length.");
                }
                int length = checked((int)lengthRaw);
                try
                {
                    string value = encoding.GetString(_bytes, _offset, length);
                    _offset += length;
                    return value;
                }
                catch (DecoderFallbackException exception)
                {
                    throw new CompactTelemetryFormatException("Participant dictionary contains invalid UTF-8.", exception);
                }
            }

            public void RequireEnd(string section)
            {
                if (_offset != _end)
                {
                    throw new CompactTelemetryFormatException("Unexpected trailing bytes in " + section + ".");
                }
            }
        }
    }
}
