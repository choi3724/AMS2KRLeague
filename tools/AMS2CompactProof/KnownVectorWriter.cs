using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AMS2LeagueClient.Core.CompactTelemetry;

namespace AMS2CompactProof
{
    internal static class KnownVectorWriter
    {
        public static void Write(string outputRoot)
        {
            string root = Path.GetFullPath(outputRoot);
            Directory.CreateDirectory(root);
            WriteOne(root, "driver-fast-fixed-v1.bin", DriverFast());
            WriteOne(root, "driver-change-irregular-v1.bin", DriverChange());
            WriteOne(root, "participant-replay-public-v1.bin", Replay());
            WriteOne(root, "race-event-strings-v1.bin", RaceEventStrings());
        }

        private static byte[] DriverFast()
        {
            var samples = new[]
            {
                new CompactTelemetrySample(1_000, new double?[] { 0, 0.5, -0.25, 50.12, 100.25, -1.2, 2.4 }),
                new CompactTelemetrySample(1_050, new double?[] { 1, 0, 0.25, 50.34, 102.75, -0.8, 2.0 })
            };
            return CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(
                0x01020304, 0x05060708, 1,
                new CompactTelemetryBlock(CompactTelemetrySchemaId.DriverFastV1, 1_000, 50, samples)));
        }

        private static byte[] DriverChange()
        {
            long[] elapsed = { 10_000, 10_152, 13_653, 13_697 };
            var samples = elapsed.Select((value, index) => new CompactTelemetrySample(
                value,
                new double?[] { 12, index * 0.125 })).ToArray();
            return CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(
                4, 5, 6,
                new CompactTelemetryBlock(CompactTelemetrySchemaId.DriverChangeV1, 10_000, 0, samples)));
        }

        private static byte[] Replay()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.ParticipantReplayV1);
            var row = new double?[schema.Fields.Count];
            Set(row, schema, "participantRef", 0);
            Set(row, schema, "slot", 0);
            Set(row, schema, "generation", 1);
            Set(row, schema, "lap", 2);
            Set(row, schema, "lapDistanceMeters", 1234.56);
            Set(row, schema, "racePosition", 5);
            Set(row, schema, "worldX", 100.25);
            Set(row, schema, "worldY", 2.5);
            Set(row, schema, "worldZ", -44.75);
            Set(row, schema, "pitStateRaw", 0);
            Set(row, schema, "isActive", 1);
            var participant = new[] { new CompactParticipantDictionaryEntry(0, "ENG-IceBlasT", "Lancer", "GT3") };
            return CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(
                7, 8, 9,
                new CompactTelemetryBlock(
                    CompactTelemetrySchemaId.ParticipantReplayV1,
                    20_000,
                    0,
                    new[] { new CompactTelemetrySample(20_000, row) }),
                participant));
        }

        private static byte[] RaceEventStrings()
        {
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.RaceEventV1);
            var row = new double?[schema.Fields.Count];
            Set(row, schema, "eventTypeRef", 0);
            Set(row, schema, "eventIdRef", 0);
            Set(row, schema, "factCodeRef", 0);
            Set(row, schema, "participantRef", 0);
            Set(row, schema, "lap", 3);
            Set(row, schema, "lapTimeMs", 98_765);
            var strings = new[]
            {
                new CompactStringDictionaryEntry(CompactStringDictionaryId.EventType, 0, "LAP_COMPLETED"),
                new CompactStringDictionaryEntry(CompactStringDictionaryId.EventId, 0, "event-0001"),
                new CompactStringDictionaryEntry(CompactStringDictionaryId.FactCode, 0, "LAP_TIME")
            };
            return CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(
                7, 8, 10,
                new CompactTelemetryBlock(
                    CompactTelemetrySchemaId.RaceEventV1,
                    30_000,
                    0,
                    new[] { new CompactTelemetrySample(30_000, row) }),
                null,
                strings));
        }

        private static void Set(double?[] row, CompactTelemetrySchema schema, string field, double value)
            => row[schema.Fields.First(value => value.Name == field).Ordinal] = value;

        private static void WriteOne(string root, string name, byte[] payload)
        {
            string path = Path.Combine(root, name);
            File.WriteAllBytes(path, payload);
            using SHA256 hash = SHA256.Create();
            string sha = Convert.ToHexString(hash.ComputeHash(payload));
            Console.WriteLine(name + " bytes=" + payload.Length.ToString(CultureInfo.InvariantCulture) + " sha256=" + sha);
        }
    }
}
