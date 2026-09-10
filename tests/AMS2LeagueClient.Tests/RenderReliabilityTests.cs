using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void BoundedLoggerDrainsAndSurvivesIo()
        {
            WithTemporaryDirectory(directory => {
                var logger = new FileLogger(directory, 2048);
                for (int i = 0; i < 512; i++) logger.Info("ORDER", "ordinal=" + i);
                logger.DisposeAsync().AsTask().GetAwaiter().GetResult();
                string[] lines = File.ReadAllLines(logger.FilePath);
                AssertEqual(512, lines.Length);
                for (int i = 0; i < 512; i++) AssertTrue(lines[i].EndsWith("ordinal=" + i, StringComparison.Ordinal));
                AssertEqual(0L, logger.DroppedLines); AssertEqual(0L, logger.WriteFailures);
                logger = new FileLogger(directory, 1);
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 20000; i++) logger.Info("BURST", new string('x', 4096));
                long producerMs = clock.ElapsedMilliseconds;
                logger.DisposeAsync().AsTask().GetAwaiter().GetResult();
                AssertEqual(20000L, logger.DroppedLines + logger.WrittenLines);
                AssertTrue(logger.DroppedLines > 0);
                Console.WriteLine("PROOF logger burst=20000 capacity=1 written=" + logger.WrittenLines + " dropped=" + logger.DroppedLines + " producerMs=" + producerMs);
                logger = new FileLogger(directory);
                using (var held = new FileStream(logger.FilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    logger.Info("IO_FAILURE", "no raw facts in this queue");
                    AssertTrue(SpinWait.SpinUntil(() => logger.WriteFailures > 0, 5000));
                }
                logger.Error("SAFE_ERROR", new IOException("must-not-be-logged"));
                logger.DisposeAsync().AsTask().GetAwaiter().GetResult();
                AssertFalse(File.ReadAllText(logger.FilePath).Contains("must-not-be-logged", StringComparison.Ordinal));
                AssertTrue(File.ReadAllText(logger.FilePath).Contains("IOException", StringComparison.Ordinal));
            });
        }

        private static void HistoryGapAndGenerationStayDistinct()
        {
            var history = new DrivingTelemetryHistory();
            DrivingTelemetrySample Sample(int ms, int generation = 1, int participant = 3) =>
                new DrivingTelemetrySample(FixedTime().AddMilliseconds(ms), generation, participant, .4, .6, 0, 0, 20, 2);
            foreach (int gap in new[] {16, 150, 300, 500})
            {
                history.Clear(); history.Add(Sample(0)); history.Add(null);
                AssertEqual(1, history.Count); AssertTrue(history.IsStale); AssertNull(history.Current);
                history.Add(Sample(gap)); AssertEqual(2, history.Count); AssertFalse(history.IsStale);
            }
            history.Add(Sample(600, 2)); AssertEqual(1, history.Count);
            history.Add(Sample(650, 2, 4)); AssertEqual(1, history.Count);
            history.Clear(); AssertEqual(0, history.Count); AssertFalse(history.IsStale);
            history.Add(Sample(0)); history.Add(Sample(50)); history.Add(Sample(400)); history.Add(Sample(450));
            MethodInfo create = typeof(PedalTelemetryView).Assembly.GetType("AMS2LeagueClient.Presentation.PedalCurveBuilder")!
                .GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!;
            var geometry = (StreamGeometry)create.Invoke(null, new object[] {history, 0, FixedTime().AddMilliseconds(450), 320d, 100d, false})!;
            // WPF flattening omits unfilled horizontal figures; inspect the
            // actual StreamGeometry commands rather than its fill geometry.
            AssertEqual(2, geometry.ToString(System.Globalization.CultureInfo.InvariantCulture).Count(value => value == 'M'));
            history.Clear();
            for (int i = 0; i < 600; i++) history.Add(new DrivingTelemetrySample(FixedTime().AddMilliseconds(i * 16),
                1, 3, i >= 240 && i < 300 ? 1 : 0, 0, 0, 0, 20, 2));
            var dense = (StreamGeometry)create.Invoke(null, new object[] {history, 0, history.Current!.CapturedAt, 4096d, 100d, false})!;
            var reduced = (StreamGeometry)create.Invoke(null, new object[] {history, 0, history.Current!.CapturedAt, 4d, 100d, false})!;
            AssertEqual(600, history.Count);
            AssertTrue(reduced.ToString().Length < dense.ToString().Length / 4);
            AssertTrue(reduced.Bounds.Top <= dense.Bounds.Top + .5 && reduced.Bounds.Bottom >= dense.Bounds.Bottom - .5);
            Console.WriteLine("PROOF stale preserves history; 350ms gap splits figures; pixel reduction retains peaks and all 600 source samples");
        }

        private static void SpeedDiagnosticsPreserveSource()
        {
            var fixture = new RawFixtureBuilder().SetViewedVehicleTelemetry();
            AssertNull(PlayerOverlayCoordinator.DescribeSpeedAnomaly(Parse(fixture), 1));
            Array.Copy(BitConverter.GetBytes(704.58075f), 0, fixture.Buffer, AMS2LeagueClient.Core.Telemetry.SharedMemoryLayout.Speeds + 4, 4);
            var sample = Parse(fixture);
            string diagnostic = PlayerOverlayCoordinator.DescribeSpeedAnomaly(sample, 7)!;
            AssertTrue(diagnostic.Contains("anomalySlot=1", StringComparison.Ordinal));
            AssertTrue(diagnostic.Contains("generation=7", StringComparison.Ordinal));
            AssertTrue(diagnostic.Contains("participantOffset=10800", StringComparison.Ordinal));
            AssertEqual(704.58075f, sample.Participants[1].SpeedMetresPerSecond);
        }

        private static void CoordinatorShutdownDoesNotBlockDispatcher()
        {
            WithTemporaryDirectory(directory => {
                using var logger = new FileLogger(directory);
                var overlay = new OverlayWindow(false, Path.Combine(directory, "layout.json"));
                var coordinator = new PlayerOverlayCoordinator(overlay, new ClientStatusViewModel(), logger, false);
                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                var timer = new Timer(_ => { entered.Set(); release.Wait(); }, null, 0, Timeout.Infinite);
                typeof(PlayerOverlayCoordinator).GetField("_telemetryTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coordinator, timer);
                try
                {
                    AssertTrue(entered.Wait(5000));
                    Task drain = coordinator.DisposeAsync().AsTask();
                    AssertFalse(drain.IsCompleted);
                    bool uiRan = false;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => uiRan = true));
                    PumpDispatcher(); AssertTrue(uiRan); AssertFalse(drain.IsCompleted);
                    release.Set(); AssertTrue(drain.Wait(5000));
                    Console.WriteLine("PROOF shutdown dispatcher runs while callback drains; durable shutdown waits for callback release");
                }
                finally { release.Set(); coordinator.Dispose(); overlay.Close(); }
            });
        }
    }
}
