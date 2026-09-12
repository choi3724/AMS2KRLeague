using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void DrivingFramesDoNotThrottleLocalReads()
        {
            WithTemporaryDirectory(directory => {
                var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
                using var mapping = MemoryMappedFile.CreateNew("ams2-frames-" + Guid.NewGuid().ToString("N"), SharedMemoryLayout.RequiredBytes);
                using var writer = mapping.CreateViewAccessor();
                writer.WriteArray(0, fixture.Buffer, 0, SharedMemoryLayout.RequiredBytes);
                using var logger = new FileLogger(directory);
                var overlay = new OverlayWindow(false, Path.Combine(directory, "layout.json"));
                var coordinator = new PlayerOverlayCoordinator(overlay, new ClientStatusViewModel(), logger, false);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(PlayerOverlayCoordinator);
                var reader = (SharedMemoryReader)type.GetField("_reader", flags)!.GetValue(coordinator)!;
                // Use a private fixture view; never open or modify the real game mapping.
                typeof(SharedMemoryReader).GetField("_view", flags)!.SetValue(reader, mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read));
                try
                {
                    overlay.SetComponentEnabled(OverlayComponentKeys.Speed, true);
                    overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                    overlay.ShowDemoAt(-5000, -5000, 96); PumpDispatcher();
                    AssertTrue(overlay.WantsDrivingTelemetry);
                    type.GetField("_processId", flags)!.SetValue(coordinator, 1);
                    type.GetField("_drivingLocalIndex", flags)!.SetValue(coordinator, 3);
                    var frame = type.GetMethod("DrivingFrame", flags)!;
                    var updates = type.GetField("_drivingUpdateCount", flags)!;
                    RenderingEventArgs Frame(double seconds) => (RenderingEventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), flags, null, new object[] { TimeSpan.FromSeconds(seconds) }, null)!;
                    const int count = 144;
                    for (int i = 1; i <= count; i++)
                    {
                        type.GetField("_latest", flags)!.SetValue(coordinator, Parse(fixture, DateTimeOffset.UtcNow));
                        var args = new object?[] { null, Frame(i / 144.0) };
                        frame.Invoke(coordinator, args);
                        frame.Invoke(coordinator, args); // WPF can repeat an event for the same frame.
                    }
                    AssertEqual(count, (int)updates.GetValue(coordinator)!);
                    AssertEqual(0L, reader.SuccessfulSnapshots);
                    AssertEqual(0, (int)type.GetField("_successCount", flags)!.GetValue(coordinator)!);
                    overlay.HideOverlay();
                    AssertFalse(overlay.WantsDrivingTelemetry);
                    frame.Invoke(coordinator, new object?[] { null, Frame(2) });
                    AssertEqual(count, (int)updates.GetValue(coordinator)!);
                    Console.WriteLine("PROOF distinct display frames=144 local reads=144 duplicate frames skipped; hidden HUD reads=0 recording reads=0 (fixture, not measured FPS)");
                }
                finally { coordinator.Dispose(); overlay.Close(); }
            });
        }

        private static void FastDrivingReadDoesNotFeedRecording()
        {
            DrivingFramesDoNotThrottleLocalReads();
            var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
            var expected = Parse(fixture);
            string name = "ams2-hud-fixture-" + Guid.NewGuid().ToString("N");
            using var mapping = MemoryMappedFile.CreateNew(name, SharedMemoryLayout.RequiredBytes);
            using var writer = mapping.CreateViewAccessor();
            writer.WriteArray(0, fixture.Buffer, 0, SharedMemoryLayout.RequiredBytes);
            using var reader = new SharedMemoryReader(name);
            AssertEqual(TelemetryReadStatus.Success, reader.TryRead().Status);
            long recordingReads = reader.SuccessfulSnapshots;
            var watch = Stopwatch.StartNew();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 6000; i++)
            {
                var sample = reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw);
                AssertNotNull(sample);
                AssertEqual("260 km/h", sample!.SpeedText); AssertEqual("4", sample.GearText);
                AssertEqual(7, sample.Generation);
            }
            Console.WriteLine("PROOF display-only reads=6000 meanUs=" + (watch.Elapsed.TotalMilliseconds * 1000 / 6000).ToString("F2")
                + " bytesPerReadWithAssertions=" + ((GC.GetAllocatedBytesForCurrentThread() - allocated) / 6000));
            AssertEqual(recordingReads, reader.SuccessfulSnapshots);
            AssertEqual(0L, reader.SequenceDrops);
            byte[] after = new byte[SharedMemoryLayout.RequiredBytes];
            writer.ReadArray(0, after, 0, after.Length);
            AssertTrue(after.SequenceEqual(fixture.Buffer.Take(after.Length)));
            AssertNull(reader.TryReadDriving(2, 7, expected.GameStateRaw, expected.SessionStateRaw));
            AssertNull(reader.TryReadDriving(64, 7, expected.GameStateRaw, expected.SessionStateRaw));
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw + 1));
            writer.Write(SharedMemoryLayout.SequenceNumber, 1U);
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw));
            writer.Write(SharedMemoryLayout.SequenceNumber, 2U);
            writer.Write(SharedMemoryLayout.Brake, float.NaN);
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw)!.Pedals[0]);
            writer.Write(SharedMemoryLayout.Version, 999U);
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw));
            reader.Reset();
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw));
            AssertEqual(recordingReads, reader.SuccessfulSnapshots);
            Console.WriteLine("PROOF high-rate display does not write SHM or increment recording counters; version/owner/session/sequence guards pass");
        }
    }
}
