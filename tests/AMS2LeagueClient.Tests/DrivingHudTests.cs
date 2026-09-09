using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void DrivingGraphScrollsLeft()
        {
            var history = new DrivingTelemetryHistory();
            DateTimeOffset now = FixedTime();
            for (int i = 0; i <= 200; i++)
                history.Add(new DrivingTelemetrySample(now.AddSeconds((i - 200) * 0.05), 1, 3,
                    Math.Max(0, 1 - Math.Abs(i - 170) / 10.0), 0.3, 0.1, 0, 30, 2));
            var view = new PedalTelemetryView { Width = 560, Height = 160 };
            var host = new Window { Content = view, Width = 580, Height = 200, Left = -5000, Top = -5000, ShowActivated = false };
            try
            {
                host.Show(); PumpDispatcher(); view.SetHistory(history); PumpDispatcher();
                FrameworkElement graph = Descendants<FrameworkElement>(view).Single(element => element.GetType().Name == "PedalGraph");
                double PeakX()
                {
                    graph.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)graph.ActualWidth, (int)graph.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    // Render at the graph's own origin, excluding the parent Grid row offset.
                    var visual = new DrawingVisual();
                    using (DrawingContext drawing = visual.RenderOpen())
                        drawing.DrawRectangle(new VisualBrush(graph) { ViewboxUnits = BrushMappingMode.Absolute,
                            Viewbox = new Rect(graph.RenderSize), Stretch = Stretch.Fill }, null, new Rect(graph.RenderSize));
                    bitmap.Render(visual);
                    byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    var points = new System.Collections.Generic.List<(int X, int Y)>();
                    for (int y = 0; y < bitmap.PixelHeight; y++)
                        for (int x = 1; x < graph.ActualWidth - 160; x++)
                        {
                            int offset = (y * bitmap.PixelWidth + x) * 4;
                            if (pixels[offset + 2] > 180 && pixels[offset + 1] < 100 && pixels[offset] < 100) points.Add((x, y));
                        }
                    if (points.Count == 0 && _layoutCaptureDirectory != null)
                    {
                        Directory.CreateDirectory(_layoutCaptureDirectory);
                        var debug = new PngBitmapEncoder(); debug.Frames.Add(BitmapFrame.Create(bitmap));
                        using var debugFile = File.Create(Path.Combine(_layoutCaptureDirectory, "graph-pixel-debug.png")); debug.Save(debugFile);
                    }
                    AssertTrue(points.Count > 0);
                    int peakY = points.Min(point => point.Y);
                    return points.Where(point => point.Y <= peakY + 5).Average(point => point.X);
                }
                AssertEqual("텔레메트리", Descendants<TextBlock>(view).Single().Text);
                var buildCurve = graph.GetType().GetMethod("CreateCurve", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                var noisy = new DrivingTelemetryHistory();
                double rawVariation = 0, lastRaw = 0;
                for (int i = 0; i <= 200; i++)
                {
                    double value = i < 30 || i > 170 ? 0 : i % 2 == 0 ? 0.9 : 0.1;
                    rawVariation += Math.Abs(value - lastRaw); lastRaw = value;
                    noisy.Add(new DrivingTelemetrySample(now.AddSeconds((i - 200) * 0.05), 1, 3, value, 1 - value, 0, 0, 30, 2));
                }
                var curve = (StreamGeometry)buildCurve.Invoke(null, new object[] { noisy, 0, now, 380.0, 100.0 })!;
                // WPF flattening drops unfilled figures. Mark only the measurement copy as filled.
                PathGeometry measured = PathGeometry.CreateFromGeometry(curve).Clone();
                foreach (PathFigure figure in measured.Figures) figure.IsFilled = true;
                PathGeometry flat = measured.GetFlattenedPathGeometry(0.01, ToleranceType.Absolute);
                double variation = 0;
                foreach (PathFigure figure in flat.Figures)
                {
                    double lastY = figure.StartPoint.Y;
                    foreach (PathSegment segment in figure.Segments)
                        foreach (Point point in segment is PolyLineSegment poly ? poly.Points.AsEnumerable() : new[] { ((LineSegment)segment).Point })
                        { variation += Math.Abs(point.Y - lastY) / 100; lastY = point.Y; }
                }
                Console.WriteLine("PROOF smoothing-measure variation=" + variation + " raw=" + rawVariation + " figures=" + flat.Figures.Count + " bounds=" + curve.Bounds);
                view.SetHistory(noisy); PumpDispatcher(); CaptureLayout(view, "driving-smoothed-noisy-input");
                AssertTrue(curve.Bounds.Top >= 20 && curve.Bounds.Bottom <= 120.001);
                AssertTrue(variation > 0 && variation < rawVariation * 0.4);
                AssertEqual(0.0, noisy.Current!.Pedals[0]!.Value);
                view.SetHistory(noisy); PumpDispatcher(); CaptureLayout(view, "driving-smoothed-noisy-input");
                Console.WriteLine("PROOF graph-display variation=" + variation.ToString("F1") + " raw=" + rawVariation.ToString("F1") + "; samples unchanged");
                noisy.Add(new DrivingTelemetrySample(now.AddSeconds(0.05), 1, 3, double.NaN, 0, 0, 0, 30, 2));
                noisy.Add(new DrivingTelemetrySample(now.AddSeconds(0.1), 1, 3, 0.5, 0, 0, 0, 30, 2));
                curve = (StreamGeometry)buildCurve.Invoke(null, new object[] { noisy, 0, now.AddSeconds(0.1), 380.0, 100.0 })!;
                AssertEqual(2, PathGeometry.CreateFromGeometry(curve).Figures.Count);
                view.SetHistory(history); PumpDispatcher();
                double before = PeakX();
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
                double after = PeakX();
                AssertTrue(before - after > 8 && before - after < 30);
                AssertEqual(201, history.Count);
                Console.WriteLine("PROOF graph-left-scroll peakX=" + before.ToString("F1") + " -> " + after.ToString("F1") + " without changing input values");

                // Render actual WPF preview frames so motion can be reviewed outside the game.
                string layout = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                var overlay = new OverlayWindow(false, layout);
                try
                {
                    overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                    overlay.ShowDemoAt(-5000, -5000, 96); AssertTrue(overlay.BeginLayoutEdit());
                    Window panel = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "AMS2 텔레메트리");
                    if (_layoutCaptureDirectory != null)
                        for (int i = 0; i < 30; i++) CaptureLayout((FrameworkElement)panel.Content, "driving-motion-" + i.ToString("D2"), 100);
                    overlay.EndLayoutEdit(false);
                }
                finally { overlay.Close(); if (File.Exists(layout)) File.Delete(layout); }
            }
            finally { host.Close(); }
        }

        private static void DrivingTelemetrySourcesAndHistory()
        {
            var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
            var snapshot = Parse(fixture, FixedTime());
            DrivingTelemetrySample sample = DrivingTelemetrySample.FromSnapshot(snapshot, 3, 1)!;
            AssertEqual("260 km/h", sample.SpeedText); AssertEqual("4", sample.GearText);
            AssertTrue(Math.Abs(sample.Pedals[0]!.Value - 0.24) < 0.00001);
            AssertTrue(Math.Abs(sample.Pedals[1]!.Value - 0.69) < 0.00001);
            AssertTrue(Math.Abs(sample.Pedals[2]!.Value - 0.41) < 0.00001);
            AssertTrue(Math.Abs(sample.Pedals[3]!.Value - 0.05) < 0.00001);
            AssertTrue(DrivingTelemetrySample.FromSnapshot(snapshot, 2, 1) == null);
            var invalid = new DrivingTelemetrySample(FixedTime(), 1, 3, double.NaN, -1, 1.1, double.PositiveInfinity, -1, 99);
            AssertTrue(invalid.Pedals.All(value => !value.HasValue));
            AssertEqual("— km/h", invalid.SpeedText); AssertEqual("—", invalid.GearText);
            AssertEqual("-1", new DrivingTelemetrySample(FixedTime(), 1, 3, 0, 0, 0, 0, 0, -1).GearText);
            AssertEqual("0", new DrivingTelemetrySample(FixedTime(), 1, 3, 0, 0, 0, 0, 0, 0).GearText);
            var history = new DrivingTelemetryHistory();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
                history.Add(new DrivingTelemetrySample(FixedTime().AddSeconds(i * 0.05), 1, 3, 0, 1, 0, 0, 80, 4));
            AssertEqual(201, history.Count);
            history.Add(history.Current); AssertEqual(201, history.Count);
            DateTimeOffset last = history.Current!.CapturedAt;
            history.Add(new DrivingTelemetrySample(last.AddMilliseconds(50), 2, 3, 0, 0, 0, 0, 0, 0));
            AssertEqual(1, history.Count);
            history.Add(new DrivingTelemetrySample(last.AddMilliseconds(100), 2, 4, 0, 0, 0, 0, 0, 0)); AssertEqual(1, history.Count);
            history.Add(new DrivingTelemetrySample(last.AddSeconds(5), 2, 4, 0, 0, 0, 0, 0, 0)); AssertEqual(1, history.Count);
            history.Add(new DrivingTelemetrySample(last, 2, 4, 0, 0, 0, 0, 0, 0)); AssertEqual(1, history.Count);
            for (int i = 1; i <= 1000; i++)
                history.Add(new DrivingTelemetrySample(last.AddMilliseconds(i), 2, 4, 0, 0, 0, 0, 0, 0));
            AssertEqual(DrivingTelemetryHistory.MaximumSamples, history.Count);
            history.Add(null); AssertEqual(0, history.Count); AssertTrue(history.Current == null);
            Console.WriteLine("PROOF driving-history 10000 samples, 20Hz window=201, hardLimit=256, elapsedMs=" + watch.ElapsedMilliseconds);
            var bad = new DrivingHudSettings { BrakeColor = "#xyzxyz", ClutchColor = "#123abc", SpeedFont = "https://remote/font", GearFont = "", SpeedShadowColor = "invalid", GearShadowColor = "#aabbcc" }.Normalize();
            AssertEqual("#FF3030", bad.BrakeColor); AssertEqual("#123ABC", bad.ClutchColor);
            AssertEqual("Pretendard", bad.SpeedFont); AssertEqual("Pretendard", bad.GearFont);
            AssertEqual("#000000", bad.SpeedShadowColor); AssertEqual("#AABBCC", bad.GearShadowColor);
            AssertEqual("#000000", JsonSerializer.Deserialize<DrivingHudSettings>("{}")!.Normalize().GearShadowColor);
        }

        private static void DrivingHudRenderingAndSettings()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-driving-ui-" + Guid.NewGuid() + ".json");
            var window = new OverlayWindow(false, path);
            try
            {
                window.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                window.ShowDemoAt(-5000, -5000, 96);
                Window Panel(string title) => Application.Current.Windows.Cast<Window>().Single(w => w.Title == title);
                Window pedals = Panel("AMS2 텔레메트리"), speed = Panel("AMS2 속도계"), gear = Panel("AMS2 기어");
                var speedView = FindDescendant<DrivingNumberView>(speed)!;
                var gearView = FindDescendant<DrivingNumberView>(gear)!;
                AssertEqual("— km/h", speedView.ValueText.Text);
                AssertTrue(window.BeginLayoutEdit()); PumpDispatcher();
                AssertEqual("123 km/h", speedView.ValueText.Text); AssertEqual("3", gearView.ValueText.Text);
                var patchFont = new Typeface(speedView.ValueText.FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
                AssertTrue(patchFont.TryGetGlyphTypeface(out GlyphTypeface bundled));
                AssertTrue(bundled.FontUri.ToString().Contains("Pretendard-Medium.otf", StringComparison.OrdinalIgnoreCase));
                foreach (char value in "기어0123456789ABCH") AssertTrue(bundled.CharacterToGlyphMap.ContainsKey(value));
                var pedalView = Descendants<PedalTelemetryView>(pedals).Single();
                AssertFalse(Descendants<TextBlock>(pedalView).Any(text => new[] { "브레이크", "악셀", "클러치", "핸드브레이크" }.Contains(text.Text)));
                Console.WriteLine("PROOF bundled-patch-font Pretendard Medium; Korean/numeric/bar glyphs present");
                foreach (Window panel in new[] { pedals, speed, gear })
                {
                    AssertTrue(panel.IsVisible);
                    var root = (Grid)panel.Content;
                    AssertTrue(root.InputHitTest(new Point(12, panel.ActualHeight / 2)) is Border);
                    AssertFalse(OverlayWindowInterop.ReadStyleState(new WindowInteropHelper(panel).Handle).ClickThrough);
                    CaptureLayout(root, "driving-preview-" + panel.Title);
                    double oldWidth = panel.ActualWidth, oldHeight = panel.ActualHeight;
                    panel.Left += 40; panel.Top += 30; panel.Width *= 1.2; panel.Height *= 1.25;
                    PumpDispatcher();
                    AssertTrue(panel.ActualWidth > oldWidth && panel.ActualHeight > oldHeight);
                    Console.WriteLine("PROOF driving-resize " + panel.Title + " " + panel.ActualWidth + "x" + panel.ActualHeight);
                }
                window.EndLayoutEdit(true); PumpDispatcher();
                AssertEqual("— km/h", speedView.ValueText.Text); AssertEqual("—", gearView.ValueText.Text);
                var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
                for (int i = 0; i <= 200; i++)
                {
                    float pedal = (float)(0.5 + Math.Sin(i * 0.07) * 0.5);
                    Array.Copy(BitConverter.GetBytes(pedal), 0, fixture.Buffer, SharedMemoryLayout.Brake, 4);
                    Array.Copy(BitConverter.GetBytes(1 - pedal), 0, fixture.Buffer, SharedMemoryLayout.Throttle, 4);
                    window.UpdateDrivingTelemetry(Parse(fixture, FixedTime().AddSeconds(i * 0.05)), 3, 1);
                }
                PumpDispatcher();
                AssertEqual("260 km/h", speedView.ValueText.Text); AssertEqual("4", gearView.ValueText.Text);
                foreach (Window panel in new[] { pedals, speed, gear })
                {
                    AssertTrue(OverlayWindowInterop.ReadStyleState(new WindowInteropHelper(panel).Handle).ClickThrough);
                    CaptureLayout((FrameworkElement)panel.Content, "driving-live-" + panel.Title);
                }
                var settings = new DrivingHudSettings { BrakeColor = "#FFAA00", ThrottleColor = "#00CCFF",
                    ClutchColor = "#FFFFFF", HandBrakeColor = "#FF55AA", SpeedFont = "Consolas", GearFont = "Arial",
                    SpeedShadowColor = "#00AAFF", GearShadowColor = "#FF0066" };
                window.SaveDrivingHudSettings(settings); PumpDispatcher();
                AssertEqual("Consolas", speedView.ValueText.FontFamily.Source);
                AssertEqual("Arial", gearView.ValueText.FontFamily.Source);
                foreach (var pair in new[] { (speedView, "#00AAFF"), (gearView, "#FF0066") })
                {
                    foreach (TextBlock text in Descendants<TextBlock>(pair.Item1))
                    {
                        var shadow = text.Effect as System.Windows.Media.Effects.DropShadowEffect;
                        AssertNotNull(shadow);
                        AssertEqual((Color)ColorConverter.ConvertFromString(pair.Item2), shadow!.Color);
                        AssertTrue(shadow.IsFrozen && shadow.BlurRadius > 0 && shadow.Opacity > 0);
                    }
                }
                foreach (Window panel in new[] { pedals, speed, gear }) CaptureLayout((FrameworkElement)panel.Content, "driving-custom-" + panel.Title);
                foreach (TextBlock text in new[] { speedView.ValueText, gearView.ValueText })
                {
                    var root = (FrameworkElement)(text == speedView.ValueText ? speed.Content : gear.Content);
                    Rect bounds = text.TransformToAncestor(root).TransformBounds(new Rect(text.RenderSize));
                    AssertTrue(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1);
                }
                window.SetComponentEnabled(OverlayComponentKeys.Speed, false); AssertFalse(speed.IsVisible); AssertTrue(gear.IsVisible);
                window.UpdateDrivingTelemetry(Parse(fixture.SetViewedIndex(2), FixedTime().AddSeconds(11)), 3, 1);
                AssertEqual("—", gearView.ValueText.Text);
                using (JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path)))
                    foreach (string key in new[] { OverlayComponentKeys.PedalTelemetry, OverlayComponentKeys.Speed, OverlayComponentKeys.Gear })
                        AssertTrue(saved.RootElement.GetProperty("components").TryGetProperty(key, out _));
                window.Close();
                window = new OverlayWindow(false, path);
                AssertEqual("#FFAA00", window.GetDrivingHudSettings().BrakeColor);
                AssertEqual("Consolas", window.GetDrivingHudSettings().SpeedFont);
                AssertEqual("#00AAFF", window.GetDrivingHudSettings().SpeedShadowColor);
                AssertEqual("#FF0066", window.GetDrivingHudSettings().GearShadowColor);
                AssertFalse(window.IsComponentEnabled(OverlayComponentKeys.Speed));
                window.ResetLayout();
                AssertEqual("Consolas", window.GetDrivingHudSettings().SpeedFont);
                AssertEqual("#FFAA00", window.GetDrivingHudSettings().BrakeColor);

                AssertEqual("#00AAFF", window.GetDrivingHudSettings().SpeedShadowColor);
                AssertEqual("#FF0066", window.GetDrivingHudSettings().GearShadowColor);
                var dialog = new DrivingHudSettingsWindow(settings);
                dialog.ShowActivated = false; dialog.Left = -5000; dialog.Top = -5000;
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Show(); PumpDispatcher();
                AssertEqual(2, Descendants<ComboBox>(dialog).Count());
                AssertEqual(6, Descendants<Button>(dialog).Count(button => button.Tag is string));
                CaptureLayout((FrameworkElement)dialog.Content, "driving-settings-menu");
                dialog.Close();
                var status = new ClientStatusWindow(new ClientStatusViewModel()) { Width = 860, Height = 820 };
                status.ShowActivated = false; status.WindowStartupLocation = WindowStartupLocation.Manual;
                status.Left = -5000; status.Top = -5000; status.Show(); PumpDispatcher();
                AssertEqual(10, status.GetLayoutComponentStates().Count);
                CaptureLayout((FrameworkElement)status.Content, "driving-status-menu-minimum");
                status.Close();
            }
            finally { window.EndLayoutEdit(false); window.Close(); if (File.Exists(path)) File.Delete(path); }
        }

        private static void UpdateHelperConfirmsRestart()
        {
            foreach (string[] arguments in new[] { new[] { "--after-update" }, new[] { "--after-update", "--background" }, new[] { "--background", "--after-update" } })
            {
                var policy = ClientStartupPolicy.FromArguments(arguments);
                AssertTrue(policy.ShowStatusWindow); AssertFalse(policy.ShowStatusWindowActivated);
            }
            string directory = Path.Combine(Path.GetTempPath(), "ams2-restart-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string source = Path.Combine(directory, "fixture.cs"), executable = Path.Combine(directory, "AMS2LeagueClient.exe");
                File.WriteAllText(source, @"using System; using System.IO; using System.Reflection; using System.Threading;
[assembly: AssemblyInformationalVersion(""0.4.4"")]
class Probe { static int Main(string[] args) {
 if (args.Length > 0 && args[0] == ""--restart"") { File.WriteAllText(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, "".started""), ""started""); Thread.Sleep(4500); }
 return 0;
} }");
                var compiler = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe")) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string arg in new[] { "/nologo", "/target:exe", "/out:" + executable, source }) compiler.ArgumentList.Add(arg);
                using (Process compile = Process.Start(compiler)!) { AssertTrue(compile.WaitForExit(15000)); AssertEqual(0, compile.ExitCode); }
                string script = Path.Combine(directory, "apply.ps1");
                using (Stream resource = typeof(GitHubAutoUpdater).Assembly.GetManifestResourceStream("AMS2LeagueClient.ApplyUpdate.ps1")!)
                using (var reader = new StreamReader(resource)) File.WriteAllText(script, reader.ReadToEnd(), new UTF8Encoding(true));
                foreach (bool earlyExit in new[] { false, true })
                {
                    string installer = Path.Combine(directory, "Setup.exe"), result = Path.Combine(directory, "result.json"), config = Path.Combine(directory, "update.json");
                    File.Copy(executable, installer, true);
                    var parentStart = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
                    foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" }) parentStart.ArgumentList.Add(arg);
                    using Process parent = Process.Start(parentStart)!;
                    File.WriteAllText(config, JsonSerializer.Serialize(new { ParentId = parent.Id, ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks.ToString(),
                        Installer = installer, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))), Size = new FileInfo(installer).Length,
                        Version = "0.4.4", InstallDirectory = directory, Executable = executable, RestartArguments = earlyExit ? "--exit" : "--restart", ResultPath = result }), new UTF8Encoding(true));
                    File.Delete(Path.Combine(directory, "ready"));
                    var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
                    foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-SettingsPath", config }) start.ArgumentList.Add(arg);
                    using Process helper = Process.Start(start)!;
                    try
                    {
                        var wait = Stopwatch.StartNew();
                        while (!File.Exists(Path.Combine(directory, "ready")) && !helper.HasExited && wait.ElapsedMilliseconds < 10000) Thread.Sleep(50);
                        AssertTrue(File.Exists(Path.Combine(directory, "ready")));
                        parent.Kill(); parent.WaitForExit();
                        AssertTrue(helper.WaitForExit(15000));
                        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(result));
                        AssertEqual(!earlyExit, report.RootElement.GetProperty("success").GetBoolean());
                        AssertEqual(!earlyExit, report.RootElement.GetProperty("restarted").GetBoolean());
                        AssertEqual(earlyExit ? 1 : 0, helper.ExitCode);
                        if (earlyExit) AssertTrue(File.Exists(Path.Combine(directory, "restart-failure.log")));
                        else AssertTrue(File.Exists(Path.ChangeExtension(executable, ".started")));
                        Console.WriteLine("PROOF update-restart earlyExit=" + earlyExit + " success=" + !earlyExit);
                        foreach (Process probe in Process.GetProcessesByName("AMS2LeagueClient"))
                            using (probe)
                                if (string.Equals(probe.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                                    AssertTrue(probe.WaitForExit(10000));
                    }
                    finally { if (!parent.HasExited) parent.Kill(); if (!helper.HasExited) helper.Kill(); }
                }
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
