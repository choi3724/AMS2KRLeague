using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.Diagnostics;

namespace AMS2LeagueClient.Runtime
{
    public sealed class GitHubAutoUpdater : IDisposable
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly string _version, _directory, _installDirectory;
        private readonly string[] _arguments;
        private readonly Action<string> _status;
        private readonly Func<Task<bool>> _exit;
        private readonly Func<bool> _canInstall;
        private readonly FileLogger _logger;
        private Task? _worker;

        public GitHubAutoUpdater(string version, string dataRoot, string[] arguments, Action<string> status, Func<Task<bool>> exit, FileLogger logger, Func<bool>? canInstall = null)
        {
            _version = version;
            _directory = Path.Combine(dataRoot, "updates");
            _installDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            _arguments = arguments;
            _status = status;
            _exit = exit;
            _canInstall = canInstall ?? (() => true);
            _logger = logger;
        }

        public void Start() => _worker ??= Task.Run(RunAsync);

        public static bool ShouldEnable(string[] args)
            => !args.Any(arg => new[] { "--demo", "--demo-events", "--capture-all", "--auto-exit-seconds", "--updates-disabled" }
                .Contains(arg, StringComparer.OrdinalIgnoreCase));

        private async Task RunAsync()
        {
            CancellationToken token = _stop.Token;
            try
            {
                Directory.CreateDirectory(_directory);
                string resultPath = Path.Combine(_directory, "last-result.json");
                if (File.Exists(resultPath))
                {
                    using JsonDocument result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, token).ConfigureAwait(false));
                    _status(result.RootElement.GetProperty("message").GetString() ?? "업데이트: 이전 설치 결과를 확인했습니다.");
                    if (!result.RootElement.GetProperty("success").GetBoolean())
                    {
                        DateTimeOffset attempted = result.RootElement.GetProperty("atUtc").GetDateTimeOffset();
                        TimeSpan cooldown = attempted.AddHours(6) - DateTimeOffset.UtcNow;
                        if (cooldown > TimeSpan.Zero) await Task.Delay(cooldown > TimeSpan.FromHours(6) ? TimeSpan.FromHours(6) : cooldown, token).ConfigureAwait(false);
                    }
                    else await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception) { _logger.Warning("UPDATE_RESULT", "reason=" + exception.GetType().Name); }

