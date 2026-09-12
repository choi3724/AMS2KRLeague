using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
        // Opt-in synthetic desktop rendering only; no game read or network.
        private static void HudResourceProbe()
        {
            foreach (string mode in new[] { "idle", "graph", "original-panels", "all-panels", "avante-panels" })
            {
                var history = new DrivingTelemetryHistory();
                var now = DateTimeOffset.UtcNow;
                DrivingTelemetrySample Sample(double seconds) => new DrivingTelemetrySample(now.AddSeconds(seconds), 1, 3,
                    .5 + .5 * Math.Sin(seconds * 2), .5 + .5 * Math.Cos(seconds * 2), 0, 0, 128 / 3.6, 3,
                    Math.Sin(seconds * 2) > .8, Math.Sin(seconds), 4000 + 2500 * (.5 + .5 * Math.Sin(seconds*2)), 8000);
                for (int i = -600; i <= 0; i++) history.Add(Sample(i / 60.0));
                var graph = new PedalTelemetryView { Width = 540, Height = 120 };
                graph.SetHistory(history);
                OverlayWindow? overlay = null;
                Window window;
                if (mode.EndsWith("panels", StringComparison.Ordinal))
                {
                    overlay = new OverlayWindow(false, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ams2-probe-" + Guid.NewGuid().ToString("N") + ".json"));
                    overlay.SetComponentEnabled(OverlayComponentKeys.Speed, true); overlay.SetComponentEnabled(OverlayComponentKeys.Gear, true);
                    if (mode == "all-panels" || mode == "avante-panels")
                    {
                        overlay.SetComponentEnabled(OverlayComponentKeys.PedalGauge, true); overlay.SetComponentEnabled(OverlayComponentKeys.DrivingDashboard, true);
                        overlay.SaveDrivingHudSettings(new DrivingHudSettings { TowerDesign = "racing", TelemetryDesign = "racing" });
                    }
                    if (mode == "avante-panels") overlay.SetComponentEnabled(OverlayComponentKeys.AvanteCluster, true);
                    overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                    overlay.ShowDemoAt(40, 40, 96);
                    window = overlay;
                }
                else
                {
                    window = new Window { Content = graph, Width = 560, Height = 160, Left = 40, Top = 40, ShowActivated = false };
                    window.Show();
                }
                var clock = Stopwatch.StartNew();
                using var process = Process.GetCurrentProcess();
                var intervals = new List<double>();
                double last = 0, startMs = 0; TimeSpan startCpu = default, lastRendering = TimeSpan.MinValue;
                long allocated = 0; int updates = 0; bool measuring = false; double nextData = 0, nextShell = 0;
                var updateCosts = new List<double>();
                var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
                EventHandler update = (s, e) =>
                {
                    if (mode == "idle") return;
                    double seconds = clock.Elapsed.TotalSeconds;
                    if (seconds < nextData) return;
                    do { nextData += 1.0 / 60; } while (nextData <= seconds);
                    var updateClock = Stopwatch.StartNew();
                    if (overlay != null)
                    {
                        Array.Copy(BitConverter.GetBytes((float)(.5 + .5 * Math.Sin(seconds * 2))), 0, fixture.Buffer, SharedMemoryLayout.Brake, 4);
                        Array.Copy(BitConverter.GetBytes(3 + (int)seconds % 3), 0, fixture.Buffer, SharedMemoryLayout.Gear, 4);
                        overlay.UpdateDrivingSample(Sample(seconds));
                        if (seconds >= nextShell)
                        {
                            do { nextShell += .05; } while (nextShell <= seconds);
                            overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false));
                        }
                    }
                    else { history.Add(Sample(seconds)); graph.SetHistory(history); }
                    if (measuring) { updates++; updateCosts.Add(updateClock.Elapsed.TotalMilliseconds); }
                };
                EventHandler rendering = (s, e) =>
                {
                    var time = ((RenderingEventArgs)e).RenderingTime;
                    if (time == lastRendering) return;
                    lastRendering = time;
                    double ms = clock.Elapsed.TotalMilliseconds;
                    if (ms < 1000) return;
                    if (!measuring)
                    {
                        measuring = true; startMs = ms; startCpu = process.TotalProcessorTime;
                        allocated = GC.GetTotalAllocatedBytes(false);
                    }
                    if (last > 0) intervals.Add(ms - last);
                    last = ms;
                };
                var frame = new DispatcherFrame();
                var end = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
                end.Tick += (s, e) => { end.Stop(); frame.Continue = false; };
                CompositionTarget.Rendering += update;
                CompositionTarget.Rendering += rendering;
                end.Start(); Dispatcher.PushFrame(frame);
                CompositionTarget.Rendering -= update; CompositionTarget.Rendering -= rendering;
                double duration = clock.Elapsed.TotalMilliseconds - startMs;
                double cpu = (process.TotalProcessorTime - startCpu).TotalMilliseconds / duration * 100;
                double allocation = (GC.GetTotalAllocatedBytes(false) - allocated) / 1048576.0 / (duration / 1000);
                process.Refresh();
                var sorted = intervals.OrderBy(value => value).ToArray();
                var costs = updateCosts.OrderBy(value => value).ToArray();
                Console.WriteLine("RESOURCE_PROBE mode=" + mode + " logicalCpus=" + Environment.ProcessorCount + " tier=" + (RenderCapability.Tier >> 16)
                    + " cpuMachinePct=" + (cpu / Environment.ProcessorCount).ToString("F3") + " cpuOneCorePct=" + cpu.ToString("F2")
                    + " workingMB=" + (process.WorkingSet64 / 1048576.0).ToString("F1") + " privateMB=" + (process.PrivateMemorySize64 / 1048576.0).ToString("F1")
                    + " allocMBps=" + allocation.ToString("F3") + " handles=" + process.HandleCount + " updatesHz=" + (updates / (duration / 1000)).ToString("F1")
                    + " callbacksHz=" + (intervals.Count / Math.Max(.001, intervals.Sum() / 1000)).ToString("F1")
                    + " over16_7msPct=" + (100.0 * intervals.Count(value => value > 16.7) / Math.Max(1, intervals.Count)).ToString("F1")
                    + " updateP95Ms=" + (costs.Length == 0 ? 0 : costs[(int)((costs.Length - 1) * .95)]).ToString("F2")
                    + " p95Ms=" + (sorted.Length == 0 ? 0 : sorted[(int)((sorted.Length - 1) * .95)]).ToString("F2"));
                window.Close(); PumpDispatcher();
            }
        }
    }
}
