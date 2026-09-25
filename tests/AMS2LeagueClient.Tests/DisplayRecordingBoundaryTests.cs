using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void CurrentDisplayDoesNotRetainGraphBacklog()
        {
            var history = new DrivingTelemetryHistory();
            var time = FixedTime();
            DrivingTelemetrySample Sample(int index, int generation = 1) => new DrivingTelemetrySample(
                time.AddMilliseconds(index * 7), generation, 0, (index % 101) / 100.0, 0, 0, 0, 30, 3, rpm: 6000, maxRpm: 8000);
            for (int i = 0; i < 10000; i++) history.Add(Sample(i), retainHistory: false);
            AssertEqual(0, history.Count);
            AssertEqual(Sample(9999).CapturedAt, history.Current!.CapturedAt);
            // Same RPM/speed must not discard a new pedal observation.
            AssertEqual(Sample(9999).Pedals[0], history.Current.Pedals[0]);
            history.Add(null, retainHistory: false);
            AssertTrue(history.IsStale);
            history.Add(Sample(10000), retainHistory: false);
            AssertFalse(history.IsStale);
            for (int i = 10001; i < 10021; i++) history.Add(Sample(i));
            AssertEqual(20, history.Count);
            AssertEqual(Sample(10001).CapturedAt, history.Samples.First().CapturedAt);
            AssertEqual(Sample(10020).CapturedAt, history.Current!.CapturedAt);
            history.Add(Sample(10021), retainHistory: false);
            AssertEqual(0, history.Count);
            history.Add(Sample(10022));
            AssertEqual(1, history.Count);
            history.Add(Sample(10023, 2));
            AssertEqual(1, history.Count);
            history.Clear(); AssertNull(history.Current); AssertEqual(0, history.Count);
        }

        private static void HudHistoryFollowsVisibleGraphOnly()
        {
            WithTemporaryDirectory(directory =>
            {
                var overlay = new OverlayWindow(false, Path.Combine(directory, "layout.json"));
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var history = (DrivingTelemetryHistory)typeof(OverlayWindow).GetField("_drivingHistory", flags)!.GetValue(overlay)!;
                var time = FixedTime(); int index = 0;
                void Send() => overlay.UpdateDrivingSample(new DrivingTelemetrySample(time.AddMilliseconds(++index * 7),
                    1, 0, .2, .8, 0, 0, index, 3, rpm: 6000, maxRpm: 8000));
                try
                {
                    foreach (string key in OverlayComponentKeys.All) overlay.SetComponentEnabled(key, false);
                    foreach (string key in new[] { OverlayComponentKeys.AvanteCluster, OverlayComponentKeys.AvanteClusterExpanded,
                        OverlayComponentKeys.Speed, OverlayComponentKeys.Gear, OverlayComponentKeys.PedalGauge, OverlayComponentKeys.DrivingDashboard })
                    {
                        overlay.SetComponentEnabled(key, true);
                        overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                        overlay.ShowDemoAt(-5000, -5000, 96); PumpDispatcher();
                        Send(); Send();
                        AssertEqual(0, history.Count);
                        AssertEqual(time.AddMilliseconds(index * 7), history.Current!.CapturedAt);
                        overlay.SetComponentEnabled(key, false);
                    }
                    overlay.SetComponentEnabled(OverlayComponentKeys.Speed, true);
                    overlay.SetComponentEnabled(OverlayComponentKeys.PedalTelemetry, true);
                    overlay.ShowDemoAt(-5000, -5000, 96); PumpDispatcher();
                    Send(); Send(); AssertEqual(2, history.Count);
                    overlay.SetComponentOpacity(OverlayComponentKeys.PedalTelemetry, 0);
                    Send(); AssertEqual(0, history.Count); AssertNotNull(history.Current);
                    overlay.SetComponentOpacity(OverlayComponentKeys.PedalTelemetry, 1);
                    PumpDispatcher(); Send(); AssertEqual(1, history.Count);
                    overlay.HideOverlay(); Send(); AssertEqual(0, history.Count);
                    overlay.ResetDrivingTelemetry(); AssertNull(history.Current);
                    Console.WriteLine("PROOF N normal/expanded, speed, gear, pedals and dashboard keep latest only; visible graph retains ordered samples; opacity/hide/reset clear history on next update");
                }
                finally { overlay.Close(); }
            });
        }

        private static void RecordingProgressesWithoutDisplayDispatcher()
        {
            WithTemporaryDirectory(directory =>
            {
                var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
                using var mapping = MemoryMappedFile.CreateNew("ams2-record-boundary-" + Guid.NewGuid().ToString("N"), SharedMemoryLayout.RequiredBytes);
                using var writer = mapping.CreateViewAccessor();
                writer.WriteArray(0, fixture.Buffer, 0, SharedMemoryLayout.RequiredBytes);
                using var logger = new FileLogger(directory);
                using var capture = new ActivityCaptureRuntime(Path.Combine(directory, "capture"), "display-record-fixture", "0.7.6", logger);
                var overlay = new OverlayWindow(false, Path.Combine(directory, "layout.json"));
                var coordinator = new PlayerOverlayCoordinator(overlay, new ClientStatusViewModel(), logger, false, capture);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(PlayerOverlayCoordinator);
                var reader = (SharedMemoryReader)type.GetField("_reader", flags)!.GetValue(coordinator)!;
                typeof(SharedMemoryReader).GetField("_view", flags)!.SetValue(reader, mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read));
                var archive = (FutureTelemetryCaptureRuntime)typeof(ActivityCaptureRuntime).GetField("_futureTelemetry", flags)!.GetValue(capture)!;
                try
                {
                    type.GetField("_processId", flags)!.SetValue(coordinator, 1);
                    var read = type.GetMethod("ReadTelemetry", flags)!;
                    // Do not pump the UI; the actual acquisition + capture path must finish independently.
                    var worker = Task.Run(() =>
                    {
                        for (int i = 0; i < 18; i++) { read.Invoke(coordinator, new object?[] { null }); Thread.Sleep(34); }
                    });
                    AssertTrue(worker.Wait(5000));
                    AssertEqual(18L, reader.SuccessfulSnapshots);
                    AssertTrue(archive.Counters.AcceptedBatches > 0);
                    AssertEqual(0L, archive.Counters.DroppedBatches);
                    AssertEqual(0, (int)type.GetField("_drivingUpdateCount", flags)!.GetValue(coordinator)!);
                    var latest = (TelemetrySnapshot)type.GetField("_latest", flags)!.GetValue(coordinator)!;
                    AssertTrue(DateTimeOffset.UtcNow - latest.CapturedAt < TimeSpan.FromSeconds(1));
                    Console.WriteLine($"PROOF UI not pumped: acquisition=18 display=0 archiveAccepted={archive.Counters.AcceptedBatches} archiveGateSkipped={archive.Counters.SkippedByTwentyHertzGate}; cadence dependency fixture, not game jitter measurement");
                    capture.Dispose();
                    AssertTrue(archive.Counters.CommittedChunks > 0);
                    AssertEqual(0L, archive.Counters.BackgroundFailures);
                    AssertEqual(0L, archive.Counters.ArchiveDroppedMessages);
                    AssertTrue(Directory.EnumerateFiles(Path.Combine(directory, "capture"), "*.a2ct.gz", SearchOption.AllDirectories).Any());
                }
                finally { coordinator.Dispose(); overlay.Close(); }
            });
        }
    }
}
