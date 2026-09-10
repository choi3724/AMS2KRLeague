using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.SessionWitness;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static class AutomaticModeTests
    {
        private static readonly DateTimeOffset Boot = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        private const string Join = "[SessionMessageManager::MemberConnected] [SessionNetworking] Session networking joined session at slot index 0";
        private const string Leave = "[SessionMessageManager::MemberDisconnected] [SessionNetworking] Session networking leaving session";
        private static string Header(DateTimeOffset boot) => "[Session][Platform] PC\n[Session][Exe] .\\AMS2AVX.exe\n[Session][Date] "
            + boot.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + "\n[Session][Time] "
            + boot.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "\n[Session][UTC] "
            + boot.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "\n";
        private static string Line(DateTimeOffset at, string message) => "[" + at.UtcDateTime.ToString("HH:mm:ss:fff", CultureInfo.InvariantCulture)
            + "][0x00000001] [Info ] " + message + "\n";
        private static void Equal<T>(T expected, T actual)
        { if (!Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", received " + actual); }
        private static void Check(bool valid) { if (!valid) throw new InvalidOperationException("Automatic session assertion failed"); }
        private static void InDirectory(Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ams2-automatic-mode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { action(directory); }
            finally { Directory.Delete(directory, true); }
        }

        public static SessionPlayModeDetector CreateMultiplayerDetector(string directory)
        {
            var now = DateTimeOffset.UtcNow;
            string logs = Path.Combine(directory, "game-log");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "online.log"), Header(now.AddMinutes(-5))
                + Line(now.AddMinutes(-4), Join) + Line(now, "[GameManager] ping"));
            var detector = new SessionPlayModeDetector(logs, Path.Combine(directory, "mode-history.json"));
            detector.Refresh(now, null, TimeZoneInfo.Local);
            return detector;
        }

        public static void LogBoundaries()
        {
            InDirectory(directory =>
            {
                string log = Header(Boot)
                    + Line(Boot.AddSeconds(1), "[ServerBrowser] active multiplayer lobby 20 drivers")
                    + Line(Boot.AddSeconds(2), "[Chat] user says " + Join)
                    + Line(Boot.AddSeconds(10), "[GameManager::JoinGame_Steam] [GameManager] Joining Steam lobby 100")
                    + Line(Boot.AddSeconds(11), "[GameManager::LobbyEnterCallback] [GameManager] Joined p2p game lobby 100")
                    + Line(Boot.AddSeconds(12), Join)
                    + Line(Boot.AddSeconds(20), "[SessionMessageManager::MemberDisconnected] [SessionNetworking] Session networking removing user from slot 1")
                    + Line(Boot.AddSeconds(30), Leave)
                    + Line(Boot.AddSeconds(40), "[GameManager::LobbyCreatedCallback] [GameManager] Created p2p game lobby 200")
                    + Line(Boot.AddSeconds(50), "[GameManager::LeaveGame_Steam] [GameManager] Leaving session with reason 1")
                    + Line(Boot.AddSeconds(60), "[GameManager] ping");
                File.WriteAllText(Path.Combine(directory, "online.log"), log);
                var detector = new SessionPlayModeDetector(directory, Path.Combine(directory, "history.json"));
                detector.Refresh(Boot.AddMinutes(1), null, TimeZoneInfo.Local);
                Equal(SessionPlayMode.SinglePlayer, detector.Classify(Boot.AddSeconds(2), Boot.AddSeconds(9)));
                Equal(SessionPlayMode.Unknown, detector.Classify(Boot.AddSeconds(10), Boot.AddSeconds(11)));
                Equal(SessionPlayMode.Multiplayer, detector.Classify(Boot.AddSeconds(13), Boot.AddSeconds(29)));
                Equal(SessionPlayMode.SinglePlayer, detector.Classify(Boot.AddSeconds(31), Boot.AddSeconds(39)));
                Equal(SessionPlayMode.Multiplayer, detector.Classify(Boot.AddSeconds(41), Boot.AddSeconds(49)));
                Equal(SessionPlayMode.Unknown, detector.Classify(Boot.AddSeconds(29), Boot.AddSeconds(31)));
                Check(!detector.CanUpload(Boot.AddSeconds(31), Boot.AddSeconds(39)));
                Check(!detector.CanUpload(null, null));
                var restarted = new SessionPlayModeDetector(Path.Combine(directory, "missing"), Path.Combine(directory, "history.json"));
                Check(restarted.CanUpload(Boot.AddSeconds(13), Boot.AddSeconds(29)));
                Check(!restarted.CanUpload(Boot.AddSeconds(29), Boot.AddSeconds(41)));
                var midnight = Boot.AddHours(11).AddMinutes(59).AddSeconds(55);
                Check(SessionPlayModeDetector.Parse(Header(midnight) + Line(midnight.AddSeconds(1), Join)
                    + Line(midnight.AddSeconds(10), Leave), TimeZoneInfo.Local) != null);
                Check(SessionPlayModeDetector.Parse("participants=20\n", TimeZoneInfo.Local) == null);
            });
        }

        public static void LogFilesAndHistory()
        {
            InDirectory(directory =>
            {
                var now = DateTimeOffset.UtcNow;
                var boot = new DateTimeOffset(now.UtcDateTime.Date.AddHours(now.UtcDateTime.Hour), TimeSpan.Zero).AddHours(-1);
                var detector = new SessionPlayModeDetector(directory, Path.Combine(directory, "history.json"));
                detector.Refresh(now, boot, TimeZoneInfo.Local);
                Equal(SessionPlayMode.Unknown, detector.CurrentMode);
                string path = Path.Combine(directory, "online.log");
                File.WriteAllText(path, Header(boot) + Line(boot.AddSeconds(1), "[GameManager] ready"));
                detector.Refresh(now, boot, TimeZoneInfo.Local);
                Equal(SessionPlayMode.SinglePlayer, detector.CurrentMode);
                File.AppendAllText(path, Line(now.AddMinutes(-2), Join));
                detector.Refresh(now, boot, TimeZoneInfo.Local);
                Equal(SessionPlayMode.Unknown, detector.CurrentMode);
                Check(!detector.CanUpload(now.AddSeconds(-90), now.AddSeconds(-60)));
                File.AppendAllText(path, Line(now, "[GameManager] ping"));
                detector.Refresh(now, boot, TimeZoneInfo.Local);
                Equal(SessionPlayMode.Multiplayer, detector.CurrentMode);
                Check(detector.CanUpload(now.AddSeconds(-90), now.AddSeconds(-60)));
                detector.Refresh(now, now.AddMinutes(-1), TimeZoneInfo.Local);
                Equal(SessionPlayMode.Unknown, detector.CurrentMode);
                File.AppendAllText(path, "[incomplete");
                detector.Refresh(now, boot, TimeZoneInfo.Local);
                Equal(SessionPlayMode.Unknown, detector.CurrentMode);
                File.WriteAllText(path, Header(now.AddSeconds(-20)) + Line(now.AddSeconds(-19), "[GameManager] ready"));
                detector.Refresh(now, now.AddSeconds(-20), TimeZoneInfo.Local);
                Equal(SessionPlayMode.SinglePlayer, detector.CurrentMode);
                Check(!detector.CanUpload(now.AddSeconds(-19), now.AddSeconds(-15)));
                Check(!detector.CanUpload(now.AddMinutes(-3), now.AddSeconds(-15)));
            });
        }

        public static void UploadFiltering()
        {
            InDirectory(directory =>
            {
                var detector = CreateMultiplayerDetector(directory);
                using var logger = new FileLogger(Path.Combine(directory, "logs"));
                using var runtime = new ActivityCaptureRuntime(Path.Combine(directory, "runtime"), "automatic-mode-fixture", "0.4.1", logger, playModeDetector: detector);
                var start = DateTimeOffset.UtcNow.AddMinutes(-2);
                var end = start.AddSeconds(30);
                var queue = new ActivityUploadQueue(Path.Combine(directory, "queue"), uploadEligibility: runtime.IsActivityUploadAllowed);
                for (int index = 0; index < 20; index++)
                    queue.Enqueue("single-" + index, Cafe24Routes.PlayerActivities, "single-fixture-" + index,
                        JsonSerializer.Serialize(new { schema = "ams2-player-activity-v2", raceMode = "SINGLE_PLAYER", startedAtUtc = start, endedAtUtc = end }));
                queue.Enqueue("old-unknown", Cafe24Routes.PlayerActivities, "unknown-fixture",
                    JsonSerializer.Serialize(new { schema = "ams2-player-activity-v2", startedAtUtc = start, endedAtUtc = end }));
                queue.Enqueue("multi", Cafe24Routes.PlayerActivities, "multi-fixture",
                    JsonSerializer.Serialize(new { schema = "ams2-player-activity-v2", raceMode = "MULTIPLAYER", startedAtUtc = start, endedAtUtc = end }));
                Equal("multi", queue.GetDueBatch(1, DateTimeOffset.UtcNow).Single().Metadata.ActivityId);
                Equal(22, queue.Scan().Count);
                var metadata = new TelemetryPendingUploadMetadata { FirstCapturedAtUtc = start, LastCapturedAtUtc = end };
                Check(runtime.IsTelemetryUploadAllowed(metadata));
                Equal("MULTIPLAYER", metadata.RaceMode);
                metadata.FirstCapturedAtUtc = null;
                Check(!runtime.IsTelemetryUploadAllowed(metadata));
                Equal("UNKNOWN", metadata.RaceMode);
                metadata.FirstCapturedAtUtc = start.AddHours(-1);
                metadata.LastCapturedAtUtc = end.AddHours(-1);
                Check(!runtime.IsTelemetryUploadAllowed(metadata));
            });
        }

        public static void ModeFields()
        {
            InDirectory(directory =>
            {
                var identity = TelemetryArchiveIdentityFactory.StartSession("mode-http-session", "mode-http-witness");
                string archiveRoot = Path.Combine(directory, "archive");
                var archive = new LocalDurableTelemetryArchive(archiveRoot, identity);
                try
                {
                    Check(archive.TryCaptureSessionMetadata(new SessionMetadataSample { CapturedAtUtc = Boot, SessionElapsedMs = 0, SessionType = "RACE", CaptureStarted = true }));
                    archive.FlushAsync().GetAwaiter().GetResult();
                }
                finally { archive.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                var chunk = new TelemetryChunkUploadQueue(archiveRoot).GetDueBatch(1, DateTimeOffset.UtcNow).Single();
                var options = ActivityConnectionOptions.Load(Path.Combine(directory, "connection.json"));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                options.BearerToken = "test_automatic_mode_token_00000000000000000001";
                var handler = new HeaderHandler();
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, "mode-http-installation", "0.4.1", http);
                foreach (SessionPlayMode mode in new[] { SessionPlayMode.Multiplayer, SessionPlayMode.SinglePlayer, SessionPlayMode.Unknown })
                {
                    string wire = SessionPlayModeDetector.WireValue(mode);
                    var record = new ActivityRecord { ActivityId = "mode-result", ActivityType = ActivityType.Race, Authority = ActivityAuthority.PlayerPersonal,
                        StartedAtUtc = Boot, EndedAtUtc = Boot.AddSeconds(1), PersonalRaceSummary = new PersonalRaceSummary { FinishPosition = 1, FieldSize = 20 } };
                    record.ObservedConditions.RaceMode = wire;
                    Check(PlayerActivityUploadPayloadBuilder.TryBuild(record, out var payload, out _));
                    using var result = JsonDocument.Parse(payload);
                    Equal(wire, result.RootElement.GetProperty("raceMode").GetString());
                    var witness = new SessionWitnessRecord { WitnessId = "mode-witness", SessionFingerprint = "mode-fingerprint", RaceMode = wire };
                    using var witnessJson = JsonDocument.Parse(SessionWitnessUploadPayloadBuilder.Build(witness));
                    Equal(wire, witnessJson.RootElement.GetProperty("raceMode").GetString());
                    chunk.Metadata.RaceMode = wire;
                    var response = transport.SendTelemetryChunkAsync(chunk, CancellationToken.None).GetAwaiter().GetResult();
                    Equal(503, response.HttpStatus);
                    Equal(wire, handler.Mode);
                    Check(handler.Body.SequenceEqual(chunk.CompressedPayload.ToArray()));
                }
            });
        }

        public static void VerifyRealLogs(string directory)
        {
            int count = 0;
            foreach (string path in Directory.EnumerateFiles(directory, "online*.log").OrderBy(x => x))
            {
                var parsed = SessionPlayModeDetector.Parse(File.ReadAllText(path), TimeZoneInfo.Local);
                Check(parsed != null);
                Console.WriteLine("REAL_LOG_VERIFIED file=" + Path.GetFileName(path) + " transitions=" + parsed!.Transitions.Count
                    + " joins=" + parsed.Transitions.Count(x => x.Mode == SessionPlayMode.Multiplayer)
                    + " exits=" + parsed.Transitions.Count(x => x.Mode == SessionPlayMode.SinglePlayer));
                count++;
            }
            Check(count > 0);
            Console.WriteLine("REAL_LOG_RESULT passed=" + count);
        }

        private sealed class HeaderHandler : HttpMessageHandler
        {
            public string Mode = "";
            public byte[] Body = Array.Empty<byte>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Mode = request.Headers.GetValues("X-AMS2-Race-Mode").Single();
                Body = await request.Content!.ReadAsByteArrayAsync();
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
        }
    }
}