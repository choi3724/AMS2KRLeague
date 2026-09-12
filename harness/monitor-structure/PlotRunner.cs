using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Presentation;

partial class Program
{
    static void RunPlot(string[] args)
    {
        string mode=args[0];double duration=double.Parse(args[2]);const double width=434,height=106;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var app=new AMS2LeagueClient.App(startRuntime:false);app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        FrameworkElement? view=null;Window? window=null;GpuSurface? gpu=null;MethodInfo? setHistory=null;
        if(mode=="G"){gpu=new GpuSurface("native plot",80,300,(int)width,(int)height);gpu.InitializePlot(width,height);}
        else{
            var panel=new PedalTelemetryView();view=(FrameworkElement)Field(panel,"_graph");((Panel)VisualTreeHelper.GetParent(view)).Children.Remove(view);
            view.Width=width;view.Height=height;setHistory=view.GetType().GetMethod("SetHistory")!;
            window=new Window{Content=view,Left=80,Top=300,Width=width,Height=height,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,ShowActivated=false,ShowInTaskbar=false,ResizeMode=ResizeMode.NoResize};window.Show();
        }
        var history=new DrivingTelemetryHistory();var origin=new DateTimeOffset(2026,9,12,0,0,0,TimeSpan.Zero);
        DrivingTelemetrySample Sample(int i){double t=i/60.0;return new(origin.AddSeconds(t),1,0,.5+.5*Math.Sin(t*2),.5+.5*Math.Cos(t*2),.5+.5*Math.Sin(t*.8),0,40+15*Math.Sin(t),3+(int)(Math.Max(0,t)/3)%3,Math.Sin(t*2)>.8,Math.Sin(t)*.3,4000+2700*Math.Sin(t*.5),8000);}
        for(int i=-600;i<0;i++){var sample=Sample(i);history.Add(sample);gpu?.AppendPlot(sample,i/60.0);}
        using var process=Process.GetCurrentProcess();var clock=Stopwatch.StartNew();var frame=new DispatcherFrame();var dispatcher=Dispatcher.CurrentDispatcher;
        Log(new{type="start",mode,scenario="plot",pid=process.Id,uiNativeThread=GetCurrentThreadId(),utc=DateTimeOffset.UtcNow,windows=new[]{new{name="graph",hwnd=gpu?.Handle.ToInt64()??new WindowInteropHelper(window!).Handle.ToInt64()}}});
        int next=0,pending=0;long allocated=0;TimeSpan cpu=default;int[] gc=new int[3];bool measuring=false,captured=false;double started=0,lastAt=0,lastInput=0;Exception? failure=null;
        using var cancel=new CancellationTokenSource();IntPtr timer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);if(timer==IntPtr.Zero)throw new InvalidOperationException("timer");
        var worker=new Thread(()=>{long n=0;while(!cancel.IsCancellationRequested){double delay=++n/144.0-clock.Elapsed.TotalSeconds;
            if(delay>0){long due=-(long)(delay*10000000);SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false);WaitForSingleObject(timer,1000);}
            if(Interlocked.Exchange(ref pending,1)!=0)continue;
            dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>{try{
                double t=clock.Elapsed.TotalSeconds;if(t>=3+duration){frame.Continue=false;return;}
                while(next<=Math.Floor(t*60)){var sample=Sample(next);history.Add(sample);gpu?.AppendPlot(sample,next/60.0);setHistory?.Invoke(view,new object[]{history});lastInput=next++/60.0;lastAt=t;}
                gpu?.RenderPlot(lastInput+Math.Min(1,t-lastAt));
                if(!captured&&t>=2){captured=true;if(args.Length>3){Directory.CreateDirectory(args[3]);string name=Path.Combine(args[3],"plot-"+mode+".png");if(gpu!=null)gpu.RenderPlot(lastInput,name);else{var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(name);encoder.Save(file);}}}
                if(!measuring&&t>=3){measuring=true;started=t;cpu=process.TotalProcessorTime;allocated=GC.GetTotalAllocatedBytes(false);for(int i=0;i<3;i++)gc[i]=GC.CollectionCount(i);Log(new{type="measure",utc=DateTimeOffset.UtcNow});}
            }catch(Exception ex){failure=ex;frame.Continue=false;}finally{Interlocked.Exchange(ref pending,0);}}));
        }}){IsBackground=true};worker.Start();
        try{Dispatcher.PushFrame(frame);cancel.Cancel();worker.Join();if(failure!=null)throw failure;process.Refresh();double elapsed=clock.Elapsed.TotalSeconds-started;
            Log(new{type="result",mode,scenario="plot",utc=DateTimeOffset.UtcNow,elapsed,cpuMachinePercent=(process.TotalProcessorTime-cpu).TotalSeconds/elapsed/Environment.ProcessorCount*100,workingSetMB=process.WorkingSet64/1048576.0,privateMB=process.PrivateMemorySize64/1048576.0,allocationMBps=(GC.GetTotalAllocatedBytes(false)-allocated)/1048576.0/elapsed,gc=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]),nativeGeometryBuilds=gpu?.GeometryBuilds,submitted=gpu?.Submitted});
            if(gpu!=null)Log(new{type="dxgi-statistics",name="graph",samples=gpu.Statistics});
        }finally{cancel.Cancel();worker.Join();CloseHandle(timer);gpu?.Dispose();window?.Close();app.Shutdown();}
    }
}
