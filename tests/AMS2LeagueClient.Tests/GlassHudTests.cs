using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
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
            var overlay = new OverlayWindow(false, path, useGlass: policy.UseGlass, useSoftwareRendering: policy.UseSoftwareRendering);
            try
            {
                AssertTrue(overlay.UsesSoftwareRendering);
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
                    // Default Monitor HUDs rasterize on the CPU and never wait on a GPU readback.
                    var source = (HwndSource)PresentationSource.FromVisual(surface)!;
                    AssertEqual(RenderMode.SoftwareOnly, source.CompositionTarget.RenderMode);
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
                if (!events.Any(e => e.StartsWith("active hwnds=2", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Retained N host status: " + string.Join(" | ", events));
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

        private static void GameplayNIntroStartsOnceWhenShown()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-n-intro-test-" + Guid.NewGuid().ToString("N") + ".json");
            var overlay = new OverlayWindow(false, path, useGlass: true);
            try
            {
                foreach (string key in OverlayComponentKeys.All)
                    overlay.SetComponentEnabled(key, key == OverlayComponentKeys.AvanteClusterExpanded);
                var layout = (OverlayLayoutProfile)typeof(OverlayWindow).GetField("_layoutProfile",
                    BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
                layout.Capture(OverlayComponentKeys.AvanteClusterExpanded,
                    new OverlayBounds(300, 450, 820, 300), 1920, 1080);
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.UpdateDrivingTelemetry(DemoSnapshotFactory.CreateSnapshot(), 16, 7);
                var first = new GameWindowSnapshot(new IntPtr(1234), 0, 0, 1920, 1080, 96, true, false, 0);
                overlay.ShowAt(first);
                PumpDispatcher();
                AssertEqual(1, layout.AvanteCanvasVersion);
                AssertEqual(new OverlayBounds(300, 390, 820, 436), layout.Resolve(
                    OverlayComponentKeys.AvanteClusterExpanded, new OverlayBounds(0, 0, 72, 48), 1920, 1080));
                var view = (AvanteClusterView)typeof(OverlayWindow).GetField("_avanteExpandedView",
                    BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
                var started = typeof(AvanteClusterView).GetField("_ignitionStarted", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var generation = typeof(AvanteClusterView).GetField("_ignitionGeneration", BindingFlags.NonPublic | BindingFlags.Instance)!;
                AssertTrue((bool)started.GetValue(view)!);
                int firstGeneration = (int)generation.GetValue(view)!;
                overlay.ShowAt(first);
                PumpDispatcher();
                AssertEqual(firstGeneration, (int)generation.GetValue(view)!);
                overlay.HideOverlay();
                overlay.ShowAt(first);
                PumpDispatcher();
                AssertTrue((bool)started.GetValue(view)!);
                AssertEqual(firstGeneration, (int)generation.GetValue(view)!);
                overlay.ShowAt(new GameWindowSnapshot(new IntPtr(5678), 0, 0, 1920, 1080, 96, true, false, 0));
                PumpDispatcher();
                AssertTrue((bool)started.GetValue(view)!);
                AssertEqual(firstGeneration + 1, (int)generation.GetValue(view)!);
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
