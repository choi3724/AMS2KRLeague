using System;
using System.IO;
using System.Linq;
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
        private static void OverlayOpacityPersistsAndRenders()
        {
            WithTemporaryDirectory(directory =>
            {
                string path = Path.Combine(directory, "layout.json");
                var prior = Application.Current.Windows.Cast<Window>().ToHashSet();
                var overlay = new OverlayWindow(false, path);
                var status = new ClientStatusWindow(new ClientStatusViewModel());
                try
                {
                    status.ComponentOpacityChanged += overlay.SetComponentOpacity;
                    status.Left = -5000; status.Top = -5000; status.Show(); PumpDispatcher();
                    var sliders = Descendants<Slider>((FrameworkElement)status.Content).Where(s => s.Tag is string).ToArray();
                    AssertEqual(OverlayComponentKeys.All.Length, sliders.Length);
                    foreach (var slider in sliders)
                    {
                        string key = (string)slider.Tag;
                        AssertEqual(1.0, overlay.GetComponentOpacity(key));
                        slider.Value = 65;
                        AssertTrue(Math.Abs(.35 - overlay.GetComponentOpacity(key)) < .001);
                    }
                    status.SetComponentOpacities(OverlayComponentKeys.All.ToDictionary(k=>k,k=>.35));
                    overlay.ResetLayout(); // Position reset must preserve appearance.
                    var reloaded = new OverlayWindow(false, path);
                    try { foreach (string key in OverlayComponentKeys.All) AssertTrue(Math.Abs(.35-reloaded.GetComponentOpacity(key)) < .001); }
                    finally { reloaded.Close(); }
                    overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                    foreach (string key in OverlayComponentKeys.All) overlay.SetComponentEnabled(key, key == OverlayComponentKeys.Speed);
                    overlay.ShowDemoAt(-5000,-5000,96); PumpDispatcher();
                    foreach (double opacity in new[] { 1.0, .5, 0.0 })
                    {
                        overlay.SetComponentOpacity(OverlayComponentKeys.Speed, opacity); PumpDispatcher();
                        var frame = overlay.CaptureVrFrame();
                        if (opacity == 0)
                        {
                            AssertNull(frame); // No invisible composition; controller hides the existing VR surface on null.
                            AssertFalse(overlay.WantsDrivingTelemetry);
                            Console.WriteLine("PROOF opacity=0 VR compositor skipped and driving demand suspended");
                            continue;
                        }
                        AssertNotNull(frame);
                        byte maximum = frame!.Rgba.Where((_,i)=>i%4==3).Max();
                        Console.WriteLine("PROOF opacity="+opacity+" VR compositor alphaMax="+maximum);
                        AssertTrue(Math.Abs(maximum - opacity*255) <= 2);
                    }
                    overlay.BeginLayoutEdit(); PumpDispatcher();
                    foreach (var panel in Application.Current.Windows.Cast<Window>().Where(w=>w!=status&&!prior.Contains(w)))
                        if (panel.Content is Grid root && root.Children.Count > 1) AssertEqual(1.0, root.Children[1].Opacity);
                    overlay.EndLayoutEdit(false);
                }
                finally { status.Close(); overlay.Close(); }
            });
        }
    }
}
