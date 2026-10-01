using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void CpuRenderingIsDefaultWithHardwareRecovery()
        {
            AssertTrue(ClientStartupPolicy.FromArguments(Array.Empty<string>()).UseSoftwareRendering);
            AssertTrue(ClientStartupPolicy.FromArguments(new[] { "--monitor-layered" }).UseSoftwareRendering);
            ClientStartupPolicy hardware = ClientStartupPolicy.FromArguments(new[] { "--MONITOR-HARDWARE" });
            AssertTrue(hardware.HardwareRequested);
            AssertFalse(hardware.UseSoftwareRendering);
            AssertFalse(hardware.UseGlass);
            AssertFalse(ClientStartupPolicy.FromArguments(new[] { "--monitor-layered", "--monitor-hardware" }).UseSoftwareRendering);
            // Glass and the DComp N remain GPU presentation paths.
            AssertFalse(ClientStartupPolicy.FromArguments(new[] { "--monitor-glass" }).UseSoftwareRendering);
            AssertFalse(ClientStartupPolicy.FromArguments(new[] { "--monitor-retained-n" }).UseSoftwareRendering);
            AssertEqual(System.Windows.Interop.RenderMode.SoftwareOnly, OverlayWindowInterop.MonitorRenderMode(false, true));
            AssertEqual(System.Windows.Interop.RenderMode.Default, OverlayWindowInterop.MonitorRenderMode(false, false));
            AssertEqual(System.Windows.Interop.RenderMode.Default, OverlayWindowInterop.MonitorRenderMode(true, true));
            if (OverlayWindowInterop.IsGlassAvailable())
            {
                string path = Path.Combine(Path.GetTempPath(), "ams2-cpu-glass-" + Guid.NewGuid().ToString("N") + ".json");
                var glass = new OverlayWindow(false, path, useGlass: true, useSoftwareRendering: true);
                try { AssertTrue(glass.UsesGlass); AssertFalse(glass.UsesSoftwareRendering); }
                finally { glass.Close(); if (File.Exists(path)) File.Delete(path); }
            }
        }

        private static void DrivingBlockParseMatchesLiveGuards()
        {
            var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
            var expected = Parse(fixture);
            byte[] block = fixture.Buffer.Take(SharedMemoryReader.DrivingBlockBytes).ToArray();
            var at = DateTimeOffset.UtcNow;
            var sample = SharedMemoryReader.ParseDrivingBlock(block, 3, 7, expected.GameStateRaw, expected.SessionStateRaw, at);
            AssertNotNull(sample);
            AssertEqual("260 km/h", sample!.SpeedText); AssertEqual("4", sample.GearText);
            AssertEqual(7, sample.Generation); AssertEqual(at, sample.CapturedAt);
            AssertNull(SharedMemoryReader.ParseDrivingBlock(block, 2, 7, expected.GameStateRaw, expected.SessionStateRaw, at));
            AssertNull(SharedMemoryReader.ParseDrivingBlock(block, 3, 7, expected.GameStateRaw + 1, expected.SessionStateRaw, at));
            AssertNull(SharedMemoryReader.ParseDrivingBlock(block, 3, 7, expected.GameStateRaw, expected.SessionStateRaw + 1, at));
            AssertNull(SharedMemoryReader.ParseDrivingBlock(block.Take(block.Length - 1).ToArray(), 3, 7, expected.GameStateRaw, expected.SessionStateRaw, at));
            block[SharedMemoryLayout.ParticipantOffset(3)] = 0;
            AssertNull(SharedMemoryReader.ParseDrivingBlock(block, 3, 7, expected.GameStateRaw, expected.SessionStateRaw, at));
            Console.WriteLine("PROOF one-copy display read keeps version/state/viewed/active guards");
        }

        // A late 30 Hz snapshot (thread starved by the game) must not freeze the HUD; the display
        // read validates live SHM itself. A snapshot beyond the bounded window still stops reads.
        private static void DrivingReadToleratesLateSnapshot()
        {
            WithTemporaryDirectory(directory => {
                var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
                using var mapping = MemoryMappedFile.CreateNew("ams2-late-" + Guid.NewGuid().ToString("N"), SharedMemoryLayout.RequiredBytes);
                using var writer = mapping.CreateViewAccessor();
                writer.WriteArray(0, fixture.Buffer, 0, SharedMemoryLayout.RequiredBytes);
                using var logger = new FileLogger(directory);
                var overlay = new OverlayWindow(false, Path.Combine(directory, "layout.json"));
                var coordinator = new PlayerOverlayCoordinator(overlay, new ClientStatusViewModel(), logger, false);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(PlayerOverlayCoordinator);
                var reader = (SharedMemoryReader)type.GetField("_reader", flags)!.GetValue(coordinator)!;
                typeof(SharedMemoryReader).GetField("_view", flags)!.SetValue(reader, mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read));
                try
                {
                    overlay.SetComponentEnabled(OverlayComponentKeys.Speed, true);
                    overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                    overlay.ShowDemoAt(-5000, -5000, 96); PumpDispatcher();
                    type.GetField("_processId", flags)!.SetValue(coordinator, 1);
                    type.GetField("_drivingLocalIndex", flags)!.SetValue(coordinator, 3);
                    var frame = type.GetMethod("DrivingFrame", flags)!;
                    var updates = type.GetField("_drivingUpdateCount", flags)!;
                    RenderingEventArgs Frame(double seconds) => (RenderingEventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), flags, null, new object[] { TimeSpan.FromSeconds(seconds) }, null)!;
                    type.GetField("_latest", flags)!.SetValue(coordinator, Parse(fixture, DateTimeOffset.UtcNow.AddMilliseconds(-800)));
                    frame.Invoke(coordinator, new object?[] { null, Frame(1) });
                    AssertEqual(1, (int)updates.GetValue(coordinator)!);
                    type.GetField("_latest", flags)!.SetValue(coordinator, Parse(fixture, DateTimeOffset.UtcNow.AddSeconds(-3)));
                    frame.Invoke(coordinator, new object?[] { null, Frame(2) });
                    AssertEqual(1, (int)updates.GetValue(coordinator)!);
                    AssertEqual(0L, reader.SuccessfulSnapshots);
                    Console.WriteLine("PROOF snapshot 800ms late: display read continues; 3s stale: display read stops (fixture)");
                }
                finally { coordinator.Dispose(); overlay.Close(); }
            });
        }
    }
}
