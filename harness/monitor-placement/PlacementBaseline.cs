using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;

partial class Program
{
 static int PlacementBaseline(string output)
 {
  Directory.CreateDirectory(output);var app=new AMS2LeagueClient.App(false);app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var back=new Window{Title="Placement reference",Left=50,Top=50,Width=1100,Height=500,WindowStyle=WindowStyle.None,Background=System.Windows.Media.Brushes.DarkSlateGray,ShowActivated=false,Topmost=true};back.Show();
  var profile=new OverlayLayoutProfile();profile.Capture("speed",new OverlayBounds(-500,-300,400,100),1920,1080);
  var negative=profile.Resolve("speed",default,1920,1080);
  var panel=new PedalTelemetryView();var type=typeof(OverlayWindow).Assembly.GetType("AMS2LeagueClient.Overlay.AuxiliaryOverlayWindow")!;
  var window=(Window)Activator.CreateInstance(type,"pedalTelemetry","baseline placement",panel,540.0,120.0)!;
  try
  {
   var reference=new GameWindowSnapshot(IntPtr.Zero,100,100,1920,1080,96,true,false,0);var bounds=new OverlayBounds(20,30,600,150);
   Call(window,"ShowAt",reference,bounds);Pump(500);var hwnd=new WindowInteropHelper(window).Handle;
   var before=OverlayWindowInterop.ReadPhysicalBounds(hwnd);Capture(Path.Combine(output,"before-move.png"),50,50,1100,500);
   reference=new GameWindowSnapshot(IntPtr.Zero,400,250,1920,1080,96,true,false,0);Call(window,"ShowAt",reference,bounds);Pump(500);
   var after=OverlayWindowInterop.ReadPhysicalBounds(hwnd);Capture(Path.Combine(output,"after-move.png"),50,50,1100,500);
   File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{negative,before,after,errorX=after.X-420,errorY=after.Y-280},new JsonSerializerOptions{WriteIndented=true}));
   var measured=new List<object>();int monitorIndex=0;
   foreach(var display in OverlayWindowInterop.Displays)
   {
    var area=display.WorkArea;
    foreach(var point in new[]{(20,30),(80,70),(140,110),(30,50),(20,30)})
    {
     var target=new GameWindowSnapshot(IntPtr.Zero,area.X,area.Y,area.Width,area.Height,display.DpiX,true,false,0);
     var local=new OverlayBounds(point.Item1,point.Item2,600,150);
     Call(window,"ShowAt",target,local);Pump(150);
     var actual=OverlayWindowInterop.ReadPhysicalBounds(hwnd);
     int errorX=actual.X-area.X-local.X,errorY=actual.Y-area.Y-local.Y;
     Check(Math.Abs(errorX)<=2&&Math.Abs(errorY)<=2&&actual.Width==600&&actual.Height==150,"physical display move/resize <=2px");
     measured.Add(new{display.DeviceName,display.Bounds,display.DpiX,actual,errorX,errorY});
    }
    var captured=OverlayWindowInterop.ReadPhysicalBounds(hwnd);
    Capture(Path.Combine(output,"monitor-"+monitorIndex+".png"),captured.X,captured.Y,captured.Width,captured.Height);
    monitorIndex++;
   }
   File.WriteAllText(Path.Combine(output,"physical-displays.json"),JsonSerializer.Serialize(measured,new JsonSerializerOptions{WriteIndented=true}));return 0;
  }
  finally{window.Close();back.Close();app.Shutdown();}
 }
}
