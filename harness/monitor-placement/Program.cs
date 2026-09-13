using System;
using System.Reflection;
using System.Windows.Threading;
partial class Program
{
 static object? Call(object x,string name,params object[] args)=>x.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(x,args);
 static void Pump(int milliseconds) {var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};t.Tick+=(s,e)=>{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Capture(string path,int x,int y,int width,int height){using var b=new System.Drawing.Bitmap(width,height);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(x,y,0,0,b.Size);b.Save(path);}
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct HitPoint { public int X,Y; }
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(HitPoint point);
 [STAThread] static int Main(string[] args)
 {
  try { return args.Length > 1 && args[1] == "--session" ? PlacementSession(args[0]) : PlacementBaseline(args[0]); }
  catch(Exception e) { Console.Error.WriteLine(e); return 1; }
 }
}
