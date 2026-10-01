using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void HudFrameLimitSettingIsNormalized()
        {
            AssertEqual(60, new DrivingHudSettings().Normalize().HudFrameLimit); // fixed FPS cap, not the monitor
            AssertEqual(144, new DrivingHudSettings { HudFrameLimit = 144 }.Normalize().HudFrameLimit);
            AssertEqual(30, new DrivingHudSettings { HudFrameLimit = 30 }.Normalize().HudFrameLimit);
            AssertEqual(60, new DrivingHudSettings { HudFrameLimit = 77 }.Normalize().HudFrameLimit);
            AssertEqual(60, new DrivingHudSettings { HudFrameLimit = 0 }.Normalize().HudFrameLimit);
        }

        private static void DisplaySignatureTracksShownContent()
        {
            OverlayViewModel first = DemoSnapshotFactory.CreateShell(false).Timing;
            OverlayViewModel second = DemoSnapshotFactory.CreateShell(false).Timing;
            AssertEqual(DisplaySignature.Of(first), DisplaySignature.Of(second));
            second.RankingRows[0].CurrentTime = "1:23.456";
            AssertTrue(DisplaySignature.Of(first) != DisplaySignature.Of(second));
            second = DemoSnapshotFactory.CreateShell(false).Timing;
            second.AheadDistanceColor = "#00FF00";
            AssertTrue(DisplaySignature.Of(first) != DisplaySignature.Of(second));
        }

        // A rebuilt model with identical content must not rebind the timing views (rebinding
        // re-creates converted brushes and repaints); changed content must.
        private static void TimingViewsRebindOnlyOnChange()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-redraw-" + Guid.NewGuid().ToString("N") + ".json");
            var overlay = new OverlayWindow(false, path);
            try
            {
                overlay.SetComponentEnabled(OverlayComponentKeys.TimingTower, true);
                var first = DemoSnapshotFactory.CreateShell(false);
                overlay.SetViewModel(first, false);
                overlay.ShowDemoAt(-5000, -5000, 96);
                PumpDispatcher();
                var tower = (FrameworkElement)typeof(OverlayWindow).GetField("TimingHud", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                object bound = tower.DataContext;
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.ShowDemoAt(-5000, -5000, 96);
                PumpDispatcher();
                AssertTrue(ReferenceEquals(bound, tower.DataContext));
                var changed = DemoSnapshotFactory.CreateShell(false);
                changed.Timing.RankingRows[0].CurrentTime = "1:23.456";
                overlay.SetViewModel(changed, false);
                PumpDispatcher();
                AssertTrue(ReferenceEquals(changed.Timing, tower.DataContext));
                Console.WriteLine("PROOF identical timing content keeps the bound model; changed content rebinds");
            }
            finally { overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }

        // The rest window shows the instrument at exactly the screen position it has on the full
        // ignition canvas, so shrinking/growing the window never moves the dial.
        private static void AvanteRestWindowKeepsDialPosition()
        {
            foreach (bool expanded in new[] { false, true })
            {
                var full = expanded ? new OverlayBounds(100, 200, 820, 436) : new OverlayBounds(100, 200, 569, 545);
                var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
                var placement = ((OverlayBounds Bounds, Rect Viewport))typeof(AvanteClusterView).GetMethod("RestPlacement", flags)!
                    .Invoke(null, new object[] { full, expanded })!;
                AssertTrue(placement.Bounds.Width * placement.Bounds.Height < full.Width * full.Height);
                AssertTrue(placement.Bounds.X >= full.X && placement.Bounds.Y >= full.Y
                    && placement.Bounds.X + placement.Bounds.Width <= full.X + full.Width
                    && placement.Bounds.Y + placement.Bounds.Height <= full.Y + full.Height);
                Point Map(OverlayBounds window, Rect? viewport, Point design)
                {
                    var view = new AvanteClusterView(expanded) { Width = window.Width, Height = window.Height };
                    typeof(AvanteClusterView).GetProperty("DesignViewport", flags)!.SetValue(view, viewport);
                    view.Measure(new Size(window.Width, window.Height)); view.Arrange(new Rect(0, 0, window.Width, window.Height));
                    var transform = (MatrixTransform)typeof(AvanteClusterView).GetField("_transform", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
                    Point local = transform.Matrix.Transform(design);
                    return new Point(window.X + local.X, window.Y + local.Y);
                }
                foreach (var design in new[] { new Point(1024, 397), new Point(expanded ? 0 : 570, 0), new Point(expanded ? 2048 : 1478, 750) })
                {
                    Point onFull = Map(full, null, design), onRest = Map(placement.Bounds, placement.Viewport, design);
                    AssertTrue(Math.Abs(onFull.X - onRest.X) < .51 && Math.Abs(onFull.Y - onRest.Y) < .51);
                }
            }
        }

        private static void AvanteWindowUsesFullCanvasOnlyForIntroAndEditing()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-n-rest-" + Guid.NewGuid().ToString("N") + ".json");
            var overlay = new OverlayWindow(false, path, useSoftwareRendering: true);
            try
            {
                foreach (string key in OverlayComponentKeys.All) overlay.SetComponentEnabled(key, key == OverlayComponentKeys.AvanteCluster);
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                var game = new GameWindowSnapshot(new IntPtr(4321), 0, 0, 1920, 1080, 96, true, false, 0);
                overlay.ShowAt(game);
                PumpDispatcher();
                var panel = (Window)((Array)typeof(OverlayWindow).GetField("_drivingWindows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!).GetValue(5)!;
                OverlayBounds Bounds() => OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(panel).Handle);
                var requested = (System.Collections.Generic.Dictionary<string, OverlayBounds>)typeof(OverlayWindow)
                    .GetField("_requestedPlacements", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                OverlayBounds full = OverlayWindowInterop.RecoverToDisplay(requested[OverlayComponentKeys.AvanteCluster]);
                AssertEqual(full, Bounds()); // the intro starts on the first gameplay frame
                double introSeconds = (double)typeof(AvanteClusterView).Assembly.GetType("AMS2LeagueClient.Presentation.AvanteIgnitionSweep")!
                    .GetField("DurationSeconds", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
                RunDispatcherFor(TimeSpan.FromSeconds(introSeconds + .4));
                overlay.ShowAt(game);
                PumpDispatcher();
                OverlayBounds rest = Bounds();
                AssertTrue(rest.Width * rest.Height < full.Width * full.Height);
                AssertTrue(overlay.BeginLayoutEdit());
                PumpDispatcher();
                AssertEqual(full.Width, Bounds().Width);
                AssertEqual(full.Height, Bounds().Height);
                overlay.EndLayoutEdit(false);
                Console.WriteLine($"PROOF N window full={full.Width}x{full.Height} rest={rest.Width}x{rest.Height}; edit restores full canvas");
            }
            finally { overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
