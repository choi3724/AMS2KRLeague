using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Tests;

internal static class RenderBench
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); Application.Current?.Shutdown(1); return 1; }
    }
    private static int Run(string[] args)
    {
        string output = Path.GetFullPath(args[0]);
        bool vr = args.Contains("--vr");
        const double warmup = 5, duration = 30;
        string scratch = Path.Combine(Path.GetDirectoryName(output)!, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var app = new AMS2LeagueClient.App(startRuntime: false);
        app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow(false, Path.Combine(scratch, "layout.json"));
        overlay.SaveDrivingHudSettings(new DrivingHudSettings { TelemetryDesign = "racing", TowerDesign = "racing" });
        foreach (var key in typeof(OverlayComponentKeys).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(string)))
        {
            string name = (string)key.GetValue(null)!;
            overlay.SetComponentEnabled(name, name == OverlayComponentKeys.TimingTower || name == OverlayComponentKeys.PedalTelemetry
                || name == OverlayComponentKeys.PedalGauge || name == OverlayComponentKeys.DrivingDashboard);
        }
        var fixture = new RawFixtureBuilder(32);
        var snapshot = new SharedMemoryParser().Parse(fixture.Buffer, DateTimeOffset.UtcNow).Snapshot!;
        var local = snapshot.Participants[3];
        var league = new LeagueClassificationResolver().Resolve(snapshot, local);
        var timing = OverlayViewModel.Build(snapshot, local, league, 30, 60, false, "BENCH_FIXTURE");
        overlay.SetViewModel(OverlayShellViewModel.Build(snapshot, timing, null, false), false);
        overlay.ShowDemoAt(-20000, -20000, 96); // No game, no focus, no headset.
        var log = new FileLogger(Path.Combine(scratch, "logs"));
        var stop = new CancellationTokenSource();
        DrivingTelemetrySample? latest = null;
        int produced = 0;
        var producer = Task.Run(async () => {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
                while (await timer.WaitForNextTickAsync(stop.Token))
                {
                    int n = Interlocked.Increment(ref produced);
                    double t = n / 100.0;
                    Volatile.Write(ref latest, new DrivingTelemetrySample(DateTimeOffset.UtcNow, 1, 3,
                        (1 + Math.Sin(t * 3)) / 2, (1 + Math.Cos(t * 2)) / 2, .1, 0, 55 + Math.Sin(t) * 15,
                        1 + (n / 90) % 6, n % 90 < 15, Math.Sin(t), 5000 + Math.Sin(t * 2) * 2500, 8000));
                    log.Info("BENCH_BACKGROUND", "sequence=" + n);
                }
            }
            catch (OperationCanceledException) { }
        });
        var intervals = new List<double>(); var ui = new List<double>(); var age = new List<double>();
        var capture = new List<double>(); var compose = new List<double>(); var render = new List<double>();
        var copy = new List<double>(); var conversion = new List<double>();
        var clock = Stopwatch.StartNew(); double last = 0, lastVr = 0;
        long allocated = 0; TimeSpan pause = default; int[] gc = new int[3]; int callbacks = 0, consumed = 0, initialProduced = 0;
        bool measuring = false; DrivingTelemetrySample? previous = null;
        EventHandler rendering = (_, __) => { if (measuring) callbacks++; };
        CompositionTarget.Rendering += rendering;
        var dispatcher = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0 / 60) };
        dispatcher.Tick += (_, __) => {
            double now = clock.Elapsed.TotalSeconds;
            if (!measuring && now >= warmup)
            {
                measuring = true; allocated = GC.GetTotalAllocatedBytes(false); pause = GC.GetTotalPauseDuration();
                for (int i = 0; i < 3; i++) gc[i] = GC.CollectionCount(i);
                initialProduced = produced; last = now;
            }
            if (measuring && now >= warmup + duration)
            {
                measuring = false; dispatcher.Stop(); stop.Cancel();
                var report = new {
                    mode = vr ? "OFFSCREEN_VR_CAPTURE_NO_HEADSET" : "OFFSCREEN_DESKTOP", participants = 32,
                    panels = "racing tower, racing telemetry, pedals, dashboard; legacy/speed/gear hidden",
                    warmupSeconds = warmup, durationSeconds = duration, sourceTargetHz = 100, drivingTargetHz = 60, vrTargetHz = vr ? 15 : 0,
                    os = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(), cpuCount = Environment.ProcessorCount,
                    renderingTier = RenderCapability.Tier >> 16, display = "1920x1080, 96dpi, offscreen; no presentation FPS measured",
                    dispatcherIntervalMs = Stats(intervals), dispatcherWorkMs = Stats(ui), syntheticSourceAgeMs = Stats(age),
                    intervalsOver16_7 = intervals.Count(v => v > 16.7), intervalsOver33_3 = intervals.Count(v => v > 33.3),
                    allocationBytes = GC.GetTotalAllocatedBytes(false) - allocated,
                    gcCounts = Enumerable.Range(0,3).Select(i => GC.CollectionCount(i) - gc[i]).ToArray(),
                    gcPauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds, renderingCallbacks = callbacks,
                    syntheticProduced = produced - initialProduced, syntheticConsumed = consumed,
                    syntheticCoalesced = produced - initialProduced - consumed,
                    realShmSkip = "NOT_RUN", realShmAge = "NOT_RUN", vrSubmission = "NOT_RUN_NO_HEADSET",
                    vrCaptureMs = Stats(capture), vrComposeMs = Stats(compose), vrRenderMs = Stats(render),
                    vrCopyMs = Stats(copy), vrConvertMs = Stats(conversion)
                };
                File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(output);
                overlay.Close(); app.Shutdown(); return;
            }
            long begin = Stopwatch.GetTimestamp();
            if (measuring) { intervals.Add((now - last) * 1000); last = now; }
            var sample = Volatile.Read(ref latest);
            if (sample != null && !ReferenceEquals(previous, sample))
            {
                overlay.UpdateDrivingSample(sample); previous = sample;
                if (measuring) { consumed++; age.Add((DateTimeOffset.UtcNow - sample.CapturedAt).TotalMilliseconds); }
                log.Info("BENCH_DISPATCHER", "sample=" + sample.CapturedAt.ToUnixTimeMilliseconds());
            }
            if (vr && now - lastVr >= 1.0 / 15)
            {
                lastVr = now; long start = Stopwatch.GetTimestamp(); var frame = overlay.CaptureVrFrame();
                if (measuring && frame != null)
                {
                    capture.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); compose.Add(overlay.LastVrComposeMs);
                    render.Add(overlay.LastVrRenderMs); copy.Add(overlay.LastVrCopyMs); conversion.Add(overlay.LastVrConvertMs);
                }
            }
            if (measuring) ui.Add(Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
        };
        dispatcher.Start(); app.Run();
        stop.Cancel(); producer.GetAwaiter().GetResult();
        if ((object)log is IDisposable disposable) disposable.Dispose();
        CompositionTarget.Rendering -= rendering;
        return 0;
    }
    private static object Stats(List<double> values)
    {
        values.Sort(); double At(double percentile) => values.Count == 0 ? 0 : values[(int)Math.Ceiling((values.Count - 1) * percentile)];
        return new { n = values.Count, p50 = At(.5), p95 = At(.95), p99 = At(.99), max = At(1) };
    }
}
