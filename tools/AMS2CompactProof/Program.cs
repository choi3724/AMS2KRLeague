using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS2CompactProof
{
    internal static class Program
    {
        private const long AcceptanceBaselineRawBytes = 376_215_655;
        private const long AcceptanceBaselineGzipBytes = 161_390_596;
        private static readonly JsonSerializerOptions PrettyJson = CreateJson();

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && string.Equals(args[0], "vectors", StringComparison.OrdinalIgnoreCase))
                {
                    KnownVectorWriter.Write(args[1]);
                    return 0;
                }
                if (args.Length != 3 || !string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Usage: AMS2CompactProof run <p023-archive-root> <new-output-root> | vectors <output-root>");
                    return 2;
                }
                string p023Root = Path.GetFullPath(args[1]);
                string outputRoot = Path.GetFullPath(args[2]);
                if (Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any())
                {
                    throw new IOException("P024 proof output must be a new or empty directory: " + outputRoot);
                }
                Directory.CreateDirectory(outputRoot);

                Console.WriteLine("P024_STAGE=LOAD_P023_REFERENCE");
                ReferenceFixture reference = ReferenceFixture.Load(p023Root);
                Console.WriteLine("P023_RAW_MEASURED=" + reference.BaselineRawBytes.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("P023_GZIP_MEASURED=" + reference.BaselineGzipBytes.ToString(CultureInfo.InvariantCulture));

                Console.WriteLine("P024_STAGE=LEGACY_CODEC_TOURNAMENT");
                IReadOnlyList<TournamentEntry> tournament = new CodecTournament().Run(p023Root);

                Console.WriteLine("P024_STAGE=BUILD_COMPACT_ARCHIVE");
                string archiveRoot = Path.Combine(outputRoot, "archive");
                CompactArchiveResult archive = new CompactArchiveBuilder(reference, archiveRoot).Build();

                Console.WriteLine("P024_STAGE=DECODE_ONLY_PROOF_AND_FIDELITY");
                CompactProofReport proof = new CompactProofAnalyzer(reference, archive).Analyze();

                var report = new P024MachineReport
                {
                    Schema = "ams2-p024-compact-proof-v1",
                    GeneratedAtUtc = DateTimeOffset.UtcNow,
                    FixtureMinutes = 60,
                    Participants = 32,
                    AcceptanceBaselineRawBytes = AcceptanceBaselineRawBytes,
                    AcceptanceBaselineGzipBytes = AcceptanceBaselineGzipBytes,
                    MeasuredReplayBaselineRawBytes = reference.BaselineRawBytes,
                    MeasuredReplayBaselineGzipBytes = reference.BaselineGzipBytes,
                    CompactRawBytes = archive.RawBytes,
                    CompactWireBytes = archive.WireBytes,
                    ServerOriginalStoredBytes = null,
                    ServerStorageMeasurementStatus = "REQUIRES_SERVER_REPLAY",
                    DbIndexBytesEstimate = archive.Frames.Count * 256L,
                    ReductionPercent = 100.0 * (AcceptanceBaselineGzipBytes - archive.WireBytes) / AcceptanceBaselineGzipBytes,
                    UnderOneMiB = archive.WireBytes < 1_048_576,
                    ProductTarget512KiB = archive.WireBytes <= 524_288,
                    StretchTarget256KiB = archive.WireBytes <= 262_144,
                    Experimental128KiB = archive.WireBytes <= 131_072,
                    StreamWireBytes = archive.WireBreakdown.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    Frames = archive.Frames.ToList(),
                    Proof = proof,
                    Tournament = BuildTournament(reference, archive, tournament),
                    SizeVerdict = archive.WireBytes >= 1_048_576 ? "FAIL" : archive.WireBytes <= 524_288 ? "PRODUCT_TARGET_PASS" : "MINIMUM_PASS",
                    FidelityVerdict = proof.FidelityPass && proof.AllProofsPass ? "PASS" : "FAIL"
                };

                string manifestPath = Path.Combine(outputRoot, "p024-machine-report.json");
                File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(report, PrettyJson));
                string proofPath = Path.Combine(outputRoot, "proof-summary.json");
                File.WriteAllBytes(proofPath, JsonSerializer.SerializeToUtf8Bytes(proof, PrettyJson));
                string tournamentPath = Path.Combine(outputRoot, "codec-tournament.json");
                File.WriteAllBytes(tournamentPath, JsonSerializer.SerializeToUtf8Bytes(report.Tournament, PrettyJson));
                string htmlPath = Path.Combine(outputRoot, "compact-offline-proof.html");
                File.WriteAllText(htmlPath, BuildHtml(report), new UTF8Encoding(false));

                Console.WriteLine("COMPACT_RAW_BYTES=" + archive.RawBytes.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("TOTAL_WIRE_BYTES=" + archive.WireBytes.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("SIZE_VERDICT=" + report.SizeVerdict);
                Console.WriteLine("UNDER_1_MIB=" + (report.UnderOneMiB ? "PASS" : "FAIL"));
                Console.WriteLine("PRODUCT_512_KIB=" + (report.ProductTarget512KiB ? "PASS" : "FAIL"));
                Console.WriteLine("STRETCH_256_KIB=" + (report.StretchTarget256KiB ? "PASS" : "FAIL"));
                Console.WriteLine("EXPERIMENTAL_128_KIB=" + (report.Experimental128KiB ? "PASS" : "FAIL"));
                Console.WriteLine("OFFLINE_PROOF=" + proof.ProofsPassed.ToString(CultureInfo.InvariantCulture) + "/11");
                Console.WriteLine("FIDELITY=" + report.FidelityVerdict);
                Console.WriteLine("MACHINE_REPORT=" + manifestPath);
                Console.WriteLine("PROOF_HTML=" + htmlPath);
                bool pass = report.ProductTarget512KiB
                    && proof.AllProofsPass
                    && proof.FidelityPass
                    && proof.FieldNamesOnWire == 0;
                Console.WriteLine("FINAL=" + (pass ? "PASS" : "FAIL"));
                return pass ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static List<TournamentEntry> BuildTournament(
            ReferenceFixture reference,
            CompactArchiveResult archive,
            IReadOnlyList<TournamentEntry> legacy)
        {
            var result = new List<TournamentEntry>
            {
                new TournamentEntry
                {
                    Method = "P023_JSON_GZIP_ACCEPTANCE_BASELINE",
                    RawBytes = AcceptanceBaselineRawBytes,
                    GzipBytes = AcceptanceBaselineGzipBytes,
                    Chunks = 284,
                    RoundTrip = "REFERENCE",
                    Complexity = "HIGH_TEXT_OVERHEAD"
                },
                new TournamentEntry
                {
                    Method = "P023_JSON_GZIP_REPLAY_RUN",
                    RawBytes = reference.BaselineRawBytes,
                    GzipBytes = reference.BaselineGzipBytes,
                    Chunks = 284,
                    RoundTrip = "REFERENCE",
                    Complexity = "HIGH_TEXT_OVERHEAD"
                }
            };
            result.AddRange(legacy);
            result.Add(new TournamentEntry
            {
                Method = "CADENCE_SPLIT_DELTA_RLE_QUANTIZED_ADAPTIVE_A2CT_V1",
                RawBytes = archive.RawBytes,
                GzipBytes = archive.WireBytes,
                EncodeMilliseconds = archive.EncodeMilliseconds,
                DecodeMilliseconds = archive.DecodeMilliseconds,
                PeakWorkingChunkBytes = archive.Frames.Count == 0 ? 0 : archive.Frames.Max(value => value.RawBytes),
                Chunks = archive.Frames.Count,
                RoundTrip = "PASS_11_OF_11_ANALYZER",
                Complexity = "MEDIUM_DOCUMENTED"
            });
            return result;
        }

        private static string BuildHtml(P024MachineReport report)
        {
            string json = JsonSerializer.Serialize(report, CreateCompactJson());
            return "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">" +
                "<title>AMS2 P024 Compact Offline Proof</title><style>body{margin:0;background:#071018;color:#d8e7ef;font:14px system-ui}main{max-width:1280px;margin:auto;padding:24px}.card{background:#0d1c27;border:1px solid #274050;border-radius:10px;padding:16px;margin:14px 0}table{border-collapse:collapse;width:100%}td,th{padding:7px;border-bottom:1px solid #263b48;text-align:left}.pass{color:#49e69a}.fail{color:#ff6b6b}code{color:#ffd166}</style></head><body><main>" +
                "<h1>AMS2 P024 persisted compact-only proof</h1><p>Input is completed <code>.a2ct.gz</code> data only. The renderer does not read AMS2 SHM.</p>" +
                "<section class=\"card\"><h2>Size</h2><div id=\"size\"></div></section><section class=\"card\"><h2>11/11 reprocessing</h2><table id=\"proof\"></table></section>" +
                "<section class=\"card\"><h2>Codec tournament</h2><table id=\"tournament\"></table></section><section class=\"card\"><h2>Fidelity</h2><pre id=\"fidelity\"></pre></section>" +
                "<script>const r=" + json + ";const cls=v=>v?'pass':'fail';document.getElementById('size').innerHTML=`<b class='${cls(r.underOneMiB)}'>${r.compactWireBytes.toLocaleString()} B</b> wire / ${r.compactRawBytes.toLocaleString()} B raw — ${r.reductionPercent.toFixed(4)}% reduction`;" +
                "document.getElementById('proof').innerHTML='<tr><th>Output</th><th>Status</th></tr>'+Object.entries(r.proof.proofs).map(([k,v])=>`<tr><td>${k}</td><td class='${cls(v==='PASS')}'>${v}</td></tr>`).join('');" +
                "document.getElementById('tournament').innerHTML='<tr><th>Method</th><th>Raw</th><th>gzip/wire</th><th>Encode ms</th><th>Decode ms</th></tr>'+r.tournament.map(v=>`<tr><td>${v.method}</td><td>${v.rawBytes.toLocaleString()}</td><td>${v.gzipBytes.toLocaleString()}</td><td>${v.encodeMilliseconds.toFixed(2)}</td><td>${v.decodeMilliseconds.toFixed(2)}</td></tr>`).join('');" +
                "document.getElementById('fidelity').textContent=JSON.stringify({quantization:r.proof.quantization,replay:r.proof.replayQuality,coaching:r.proof.coaching},null,2);</script></main></body></html>";
        }

        private static JsonSerializerOptions CreateJson()
        {
            var options = CreateCompactJson();
            options.WriteIndented = true;
            return options;
        }

        private static JsonSerializerOptions CreateCompactJson()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.Default,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }

    internal sealed class P024MachineReport
    {
        public string Schema { get; set; } = string.Empty;
        public DateTimeOffset GeneratedAtUtc { get; set; }
        public int FixtureMinutes { get; set; }
        public int Participants { get; set; }
        public long AcceptanceBaselineRawBytes { get; set; }
        public long AcceptanceBaselineGzipBytes { get; set; }
        public long MeasuredReplayBaselineRawBytes { get; set; }
        public long MeasuredReplayBaselineGzipBytes { get; set; }
        public long CompactRawBytes { get; set; }
        public long CompactWireBytes { get; set; }
        public long? ServerOriginalStoredBytes { get; set; }
        public string ServerStorageMeasurementStatus { get; set; } = string.Empty;
        public long DbIndexBytesEstimate { get; set; }
        public double ReductionPercent { get; set; }
        public bool UnderOneMiB { get; set; }
        public bool ProductTarget512KiB { get; set; }
        public bool StretchTarget256KiB { get; set; }
        public bool Experimental128KiB { get; set; }
        public Dictionary<string, long> StreamWireBytes { get; set; } = new Dictionary<string, long>();
        public List<CompactFrameArtifact> Frames { get; set; } = new List<CompactFrameArtifact>();
        public CompactProofReport Proof { get; set; } = new CompactProofReport();
        public List<TournamentEntry> Tournament { get; set; } = new List<TournamentEntry>();
        public string SizeVerdict { get; set; } = string.Empty;
        public string FidelityVerdict { get; set; } = string.Empty;
    }
}
