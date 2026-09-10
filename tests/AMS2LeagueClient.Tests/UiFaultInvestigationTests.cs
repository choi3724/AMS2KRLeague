using System;
using System.IO;
using System.Reflection;
using System.Windows;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void UiTickFaultBoundaryIsConservative()
        {
            WithTemporaryDirectory(directory => {
                var logger = new FileLogger(directory);
                var overlay = new OverlayWindow(false, Path.Combine(directory, "layout.json"));
                var coordinator = new PlayerOverlayCoordinator(overlay, new ClientStatusViewModel(), logger, false);
                Type type = typeof(PlayerOverlayCoordinator);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo tracker = type.GetField("_windowTracker", flags)!;
                object original = tracker.GetValue(coordinator)!;
                try
                {
                    overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                    overlay.ShowDemoAt(-5000, -5000, 96); PumpDispatcher(); AssertTrue(overlay.IsVisible);
                    // Deliberately fault a dependency inside the real UiTick try
                    // block. This is not a claim of a naturally failing panel.
                    type.GetField("_processId", flags)!.SetValue(coordinator, 1);
                    type.GetField("_nextUiDueTicks", flags)!.SetValue(coordinator, 0L);
                    tracker.SetValue(coordinator, null);
                    type.GetMethod("UiTick", flags)!.Invoke(coordinator, new object?[] { null, EventArgs.Empty });
                    AssertFalse(overlay.IsVisible);
                    Console.WriteLine("PROOF injected UiTick dependency fault hides overlay; conservative boundary retained, real panel failure NOT_REPRODUCED");
                }
                finally { tracker.SetValue(coordinator, original); coordinator.Dispose(); overlay.Close(); logger.Dispose(); }
                AssertTrue(File.ReadAllText(logger.FilePath).Contains("UI_TICK_EXCEPTION", StringComparison.Ordinal));
            });
        }
    }
}
