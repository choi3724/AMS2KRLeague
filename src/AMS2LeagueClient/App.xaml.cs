using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AMS2LeagueClient.Capture;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient
{
    public partial class App : Application
    {
        private FileLogger? _logger;
        private OverlayWindow? _overlay;
        private ClientStatusWindow? _statusWindow;
        private PlayerOverlayCoordinator? _coordinator;
        private MemoryDiagnosticsWriter? _memoryDiagnostics;
        private ActivityCaptureRuntime? _activityCapture;
        private CancellationTokenSource? _bootstrapCancellation;
        private Task? _bootstrapTask;
        private DispatcherTimer? _autoExitTimer;
        private DispatcherTimer? _demoEventTimer;
        private bool _allowInteractiveErrors;
        private bool _cleanupStarted;
        private bool _exitStarted;

        protected override void OnStartup(StartupEventArgs eventArgs)
        {
            base.OnStartup(eventArgs);
            string[] args = eventArgs.Args;
            ClientStartupPolicy startupPolicy = ClientStartupPolicy.FromArguments(args);
            _allowInteractiveErrors = startupPolicy.ShowStatusWindow;
            string userDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AMS2KRLeague");
            string logDirectory = ValueAfter(args, "--log-dir") ?? Path.Combine(userDataRoot, "logs");
            _logger = new FileLogger(logDirectory);
            _logger.Info("CLIENT_VERSION", "product=AMS2_LEAGUE_OVERLAY version=" + ClientVersion());
            DispatcherUnhandledException += HandleDispatcherException;

            try
            {
                string? captureDirectory = ValueAfter(args, "--capture-all");
                if (captureDirectory != null)
                {
                    IReadOnlyList<string> files = CaptureRunner.Run(Path.GetFullPath(captureDirectory));
                    _logger.Info("CAPTURE_COMPLETE", "files=" + files.Count + " output=" + Path.GetFullPath(captureDirectory));
                    Shutdown(0);
                    return;
                }

                bool demoEvents = args.Contains("--demo-events", StringComparer.OrdinalIgnoreCase);
                bool demo = demoEvents || args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
                var status = new ClientStatusViewModel(ClientVersion());
                if (startupPolicy.ShowStatusWindow)
                {
                    _statusWindow = new ClientStatusWindow(status)
                    {
                        ShowActivated = startupPolicy.ShowStatusWindowActivated
                    };
                    _statusWindow.Closed += (sender, closedArgs) => ExitClient();
                    _statusWindow.Show();
                }

                _overlay = new OverlayWindow(startupPolicy.Diagnostic);
                if (demo)
                {
                    status.SetDemo(startupPolicy.Diagnostic);
                    _overlay.SetViewModel(DemoSnapshotFactory.CreateShell(startupPolicy.Diagnostic), false);
                    _overlay.ShowDemoAt(64, 64, 96);
                    _logger.Info("DEMO_START", "fixture=true realAms2=false diagnostic=" + startupPolicy.Diagnostic);
                    _logger.Info("OVERLAY_STYLES", _overlay.GetStyleState().ToString());
                    if (demoEvents)
                    {
                        OverlayEventType[] types = { OverlayEventType.PositionGained, OverlayEventType.PositionLost, OverlayEventType.PersonalBest, OverlayEventType.RaceFastestLap, OverlayEventType.PitEntry, OverlayEventType.FinalLap, OverlayEventType.Finish };
                        int eventIndex = 0;
                        _demoEventTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                        _demoEventTimer.Tick += (sender, tickArgs) =>
                        {
                            OverlayEventType type = types[eventIndex++ % types.Length];
                            _overlay.SetViewModel(DemoSnapshotFactory.CreateShell(startupPolicy.Diagnostic, type));
                            _logger.Info("DEMO_EVENT", "type=" + type + " fixture=true");
                        };
                        _demoEventTimer.Start();
                    }
                }
                else
                {
                    bool networkEnabled = !args.Contains("--activity-upload-disabled", StringComparer.OrdinalIgnoreCase);
                    string activityData = ValueAfter(args, "--activity-data")
                        ?? Path.Combine(userDataRoot, "activity");
                    ActivityConnectionOptions connection = ActivityConnectionOptions.Load(ValueAfter(args, "--activity-config"));
                    status.SetAccount(connection.HasPlayerCredentials);
                    string installationId = ValueAfter(args, "--installation-id")
                        ?? ClientInstallationIdentity.LoadOrCreate(activityData);
                    Cafe24ActivityUploadTransport? playerTransport = networkEnabled && connection.HasPlayerCredentials
                        ? new Cafe24ActivityUploadTransport(connection)
                        : null;
                    try
                    {
                        _activityCapture = new ActivityCaptureRuntime(
                            activityData,
                            installationId,
                            ClientVersion(),
                            _logger,
                            playerTransport);
                        playerTransport = null; // ActivityCaptureRuntime owns it after a successful construction.
                    }
                    finally
                    {
                        playerTransport?.Dispose();
                    }
                    _logger.Info(
                        "ACTIVITY_CONNECTION",
                        "configPresent=" + connection.ConfigFileExists
                        + " networkEnabled=" + networkEnabled
                        + " playerPaired=" + connection.HasPlayerCredentials);

                    // Local event data from 0.1.x is never authoritative in a public client.
                    // Only the authenticated server bootstrap may activate a league event.
                    _activityCapture.SetScheduledEvent(null);
                    bool bootstrapRequired = networkEnabled && connection.HasPlayerCredentials;

                    _coordinator = new PlayerOverlayCoordinator(
                        _overlay,
                        status,
                        _logger,
                        startupPolicy.Diagnostic,
                        _activityCapture);
                    if (bootstrapRequired)
                    {
                        // Do not begin session classification until the server's
                        // current event is known (or the bounded request fails).
                        // Otherwise a race already in progress can be permanently
                        // classified as General before bootstrap finishes.
                        status.SetWaiting();
                        StartBootstrapRefresh(connection, _coordinator, status);
                    }
                    else
                    {
                        _coordinator.Start();
                        if (networkEnabled)
                        {
                            StartHealthRefresh(connection, status);
                        }
                        else
                        {
                            status.SetServerOffline();
                        }
                    }
                }

                _logger.Info(
                    "STARTUP_POLICY",
                    "background=" + startupPolicy.IsBackgroundStartup
                    + " statusWindow=" + startupPolicy.ShowStatusWindow
                    + " showActivated=" + startupPolicy.ShowStatusWindowActivated);

                string? memoryCsv = ValueAfter(args, "--memory-csv");
                if (memoryCsv != null)
                {
                    int configuredSampleSeconds = ParseInt(ValueAfter(args, "--memory-sample-seconds"));
                    int memorySampleSeconds = configuredSampleSeconds > 0 ? configuredSampleSeconds : 15;
                    _memoryDiagnostics = new MemoryDiagnosticsWriter(memoryCsv, TimeSpan.FromSeconds(memorySampleSeconds));
                    _memoryDiagnostics.Start();
                    _logger.Info("MEMORY_DIAGNOSTICS", "enabled=true intervalSeconds=" + memorySampleSeconds + " path=" + Path.GetFullPath(memoryCsv));
                }

                int autoExitSeconds = ParseInt(ValueAfter(args, "--auto-exit-seconds"));
                if (autoExitSeconds > 0)
                {
                    _autoExitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoExitSeconds) };
                    _autoExitTimer.Tick += (sender, tickArgs) => ExitClient();
                    _autoExitTimer.Start();
                }
            }
            catch (Exception exception)
            {
                _logger.Error("STARTUP_EXCEPTION", exception);
                if (_allowInteractiveErrors)
                {
                    MessageBox.Show(exception.Message, "AMS2 League Client failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                Shutdown(1);
            }
        }

        private void ExitClient()
        {
            if (_exitStarted) return;
            _exitStarted = true;
            CleanupRuntime();

            if (_overlay != null)
            {
                _overlay.Close();
                _overlay = null;
            }

            if (_statusWindow != null && _statusWindow.IsVisible)
            {
                _statusWindow.Close();
            }

            _statusWindow = null;
            Shutdown(0);
        }

        protected override void OnExit(ExitEventArgs eventArgs)
        {
            // Also covers startup exceptions, Windows logoff/shutdown and any
            // WPF shutdown path that does not flow through ExitClient.
            CleanupRuntime();
            DispatcherUnhandledException -= HandleDispatcherException;
            base.OnExit(eventArgs);
        }

        private void CleanupRuntime()
        {
            if (_cleanupStarted) return;
            _cleanupStarted = true;

            _autoExitTimer?.Stop();
            _demoEventTimer?.Stop();
            CleanupComponent("MEMORY_DIAGNOSTICS", () => _memoryDiagnostics?.Dispose());
            _memoryDiagnostics = null;
            CleanupComponent("BOOTSTRAP", StopBootstrapRefresh);
            CleanupComponent("COORDINATOR", () => _coordinator?.Dispose());
            _coordinator = null;
            CleanupComponent("ACTIVITY_CAPTURE", () => _activityCapture?.Dispose());
            _activityCapture = null;
        }

        private void CleanupComponent(string component, Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                try
                {
                    _logger?.Error("CLEANUP_EXCEPTION", new InvalidOperationException(component + " cleanup failed.", exception));
                }
                catch
                {
                    // Continue releasing the remaining resources even when the
                    // diagnostic log itself is unavailable.
                }
            }
        }

        private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
        {
            _logger?.Error("UNHANDLED_UI_EXCEPTION", eventArgs.Exception);
            eventArgs.Handled = true;
            if (_allowInteractiveErrors)
            {
                MessageBox.Show(eventArgs.Exception.Message, "AMS2 League Client error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string? ValueAfter(string[] args, string key)
        {
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private static int ParseInt(string? value)
        {
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : 0;
        }

        private void StartBootstrapRefresh(
            ActivityConnectionOptions connection,
            PlayerOverlayCoordinator coordinator,
            ClientStatusViewModel status)
        {
            _bootstrapCancellation = new CancellationTokenSource();
            CancellationToken token = _bootstrapCancellation.Token;
            _bootstrapTask = Task.Run(async () =>
            {
                try
                {
                    using var transport = new Cafe24ActivityUploadTransport(connection);
                    Cafe24BootstrapResponse bootstrap = await transport.GetBootstrapAsync(token).ConfigureAwait(false);
                    ScheduledLeagueEvent? scheduledEvent = ToScheduledEvent(bootstrap.ScheduledEvent);
                    _activityCapture?.SetScheduledEvent(scheduledEvent);
                    _logger?.Info(
                        "ACTIVITY_BOOTSTRAP",
                        "status=OK event=" + (bootstrap.ScheduledEvent.PublicId.Length == 0 ? "none" : bootstrap.ScheduledEvent.PublicId)
                        + " serviceVersion=" + bootstrap.ServiceVersion);
                    _ = Dispatcher.BeginInvoke(new Action(() => status.SetServerConnected(bootstrap.ServiceVersion)));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    _activityCapture?.SetScheduledEvent(null);
                    _logger?.Error("ACTIVITY_BOOTSTRAP_EXCEPTION", exception);
                    _ = Dispatcher.BeginInvoke(new Action(status.SetServerOffline));
                }
                finally
                {
                    // Never await the dispatcher here: shutdown waits for this
                    // task on the UI thread. A queued callback checks cancellation
                    // and ownership before starting telemetry.
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!token.IsCancellationRequested
                            && !_cleanupStarted
                            && ReferenceEquals(_coordinator, coordinator))
                        {
                            coordinator.Start();
                        }
                    }));
                }
            }, token);
        }

        private void StartHealthRefresh(
            ActivityConnectionOptions connection,
            ClientStatusViewModel status)
        {
            _bootstrapCancellation = new CancellationTokenSource();
            CancellationToken token = _bootstrapCancellation.Token;
            _bootstrapTask = Task.Run(async () =>
            {
                try
                {
                    using var transport = new Cafe24ActivityUploadTransport(connection);
                    Cafe24HealthResponse health = await transport.GetHealthAsync(token).ConfigureAwait(false);
                    _logger?.Info(
                        "SERVER_HEALTH",
                        "status=" + health.Status
                        + " serviceVersion=" + health.ServiceVersion
                        + " schema=" + (health.SchemaVersion?.ToString() ?? "unknown"));
                    _ = Dispatcher.BeginInvoke(new Action(() => status.SetServerConnected(health.ServiceVersion)));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    _logger?.Warning("SERVER_HEALTH", "status=OFFLINE reason=" + exception.GetType().Name);
                    _ = Dispatcher.BeginInvoke(new Action(status.SetServerOffline));
                }
            }, token);
        }

        private void StopBootstrapRefresh()
        {
            _bootstrapCancellation?.Cancel();
            if (_bootstrapTask != null)
            {
                try
                {
                    _bootstrapTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
            }
            _bootstrapCancellation?.Dispose();
            _bootstrapCancellation = null;
            _bootstrapTask = null;
        }

        private static ScheduledLeagueEvent? ToScheduledEvent(ActivityScheduledEventOptions? value)
        {
            if (value == null
                || string.IsNullOrWhiteSpace(value.PublicId)
                || (!value.CaptureOpensAtUtc.HasValue && !value.ScheduledAtUtc.HasValue))
            {
                return null;
            }
            return new ScheduledLeagueEvent
            {
                EventId = value.PublicId,
                CaptureOpensAtUtc = value.CaptureOpensAtUtc,
                ScheduledAtUtc = value.ScheduledAtUtc,
                ExpectedTrack = string.IsNullOrWhiteSpace(value.Track) ? null : value.Track,
                ExpectedVehicleClass = string.IsNullOrWhiteSpace(value.ExpectedVehicleClass) ? null : value.ExpectedVehicleClass
            };
        }

        private static string ClientVersion()
        {
            AssemblyInformationalVersionAttribute? informational = typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return informational?.InformationalVersion ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }
}
