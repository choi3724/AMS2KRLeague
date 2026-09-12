using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void DisabledOverlaysReleaseResources()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-lifetime-" + Guid.NewGuid() + ".json");
            var profile = new OverlayLayoutProfile();
            foreach (string key in OverlayComponentKeys.All) profile.SetEnabled(key, false);
            File.WriteAllText(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            int baseline = Application.Current.Windows.Count;
            var overlay = new OverlayWindow(false, path);
            try
            {
                AssertEqual(baseline + 1, Application.Current.Windows.Count);
                AssertFalse(overlay.WantsDrivingTelemetry);
                AssertTrue(((Viewbox)overlay.FindName("TimingHost")).Child == null);
                foreach (FieldInfo field in typeof(OverlayWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(f => typeof(FrameworkElement).IsAssignableFrom(f.FieldType) && f.Name.EndsWith("View", StringComparison.Ordinal)))
                    AssertTrue(field.GetValue(overlay) == null);
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.ShowDemoAt(-5000, -5000, 96);
                // Every option can be created, closed and recreated independently.
                foreach (string key in OverlayComponentKeys.All)
                {
                    overlay.SetComponentOpacity(key, .7);
                    for (int cycle = 0; cycle < 2; cycle++)
                    {
                        overlay.SetComponentEnabled(key, true);
                        PumpDispatcher();
                        AssertEqual(baseline + (key == OverlayComponentKeys.TimingTower ? 1 : 2), Application.Current.Windows.Count);
                        AssertEqual(.7, overlay.GetComponentOpacity(key));
                        AssertOverlayRasterizers(overlay);
                        overlay.SetComponentEnabled(key, false);
                        PumpDispatcher();
                        AssertEqual(baseline + 1, Application.Current.Windows.Count);
                        AssertFalse(overlay.WantsDrivingTelemetry);
                    }
                }
                WeakReference[] released = EnableThenReleaseAvante(overlay);
                for (int i = 0; i < 3; i++)
                {
                    PumpDispatcher(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                }
                AssertTrue(released.All(reference => !reference.IsAlive));
                Console.WriteLine("PROOF lifetime options=" + OverlayComponentKeys.All.Length + " cycles=2 auxiliaryRemaining=0 avanteViewsAndImagesCollected=" + released.Length);
            }
            finally { overlay.Close(); File.Delete(path); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AssertOverlayRasterizers(OverlayWindow overlay)
        {
            // Keep HWND/source inspection references outside the subsequent GC assertion.
            foreach (Window window in Application.Current.Windows)
            {
                if (window != overlay && !window.GetType().Name.Equals("AuxiliaryOverlayWindow")) continue;
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
                AssertTrue(source?.CompositionTarget != null);
                AssertEqual(System.Windows.Interop.RenderMode.Default, source!.CompositionTarget.RenderMode);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] EnableThenReleaseAvante(OverlayWindow overlay)
        {
            overlay.SetComponentEnabled(OverlayComponentKeys.AvanteCluster, true);
            overlay.SetComponentEnabled(OverlayComponentKeys.AvanteClusterExpanded, true);
            var normal = (AvanteClusterView)typeof(OverlayWindow).GetField("_avanteView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
            var expanded = (AvanteClusterView)typeof(OverlayWindow).GetField("_avanteExpandedView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
            var field = typeof(AvanteClusterView).GetField("_images", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object images = field.GetValue(normal)!;
            AssertTrue(ReferenceEquals(images, field.GetValue(expanded)));
            var references = new[] { new WeakReference(normal), new WeakReference(expanded), new WeakReference(images) };
            overlay.SetComponentEnabled(OverlayComponentKeys.AvanteCluster, false);
            overlay.SetComponentEnabled(OverlayComponentKeys.AvanteClusterExpanded, false);
            return references;
        }
    }
}
