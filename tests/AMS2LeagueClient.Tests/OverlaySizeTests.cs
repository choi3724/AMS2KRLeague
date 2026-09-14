using System;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void SavedHudSizeSurvivesViewportChanges()
        {
            foreach (string key in OverlayComponentKeys.All)
            foreach (var viewport in new[] { (1920,1080),(2560,1440),(3840,2160),(7680,1440),(5760,1080) })
            {
                var profile=new OverlayLayoutProfile();
                profile.Capture(key,new OverlayBounds(100,80,600,320),viewport.Item1,viewport.Item2);
                foreach (var target in new[] { (1920,1080),(2560,1440),(3840,2160),(7680,1440),(5760,1080) })
                {
                    profile=JsonSerializer.Deserialize<OverlayLayoutProfile>(JsonSerializer.Serialize(profile))!;
                    var actual=profile.Resolve(key,default,target.Item1,target.Item2);
                    AssertEqual(600,actual.Width);AssertEqual(320,actual.Height);
                    AssertEqual((int)Math.Round(100.0*target.Item1/viewport.Item1),actual.X);
                    AssertEqual((int)Math.Round(80.0*target.Item2/viewport.Item2),actual.Y);
                }
            }
        }

        private static OverlayLayoutProfile LegacySizeProfile(int width,bool racing=false)
        {
            var p=new OverlayLayoutProfile {TowerDesignWidth=648,PreviewViewport=new OverlayPreviewViewport{Width=width,Height=1440}};
            p.DrivingHud.TowerDesign=racing?"racing":"legacy";
            foreach(string key in OverlayComponentKeys.All)
            {
                p.SetEnabled(key,key==OverlayComponentKeys.TimingTower);
                p.Components[key]=new NormalizedOverlayBounds{AllowOutsideViewport=true,X=40.0/width,Y=40.0/1440,Width=600.0/width,Height=650.0/1440};
            }
            return p;
        }

        private static void SavedHudLegacyReferenceAndBackup()
        {
            string dir=Path.Combine(Path.GetTempPath(),"ams2-size-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            string path=Path.Combine(dir,"layout.json");
            var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
            var type=typeof(OverlayWindow).Assembly.GetType("AMS2LeagueClient.Overlay.OverlayLayoutStore")!;
            object store=Activator.CreateInstance(type,path)!;
            try
            {
                string original=JsonSerializer.Serialize(LegacySizeProfile(2560),options);File.WriteAllText(path,original);
                var p=(OverlayLayoutProfile)type.GetMethod("Load")!.Invoke(store,null)!;
                AssertEqual(original,File.ReadAllText(path)); // Loading is not a disk migration.
                AssertEqual(600,p.Resolve(OverlayComponentKeys.Gear,default,7680,1440).Width);
                p.Capture(OverlayComponentKeys.TimingTower,new OverlayBounds(120,40,620,650),7680,1440);
                p.PreviewViewport=new OverlayPreviewViewport{Width=7680,Height=1440};
                type.GetMethod("Save")!.Invoke(store,new object[]{p});
                AssertEqual(original,File.ReadAllText(path+".before-fixed-size.bak"));
                p=(OverlayLayoutProfile)type.GetMethod("Load")!.Invoke(store,null)!;
                AssertEqual(600,p.Resolve(OverlayComponentKeys.Gear,default,7680,1440).Width); // Hidden HUD keeps old reference.
                AssertEqual(620,p.Resolve(OverlayComponentKeys.TimingTower,default,2560,1440).Width);
                type.GetMethod("Save")!.Invoke(store,new object[]{p});
                AssertEqual(original,File.ReadAllText(path+".before-fixed-size.bak"));
                p=new OverlayLayoutProfile();
                p.Components["speed"]=new NormalizedOverlayBounds{Width=.25,Height=.1};
                AssertEqual(480,p.Resolve("speed",default,1920,1080).Width); // No reference: retain legacy semantics, do not guess.
                p.Capture("speed",new OverlayBounds(-30,-40,480,108),1920,1080);
                AssertEqual(480,p.Resolve("speed",default,7680,1440).Width);
            }
            finally {Directory.Delete(dir,true);}
        }

        private static void SavedHudTowerScaleThroughEditAndLive()
        {
            foreach(bool racing in new[]{false,true})
            foreach(int editWidth in new[]{7680,2560})
            {
                string path=Path.Combine(Path.GetTempPath(),"ams2-tower-size-"+Guid.NewGuid()+".json");
                File.WriteAllText(path,JsonSerializer.Serialize(LegacySizeProfile(editWidth,racing),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
                OverlayWindow? overlay=new OverlayWindow(false,path);
                try
                {
                    overlay.BeginLayoutPreview(false);PumpDispatcher();overlay.UpdateLayout();PumpDispatcher();
                    var host=(Viewbox)overlay.FindName("TimingHost");var view=(OverlayHudView)host.Child;
                    double scale=view.TransformToAncestor(overlay).Transform(new Point(1,0)).X-view.TransformToAncestor(overlay).Transform(new Point()).X;
                    var before=OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(overlay).Handle);
                    AssertEqual(600,before.Width);
                    CaptureLayout((FrameworkElement)overlay.Content,"size-edit-"+editWidth+"-"+racing);
                    overlay.EndLayoutEdit(true);overlay.Close();overlay=new OverlayWindow(false,path);
                    var game=new GameWindowSnapshot(IntPtr.Zero,0,0,7680,1440,96,true,false,0);
                    foreach(int count in new[]{16,1,16,1})
                    {
                        var shell=DemoSnapshotFactory.CreateShell(false);
                        var rows=shell.Timing.AllRankingRows.Take(count).ToArray();
                        shell.Timing.AllRankingRows=rows;shell.Timing.RankingRows=rows;
                        overlay.SetViewModel(shell,false);overlay.ShowAt(game);PumpDispatcher();overlay.UpdateLayout();PumpDispatcher();
                        host=(Viewbox)overlay.FindName("TimingHost");view=(OverlayHudView)host.Child;
                        double actualScale=view.TransformToAncestor(overlay).Transform(new Point(1,0)).X-view.TransformToAncestor(overlay).Transform(new Point()).X;
                        var actual=OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(overlay).Handle);
                        Console.WriteLine($"PROOF racing={racing} editWidth={editWidth} liveWidth=7680 rowsRequested={count} rowsShown={shell.Timing.RankingRows.Count} HUD={actual.Width}x{actual.Height} scale={actualScale:F6} editScale={scale:F6}");
                        CaptureLayout((FrameworkElement)overlay.Content,$"size-live-{editWidth}-{count}-{racing}");
                        AssertEqual(600,actual.Width);AssertTrue(Math.Abs(scale-actualScale)<.005);
                    }
                }
                finally {overlay?.Close();File.Delete(path);File.Delete(path+".before-fixed-size.bak");}
            }
        }
    }
}
