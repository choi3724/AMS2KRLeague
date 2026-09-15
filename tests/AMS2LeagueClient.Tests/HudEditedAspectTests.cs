using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private static void HudEditedAspectProbe()
        {
            int differences=0;
            foreach(var size in new[]{(600,650),(1200,300),(1800,650),(600,150),(900,160),(300,650)})
            {
                string path=Path.Combine(Path.GetTempPath(),"ams2-aspect-"+Guid.NewGuid()+".json");
                var profile=LegacySizeProfile(7680,true);
                File.WriteAllText(path,JsonSerializer.Serialize(profile,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
                OverlayWindow? w=new OverlayWindow(false,path);
                try
                {
                    w.BeginLayoutPreview(false);PumpDispatcher();w.UpdateLayout();PumpDispatcher();
                    var handle=new WindowInteropHelper(w).Handle;
                    OverlayWindowInterop.SetPhysicalBounds(handle,40,40,size.Item1,size.Item2);
                    PumpDispatcher();w.UpdateLayout();PumpDispatcher();
                    var view=(OverlayHudView)((Viewbox)w.FindName("TimingHost")).Child;
                    double scale=view.TransformToAncestor(w).Transform(new Point(1,0)).X-view.TransformToAncestor(w).Transform(new Point()).X;
                    var before=OverlayWindowInterop.ReadPhysicalBounds(handle);
                    Console.WriteLine($"EDIT requested={size} actual={before.Width}x{before.Height} content={view.ActualWidth}x{view.ActualHeight} scale={scale:F6}");
                    CaptureLayout((FrameworkElement)w.Content,$"aspect-edit-{size.Item1}-{size.Item2}");
                    w.EndLayoutEdit(true);w.Close();w=new OverlayWindow(false,path);
                    foreach(int rows in new[]{16,1,16})
                    {
                        var model=DemoSnapshotFactory.CreateShell(false);var data=model.Timing.AllRankingRows.Take(rows).ToArray();model.Timing.AllRankingRows=data;model.Timing.RankingRows=data;
                        w.SetViewModel(model,false);w.ShowAt(new GameWindowSnapshot(IntPtr.Zero,0,0,7680,1440,96,true,false,0));PumpDispatcher();w.UpdateLayout();PumpDispatcher();
                        view=(OverlayHudView)((Viewbox)w.FindName("TimingHost")).Child;
                        double afterScale=view.TransformToAncestor(w).Transform(new Point(1,0)).X-view.TransformToAncestor(w).Transform(new Point()).X;
                        var after=OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(w).Handle);
                        Console.WriteLine($"LIVE requested={size} rows={rows} actual={after.Width}x{after.Height} content={view.ActualWidth}x{view.ActualHeight} scale={afterScale:F6} delta={afterScale-scale:F6}");
                        CaptureLayout((FrameworkElement)w.Content,$"aspect-live-{size.Item1}-{size.Item2}-{rows}");
                        if(Math.Abs(afterScale-scale)>.005)differences++;
                    }
                }
                finally{w?.Close();File.Delete(path);File.Delete(path+".before-fixed-size.bak");}
            }
            AssertEqual(0,differences);
        }
    }
}
