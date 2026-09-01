using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using AMS2LeagueClient.Capture;
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
        private DispatcherTimer? _autoExitTimer;
        private DispatcherTimer? _demoEventTimer;
        private bool _allowInteractiveErrors;

        protected override void OnStartup(StartupEventArgs eventArgs)
        {
            base.OnStartup(eventArgs);
            string[] args = eventArgs.Args;
            ClientStartupPolicy startupPolicy = ClientStartupPolicy.FromArguments(args);
            _allowInteractiveErrors = startupPolicy.ShowStatusWindow;
            string logDirectory = ValueAfter(args, "--log-dir") ?? Path.Combine(AppContext.BaseDirectory, "logs");
            _logger = new FileLogger(logDirectory);
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
                var status = new ClientStatusViewModel();
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
                    _coordinator = new PlayerOverlayCoordinator(_overlay, status, _logger, startupPolicy.Diagnostic);
                    _coordinator.Start();
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
            _autoExitTimer?.Stop();
            _demoEventTimer?.Stop();
            _memoryDiagnostics?.Dispose();
            _memoryDiagnostics = null;
            _coordinator?.Dispose();
            _coordinator = null;

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
    }
}
