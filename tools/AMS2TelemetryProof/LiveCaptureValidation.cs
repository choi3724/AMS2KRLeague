using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMS2LeagueClient.Core.CompactTelemetry;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2TelemetryProof
{
    internal static class LiveCaptureValidation
    {
        private static readonly Dictionary<string, double> Thresholds = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["worldX"] = 0.01,
            ["worldY"] = 0.01,
            ["worldZ"] = 0.01,
            ["lapDistanceMeters"] = 0.01,
            ["speedMetersPerSecond"] = 0.01,
            ["throttle"] = 0.01,
            ["brake"] = 0.01,
            ["steering"] = 0.01,
            ["unfilteredSteering"] = 0.01,
            ["rpm"] = 1.0,
            ["gearRaw"] = 0.0,
            ["longitudinalAccelerationMetersPerSecondSquared"] = 0.01,
            ["lateralAccelerationMetersPerSecondSquared"] = 0.01,
            ["verticalAccelerationMetersPerSecondSquared"] = 0.01
        };

        public static bool Run(string archiveRoot, string outputPath)
        {
            if (!Directory.Exists(archiveRoot)) throw new DirectoryNotFoundException(archiveRoot);
            string[] chunkPaths = Directory.EnumerateFiles(archiveRoot, "*.gz", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".a2ct.gz", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (chunkPaths.Length == 0) throw new InvalidDataException("No persisted telemetry chunks were found.");

            var report = new ValidationReport
            {
                ArchiveRoot = archiveRoot,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ChunkCount = chunkPaths.Length
            };
            foreach (string name in Thresholds.Keys)
            {
                report.Fields[name] = new FieldRange { Field = name, Threshold = Thresholds[name] };
            }

            foreach (string chunkPath in chunkPaths)
            {
                byte[] compressed = File.ReadAllBytes(chunkPath);
                byte[] payload;
                using (var stream = new MemoryStream(compressed, false))
                {
                    payload = TelemetryChunkSerializer.Gunzip(stream);
                }
                report.TotalCompressedBytes += compressed.LongLength;
                report.TotalUncompressedBytes += payload.LongLength;
                if (chunkPath.EndsWith(".a2ct.gz", StringComparison.OrdinalIgnoreCase))
                {
                    ReadCompactChunk(report, chunkPath, compressed, payload);
                }
                else
                {
                    ReadLegacyChunk(report, chunkPath, compressed, payload);
                }
            }

            foreach (FieldRange field in report.Fields.Values) field.FinalizeRange();
            report.Checks["persistedChunkIntegrity"] = report.IntegrityFailures.Count == 0
                && report.MetadataFilesVerified == report.ChunkCount;
            report.Checks["worldPositionChanged"] = Changed(report, "worldX")
                || Changed(report, "worldY")
                || Changed(report, "worldZ");
            report.Checks["lapDistanceChanged"] = Changed(report, "lapDistanceMeters");
            report.Checks["speedChanged"] = Changed(report, "speedMetersPerSecond");
            report.Checks["throttleChanged"] = Changed(report, "throttle");
            report.Checks["brakeChanged"] = Changed(report, "brake");
            report.Checks["steeringChanged"] = Changed(report, "steering") || Changed(report, "unfilteredSteering");
            report.Checks["rpmChanged"] = Changed(report, "rpm");
            report.Checks["gearChanged"] = Changed(report, "gearRaw");
            report.Checks["accelerationChanged"] = Changed(report, "longitudinalAccelerationMetersPerSecondSquared")
                || Changed(report, "lateralAccelerationMetersPerSecondSquared")
                || Changed(report, "verticalAccelerationMetersPerSecondSquared");
            report.Checks["noDroppedInputMessages"] = report.DroppedInputMessages == 0;
            report.AllRequiredChecksPass = report.Checks.Values.All(value => value);

            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var json = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            File.WriteAllBytes(outputPath, JsonSerializer.SerializeToUtf8Bytes(report, json));
            Console.WriteLine("REAL_VALIDATION=" + outputPath);
            Console.WriteLine("CHUNKS=" + report.ChunkCount);
            Console.WriteLine("DRIVER_SAMPLES=" + report.DriverSampleCount);
            foreach (KeyValuePair<string, bool> check in report.Checks)
            {
                Console.WriteLine("CHECK " + check.Key + "=" + (check.Value ? "PASS" : "FAIL"));
            }
            Console.WriteLine("FINAL=" + (report.AllRequiredChecksPass ? "PASS" : "FAIL"));
            return report.AllRequiredChecksPass;
        }

        private static void ReadLegacyChunk(
            ValidationReport report,
            string chunkPath,
            byte[] compressed,
            byte[] payload)
        {
            TelemetryChunkEnvelope chunk = TelemetryChunkSerializer.Deserialize(payload);
            report.LegacyChunkCount++;
            CountStream(report, chunk.StreamType.ToString());
            AddQuality(report, chunk.Quality);
            TelemetryPendingUploadMetadata? metadata = ReadAndVerifyMetadata(
                report, chunkPath, ".json.gz", compressed, payload);
            if (metadata != null && !string.Equals(metadata.ChunkId, chunk.ChunkId, StringComparison.Ordinal))
            {
                report.IntegrityFailures.Add(Path.GetFileName(chunkPath) + ":CHUNK_ID_MISMATCH");
            }
            if (chunk.StreamType != TelemetryStreamType.DRIVER_TELEMETRY) return;

            report.DriverSampleCount += chunk.Data.Rows.Count;
            Dictionary<string, int> fields = chunk.Data.Fields
                .Select((name, index) => new { name, index })
                .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
            foreach (double?[] row in chunk.Data.Rows)
            {
                ObserveCatalogRow(report, fields, row);
            }
        }

        private static void ReadCompactChunk(
            ValidationReport report,
            string chunkPath,
            byte[] compressed,
            byte[] payload)
        {
            CompactTelemetryEnvelope envelope = CompactTelemetryCodec.Decode(payload);
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(envelope.Block.SchemaId);
            report.CompactChunkCount++;
            CountStream(report, schema.Name);
            TelemetryPendingUploadMetadata? metadata = ReadAndVerifyMetadata(
                report, chunkPath, ".a2ct.gz", compressed, payload);
            if (metadata != null)
            {
                AddQuality(report, metadata.Quality);
                if (metadata.CompactSchemaId != (ushort)envelope.Block.SchemaId)
                    report.IntegrityFailures.Add(Path.GetFileName(chunkPath) + ":SCHEMA_ID_MISMATCH");
                if (metadata.SessionLocalId != envelope.SessionLocalId)
                    report.IntegrityFailures.Add(Path.GetFileName(chunkPath) + ":SESSION_LOCAL_ID_MISMATCH");
                if (metadata.AttemptLocalId != envelope.AttemptLocalId)
                    report.IntegrityFailures.Add(Path.GetFileName(chunkPath) + ":ATTEMPT_LOCAL_ID_MISMATCH");
                if (metadata.ChunkIndex != checked((int)envelope.ChunkSequence))
                    report.IntegrityFailures.Add(Path.GetFileName(chunkPath) + ":CHUNK_SEQUENCE_MISMATCH");
            }

            if (envelope.Block.SchemaId == CompactTelemetrySchemaId.DriverFastV1)
            {
                report.DriverSampleCount += envelope.Block.Samples.Count;
                ObserveSchemaSamples(report, schema, envelope.Block.Samples);
            }
            else if (envelope.Block.SchemaId == CompactTelemetrySchemaId.DriverMotionV1)
            {
                ObserveSchemaSamples(report, schema, envelope.Block.Samples);
            }
            else if (envelope.Block.SchemaId == CompactTelemetrySchemaId.DriverChangeV1)
            {
                ObserveDriverChanges(report, envelope.Block.Samples);
            }
        }

        private static void ObserveSchemaSamples(
            ValidationReport report,
            CompactTelemetrySchema schema,
            IReadOnlyList<CompactTelemetrySample> samples)
        {
            foreach (CompactTelemetrySample sample in samples)
            {
                for (int index = 0; index < schema.Fields.Count && index < sample.Values.Count; index++)
                {
                    double? value = sample.Values[index];
                    if (value.HasValue && report.Fields.TryGetValue(schema.Fields[index].Name, out FieldRange? range))
                    {
                        range.Observe(value.Value);
                    }
                }
            }
        }

        private static void ObserveDriverChanges(
            ValidationReport report,
            IReadOnlyList<CompactTelemetrySample> samples)
        {
            IReadOnlyList<string> fields = TelemetryFieldCatalog.DriverTelemetryFields;
            foreach (CompactTelemetrySample sample in samples)
            {
                if (sample.Values.Count < 2 || !sample.Values[0].HasValue || !sample.Values[1].HasValue) continue;
                int ordinal = checked((int)sample.Values[0]!.Value);
                if (ordinal < 0 || ordinal >= fields.Count) continue;
                if (report.Fields.TryGetValue(fields[ordinal], out FieldRange? range))
                {
                    range.Observe(sample.Values[1]!.Value);
                }
            }
        }

        private static void ObserveCatalogRow(
            ValidationReport report,
            IReadOnlyDictionary<string, int> fields,
            IReadOnlyList<double?> row)
        {
            foreach (KeyValuePair<string, FieldRange> pair in report.Fields)
            {
                if (!fields.TryGetValue(pair.Key, out int index) || index >= row.Count || !row[index].HasValue) continue;
                pair.Value.Observe(row[index]!.Value);
            }
        }

        private static void CountStream(ValidationReport report, string streamName)
        {
            report.StreamChunkCounts[streamName] = report.StreamChunkCounts.TryGetValue(streamName, out int count)
                ? count + 1
                : 1;
        }

        private static void AddQuality(ValidationReport report, TelemetryChunkQuality quality)
        {
            report.DroppedSamples += quality.DroppedSamples;
            report.DroppedInputMessages += quality.DroppedInputMessages;
            report.MissingSamples += quality.MissingSamples;
        }

        private static bool Changed(ValidationReport report, string name)
        {
            return report.Fields.TryGetValue(name, out FieldRange? field) && field.Changed;
        }

        private static TelemetryPendingUploadMetadata? ReadAndVerifyMetadata(
            ValidationReport report,
            string chunkPath,
            string suffix,
            byte[] compressed,
            byte[] payload)
        {
            string metadataPath = chunkPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? chunkPath.Substring(0, chunkPath.Length - suffix.Length) + ".upload.json"
                : chunkPath + ".upload.json";
            if (!File.Exists(metadataPath))
            {
                report.IntegrityFailures.Add(Path.GetFileName(chunkPath) + ":MISSING_UPLOAD_METADATA");
                return null;
            }
            TelemetryPendingUploadMetadata metadata = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(metadataPath));
            report.MetadataFilesVerified++;
            if (!string.Equals(metadata.CompressedSha256, TelemetryChunkSerializer.Sha256(compressed), StringComparison.OrdinalIgnoreCase))
                report.IntegrityFailures.Add(Path.GetFileName(chunkPath) + ":COMPRESSED_SHA256_MISMATCH");
            if (!string.Equals(metadata.PayloadSha256, TelemetryChunkSerializer.Sha256(payload), StringComparison.OrdinalIgnoreCase))
                report.IntegrityFailures.Add(Path.GetFileName(chunkPath) + ":PAYLOAD_SHA256_MISMATCH");
            return metadata;
        }

        private sealed class ValidationReport
        {
            public string Schema { get; set; } = "ams2-real-telemetry-validation-v2";
            public string InputSource { get; set; } = "PERSISTED_JSON_OR_A2CT_GZIP_CHUNKS_ONLY";
            public bool SharedMemoryReadDuringAnalysis { get; set; }
            public string ArchiveRoot { get; set; } = string.Empty;
            public DateTimeOffset CreatedAtUtc { get; set; }
            public int ChunkCount { get; set; }
            public int CompactChunkCount { get; set; }
            public int LegacyChunkCount { get; set; }
            public int MetadataFilesVerified { get; set; }
            public long DriverSampleCount { get; set; }
            public long TotalCompressedBytes { get; set; }
            public long TotalUncompressedBytes { get; set; }
            public long MissingSamples { get; set; }
            public long DroppedSamples { get; set; }
            public long DroppedInputMessages { get; set; }
            public Dictionary<string, int> StreamChunkCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
            public Dictionary<string, FieldRange> Fields { get; set; } = new Dictionary<string, FieldRange>(StringComparer.Ordinal);
            public Dictionary<string, bool> Checks { get; set; } = new Dictionary<string, bool>(StringComparer.Ordinal);
            public List<string> IntegrityFailures { get; set; } = new List<string>();
            public bool AllRequiredChecksPass { get; set; }
        }

        private sealed class FieldRange
        {
            private readonly HashSet<double> _distinct = new HashSet<double>();
            public string Field { get; set; } = string.Empty;
            public double Threshold { get; set; }
            public long Samples { get; set; }
            public double? Min { get; set; }
            public double? Max { get; set; }
            public double? Range { get; set; }
            public int DistinctValues { get; set; }
            public bool Changed { get; set; }

            public void Observe(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                Samples++;
                Min = !Min.HasValue || value < Min.Value ? value : Min;
                Max = !Max.HasValue || value > Max.Value ? value : Max;
                if (_distinct.Count < 10_000) _distinct.Add(value);
            }

            public void FinalizeRange()
            {
                DistinctValues = _distinct.Count;
                if (!Min.HasValue || !Max.HasValue) return;
                Range = Max.Value - Min.Value;
                Changed = Field == "gearRaw"
                    ? DistinctValues > 1
                    : Range.Value > Threshold;
            }
        }
    }
}
