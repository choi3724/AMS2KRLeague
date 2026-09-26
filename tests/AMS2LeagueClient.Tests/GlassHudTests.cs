using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void DefaultHudWindowsRetainPixelTransparency()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-default-hud-test-" + Guid.NewGuid().ToString("N") + ".json");
            var previous = Application.Current.Windows.Cast<Window>().ToHashSet();
            var policy = ClientStartupPolicy.FromArguments(Array.Empty<string>());
            var overlay = new OverlayWindow(false, path, useGlass: policy.UseGlass);
            try
            {
                foreach (string key in OverlayComponentKeys.All) overlay.SetComponentEnabled(key, true);
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.ShowAt(new GameWindowSnapshot(IntPtr.Zero, 0, 0, 1920, 1080, 96, true, false, 0));
                PumpDispatcher();
                var surfaces = Application.Current.Windows.Cast<Window>().Where(w => !previous.Contains(w) && w.IsVisible).ToArray();
                AssertTrue(surfaces.Length >= 10);
                foreach (Window surface in surfaces)
                {
                    AssertTrue(surface.AllowsTransparency);
                    OverlayStyleState style = OverlayWindowInterop.ReadStyleState(new WindowInteropHelper(surface).Handle);
                    AssertTrue(style.Layered);
                    AssertTrue(style.ClickThrough && style.NoActivate && style.ToolWindow);
                }
            }
            finally
            {
                overlay.Close();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void GlassHudLifecycle()
        {
            if (!OverlayWindowInterop.IsGlassAvailable()) return;
            string path = Path.Combine(Path.GetTempPath(), "ams2-glass-test-" + Guid.NewGuid().ToString("N") + ".json");
            var previous = Application.Current.Windows.Cast<Window>().ToHashSet();
            var overlay = new OverlayWindow(false, path, useGlass: true);
            try
            {
                foreach (string key in OverlayComponentKeys.All) overlay.SetComponentEnabled(key, true);
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.UpdateDrivingTelemetry(DemoSnapshotFactory.CreateSnapshot(), 16, 7);
                overlay.ShowAt(new GameWindowSnapshot(IntPtr.Zero, 0, 0, 1920, 1080, 96, true, false, 0));
                PumpDispatcher();
                var surfaces = Application.Current.Windows.Cast<Window>().Where(w => !previous.Contains(w) && w.IsVisible).ToArray();
                AssertTrue(surfaces.Length >= 10);
                AssertTrue(overlay.UsesGlass);
                foreach (Window surface in surfaces)
                {
                    OverlayStyleState style = OverlayWindowInterop.ReadStyleState(new WindowInteropHelper(surface).Handle);
                    AssertFalse(style.Layered);
                    AssertTrue(style.ClickThrough && style.NoActivate && style.ToolWindow);
                }
                AssertTrue(overlay.BeginLayoutEdit());
                PumpDispatcher();
                AssertFalse(overlay.GetStyleState().ClickThrough);
                overlay.EndLayoutEdit(false);
                PumpDispatcher();
                AssertTrue(overlay.GetStyleState().ClickThrough);
                overlay.HideOverlay();
                AssertTrue(surfaces.All(w => !w.IsVisible));
            }
            finally
            {
                overlay.Close();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void RetainedNHudLifecycle()
        {
            if (!OverlayWindowInterop.IsGlassAvailable()) return;
            string path = Path.Combine(Path.GetTempPath(), "ams2-retained-n-test-" + Guid.NewGuid().ToString("N") + ".json");
            var events = new List<string>();
            var overlay = new OverlayWindow(false, path, useGlass: true, useRetainedN: true);
            overlay.RetainedNStatus += events.Add;
            try
            {
                foreach (string key in OverlayComponentKeys.All) overlay.SetComponentEnabled(key, true);
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.UpdateDrivingTelemetry(DemoSnapshotFactory.CreateSnapshot(), 16, 7);
                overlay.ShowAt(new GameWindowSnapshot(IntPtr.Zero, 0, 0, 1920, 1080, 96, true, false, 0));
                RunDispatcherFor(TimeSpan.FromSeconds(3));
                AssertTrue(events.Any(e => e.StartsWith("active hwnds=2", StringComparison.Ordinal)));
                overlay.HideOverlay();
                PumpDispatcher();
                AssertTrue(events.Any(e => e.StartsWith("released commits=", StringComparison.Ordinal)));
            }
            finally
            {
                overlay.Close();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void RunDispatcherFor(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var stop = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
            stop.Tick += (_, __) => { stop.Stop(); frame.Continue = false; };
            stop.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
