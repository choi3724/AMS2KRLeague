using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AMS2LeagueClient.Core.CompactTelemetry;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2LeagueActivity.Tests
{
    internal static class LongTrackTests
    {
        private static readonly CompactTelemetrySchemaId[] Bases = {
            CompactTelemetrySchemaId.SessionStaticV1, CompactTelemetrySchemaId.RaceEventV1,
            CompactTelemetrySchemaId.ParticipantReplayV1, CompactTelemetrySchemaId.TrackGeometryV1,
            CompactTelemetrySchemaId.DriverFastV1, CompactTelemetrySchemaId.IncidentV1 };

        public static IEnumerable<TestCase> Cases()
        {
            yield return new TestCase("Long-track schemas preserve V1 bytes, precision and non-distance limits", Boundaries);
        }

        private static CompactTelemetryBlock Block(CompactTelemetrySchemaId id, params double[] distances)
        {
            var schema = CompactTelemetrySchemaRegistry.Get(id);
            return CompactTelemetryBlock.FromRows(id, 0, 200, distances.Select(distance => {
                var row = new double?[schema.Fields.Count];
                foreach (var field in schema.Fields)
                {
                    if (field.Name == "trackLengthMeters" || field.Name == "lapDistanceMeters") row[field.Ordinal] = distance;
                    if (field.Name == "participantRef") row[field.Ordinal] = 0;
                    if (field.Name == "worldX") row[field.Ordinal] = distance;
                    if (field.Name == "worldZ") row[field.Ordinal] = distance / 2;
                    if (field.Name == "lap" || field.Name == "racePosition") row[field.Ordinal] = 1;
                }
                return (IReadOnlyList<double?>)row;
            }));
        }

        private static byte[] Encode(CompactTelemetryBlock block)
            => CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(101, 201, 1, block,
                new[] { new CompactParticipantDictionaryEntry(0, "Long Track Test", "Test Car", "Test Class") }));

        private static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (CompactTelemetryFormatException) { rejected = true; }
            AssertEx.True(rejected, "Invalid source must still fail closed.");
        }

        private static void Boundaries()
        {
            foreach (var id in Bases)
            {
                var extended = (CompactTelemetrySchemaId)((ushort)id + 0x100);
                var old = CompactTelemetrySchemaRegistry.Get(id);
                var next = CompactTelemetrySchemaRegistry.Get(extended);
                for (int i = 0; i < old.Fields.Count; i++)
                {
                    var a = old.Fields[i]; var b = next.Fields[i];
                    AssertEx.Equal(a.Name, b.Name); AssertEx.Equal(a.Ordinal, b.Ordinal);
                    AssertEx.Equal(a.Scale, b.Scale); AssertEx.Equal(a.Offset, b.Offset);
                    AssertEx.Equal(a.Encoding, b.Encoding); AssertEx.Equal(a.FixedWidth, b.FixedWidth);
                    AssertEx.Equal(a.QuantizedMinimum, b.QuantizedMinimum);
                    bool distance = a.Name == "trackLengthMeters" || a.Name == "lapDistanceMeters";
                    AssertEx.Equal(distance ? (long)(100_000 / a.Scale) : a.QuantizedMaximum, b.QuantizedMaximum);
                }
                var shortBlock = Block(id, 0, 19_999.9, 20_000);
                AssertEx.Equal(id, CompactTelemetrySchemaRegistry.SelectForWrite(id, shortBlock.Samples));
                byte[] v1 = Encode(shortBlock), v2 = Encode(Block(extended, 0, 19_999.9, 20_000));
                AssertEx.Equal(v1.Length, v2.Length);
                v2[6] = v1[6]; v2[7] = v1[7];
                AssertEx.True(v1.SequenceEqual(v2), "Only the schema ID may differ for identical values.");
                var longBlock = Block(id, 19_999.9, 20_815.41, 100_000, 0);
                AssertEx.Equal(extended, CompactTelemetrySchemaRegistry.SelectForWrite(id, longBlock.Samples));
                Reject(() => Encode(longBlock));
                var decoded = CompactTelemetryCodec.Decode(Encode(Block(extended, 19_999.9, 20_815.41, 100_000, 0)));
                int ordinal = next.Fields.ToList().FindIndex(f => f.Name == "trackLengthMeters" || f.Name == "lapDistanceMeters");
                AssertEx.True(Math.Abs(decoded.Block.Samples[1].Values[ordinal]!.Value - 20_815.41) <= next.Fields[ordinal].MaximumQuantizationError);
                AssertEx.Equal(100_000.0, decoded.Block.Samples[2].Values[ordinal]!.Value);
                AssertEx.Equal(0.0, decoded.Block.Samples[3].Values[ordinal]!.Value);
                Reject(() => Encode(Block(extended, 100_001)));
                Reject(() => Encode(Block(extended, -1)));
                Reject(() => Encode(Block(extended, double.NaN)));
                Reject(() => Encode(Block(extended, double.PositiveInfinity)));
            }
            var speed = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.DriverFastV2).Fields.Single(f => f.Name == "speedMetersPerSecond");
            Reject(() => speed.Quantize(704.58));
        }

        // Offline diagnostics only. Never edits the live queue or asserts race completeness.
        public static int Recheck(string sourceDirectory, string outputDirectory)
        {
            string source = Path.GetFullPath(sourceDirectory), output = Path.GetFullPath(outputDirectory);
            if (Directory.Exists(output) || File.Exists(output) || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use a new output directory outside the source archive.");
            Directory.CreateDirectory(output);
            int count = 0;
            var stores = new Dictionary<string, CompactTelemetryChunkStore>();
            foreach (string path in Directory.GetFiles(source, "*.source.json.gz", SearchOption.AllDirectories).OrderBy(p => p))
            {
                using var stream = File.OpenRead(path);
                byte[] bytes = TelemetryChunkSerializer.Gunzip(stream);
                string hash = TelemetryChunkSerializer.Sha256(bytes);
                AssertEx.True(Path.GetFileName(path).EndsWith("-" + hash + ".source.json.gz", StringComparison.Ordinal));
                var chunk = TelemetryChunkSerializer.Deserialize(bytes);
                string key = TelemetryChunkSerializer.StableId(chunk.SessionFingerprint, chunk.WitnessId, chunk.AttemptId);
                if (!stores.TryGetValue(key, out var store))
                {
                    store = new CompactTelemetryChunkStore(output, new TelemetryArchiveIdentity {
                        SessionId = chunk.SessionId, SessionFingerprint = chunk.SessionFingerprint,
                        WitnessId = chunk.WitnessId, AttemptId = chunk.AttemptId, AttemptNumber = chunk.AttemptNumber });
                    stores.Add(key, store);
                }
                store.Commit(chunk);
                Console.WriteLine("RECOVERABLE " + chunk.StreamType + " chunk=" + chunk.ChunkIndex + " rawBytes=" + bytes.Length + " sha256=" + hash);
                count++;
            }
            int compact = 0;
            foreach (string path in Directory.GetFiles(output, "*.a2ct.gz", SearchOption.AllDirectories))
            {
                using var stream = File.OpenRead(path);
                CompactTelemetryCodec.Decode(TelemetryChunkSerializer.Gunzip(stream)); compact++;
            }
            Console.WriteLine("RECHECK sources=" + count + " compactBlocks=" + compact + " liveQueueChanged=false finalizeCreated=false");
            return count > 0 ? 0 : 1;
        }

        public static int Export(string outputDirectory)
        {
            if (Directory.Exists(outputDirectory)) throw new ArgumentException("Use a new vector directory.");
            Directory.CreateDirectory(outputDirectory);
            foreach (var id in Bases)
            {
                var extended = (CompactTelemetrySchemaId)((ushort)id + 0x100);
                File.WriteAllText(Path.Combine(outputDirectory, ((ushort)extended).ToString("X4") + ".bin.base64"),
                    Convert.ToBase64String(Encode(Block(extended, 20_815.41, 100_000, 0))));
            }
            return 0;
        }
    }
}
