using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void AvanteHighRpmWindowReload()
        {
            string path=Path.Combine(Path.GetTempPath(),"ams2-high-rpm-"+Guid.NewGuid()+".json");
            var profile=new OverlayLayoutProfile();
            foreach(string key in OverlayComponentKeys.All) profile.EnabledComponents[key]=false;
            profile.EnabledComponents[OverlayComponentKeys.AvanteCluster]=true;
            profile.EnabledComponents[OverlayComponentKeys.AvanteClusterExpanded]=true;
            File.WriteAllText(path,JsonSerializer.Serialize(profile,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            try
            {
                for(int reload=0;reload<2;reload++)
                {
                    var overlay=new OverlayWindow(false,path);
                    try
                    {
                        overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false),false);
                        overlay.ShowAt(new GameWindowSnapshot(IntPtr.Zero,0,0,1920,1080,96,true,false,0));
                        overlay.UpdateDrivingSession(new TelemetrySnapshot(DateTimeOffset.UtcNow,14,3398,0,2,1,2,0,2,0,0,0,0,0,
                            Array.Empty<ParticipantSnapshot>(),rootCarName:"high-rpm"));
                        overlay.UpdateDrivingSample(new DrivingTelemetrySample(DateTimeOffset.UtcNow,1,0,0,1,0,0,50,3,rpm:18800,maxRpm:19000));
                        if(reload==0)
                        {
                            var settings=overlay.GetDrivingHudSettings();
                            settings.AvanteVehicles["high-rpm"]=new AvanteRpmCalibration{Maximum=19000,YellowStart=17100,RedStart=18430};
                            overlay.SaveDrivingHudSettings(settings);
                        }
                        PumpDispatcher();
                        foreach(string field in new[]{"_avanteView","_avanteExpandedView"})
                        {
                            var view=(AvanteClusterView)typeof(OverlayWindow).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(overlay)!;
                            AssertEqual(19000.0,view.RpmScale.Maximum);
                            AssertEqual(18430.0,view.RpmScale.RedStart);
                            AssertTrue(view.IsVisible); AssertTrue(view.StaticFaceBuilds>0);
                        }
                        Console.WriteLine($"PROOF high RPM real OverlayWindow settings apply/reload={reload} both HUDs visible manualMax=19000 red=18430; fixture, not headset or process restart");
                    }
                    finally{overlay.Close();}
                }
            }
            finally{File.Delete(path);File.Delete(path+".before-fixed-size.bak");}
        }
        private static void AvanteHighRpmRestore()
        {
            foreach (double maximum in new[] { 8000.0, 19000, 20000, 100000 })
            foreach (bool expanded in new[] { false, true })
            {
                var view = new AvanteClusterView(expanded) { Width = expanded ? 1024 : 454, Height = 375 };
                ConfigureReferenceN(view, maximum, maximum * .9, maximum * .97);
                var settings = new DrivingHudSettings();
                settings.AvanteVehicles["reference-n"] = new AvanteRpmCalibration
                    { Maximum = maximum, YellowStart = maximum * .9, RedStart = maximum * .97 };
                settings = JsonSerializer.Deserialize<DrivingHudSettings>(JsonSerializer.Serialize(settings))!.Normalize();
                view.ApplySettings(settings);
                var image = new RenderTargetBitmap((int)view.Width, 375, 96, 96, PixelFormats.Pbgra32);
                void Render(double rpm)
                {
                    view.SetSample(new DrivingTelemetrySample(DateTimeOffset.UtcNow, 1, 0, 0, 0, 0, 0, 50, 3, rpm: rpm, maxRpm: maximum));
                    view.Measure(new Size(view.Width,375)); view.Arrange(new Rect(0,0,view.Width,375)); view.UpdateLayout();
                    image.Clear(); image.Render(view);
                }
                Render(0);
                int builds = view.StaticFaceBuilds;
                var times = new double[8];
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i=0;i<times.Length;i++)
                {
                    long begin = Stopwatch.GetTimestamp();
                    Render(maximum * ((i % 4) switch { 0 => .5, 1 => .95, 2 => .969, _ => .98 }));
                    times[i] = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
                    AssertEqual(builds, view.StaticFaceBuilds);
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                AssertEqual(maximum, view.RpmScale.Maximum);
                AssertEqual((int)(maximum/1000)+1, ((DrawingVisual)VisualTreeHelper.GetChild(view,5)).Children.Count);
                Array.Sort(times);
                Console.WriteLine($"PROOF high RPM max={maximum} expanded={expanded} captureMedianMs={times[4]:F3} captureMaxMs={times[^1]:F3} allocated={allocated} staticRebuilds={view.StaticFaceBuilds-builds}; synchronous bitmap diagnostic, not display FPS");
                CaptureLayout(view, $"high-rpm-{maximum}-{expanded}");
                view.SetSample(null);
            }
        }
    }
}
