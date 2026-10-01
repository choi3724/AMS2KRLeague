using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void ThreadedNIsExplicitAndLayeredOnly()
        {
            AssertFalse(ClientStartupPolicy.FromArguments(Array.Empty<string>()).UseThreadedN);
            ClientStartupPolicy threaded = ClientStartupPolicy.FromArguments(new[] { "--MONITOR-THREADED-N" });
            AssertTrue(threaded.UseThreadedN);
            AssertTrue(threaded.UseSoftwareRendering);
            AssertFalse(ClientStartupPolicy.FromArguments(new[] { "--monitor-threaded-n", "--monitor-glass" }).UseThreadedN);
            AssertFalse(ClientStartupPolicy.FromArguments(new[] { "--monitor-threaded-n", "--monitor-retained-n" }).UseThreadedN);
        }

        // A threaded view draws the same static resources; unfrozen statics would be bound to the
        // first UI thread and throw on the HUD thread.
        private static void AvanteStaticResourcesAreFrozen()
        {
            var unfrozen = typeof(AvanteClusterView).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => typeof(Freezable).IsAssignableFrom(field.FieldType))
                .Where(field => field.GetValue(null) is Freezable freezable && !freezable.IsFrozen)
                .Select(field => field.Name).ToArray();
            AssertEqual(string.Empty, string.Join(",", unfrozen));
        }

        private static void ThreadedNPresentsOffTheUiThreadAndFallsBack()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-threaded-n-" + Guid.NewGuid().ToString("N") + ".json");
            var status = new List<string>();
            var overlay = new OverlayWindow(false, path, useSoftwareRendering: true, useThreadedN: true);
            overlay.ThreadedNStatus += status.Add;
            try
            {
                AssertTrue(overlay.UsesThreadedN);
                overlay.SetComponentEnabled(OverlayComponentKeys.AvanteCluster, true);
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.ShowDemoAt(-5000, -5000, 96);
                overlay.UpdateDrivingSample(new DrivingTelemetrySample(DateTimeOffset.UtcNow, 1, 0, .2, .8, 0, 0, 40, 3, false, 0, 6500, 8000));
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var slots = (Array)typeof(OverlayWindow).GetField("_threadedN", flags)!.GetValue(overlay)!;
                var panels = (Array)typeof(OverlayWindow).GetField("_drivingWindows", flags)!.GetValue(overlay)!;
                object compactPanel = panels.GetValue(5)!;
                bool ExternallyPresented() => (bool)compactPanel.GetType().GetProperty("IsExternallyPresented")!.GetValue(compactPanel)!;
                var watch = Stopwatch.StartNew();
                while (!status.Any(s => s.StartsWith("active slot=0", StringComparison.Ordinal)) && watch.Elapsed < TimeSpan.FromSeconds(10))
                {
                    PumpDispatcher();
                    Thread.Sleep(10);
                }
                AssertTrue(status.Any(s => s.StartsWith("active slot=0", StringComparison.Ordinal)));
                AssertFalse(status.Any(s => s.StartsWith("failed", StringComparison.Ordinal)));
                object hud = slots.GetValue(0)!;
                AssertNotNull(hud);
                AssertTrue((long)hud.GetType().GetProperty("Frames", flags)!.GetValue(hud)! > 0);
                AssertTrue(ExternallyPresented());
                // Editing returns the HUD to the WPF view, which owns the edit chrome.
                AssertTrue(overlay.BeginLayoutEdit());
                PumpDispatcher();
                AssertNull(slots.GetValue(0));
                AssertFalse(ExternallyPresented());
                AssertTrue(status.Any(s => s.StartsWith("released slot=0", StringComparison.Ordinal)));
                overlay.EndLayoutEdit(false);
                Console.WriteLine("PROOF threaded N: first frame presented off the UI thread; edit releases to WPF; statuses=" + string.Join("|", status));
            }
            finally { overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
