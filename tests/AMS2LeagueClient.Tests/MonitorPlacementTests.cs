using System;
using System.Collections.Generic;
using System.Text.Json;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void MonitorPlacementFixtures()
        {
            var primary = new OverlayBounds(0, 0, 2560, 1440);
            var configurations = new List<OverlayBounds[]>();
            foreach (var size in new[] { (1920,1080), (2560,1440), (3840,2160) })
                configurations.Add(new[] { new OverlayBounds(0,0,size.Item1,size.Item2) });
            configurations.Add(new[] {primary,new OverlayBounds(-1920,0,1920,1080)});
            configurations.Add(new[] {primary,new OverlayBounds(2560,-620,1440,2560)});
            configurations.Add(new[] {primary,new OverlayBounds(200,-2160,3840,2160)});
            configurations.Add(new[] {new OverlayBounds(-1920,250,1920,1080),primary,new OverlayBounds(2560,-620,1440,2560)});
            configurations.Add(new[] {new OverlayBounds(0,0,7680,1440)}); // one logical Surround display, no panel inference
            foreach (var screens in configurations)
            foreach (double dpiScale in new[] {1.0,1.25,1.5,2.0})
            foreach (bool spanning in new[] {false,true})
            {
                var origin = screens[0];
                int vx = origin.X + 30, vy = origin.Y + 40;
                int vw = spanning ? 3440 : origin.Width - 80, vh = origin.Height - 100;
                foreach (var monitor in screens)
                {
                    // Fixture DIP is local to its HWND; never multiply the desktop origin by DPI.
                    int x = monitor.X + 63, y = monitor.Y + 57;
                    int width = (int)Math.Round(120 * dpiScale), height = (int)Math.Round(80 * dpiScale);
                    var original = new OverlayBounds(x, y, width, height);
                    var profile = new OverlayLayoutProfile();
                    for (int repeat=0;repeat<25;repeat++)
                    {
                        profile.Capture("speed", new OverlayBounds(x-vx,y-vy,width,height),vw,vh);
                        profile = JsonSerializer.Deserialize<OverlayLayoutProfile>(JsonSerializer.Serialize(profile))!;
                        var local = profile.Resolve("speed",default,vw,vh);
                        var restored = new OverlayBounds(vx+local.X,vy+local.Y,local.Width,local.Height);
                        AssertEqual(original, restored);
                        AssertEqual(original, OverlayScreenPlacement.Recover(restored,screens,screens));
                        x=restored.X;y=restored.Y;width=restored.Width;height=restored.Height;
                    }
                }
            }
            var disconnected = new OverlayBounds(-1800,300,500,200);
            var both = new[] {new OverlayBounds(-1920,0,1920,1080),primary};
            var only = new[] {primary};
            var work = new[] {new OverlayBounds(0,0,2560,1400)};
            AssertEqual(disconnected, OverlayScreenPlacement.Recover(disconnected,both,both));
            AssertEqual(new OverlayBounds(0,300,500,200), OverlayScreenPlacement.Recover(disconnected,only,work));
            AssertEqual(disconnected, OverlayScreenPlacement.Recover(disconnected,both,both));
            var hole = new OverlayBounds(-1700,-500,200,100);
            AssertEqual(new OverlayBounds(-1700,0,200,100), OverlayScreenPlacement.Recover(hole,both,both));
            // Partial screen intersection is accessible; spanning a gap is not forced onto primary.
            var edge = new OverlayBounds(-20,-20,100,100);
            AssertEqual(edge,OverlayScreenPlacement.Recover(edge,only,work));
        }
    }
}
