using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static string SuppliedTripleJson()
        {
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AMS2LeagueClient.Tests.Fixtures.overlay-layout-triple-075.json")!;
            using var reader=new StreamReader(stream);return reader.ReadToEnd();
        }
        private static void TripleSuppliedLayoutPositions()
        {
            var p=JsonSerializer.Deserialize<OverlayLayoutProfile>(SuppliedTripleJson(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            foreach(var item in p.Components)
            {
                var saved=item.Value;
                var expected=new OverlayBounds((int)Math.Round(saved.X*2560),(int)Math.Round(saved.Y*1440),(int)Math.Round(saved.Width*2560),(int)Math.Round(saved.Height*1440));
                foreach(var viewport in new[]{(2560,1440),(7680,1440),(7680,2160)})
                {
                    var actual=p.Resolve(item.Key,default,viewport.Item1,viewport.Item2);
                    Console.WriteLine($"POSITION {item.Key} viewport={viewport} expected={expected.X},{expected.Y},{expected.Width},{expected.Height} actual={actual.X},{actual.Y},{actual.Width},{actual.Height}");
                    AssertEqual(expected,actual);
                }
            }
            // No reference at all: old normalized semantics, no invented pixel origin.
            p=new OverlayLayoutProfile();p.Components["speed"]=new NormalizedOverlayBounds{X=.25,Y=.1,Width=.1,Height=.1};
            AssertEqual(1920,p.Resolve("speed",default,7680,1440).X);
            // New negative offsets remain anchored to the game client origin, not a monitor index.
            p.Capture("speed",new OverlayBounds(-300,-50,480,120),2560,1440);
            for(int i=0;i<10;i++)
            {
                p=JsonSerializer.Deserialize<OverlayLayoutProfile>(JsonSerializer.Serialize(p))!;
                AssertEqual(new OverlayBounds(-300,-50,480,120),p.Resolve("speed",default,7680,2160));
            }
        }
        private static void TripleSuppliedLayoutWpfRestore()
        {
            string path=Path.Combine(Path.GetTempPath(),"ams2-supplied-position-"+Guid.NewGuid()+".json");
            File.WriteAllText(path,SuppliedTripleJson());
            OverlayWindow? w=new OverlayWindow(false,path);
            try
            {
                foreach(var display in OverlayWindowInterop.Displays)
                    Console.WriteLine($"WPF DISPLAY bounds={display.Bounds} work={display.WorkArea} dpi={display.DpiX},{display.DpiY}");
                w.BeginLayoutPreview(false);PumpDispatcher();w.UpdateLayout();PumpDispatcher();
                var edit=OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(w).Handle);
                Console.WriteLine($"WPF EDIT {edit.X},{edit.Y},{edit.Width},{edit.Height}");
                AssertEqual(590,edit.X);AssertEqual(648,edit.Width);
                w.EndLayoutEdit(true);w.Close();w=null;
                for(int repeat=0;repeat<2;repeat++)
                {
                    w=new OverlayWindow(false,path);
                    var model=DemoSnapshotFactory.CreateShell(false);var rows=model.Timing.AllRankingRows.Take(2).ToArray();model.Timing.AllRankingRows=rows;model.Timing.RankingRows=rows;
                    w.SetViewModel(model,false);
                    foreach(var origin in new[]{(0,0),(100,50),(0,0)})
                    {
                        w.ShowAt(new GameWindowSnapshot(IntPtr.Zero,origin.Item1,origin.Item2,7680,1440,96,true,false,0));PumpDispatcher();w.UpdateLayout();PumpDispatcher();
                        var actual=OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(w).Handle);
                        Console.WriteLine($"WPF LIVE repeat={repeat} origin={origin} actual={actual.X},{actual.Y},{actual.Width},{actual.Height}");
                        CaptureLayout((FrameworkElement)w.Content,$"supplied-position-{repeat}-{origin.Item1}");
                        AssertEqual(590+origin.Item1,actual.X);AssertEqual(4+origin.Item2,actual.Y);AssertEqual(648,actual.Width);
                        foreach(string field in new[]{"_sessionWindow","_lapTimingWindow"})
                        {
                            var panel=(Window)typeof(OverlayWindow).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(w)!;
                            var rect=OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(panel).Handle);
                            Console.WriteLine($"WPF {field} actual={rect.X},{rect.Y},{rect.Width},{rect.Height}");
                            AssertEqual(915+origin.Item1,rect.X);
                        }
                        var panels=(System.Collections.IEnumerable)typeof(OverlayWindow).GetField("_drivingWindows",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(w)!;
                        foreach(var value in panels)
                        {
                            if (!(value is Window panel) || !panel.IsVisible) continue;
                            string key=(string)panel.GetType().GetProperty("ComponentKey")!.GetValue(panel)!;
                            var saved=key==OverlayComponentKeys.PedalTelemetry
                                ?new OverlayBounds(1220,983,365,105):new OverlayBounds(1188,985,90,102);
                            var desired=new OverlayBounds(saved.X+origin.Item1,saved.Y+origin.Item2,saved.Width,saved.Height);
                            var requested=(System.Collections.Generic.Dictionary<string,OverlayBounds>)typeof(OverlayWindow)
                                .GetField("_requestedPlacements",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(w)!;
                            // Check the exact game-relative mapping even when the CI desktop is too small.
                            AssertEqual(desired,requested[key]);
                            // The real HWND also follows the existing disconnected-screen recovery policy.
                            var expected=OverlayWindowInterop.RecoverToDisplay(desired);
                            var rect=OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(panel).Handle);
                            Console.WriteLine($"WPF {key} actual={rect.X},{rect.Y},{rect.Width},{rect.Height} desired={desired} expected={expected}");
                            AssertEqual(expected,rect);
                        }
                    }
                    w.BeginLayoutEdit();PumpDispatcher();w.EndLayoutEdit(true);w.Close();w=null;
                    var restored=JsonSerializer.Deserialize<OverlayLayoutProfile>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
                    AssertEqual(new OverlayBounds(1220,983,365,105),restored.Resolve(OverlayComponentKeys.PedalTelemetry,default,7680,1440));
                    AssertEqual(new OverlayBounds(1188,985,90,102),restored.Resolve(OverlayComponentKeys.PedalGauge,default,7680,1440));
                }
            }
            finally{w?.Close();File.Delete(path);File.Delete(path+".before-fixed-size.bak");}
        }
    }
}
