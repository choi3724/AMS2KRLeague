using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;

partial class Program
{
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] static extern bool GetCursorPos(out HitPoint point);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 static int PlacementSession(string output)
 {
  Directory.CreateDirectory(output);string layout=Path.Combine(output,"session-layout.json"),expectedFile=Path.Combine(output,"expected.json");bool restore=File.Exists(expectedFile);
  var app=new AMS2LeagueClient.App(false);app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var overlay=new OverlayWindow(false,layout);
  try
  {
   foreach(string key in OverlayComponentKeys.All)overlay.SetComponentEnabled(key,key==OverlayComponentKeys.PedalTelemetry);
   var display=OverlayWindowInterop.Displays[0].WorkArea;
   overlay.BeginLayoutPreview(false,restore?null:new GameWindowSnapshot(IntPtr.Zero,display.X,display.Y,display.Width,display.Height,96,true,false,0));Pump(500);
   var panel=app.Windows.Cast<Window>().Single(w=>w.IsVisible&&w.Title.Contains("텔레메트리"));var hwnd=new WindowInteropHelper(panel).Handle;
   if(!restore)
   {
    OverlayWindowInterop.SetPhysicalBounds(hwnd,display.X+200,display.Y+180,650,160);Pump(300);
    var start=OverlayWindowInterop.ReadPhysicalBounds(hwnd);var point=new HitPoint{X=start.X+40,Y=start.Y+6};
    Check(WindowFromPoint(point)==hwnd,"drag hit target is our editable overlay HWND");
    GetCursorPos(out var previous);
    var input=new Thread(()=>{try{SetCursorPos(point.X,point.Y);mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(120);SetCursorPos(point.X+123,point.Y+57);Thread.Sleep(160);}finally{mouse_event(4,0,0,0,UIntPtr.Zero);SetCursorPos(previous.X,previous.Y);}}){IsBackground=true};input.Start();Pump(700);Check(!input.IsAlive,"bounded drag input completed");
    var moved=OverlayWindowInterop.ReadPhysicalBounds(hwnd);Check(Math.Abs(moved.X-start.X-123)<=2&&Math.Abs(moved.Y-start.Y-57)<=2,"actual drag uses physical screen delta");
    File.WriteAllText(expectedFile,JsonSerializer.Serialize(new[]{moved.X,moved.Y,moved.Width,moved.Height}));
   }
   var actual=OverlayWindowInterop.ReadPhysicalBounds(hwnd);var expected=JsonSerializer.Deserialize<int[]>(File.ReadAllText(expectedFile))!;
   Check(Math.Abs(actual.X-expected[0])<=2&&Math.Abs(actual.Y-expected[1])<=2&&Math.Abs(actual.Width-expected[2])<=2&&Math.Abs(actual.Height-expected[3])<=2,"process restart restores physical layout <=2px");
   Capture(Path.Combine(output,restore?"restored.png":"dragged.png"),actual.X,actual.Y,actual.Width,actual.Height);
   overlay.EndLayoutEdit(true);Console.WriteLine(JsonSerializer.Serialize(new{restore,actual,pid=Environment.ProcessId}));return 0;
  }
  catch(Exception e){Console.Error.WriteLine(e);return 1;}
  finally{overlay.Close();app.Shutdown();}
 }
}
