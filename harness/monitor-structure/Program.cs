using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

partial class Program
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static object Field(object o,string name)=>o.GetType().GetField(name,Private)!.GetValue(o)!;
    static void Stop(object o)=>o.GetType().GetMethod("Stop",Private)!.Invoke(o,null);
    static readonly DependencyProperty Rpm=(DependencyProperty)typeof(AvanteClusterView).GetField("RpmPositionProperty",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWaitableTimerEx(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr argument,bool resume);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    static void Log(object value)=>Console.WriteLine(JsonSerializer.Serialize(value));
    [STAThread] static int Main(string[] args)
    {
        try { Run(args);return 0; } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
    static void Run(string[] args)
    {
        string mode=args[0],scenario=args[1];double duration=args.Length>2?double.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture):30;
        string diagnostic=args.Length>4?args[4]:"full";
        double hz=args.Length>5?double.Parse(args[5]):144;
        if(diagnostic=="harness"||diagnostic=="models") { Headless(mode,scenario,duration,diagnostic,hz);return; }
        if(scenario=="plot"){RunPlot(args);return;}
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var app=new AMS2LeagueClient.App(startRuntime:false);app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var avante=new AvanteClusterView();var graph=new PedalTelemetryView();var pedal=new PedalTelemetryView(true);var timing=new LapTimingView();
        var shell=DemoSnapshotFactory.CreateShell(false);timing.SetViewModel(shell.Timing);
        var clone=typeof(object).GetMethod("MemberwiseClone",Private)!;
        FrameworkElement[] views=scenario=="avante"?new FrameworkElement[]{avante}:new FrameworkElement[]{avante,graph,pedal,timing};
        string[] names={"avante","graph","pedal","timing"};
        (int x,int y,int w,int h)[] bounds={(80,300,454,375),(560,300,540,120),(560,440,120,120),(710,440,390,150)};
        var windows=new List<Window>();var gpu=new List<GpuSurface>();
        for(int i=0;i<views.Length;i++) {
            var b=bounds[i];var v=views[i];v.Width=b.w;v.Height=b.h;
            if(mode=="D") {
                v.Measure(new Size(b.w,b.h));v.Arrange(new Rect(0,0,b.w,b.h));v.UpdateLayout();
                var bootstrap=new RenderTargetBitmap(b.w,b.h,96,96,PixelFormats.Pbgra32);bootstrap.Render(v);
                gpu.Add(new GpuSurface(names[i],b.x,b.y,b.w,b.h));
            } else {
                var window=new Window {Title="Renderer "+mode+" "+names[i],Content=v,Left=b.x,Top=b.y,Width=b.w,Height=b.h,WindowStyle=WindowStyle.None,
                    AllowsTransparency=mode!="C",Background=mode=="C"?Brushes.Black:Brushes.Transparent,ShowActivated=false,ShowInTaskbar=false,Topmost=true,ResizeMode=ResizeMode.NoResize};
                window.SourceInitialized+=(_,__)=>{
                    var h=new WindowInteropHelper(window).Handle;SetWindowLongPtr(h,-20,new IntPtr(GetWindowLongPtr(h,-20).ToInt64()|0x08000020));
                    if(mode=="A"||mode=="N")((HwndSource)PresentationSource.FromVisual(window)).CompositionTarget.RenderMode=RenderMode.SoftwareOnly;
                };window.Show();windows.Add(window);
            }
        }
        // Harness-only common interpolation; source product callbacks/timers are stopped.
        // This separates renderer cost from differing visible/offscreen motion policies.
        var history=new DrivingTelemetryHistory();var origin=new DateTimeOffset(2026,9,12,0,0,0,TimeSpan.Zero);
        DrivingTelemetrySample Sample(int index) {double t=index/60.0;return new(origin.AddSeconds(t),1,0,.5+.5*Math.Sin(t*2),.5+.5*Math.Cos(t*2),.5+.5*Math.Sin(t*.8),0,
            40+15*Math.Sin(t),3+(int)(Math.Max(0,t)/3)%3,Math.Sin(t*2)>.8,Math.Sin(t)*.3,4000+2700*Math.Sin(t*.5),8000);}
        for(int i=-600;i<0;i++)history.Add(Sample(i));
        object rpmMotion=Field(avante,"_rpmMotion"),flash=Field(avante,"_flashTimer");
        object graphElement=Field(graph,"_graph"),wheel=Field(graph,"_wheel"),graphMotion=Field(graphElement,"_motion"),wheelMotion=Field(wheel,"_motion");
        var scroll=(TranslateTransform)Field(graphElement,"_scroll");var rotation=(RotateTransform)Field(wheel,"_rotation");
        var drawMotion=typeof(AvanteClusterView).GetMethod("DrawMotion",Private)!;
        var flashOn=typeof(AvanteClusterView).GetField("_flashOn",Private)!;
        if(diagnostic=="text") ((DrawingVisual)Field(avante,"_values")).Opacity=0;
        if(diagnostic=="graph") ((FrameworkElement)Field(graph,"_graph")).Opacity=0;
        if(diagnostic=="effects") foreach(string layer in new[]{"_follower","_motion","_needle"}) ((DrawingVisual)Field(avante,layer)).Clip=Geometry.Empty;
        var clock=Stopwatch.StartNew();int nextSample=0,nextTiming=0;double from=0,target=0,targetAt=0,steerFrom=0,steerTarget=0,steerAt=0;
        double Lerp(double a,double b,double at,double t,double seconds)=>a+(b-a)*Math.Clamp((t-at)/seconds,0,1);
        var intervals=new List<double>();TimeSpan cpu=default;long allocated=0;int[] gc=new int[3];double started=0,last=0;int measuredSamples=0;
        using var process=Process.GetCurrentProcess();bool measuring=false;var dispatcher=Dispatcher.CurrentDispatcher;var frame=new DispatcherFrame();
        Log(new {type="start",mode,scenario,diagnostic,hz,uiNativeThread=GetCurrentThreadId(),uiManagedThread=Environment.CurrentManagedThreadId,pid=process.Id,utc=DateTimeOffset.UtcNow,logicalCores=Environment.ProcessorCount,
            windows=views.Select((v,i)=>new{name=names[i],hwnd=mode=="D"?gpu[i].Handle.ToInt64():new WindowInteropHelper(windows[i]).Handle.ToInt64(),bounds=bounds[i].ToString()})});
        var queueDelays=new List<double>();int skipped=0;long queuedAt=0;int pending=0;using var cancel=new CancellationTokenSource();Exception? failure=null;int captured=0;
        double[] captureAt={.2,1,2};
        IntPtr waitTimer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);if(waitTimer==IntPtr.Zero)throw new InvalidOperationException("High resolution waitable timer unavailable");
        var timerThread=new Thread(()=>{Log(new{type="scheduler",nativeThread=GetCurrentThreadId(),managedThread=Environment.CurrentManagedThreadId});long n=0;var timer=Stopwatch.StartNew();while(!cancel.IsCancellationRequested) {
            double wait=(++n/hz)-timer.Elapsed.TotalSeconds;if(wait>0) {long due=-(long)(wait*10000000);SetWaitableTimer(waitTimer,ref due,0,IntPtr.Zero,IntPtr.Zero,false);WaitForSingleObject(waitTimer,1000);}
            if(Interlocked.Exchange(ref pending,1)!=0){Interlocked.Increment(ref skipped);continue;}
            queuedAt=Stopwatch.GetTimestamp();
            dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>{
                try {
                    double t=clock.Elapsed.TotalSeconds; if(measuring)queueDelays.Add(Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds);
                    if(t>=3+duration) {frame.Continue=false;return;}
                    while(nextSample<=Math.Floor(t*60) && (diagnostic!="static"||nextSample==0)) {
                        double at=nextSample/60.0;var sample=Sample(nextSample++);
                        from=Lerp(from,target,targetAt,at,.065);target=sample.Rpm!.Value;targetAt=at;
                        steerFrom=Lerp(steerFrom,steerTarget,steerAt,at,.035);steerTarget=sample.SteeringDegrees(900)??0;steerAt=at;
                        history.Add(sample);if(diagnostic!="blocked" || nextSample==1) avante.SetSample(sample,true);
                        if((mode!="P"&&mode!="N")||diagnostic=="static"||diagnostic=="blocked") {Stop(rpmMotion);((DispatcherTimer)flash).Stop();}
                        if(views.Length>1) {if(diagnostic!="blocked" || nextSample==1){graph.SetHistory(history);pedal.SetHistory(history);}if((mode!="P"&&mode!="N")||diagnostic=="static"||diagnostic=="blocked"){Stop(graphMotion);Stop(wheelMotion);}}
                        if(measuring)measuredSamples++;
                    }
                    if(diagnostic!="static" && views.Length>1 && nextTiming<=Math.Floor(t*20)) {
                        nextTiming=(int)Math.Floor(t*20)+1;var timingModel=(OverlayViewModel)clone.Invoke(shell.Timing,null)!;
                        timingModel.CurrentLapText=TimeSpan.FromSeconds(nextTiming/20.0).ToString(@"m\:ss\.fff");if(diagnostic!="blocked")timing.SetViewModel(timingModel);
                    }
                    if(diagnostic!="static" && mode!="P"&&mode!="N") {
                        flashOn.SetValue(avante,((int)(t/.125)&1)==0);avante.SetValue(Rpm,Lerp(from,target,targetAt,t,.065));drawMotion.Invoke(avante,null);
                        scroll.X=-Math.Max(1,((FrameworkElement)graphElement).ActualWidth)/10*Math.Max(0,t-targetAt);
                        rotation.Angle=Lerp(steerFrom,steerTarget,steerAt,t,.035);
                    }
                    for(int i=0;i<views.Length;i++){views[i].UpdateLayout();if(mode=="D")gpu[i].Render(views[i]);}
                    if(captured<captureAt.Length&&t>=captureAt[captured]) {
                        string phase=new[]{"white","yellow","red"}[captured++];
                        if(args.Length>3)for(int i=0;i<views.Length;i++) {
                            Directory.CreateDirectory(args[3]);var bitmap=new RenderTargetBitmap(bounds[i].w,bounds[i].h,96,96,PixelFormats.Pbgra32);bitmap.Render(views[i]);
                            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(args[3],mode+"-"+names[i]+"-"+phase+"-source.png")))encoder.Save(file);
                            if(mode=="D")gpu[i].Render(views[i],Path.Combine(args[3],mode+"-"+names[i]+"-"+phase+"-gpu.png"));
                        }
                    }
                    if(!measuring&&t>=3) {measuring=true;started=t;cpu=process.TotalProcessorTime;allocated=GC.GetTotalAllocatedBytes(false);for(int i=0;i<3;i++)gc[i]=GC.CollectionCount(i);
                        Log(new{type="measure",mode,scenario,utc=DateTimeOffset.UtcNow});}
                    if(measuring){if(last>0)intervals.Add((t-last)*1000);last=t;}
                } catch(Exception ex) {failure=ex;frame.Continue=false;}
                finally {Interlocked.Exchange(ref pending,0);}
            }));
        }}){IsBackground=true,Name="Matrix common 144Hz presentation scheduler"};
        timerThread.Start();
        try {
            Dispatcher.PushFrame(frame);cancel.Cancel();timerThread.Join();
            if(failure!=null)throw new InvalidOperationException("Scene rendering failed",failure);
            process.Refresh();double elapsed=clock.Elapsed.TotalSeconds-started;intervals.Sort();queueDelays.Sort();
            Log(new {type="result",mode,scenario,diagnostic,hz,skipped,queueP95=queueDelays.Count==0?0:queueDelays[(int)((queueDelays.Count-1)*.95)],queueMax=queueDelays.Count==0?0:queueDelays[^1],utc=DateTimeOffset.UtcNow,elapsed,measuredSamples,
                cpuMachinePercent=(process.TotalProcessorTime-cpu).TotalSeconds/elapsed/Environment.ProcessorCount*100,
                workingSetMB=process.WorkingSet64/1048576.0,privateMB=process.PrivateMemorySize64/1048576.0,allocationMBps=(GC.GetTotalAllocatedBytes(false)-allocated)/1048576.0/elapsed,
                gc=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]),callbackHz=1000*intervals.Count/intervals.Sum(),callbackP95=intervals[(int)((intervals.Count-1)*.95)],callbackP99=intervals[(int)((intervals.Count-1)*.99)],
                stalls33=intervals.Count(x=>x>33),imageUploads=gpu.Select(x=>x.ImageUploads),geometryBuilds=gpu.Select(x=>x.GeometryBuilds),submitted=gpu.Select(x=>x.Submitted)});
            for(int i=0;i<gpu.Count;i++)Log(new{type="dxgi-statistics",name=names[i],samples=gpu[i].Statistics});
        } finally {cancel.Cancel();timerThread.Join();CloseHandle(waitTimer);foreach(var g in gpu)g.Dispose();foreach(var w in windows)w.Close();app.Shutdown();}
    }
    static void Headless(string mode,string scenario,double duration,string diagnostic,double hz)
    {
        var dispatcher=Dispatcher.CurrentDispatcher;var frame=new DispatcherFrame();var time=Stopwatch.StartNew();
        using var process=Process.GetCurrentProcess();var history=new DrivingTelemetryHistory();
        var origin=new DateTimeOffset(2026,9,12,0,0,0,TimeSpan.Zero);var gaps=new List<double>();
        int index=0,pending=0;double start=0,last=0;long allocated=0;TimeSpan cpu=default;int[] gc=new int[3];bool measuring=false;
        Log(new{type="start",mode,scenario,diagnostic,hz,pid=process.Id,uiNativeThread=GetCurrentThreadId(),utc=DateTimeOffset.UtcNow,windows=Array.Empty<object>()});
        using var cancel=new CancellationTokenSource();IntPtr timer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);
        if(timer==IntPtr.Zero)throw new InvalidOperationException("Timer unavailable");
        var worker=new Thread(()=>{long n=0;while(!cancel.IsCancellationRequested){double wait=++n/hz-time.Elapsed.TotalSeconds;
            if(wait>0){long due=-(long)(wait*10000000);SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false);WaitForSingleObject(timer,1000);}
            if(Interlocked.Exchange(ref pending,1)!=0)continue;
            dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>{try{double t=time.Elapsed.TotalSeconds;
                if(t>=3+duration){frame.Continue=false;return;}
                while(index<=Math.Floor(t*60)){double at=index++/60.0;if(diagnostic=="models")history.Add(new DrivingTelemetrySample(origin.AddSeconds(at),1,0,.5+.5*Math.Sin(at*2),.5+.5*Math.Cos(at*2),.5+.5*Math.Sin(at*.8),0,40+15*Math.Sin(at),3+(int)(at/3)%3,Math.Sin(at*2)>.8,Math.Sin(at)*.3,4000+2700*Math.Sin(at*.5),8000));}
                if(!measuring&&t>=3){measuring=true;start=t;cpu=process.TotalProcessorTime;allocated=GC.GetTotalAllocatedBytes(false);for(int i=0;i<3;i++)gc[i]=GC.CollectionCount(i);Log(new{type="measure",utc=DateTimeOffset.UtcNow});}
                if(measuring){if(last>0)gaps.Add((t-last)*1000);last=t;}
            }finally{Interlocked.Exchange(ref pending,0);}}));
        }}){IsBackground=true};worker.Start();
        try{Dispatcher.PushFrame(frame);cancel.Cancel();worker.Join();process.Refresh();double elapsed=time.Elapsed.TotalSeconds-start;
            Log(new{type="result",mode,scenario,diagnostic,hz,elapsed,utc=DateTimeOffset.UtcNow,cpuMachinePercent=(process.TotalProcessorTime-cpu).TotalSeconds/elapsed/Environment.ProcessorCount*100,workingSetMB=process.WorkingSet64/1048576.0,privateMB=process.PrivateMemorySize64/1048576.0,allocationMBps=(GC.GetTotalAllocatedBytes(false)-allocated)/1048576.0/elapsed,gc=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]),callbackHz=gaps.Count*1000/gaps.Sum()});
        }finally{cancel.Cancel();worker.Join();CloseHandle(timer);}
    }

}
