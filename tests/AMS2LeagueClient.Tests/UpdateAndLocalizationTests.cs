using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AMS2LeagueClient.Core.Localization;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void KoreanLabelsAndPenaltyColumn()
        {
            var fixture = new RawFixtureBuilder(6).SetSession(SessionState.Race)
                .SetParticipant(0, true, "드라이브스루", 1, 3, 4, RaceState.Racing, PitMode.None).SetParticipantControl(0, PitSchedule.DriveThrough)
                .SetParticipant(1, true, "피트와 페널티", 2, 3, 4, RaceState.Racing, PitMode.InPit).SetParticipantControl(1, PitSchedule.StopGo)
                .SetParticipant(2, true, "완주와 페널티", 3, 10, 10, RaceState.Finished, PitMode.None).SetParticipantControl(2, PitSchedule.StopGo)
                .SetParticipant(3, true, "의무 피트", 4, 3, 4, RaceState.Racing, PitMode.None).SetParticipantControl(3, PitSchedule.Mandatory)
                .SetParticipant(4, true, "실격", 5, 3, 4, RaceState.Disqualified, PitMode.None)
                .SetParticipant(5, true, "미확인", 6, 3, 4, RaceState.Racing, PitMode.None).SetParticipantControl(5, (PitSchedule)999);
            TelemetrySnapshot snapshot = Parse(fixture);
            var analyzer = new RaceControlAnalyzer(EvidenceKind.Fixture);
            var update = analyzer.Observe(snapshot, Classify(snapshot), 1, snapshot.CapturedAt);
            var timing = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "검증", raceControl: update);
            RankingRowViewModel RowAt(int index) => timing.AllRankingRows.Single(row => row.ParticipantIndex == index);
            AssertEqual("드라이브스루", RowAt(0).PenaltyText);
            AssertEqual("스톱 앤 고", RowAt(1).PenaltyText);
            AssertEqual("피트", RowAt(1).StatusLabel);
            AssertEqual("완주", RowAt(2).StatusLabel);
            AssertEqual("스톱 앤 고", RowAt(2).PenaltyText);
            AssertEqual("—", RowAt(3).PenaltyText);
            AssertEqual("실격", RowAt(4).PenaltyText);
            AssertEqual("미확인", RowAt(5).PenaltyText);
            AssertFalse(RowAt(0).IsDimmed);
            AssertEqual("완주", OverlayTextCatalog.Korean.RaceStateName(RaceState.Finished));
            AssertEqual("미완주", OverlayTextCatalog.Korean.RaceStateName(RaceState.Dnf));
            AssertEqual("미완주", StateText.TowerStatus("DNF"));
            var chequered = new RaceControlUpdate(Array.Empty<RaceControlEvent>(), null, Array.Empty<RaceControlEvent>(), update.ParticipantStates, BroadcastOverlayState.Chequered, 1, false);
            AssertEqual("완주", RaceControlViewModel.FromUpdate(chequered).StateLabel);
            var tower = new OverlayHudView(); tower.SetViewModel(timing); LayoutTower(tower);
            string[] labels = DescendantText(tower).ToArray();
            foreach (string label in new[] { "페널티", "드라이브스루", "스톱 앤 고", "완주", "피트" }) AssertTrue(labels.Contains(label));
            foreach (string label in new[] { "FINAL", "FIN", "PIT", "DT", "SG", "DSQ", "BEST" }) AssertFalse(labels.Contains(label));
            foreach (TextBlock cell in Descendants<TextBlock>(tower).Where(text => text.Name == "PenaltyText"))
            {
                AssertTrue(cell.ActualWidth >= 85);
                Rect bounds = cell.TransformToAncestor(tower).TransformBounds(new Rect(cell.RenderSize));
                AssertTrue(bounds.Right <= tower.ActualWidth);
                var measured = new TextBlock { Text = cell.Text, FontFamily = cell.FontFamily, FontSize = cell.FontSize, FontWeight = cell.FontWeight, TextWrapping = cell.TextWrapping };
                measured.Measure(new Size(cell.ActualWidth, double.PositiveInfinity));
                AssertTrue(measured.DesiredSize.Height <= cell.ActualHeight + 0.5);
            }
            CaptureLayout(tower, "korean-penalties");
            var existing = RowAt(0); int changed = 0;
            existing.PropertyChanged += (_, __) => changed++;
            var clearedSnapshot = Parse(fixture.SetParticipantControl(0, PitSchedule.None));
            var cleared = OverlayViewModel.Build(clearedSnapshot, ResolveLocal(clearedSnapshot), Classify(clearedSnapshot), 30, 20, false, "검증");
            existing.UpdateFrom(cleared.AllRankingRows.Single(row => row.ParticipantIndex == 0));
            AssertEqual("—", existing.PenaltyText); AssertTrue(changed > 0);
            tower.SetViewModel(cleared); LayoutTower(tower);
            AssertFalse(Descendants<TextBlock>(tower).Where(text => text.Name == "PenaltyText").Any(text => text.Text == "드라이브스루"));
            var status = new ClientStatusView { DataContext = new ClientStatusViewModel("0.4.1") { UpdateText = "업데이트: 0.4.2 다운로드 중 · 75%" } };
            status.Measure(new Size(780, 500)); status.Arrange(new Rect(0, 0, 780, 500)); status.UpdateLayout();
            CaptureLayout(status, "korean-update-status");
            ((ClientStatusViewModel)status.DataContext).UpdateText = "업데이트: 확인 또는 설치 준비 실패 · 6시간 후 재시도합니다. 오버레이는 계속 작동합니다.";
            status.UpdateLayout();
            CaptureLayout(status, "korean-update-failure");
            foreach (TextBlock text in Descendants<TextBlock>(status))
            {
                Rect bounds = text.TransformToAncestor(status).TransformBounds(new Rect(text.RenderSize));
                AssertTrue(bounds.Bottom <= status.ActualHeight && bounds.Right <= status.ActualWidth);
            }
        }

        private static void LegacyTowerWidthMigration()
        {
            var legacy = JsonSerializer.Deserialize<OverlayLayoutProfile>("{\"Schema\":1,\"Components\":{\"timingTower\":{\"X\":0.01,\"Y\":0.02,\"Width\":0.2708333333333333,\"Height\":0.5}}}")!;
            var fallback = new OverlayBounds(0, 0, 648, 608);
            OverlayBounds expanded = legacy.Resolve(OverlayComponentKeys.TimingTower, fallback, 1920, 1080);
            AssertEqual(648, expanded.Width); AssertEqual(562, expanded.Height); AssertEqual(19, expanded.X);
            legacy.Capture(OverlayComponentKeys.TimingTower, expanded, 1920, 1080);
            var saved = JsonSerializer.Deserialize<OverlayLayoutProfile>(JsonSerializer.Serialize(legacy))!;
            AssertEqual(648, saved.Resolve(OverlayComponentKeys.TimingTower, fallback, 1920, 1080).Width);
            AssertEqual(562, saved.Resolve(OverlayComponentKeys.TimingTower, fallback, 1920, 1080).Height);
            saved.Capture(OverlayComponentKeys.RelativeDrivers, new OverlayBounds(22, 33, 520, 104), 1920, 1080);
            AssertEqual(520, saved.Resolve(OverlayComponentKeys.RelativeDrivers, fallback, 1920, 1080).Width);
        }

        private static string ReleaseJson(string version, byte[] bytes, string? digest = null, string? url = null)
        {
            string name = "AMS2-League-Overlay-" + version + "-Setup.exe";
            return JsonSerializer.Serialize(new
            {
                draft = false, prerelease = false, tag_name = "v" + version,
                assets = new[] { new { name, size = bytes.Length, digest = digest ?? "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)), browser_download_url = url ?? "https://github.com/choi3724/AMS2KRLeague/releases/download/v" + version + "/" + name } }
            });
        }

        private static void ReleaseVersionAndMetadata()
        {
            AssertTrue(GitHubReleaseClient.IsNewer("0.4.10", "0.4.9"));
            AssertTrue(GitHubReleaseClient.IsNewer("0.4.1", "0.4.1-beta.10"));
            AssertTrue(GitHubReleaseClient.IsNewer("0.4.1-beta.10", "0.4.1-beta.2"));
            AssertFalse(GitHubReleaseClient.IsNewer("0.4.1-beta.10", "0.4.1"));
            AssertFalse(GitHubReleaseClient.IsNewer("0.4.1+new", "0.4.1+old"));
            AssertFalse(GitHubReleaseClient.IsNewer("0.3.9", "0.4.1"));
            foreach (string bad in new[] { "../0.4.2", "0.4", "01.4.2", "0.4.2-beta.01", "0.4.2\n" })
                ExpectUpdateFailure(() => GitHubReleaseClient.IsNewer(bad, "0.4.1"));
            byte[] data = Encoding.UTF8.GetBytes("installer fixture");
            AssertEqual("0.4.2", GitHubReleaseClient.ParseRelease(ReleaseJson("0.4.2", data), "0.4.1")?.Version);
            AssertTrue(GitHubReleaseClient.ParseRelease(ReleaseJson("0.4.0", data), "0.4.1") == null);
            AssertTrue(GitHubReleaseClient.ParseRelease(ReleaseJson("0.4.2", data).Replace("\"draft\":false", "\"draft\":true"), "0.4.1") == null);
            AssertTrue(GitHubReleaseClient.ParseRelease(ReleaseJson("0.4.2", data).Replace("\"prerelease\":false", "\"prerelease\":true"), "0.4.1") == null);
            foreach (string bad in new[] {
                ReleaseJson("0.4.2", data, digest: ""), ReleaseJson("0.4.2", data, digest: "sha256:123"),
                ReleaseJson("0.4.2", data, url: "https://example.com/Setup.exe"),
                ReleaseJson("0.4.2", data).Replace("\"size\":17", "\"size\":0"),
                ReleaseJson("0.4.2", data).Replace("\"size\":17", "\"size\":600000000") })
                ExpectUpdateFailure(() => GitHubReleaseClient.ParseRelease(bad, "0.4.1"));
        }

        private static void UpdateDownloadValidation()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ams2-update-tests-" + Guid.NewGuid().ToString("N"));
            byte[] bytes = Encoding.UTF8.GetBytes("installer fixture");
            string json = ReleaseJson("0.4.2", bytes);
            try
            {
                using var handler = new UpdateHttpFixture(json, bytes);
                using var http = new HttpClient(handler);
                var client = new GitHubReleaseClient(http);
                ReleaseUpdate update = client.FindUpdateAsync("0.4.1", CancellationToken.None).GetAwaiter().GetResult()!;
                string path = client.DownloadAsync(update, directory, null, CancellationToken.None).GetAwaiter().GetResult();
                AssertTrue(File.ReadAllBytes(path).SequenceEqual(bytes));
                AssertFalse(handler.SentAuthorization);
                AssertTrue(handler.SentUserAgent);
                foreach (byte[] invalid in new[] { Encoding.UTF8.GetBytes("tampered! fixture"), bytes.Take(5).ToArray(), bytes.Concat(bytes).ToArray() })
                {
                    handler.Bytes = invalid;
                    ExpectUpdateFailure(() => client.DownloadAsync(update, directory, null, CancellationToken.None).GetAwaiter().GetResult());
                    AssertTrue(File.ReadAllBytes(path).SequenceEqual(bytes));
                    AssertFalse(Directory.GetFiles(directory, "*.partial").Any());
                }
                handler.Bytes = bytes;
                handler.Status = HttpStatusCode.ServiceUnavailable;
                ExpectUpdateFailure(() => client.DownloadAsync(update, directory, null, CancellationToken.None).GetAwaiter().GetResult());
                handler.Status = HttpStatusCode.OK;
                handler.Redirect = "https://example.com/untrusted.exe";
                ExpectUpdateFailure(() => client.DownloadAsync(update, directory, null, CancellationToken.None).GetAwaiter().GetResult());
                handler.Redirect = null;
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                ExpectUpdateFailure(() => client.DownloadAsync(update, directory, null, canceled.Token).GetAwaiter().GetResult());
                AssertFalse(Directory.GetFiles(directory, "*.partial").Any());
                AssertTrue(File.Exists(client.DownloadAsync(update, directory, null, CancellationToken.None).GetAwaiter().GetResult()));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static void UpdateStartupAndArguments()
        {
            AssertTrue(GitHubAutoUpdater.ShouldEnable(Array.Empty<string>()));
            AssertTrue(GitHubAutoUpdater.ShouldEnable(new[] { "--background" }));
            foreach (string flag in new[] { "--demo", "--demo-events", "--capture-all", "--updates-disabled", "--auto-exit-seconds" }) AssertFalse(GitHubAutoUpdater.ShouldEnable(new[] { flag }));
            AssertEqual("\"\"", GitHubAutoUpdater.QuoteArgument(""));
            AssertEqual("\"한글 경로\"", GitHubAutoUpdater.QuoteArgument("한글 경로"));
            AssertEqual("\"a\\\"b\"", GitHubAutoUpdater.QuoteArgument("a\"b"));
            AssertEqual("\"C:\\path\\\\\"", GitHubAutoUpdater.QuoteArgument("C:\\path\\"));
            AssertEqual("\"$(literal); & test\"", GitHubAutoUpdater.QuoteArgument("$(literal); & test"));
            UpdateHelperRejectsTamperingAndCancellation();
        }

        private static void UpdateHelperRejectsTamperingAndCancellation()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ams2-helper-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string script = Path.Combine(directory, "apply-update.ps1"), settings = Path.Combine(directory, "update.json");
                using (Stream resource = typeof(GitHubAutoUpdater).Assembly.GetManifestResourceStream("AMS2LeagueClient.ApplyUpdate.ps1")!)
                using (var reader = new StreamReader(resource)) File.WriteAllText(script, reader.ReadToEnd(), new UTF8Encoding(true));
                string installer = Path.Combine(directory, "Setup.exe"), executable = Path.Combine(directory, "AMS2LeagueClient.exe"), result = Path.Combine(directory, "result.json");
                File.WriteAllText(installer, "fixture"); File.WriteAllText(executable, "must never run");
                foreach (bool tamper in new[] { true, false })
                {
                    File.WriteAllText(installer, "fixture");
                    using Process parent = Process.GetCurrentProcess();
                    File.WriteAllText(settings, JsonSerializer.Serialize(new { ParentId = parent.Id, ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks.ToString(), Installer = installer, Sha256 = tamper ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))), Size = new FileInfo(installer).Length, Version = "0.4.2", InstallDirectory = directory, Executable = executable, RestartArguments = "", ResultPath = result }), new UTF8Encoding(true));
                    var start = new ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                    foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-SettingsPath", settings }) start.ArgumentList.Add(arg);
                    using Process helper = Process.Start(start)!;
                    if (!tamper)
                    {
                        var deadline = Stopwatch.StartNew();
                        while (!File.Exists(Path.Combine(directory, "ready")) && !helper.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(50);
                        AssertTrue(File.Exists(Path.Combine(directory, "ready")));
                        File.WriteAllText(Path.Combine(directory, "cancel"), "cancel");
                    }
                    AssertTrue(helper.WaitForExit(15000)); AssertEqual(1, helper.ExitCode);
                    using JsonDocument report = JsonDocument.Parse(File.ReadAllText(result));
                    AssertFalse(report.RootElement.GetProperty("success").GetBoolean());
                    AssertEqual("업데이트: 설치 실패 또는 연기 · 업데이트 로그를 확인해 주세요. 6시간 후 다시 확인합니다.", report.RootElement.GetProperty("message").GetString());
                    AssertFalse(File.Exists(Path.Combine(directory, "install.log")));
                    AssertEqual("must never run", File.ReadAllText(executable));
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void ExpectUpdateFailure(Action action)
        {
            try { action(); }
            catch (Exception exception) when (exception is InvalidDataException || exception is HttpRequestException || exception is OperationCanceledException) { return; }
            throw new InvalidOperationException("Expected update validation to fail.");
        }

        private sealed class UpdateHttpFixture : HttpMessageHandler
        {
            private readonly string _json;
            public byte[] Bytes { get; set; }
            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
            public string? Redirect { get; set; }
            public bool SentAuthorization { get; private set; }
            public bool SentUserAgent { get; private set; }
            public UpdateHttpFixture(string json, byte[] bytes) { _json = json; Bytes = bytes; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                SentAuthorization |= request.Headers.Authorization != null;
                SentUserAgent |= request.Headers.UserAgent.Any();
                return Task.FromResult(new HttpResponseMessage(Status)
                {
                    RequestMessage = Redirect == null ? request : new HttpRequestMessage(HttpMethod.Get, Redirect),
                    Content = request.RequestUri!.Host == "api.github.com" ? (HttpContent)new StringContent(_json, Encoding.UTF8, "application/json") : new ByteArrayContent(Bytes)
                });
            }
        }
    }
}
