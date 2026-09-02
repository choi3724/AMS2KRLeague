using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.CompactTelemetry
{
    public static class CompactVarInt
    {
        public static byte[] EncodeUInt64(ulong value)
        {
            var bytes = new List<byte>(10);
            do
            {
                byte next = (byte)(value & 0x7fU);
                value >>= 7;
                if (value != 0) next |= 0x80;
                bytes.Add(next);
            }
            while (value != 0);
            return bytes.ToArray();
        }

        public static ulong DecodeUInt64(ReadOnlySpan<byte> bytes, out int bytesRead)
        {
            int offset = 0;
            ulong value = ReadUInt64(bytes, ref offset);
            bytesRead = offset;
            return value;
        }

        public static byte[] EncodeInt64ZigZag(long value)
        {
            ulong zigZag = (unchecked((ulong)value) << 1) ^ unchecked((ulong)(value >> 63));
            return EncodeUInt64(zigZag);
        }

        public static long DecodeInt64ZigZag(ReadOnlySpan<byte> bytes, out int bytesRead)
        {
            ulong value = DecodeUInt64(bytes, out bytesRead);
            return DecodeZigZag(value);
        }

        internal static ulong ReadUInt64(ReadOnlySpan<byte> bytes, ref int offset)
        {
            ulong value = 0;
            for (int index = 0; index < 10; index++)
            {
                if (offset >= bytes.Length) throw new CompactTelemetryFormatException("Truncated varint.");
                byte current = bytes[offset++];
                if (index == 9 && (current & 0xfe) != 0)
                {
                    throw new CompactTelemetryFormatException("Varint exceeds UInt64.");
                }
                value |= ((ulong)(current & 0x7f)) << (index * 7);
                if ((current & 0x80) == 0) return value;
            }
            throw new CompactTelemetryFormatException("Varint exceeds ten bytes.");
        }

        internal static long DecodeZigZag(ulong value)
        {
            return unchecked((long)(value >> 1)) ^ -unchecked((long)(value & 1));
        }
    }
}
