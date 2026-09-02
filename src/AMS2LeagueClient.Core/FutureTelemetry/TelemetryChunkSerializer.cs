using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public static class TelemetryChunkSerializer
    {
        private static readonly JsonSerializerOptions Canonical = CreateOptions(false);
        private static readonly JsonSerializerOptions Pretty = CreateOptions(true);

        public static byte[] Serialize(TelemetryChunkEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            return JsonSerializer.SerializeToUtf8Bytes(envelope, Canonical);
        }

        public static byte[] SerializeMetadata(TelemetryPendingUploadMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            return JsonSerializer.SerializeToUtf8Bytes(metadata, Pretty);
        }

        public static TelemetryChunkEnvelope Deserialize(byte[] payloadUtf8)
        {
            if (payloadUtf8 == null) throw new ArgumentNullException(nameof(payloadUtf8));
            TelemetryChunkEnvelope? value = JsonSerializer.Deserialize<TelemetryChunkEnvelope>(payloadUtf8, Canonical);
            return value ?? throw new InvalidDataException("Telemetry chunk JSON was empty.");
        }

        public static TelemetryPendingUploadMetadata DeserializeMetadata(byte[] payloadUtf8)
        {
            if (payloadUtf8 == null) throw new ArgumentNullException(nameof(payloadUtf8));
            TelemetryPendingUploadMetadata? value =
                JsonSerializer.Deserialize<TelemetryPendingUploadMetadata>(payloadUtf8, Canonical);
            return value ?? throw new InvalidDataException("Telemetry upload metadata JSON was empty.");
        }

        public static byte[] Gzip(byte[] payloadUtf8)
        {
            if (payloadUtf8 == null) throw new ArgumentNullException(nameof(payloadUtf8));
            using var destination = new MemoryStream();
            using (var gzip = new GZipStream(destination, CompressionLevel.SmallestSize, true))
            {
                gzip.Write(payloadUtf8, 0, payloadUtf8.Length);
            }
            return destination.ToArray();
        }

        public static byte[] Gunzip(Stream compressed, int maximumUncompressedBytes = 268_435_456)
        {
            if (compressed == null) throw new ArgumentNullException(nameof(compressed));
            if (maximumUncompressedBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumUncompressedBytes));
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress, true);
            using var destination = new MemoryStream();
            var buffer = new byte[81_920];
            while (true)
            {
                int read = gzip.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                if (destination.Length + read > maximumUncompressedBytes)
                {
                    throw new InvalidDataException("Telemetry chunk exceeds the decompression limit.");
                }
                destination.Write(buffer, 0, read);
            }
            return destination.ToArray();
        }

        public static string Sha256(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }

        public static string StableId(params string[] parts)
        {
            string joined = string.Join("\u001f", parts ?? throw new ArgumentNullException(nameof(parts)));
            return Sha256(System.Text.Encoding.UTF8.GetBytes(joined));
        }

        private static JsonSerializerOptions CreateOptions(bool indented)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
