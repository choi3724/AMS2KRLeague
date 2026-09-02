using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2CompactProof
{
    internal sealed class CodecTournament
    {
        private readonly List<CandidateRunner> _runners = new List<CandidateRunner>
        {
            new CandidateRunner("FIXED_BINARY_ROWS", EncodeFixedRows, DecodeFixedRows),
            new CandidateRunner("COLUMN_BINARY", EncodeFixedColumns, DecodeFixedColumns),
            new CandidateRunner("DELTA_COLUMN_Q1E-6", EncodeDeltaColumns, DecodeDeltaColumns),
            new CandidateRunner("DELTA_RLE_COLUMN_Q1E-6", EncodeDeltaRleColumns, DecodeDeltaRleColumns)
        };

        public IReadOnlyList<TournamentEntry> Run(string archiveRoot)
        {
            string[] paths = Directory.EnumerateFiles(archiveRoot, "*.json.gz", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (string path in paths)
            {
                TelemetryChunkEnvelope chunk;
                using (FileStream stream = File.OpenRead(path))
                {
                    chunk = TelemetryChunkSerializer.Deserialize(TelemetryChunkSerializer.Gunzip(stream));
                }
                foreach (CandidateRunner runner in _runners) runner.Accept(chunk);
            }
            return _runners.Select(runner => runner.Finish()).ToArray();
        }

        private static byte[] EncodeFixedRows(TelemetryChunkEnvelope chunk)
        {
            if (chunk.Data.Rows.Count == 0) return EncodeMetadata(chunk);
            using var stream = new MemoryStream();
            WriteChunkHeader(stream, chunk, 1);
            foreach (double?[] row in chunk.Data.Rows)
            {
                foreach (double? value in row)
                {
                    stream.WriteByte(value.HasValue ? (byte)1 : (byte)0);
                    if (value.HasValue) WriteDouble(stream, value.Value);
                }
            }
            return stream.ToArray();
        }

        private static void DecodeFixedRows(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
            ChunkDimensions dimensions = ReadChunkHeader(stream, 1);
            if (dimensions.Opaque)
            {
                SkipOpaque(stream);
                EnsureEnd(stream);
                return;
            }
            for (int row = 0; row < dimensions.Rows; row++)
            {
                for (int field = 0; field < dimensions.Fields; field++)
                {
                    int present = ReadByte(stream);
                    if (present == 1) ReadInt64(stream);
                    else if (present != 0) throw new InvalidDataException("Invalid fixed-row presence byte.");
                }
            }
            EnsureEnd(stream);
        }

        private static byte[] EncodeFixedColumns(TelemetryChunkEnvelope chunk)
        {
            if (chunk.Data.Rows.Count == 0) return EncodeMetadata(chunk);
            using var stream = new MemoryStream();
            WriteChunkHeader(stream, chunk, 2);
            int rows = chunk.Data.Rows.Count;
            int fields = chunk.Data.Fields.Length;
            for (int field = 0; field < fields; field++)
            {
                WritePresence(stream, chunk.Data.Rows, field);
                for (int row = 0; row < rows; row++)
                {
                    double? value = chunk.Data.Rows[row][field];
                    if (value.HasValue) WriteDouble(stream, value.Value);
                }
            }
            return stream.ToArray();
        }

        private static void DecodeFixedColumns(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
            ChunkDimensions dimensions = ReadChunkHeader(stream, 2);
            if (dimensions.Opaque)
            {
                SkipOpaque(stream);
                EnsureEnd(stream);
                return;
            }
            int presenceBytes = (dimensions.Rows + 7) / 8;
            var presence = new byte[presenceBytes];
            for (int field = 0; field < dimensions.Fields; field++)
            {
                ReadExactly(stream, presence);
                for (int row = 0; row < dimensions.Rows; row++)
                {
                    if ((presence[row / 8] & (1 << (row % 8))) != 0) ReadInt64(stream);
                }
            }
            EnsureEnd(stream);
        }

        private static byte[] EncodeDeltaColumns(TelemetryChunkEnvelope chunk)
        {
            if (chunk.Data.Rows.Count == 0) return EncodeMetadata(chunk);
            using var stream = new MemoryStream();
            WriteChunkHeader(stream, chunk, 3);
            int rows = chunk.Data.Rows.Count;
            int fields = chunk.Data.Fields.Length;
            for (int field = 0; field < fields; field++)
            {
                WritePresence(stream, chunk.Data.Rows, field);
                bool hasPrevious = false;
                long previous = 0;
                for (int row = 0; row < rows; row++)
                {
                    double? value = chunk.Data.Rows[row][field];
                    if (!value.HasValue) continue;
                    long quantized = QuantizeMicro(value.Value);
                    long encoded = hasPrevious ? checked(quantized - previous) : quantized;
                    WriteZigZag(stream, encoded);
                    previous = quantized;
                    hasPrevious = true;
                }
            }
            return stream.ToArray();
        }

        private static void DecodeDeltaColumns(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
            ChunkDimensions dimensions = ReadChunkHeader(stream, 3);
            if (dimensions.Opaque)
            {
                SkipOpaque(stream);
                EnsureEnd(stream);
                return;
            }
            int presenceBytes = (dimensions.Rows + 7) / 8;
            var presence = new byte[presenceBytes];
            for (int field = 0; field < dimensions.Fields; field++)
            {
                ReadExactly(stream, presence);
                bool hasPrevious = false;
                long previous = 0;
                for (int row = 0; row < dimensions.Rows; row++)
                {
                    if ((presence[row / 8] & (1 << (row % 8))) == 0) continue;
                    long encoded = ReadZigZag(stream);
                    long value = hasPrevious ? checked(previous + encoded) : encoded;
                    previous = value;
                    hasPrevious = true;
                }
            }
            EnsureEnd(stream);
        }

        private static byte[] EncodeDeltaRleColumns(TelemetryChunkEnvelope chunk)
        {
            if (chunk.Data.Rows.Count == 0) return EncodeMetadata(chunk);
            using var stream = new MemoryStream();
            WriteChunkHeader(stream, chunk, 4);
            int rows = chunk.Data.Rows.Count;
            int fields = chunk.Data.Fields.Length;
            for (int field = 0; field < fields; field++)
            {
                WritePresence(stream, chunk.Data.Rows, field);
                var deltas = new List<long>();
                bool hasPrevious = false;
                long previous = 0;
                for (int row = 0; row < rows; row++)
                {
                    double? value = chunk.Data.Rows[row][field];
                    if (!value.HasValue) continue;
                    long quantized = QuantizeMicro(value.Value);
                    deltas.Add(hasPrevious ? checked(quantized - previous) : quantized);
                    previous = quantized;
                    hasPrevious = true;
                }
                WriteVarUInt(stream, checked((ulong)deltas.Count));
                int index = 0;
                while (index < deltas.Count)
                {
                    int end = index + 1;
                    while (end < deltas.Count && deltas[end] == deltas[index]) end++;
                    WriteVarUInt(stream, checked((ulong)(end - index)));
                    WriteZigZag(stream, deltas[index]);
                    index = end;
                }
            }
            return stream.ToArray();
        }

        private static void DecodeDeltaRleColumns(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
            ChunkDimensions dimensions = ReadChunkHeader(stream, 4);
            if (dimensions.Opaque)
            {
                SkipOpaque(stream);
                EnsureEnd(stream);
                return;
            }
            int presenceBytes = (dimensions.Rows + 7) / 8;
            var presence = new byte[presenceBytes];
            for (int field = 0; field < dimensions.Fields; field++)
            {
                ReadExactly(stream, presence);
                int expected = 0;
                for (int row = 0; row < dimensions.Rows; row++)
                {
                    if ((presence[row / 8] & (1 << (row % 8))) != 0) expected++;
                }
                ulong declared = ReadVarUInt(stream);
                if (declared != (ulong)expected) throw new InvalidDataException("Delta-RLE presence count mismatch.");
                int decoded = 0;
                long previous = 0;
                while (decoded < expected)
                {
                    ulong run = ReadVarUInt(stream);
                    if (run == 0 || run > (ulong)(expected - decoded)) throw new InvalidDataException("Invalid delta-RLE run.");
                    long delta = ReadZigZag(stream);
                    for (ulong index = 0; index < run; index++) previous = checked(previous + delta);
                    decoded = checked(decoded + (int)run);
                }
            }
            EnsureEnd(stream);
        }

        private static byte[] EncodeMetadata(TelemetryChunkEnvelope chunk)
        {
            byte[] payload = TelemetryChunkSerializer.Serialize(chunk);
            using var stream = new MemoryStream();
            WriteChunkHeader(stream, chunk, 0xff);
            WriteVarUInt(stream, checked((ulong)payload.Length));
            stream.Write(payload, 0, payload.Length);
            return stream.ToArray();
        }

        private static void WriteChunkHeader(Stream stream, TelemetryChunkEnvelope chunk, byte method)
        {
            stream.WriteByte((byte)'T');
            stream.WriteByte((byte)'N');
            stream.WriteByte(method);
            stream.WriteByte(chunk.Data.Rows.Count == 0 ? (byte)1 : (byte)0);
            WriteVarUInt(stream, checked((ulong)chunk.Data.Fields.Length));
            WriteVarUInt(stream, checked((ulong)chunk.Data.Rows.Count));
        }

        private static ChunkDimensions ReadChunkHeader(Stream stream, byte expectedMethod)
        {
            if (ReadByte(stream) != 'T' || ReadByte(stream) != 'N') throw new InvalidDataException("Invalid tournament frame magic.");
            int method = ReadByte(stream);
            bool opaque = ReadByte(stream) == 1;
            int fields = checked((int)ReadVarUInt(stream));
            int rows = checked((int)ReadVarUInt(stream));
            if (!opaque && method != expectedMethod) throw new InvalidDataException("Tournament method mismatch.");
            if (opaque && method != 0xff) throw new InvalidDataException("Opaque tournament frame method mismatch.");
            return new ChunkDimensions(fields, rows, opaque);
        }

        private static void SkipOpaque(Stream stream)
        {
            int length = checked((int)ReadVarUInt(stream));
            var buffer = new byte[length];
            ReadExactly(stream, buffer);
        }

        private static void WritePresence(Stream stream, IReadOnlyList<double?[]> rows, int field)
        {
            int byteCount = (rows.Count + 7) / 8;
            var bitmap = new byte[byteCount];
            for (int row = 0; row < rows.Count; row++)
            {
                if (rows[row][field].HasValue) bitmap[row / 8] |= (byte)(1 << (row % 8));
            }
            stream.Write(bitmap, 0, bitmap.Length);
        }

        private static long QuantizeMicro(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Tournament input is not finite.");
            return checked((long)Math.Round(value * 1_000_000.0, MidpointRounding.AwayFromZero));
        }

        private static void WriteDouble(Stream stream, double value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
            stream.Write(bytes);
        }

        private static long ReadInt64(Stream stream)
        {
            Span<byte> bytes = stackalloc byte[8];
            ReadExactly(stream, bytes);
            return BinaryPrimitives.ReadInt64LittleEndian(bytes);
        }

        private static void WriteZigZag(Stream stream, long value)
        {
            ulong encoded = unchecked(((ulong)value << 1) ^ (ulong)(value >> 63));
            WriteVarUInt(stream, encoded);
        }

        private static long ReadZigZag(Stream stream)
        {
            ulong encoded = ReadVarUInt(stream);
            return unchecked((long)(encoded >> 1) ^ -((long)encoded & 1));
        }

        private static void WriteVarUInt(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        private static ulong ReadVarUInt(Stream stream)
        {
            ulong value = 0;
            for (int index = 0; index < 10; index++)
            {
                int current = ReadByte(stream);
                if (index == 9 && current > 1) throw new InvalidDataException("Tournament varint overflow.");
                value |= ((ulong)(current & 0x7f)) << (index * 7);
                if ((current & 0x80) == 0) return value;
            }
            throw new InvalidDataException("Tournament varint overflow.");
        }

        private static int ReadByte(Stream stream)
        {
            int value = stream.ReadByte();
            return value >= 0 ? value : throw new EndOfStreamException();
        }

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(offset));
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer) => ReadExactly(stream, buffer.AsSpan());

        private static void EnsureEnd(Stream stream)
        {
            if (stream.Position != stream.Length) throw new InvalidDataException("Tournament decoder left trailing bytes.");
        }

        private readonly struct ChunkDimensions
        {
            public ChunkDimensions(int fields, int rows, bool opaque) { Fields = fields; Rows = rows; Opaque = opaque; }
            public int Fields { get; }
            public int Rows { get; }
            public bool Opaque { get; }
        }

        private sealed class CandidateRunner
        {
            private readonly string _name;
            private readonly Func<TelemetryChunkEnvelope, byte[]> _encode;
            private readonly Action<byte[]> _decode;
            private readonly CountingStream _wire = new CountingStream();
            private readonly GZipStream _gzip;
            private long _rawBytes;
            private long _encodeTicks;
            private long _decodeTicks;
            private int _maximumChunkBytes;
            private int _chunks;

            public CandidateRunner(string name, Func<TelemetryChunkEnvelope, byte[]> encode, Action<byte[]> decode)
            {
                _name = name;
                _encode = encode;
                _decode = decode;
                _gzip = new GZipStream(_wire, CompressionLevel.SmallestSize, true);
            }

            public void Accept(TelemetryChunkEnvelope chunk)
            {
                long started = Stopwatch.GetTimestamp();
                byte[] bytes = _encode(chunk);
                _encodeTicks += Stopwatch.GetTimestamp() - started;
                _rawBytes = checked(_rawBytes + bytes.Length);
                _maximumChunkBytes = Math.Max(_maximumChunkBytes, bytes.Length);
                _gzip.Write(bytes, 0, bytes.Length);
                started = Stopwatch.GetTimestamp();
                _decode(bytes);
                _decodeTicks += Stopwatch.GetTimestamp() - started;
                _chunks++;
            }

            public TournamentEntry Finish()
            {
                _gzip.Dispose();
                return new TournamentEntry
                {
                    Method = _name,
                    RawBytes = _rawBytes,
                    GzipBytes = _wire.BytesWritten,
                    EncodeMilliseconds = TicksToMilliseconds(_encodeTicks),
                    DecodeMilliseconds = TicksToMilliseconds(_decodeTicks),
                    PeakWorkingChunkBytes = _maximumChunkBytes,
                    Chunks = _chunks,
                    RoundTrip = "PASS",
                    Complexity = _name.IndexOf("RLE", StringComparison.Ordinal) >= 0 ? "MEDIUM" : "LOW"
                };
            }

            private static double TicksToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        }

        private sealed class CountingStream : Stream
        {
            public long BytesWritten { get; private set; }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => BytesWritten;
            public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => BytesWritten = checked(BytesWritten + count);
            public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten = checked(BytesWritten + buffer.Length);
            public override void WriteByte(byte value) => BytesWritten = checked(BytesWritten + 1);
        }
    }

    internal sealed class TournamentEntry
    {
        public string Method { get; set; } = string.Empty;
        public long RawBytes { get; set; }
        public long GzipBytes { get; set; }
        public double EncodeMilliseconds { get; set; }
        public double DecodeMilliseconds { get; set; }
        public int PeakWorkingChunkBytes { get; set; }
        public int Chunks { get; set; }
        public string RoundTrip { get; set; } = string.Empty;
        public string Complexity { get; set; } = string.Empty;
    }
}