            while (!token.IsCancellationRequested)
            {
                string? downloadedInstaller = null;
                bool handedOff = false;
                try
                {
                    _status("업데이트: GitHub 최신 버전 확인 중");
                    var client = new GitHubReleaseClient(_http);
                    ReleaseUpdate? update;
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        update = await client.FindUpdateAsync(_version, timeout.Token).ConfigureAwait(false);
                    }
                    if (update == null) _status("업데이트: 최신 버전입니다 (" + _version + ")");
                    else
                    {
                        string attempt = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
                        var progress = new UpdateProgress(percent => _status("업데이트: " + update.Version + " 다운로드 중 · " + percent + "%"));
                        string installer = downloadedInstaller = await client.DownloadAsync(update, attempt, progress, token).ConfigureAwait(false);
                        if (!_canInstall())
                            _status("업데이트: 다운로드 완료 · 경기 기록 저장이 끝나면 자동 설치합니다");
                        while (!_canInstall()) await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                        _status("업데이트: " + update.Version + " 다운로드 완료 · 10초 후 설치를 위해 종료하며, 설치 후 자동으로 다시 실행됩니다.");
                        await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);

                        // The helper must be alive and holding the update lock before we release telemetry and exit.
                        if (await PrepareInstallerAsync(update, installer, attempt, token).ConfigureAwait(false))
                        {
                            handedOff = true;
                            _status("업데이트: " + update.Version + " 설치를 위해 종료합니다. 설치 후 자동 실행됩니다.");
                            if (await _exit().ConfigureAwait(false)) return;
                            await File.WriteAllTextAsync(Path.Combine(attempt, "cancel"), "cancel", token).ConfigureAwait(false);
                            _status("업데이트: 설치를 연기했습니다. 프로그램은 계속 작동합니다.");
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    _logger.Warning("AUTO_UPDATE_FAILED", "reason=" + exception.GetType().Name);
                    _status("업데이트: 확인 또는 설치 준비 실패 · 6시간 후 재시도합니다. 오버레이는 계속 작동합니다.");
                }
                finally
                {
                    if (!handedOff && downloadedInstaller != null)
                    {
                        try { File.Delete(downloadedInstaller); }
                        catch (Exception exception) { _logger.Warning("UPDATE_DOWNLOAD_CLEANUP", "reason=" + exception.GetType().Name); }
                    }
                }
                try { await Task.Delay(TimeSpan.FromHours(6), token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            }
        }

        private async Task<bool> PrepareInstallerAsync(ReleaseUpdate update, string installer, string attempt, CancellationToken token)
        {
            // Do not turn a development checkout into an installed application.
            if (Directory.Exists(Path.Combine(_installDirectory, ".git")) || File.Exists(Path.Combine(_installDirectory, "AMS2KRLeague.sln")))
                throw new IOException("소스 저장소에는 자동 설치할 수 없습니다.");
            string executable = Path.Combine(_installDirectory, "AMS2LeagueClient.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("실행 파일을 찾을 수 없습니다.");
            string probe = Path.Combine(_installDirectory, ".update-write-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            string script = Path.Combine(attempt, "apply-update.ps1");
            using (Stream resource = typeof(GitHubAutoUpdater).Assembly.GetManifestResourceStream("AMS2LeagueClient.ApplyUpdate.ps1")
                ?? throw new FileNotFoundException("업데이트 실행 도구를 찾을 수 없습니다."))
            using (var reader = new StreamReader(resource, Encoding.UTF8))
                await File.WriteAllTextAsync(script, await reader.ReadToEndAsync().ConfigureAwait(false), new UTF8Encoding(true), token).ConfigureAwait(false);
            using Process parent = Process.GetCurrentProcess();
            string settings = Path.Combine(attempt, "update.json");
            await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new
            {
                ParentId = parent.Id,
                ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Installer = installer,
                update.Sha256,
                update.Size,
                update.Version,
                InstallDirectory = _installDirectory,
                Executable = executable,
                RestartArguments = string.Join(" ", _arguments.Where(arg => !string.Equals(arg, "--after-update", StringComparison.OrdinalIgnoreCase)).Concat(new[] { "--after-update" }).Select(QuoteArgument)),
                ResultPath = Path.Combine(_directory, "last-result.json")
            }), new UTF8Encoding(true), token).ConfigureAwait(false);
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = attempt
            };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-SettingsPath", settings }) start.ArgumentList.Add(argument);
            using Process helper = Process.Start(start) ?? throw new IOException("업데이트 실행 도구를 시작하지 못했습니다.");
            for (int i = 0; i < 100; i++)
            {
                if (helper.HasExited) throw new IOException("업데이트 실행 도구가 준비되지 않았습니다.");
                if (File.Exists(Path.Combine(attempt, "ready"))) return true;
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            await File.WriteAllTextAsync(Path.Combine(attempt, "cancel"), "cancel", token).ConfigureAwait(false);
            throw new TimeoutException("업데이트 실행 도구의 준비 시간이 초과되었습니다.");
        }

        private sealed class UpdateProgress : IProgress<int>
        {
            private readonly Action<int> _report;
            public UpdateProgress(Action<int> report) => _report = report;
            public void Report(int value) => _report(value);
        }

        public static string QuoteArgument(string argument)
        {
            // Windows CommandLineToArgvW rules; no shell interpolation or evaluation.
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        public void Dispose()
        {
            _stop.Cancel();
            // Shutdown stays on the dispatcher; the worker never blocks telemetry flushing.
            if (_worker == null) { _http.Dispose(); _stop.Dispose(); }
            else _ = _worker.ContinueWith(_ => { _http.Dispose(); _stop.Dispose(); }, TaskScheduler.Default);
        }
    }
}
