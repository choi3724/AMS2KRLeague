using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void InterruptedHudMotionResumesObservedTarget()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(PedalTelemetryView).Assembly.GetType("AMS2LeagueClient.Presentation.HudTargetMotion")!;
            double presented = -1;
            var motion = Activator.CreateInstance(type, flags, null,
                new object[] { 35.0, (Action<double>)(value => presented = value) }, null)!;
            var set = type.GetMethod("Set", flags)!;
            var stop = type.GetMethod("Stop", flags)!;
            try
            {
                set.Invoke(motion, new object[] { 0.0, false });
                set.Invoke(motion, new object[] { 1.0, true });
                // Hide/unload can interrupt before the first presentation frame.
                stop.Invoke(motion, null);
                AssertEqual(0.0, presented);
                set.Invoke(motion, new object[] { 1.0, true });
                AssertEqual(1.0, presented);
                AssertFalse((bool)type.GetProperty("IsActive", flags)!.GetValue(motion)!);
                set.Invoke(motion, new object[] { 0.0, true });
                // Stale/reset is discontinuous and cancels any remaining motion immediately.
                set.Invoke(motion, new object[] { 0.0, false });
                AssertEqual(0.0, presented);
                AssertFalse((bool)type.GetProperty("IsActive", flags)!.GetValue(motion)!);
                set.Invoke(motion, new object[] { 0.6, false });
                AssertEqual(0.6, presented);
                Console.WriteLine("PROOF interrupted 35ms HUD motion recovers unchanged target; stale/reset cancels residual motion");
            }
            finally { stop.Invoke(motion, null); }
        }

        private static void RenderRemediationRetainsPathsAndStopsWork()
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var motionType = typeof(PedalTelemetryView).Assembly.GetType("AMS2LeagueClient.Presentation.HudTargetMotion")!;
            double presented = 0;
            var motion = Activator.CreateInstance(motionType, flags, null, new object[]{65.0, (Action<double>)(v=>presented=v)}, null)!;
            var setTarget = motionType.GetMethod("Set",flags)!;
            setTarget.Invoke(motion,new object[]{0.0,false});
            setTarget.Invoke(motion,new object[]{1.0,true});
            setTarget.Invoke(motion,new object[]{2.0,true});
            AssertTrue((bool)motionType.GetProperty("IsActive",flags)!.GetValue(motion)!);
            setTarget.Invoke(motion,new object[]{3.0,false});
            AssertEqual(3.0,presented);
            AssertFalse((bool)motionType.GetProperty("IsActive",flags)!.GetValue(motion)!);
            Console.WriteLine("PROOF target updates reuse target follower; inactive motion stops");

            var clockType = typeof(PedalTelemetryView).Assembly.GetType("AMS2LeagueClient.Presentation.MonitorPresentationClock")!;
            var staticFlags = BindingFlags.NonPublic | BindingFlags.Static;
            var subscribe = clockType.GetMethod("Subscribe", staticFlags)!;
            var unsubscribe = clockType.GetMethod("Unsubscribe", staticFlags)!;
            int delivered = 0;
            EventHandler callback = (_, __) => delivered++;
            AssertTrue((bool)subscribe.Invoke(null, new object[]{callback})!);
            var clockFrame = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timeout.Tick += (_, __) => { timeout.Stop(); clockFrame.Continue = false; };
            try { timeout.Start(); Dispatcher.PushFrame(clockFrame); }
            finally { timeout.Stop(); unsubscribe.Invoke(null, new object[]{callback}); }
            AssertTrue(delivered >= 2);
            int stoppedAt = delivered; PumpDispatcher();
            AssertEqual(stoppedAt, delivered);
            AssertFalse((bool)clockType.GetProperty("IsRunning", staticFlags)!.GetValue(null)!);
            // Immediate stop/restart must not invoke a stale queued callback on the new run.
            for (int i=0;i<3;i++) { AssertTrue((bool)subscribe.Invoke(null,new object[]{callback})!); unsubscribe.Invoke(null,new object[]{callback}); }
            PumpDispatcher(); AssertEqual(stoppedAt,delivered);
            Console.WriteLine("PROOF shared Monitor clock delivers callbacks, stops when idle, and cancels stale dispatch; callback count is not FPS");

            var cacheType = typeof(PedalTelemetryView).Assembly.GetType("AMS2LeagueClient.Presentation.PedalCurveCache")!;
            var cache = Activator.CreateInstance(cacheType, true)!;
            var update = cacheType.GetMethod("Update", flags)!;
            var processed = cacheType.GetProperty("SamplesProcessed", flags)!;
            var curve = cacheType.GetMethod("Drawing", flags)!;
            var inspectionPen = new Pen(Brushes.Red,0); inspectionPen.Freeze();
            var history = new DrivingTelemetryHistory(); var start = FixedTime();
            DrivingTelemetrySample Sample(int index, int generation = 1) => new DrivingTelemetrySample(start.AddSeconds(index / 240.0), generation, 0,
                .5 + .5 * Math.Sin(index * .02), .5, 0, 0, 40, 4, index % 50 < 10, .2, 5000, 8000);
            for (int i=0;i<=2400;i++) history.Add(Sample(i));
            void Refresh() => update.Invoke(cache,new object[]{history,450.0,100.0});
            Refresh(); var retained = (DrawingGroup)curve.Invoke(cache,new object[]{0,inspectionPen})!;
            AssertEqual(2401L,(long)processed.GetValue(cache)!);
            for (int i=2401;i<=4800;i++) { history.Add(Sample(i)); Refresh(); }
            AssertEqual(4801L,(long)processed.GetValue(cache)!);
            AssertTrue(ReferenceEquals(retained,curve.Invoke(cache,new object[]{0,inspectionPen})));
            AssertTrue(retained.Children.Count < 50);
            AssertTrue(retained.Children.Take(retained.Children.Count-1).All(path=>path.IsFrozen));
            var recolor = new Pen(Brushes.Blue,3); recolor.Freeze();
            curve.Invoke(cache,new object[]{0,recolor});
            AssertTrue(retained.Children.Take(retained.Children.Count-1).Cast<GeometryDrawing>()
                .All(d=>d.Geometry is StreamGeometry && d.Pen == null && ReferenceEquals(d.Brush,recolor.Brush)));
            AssertTrue(ReferenceEquals(((GeometryDrawing)retained.Children.Last()).Pen,recolor));
            AssertTrue(retained.Children.Take(retained.Children.Count-1).All(d=>d.IsFrozen));
            history.MarkStale(); Refresh(); AssertEqual(4801L,(long)processed.GetValue(cache)!);
            history.Add(Sample(4900,2)); Refresh(); AssertEqual(1,history.Count);
            AssertTrue(retained.Children.Count <= 1);
            history.Clear(); Refresh(); AssertEqual(0,retained.Children.Count);
            history.Add(Sample(5000,3)); Refresh(); history.Add(Sample(5001,3)); Refresh();
            AssertTrue(Math.Abs(((GeometryDrawing)retained.Children[0]).Geometry.Bounds.Right + retained.Transform.Value.OffsetX-450) < .01); // Clear then first sample reanchors time; no year-one coordinates.
            Console.WriteLine("PROOF incremental graph initial=2401 appended=2400 processed=4801 retained path; bounded chunks; stale/reset preserved");
            var compileType=typeof(PedalTelemetryView).Assembly.GetType("AMS2LeagueClient.Presentation.RetainedGeometry")!;
            var source=Geometry.Parse("M0,0 L10,0 Q15,10 10,20 L0,20 Z M3,3 L3,8 L7,8 L7,3 Z").Clone();
            source.Transform=new TranslateTransform(17,9);
            var compiled=(StreamGeometry)compileType.GetMethod("Compile",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{source})!;
            AssertTrue(compiled.IsFrozen); AssertEqual(source.Bounds,compiled.Bounds);
            AssertTrue(Math.Abs(source.GetArea()-compiled.GetArea())<.001);
            Console.WriteLine("PROOF compiled geometry retains transformed contours, holes and area");


            var cluster = new AvanteClusterView { Width=340, Height=281 };
            var clusterWindow = new Window { Content=cluster, Width=340, Height=281, Left=-5000, Top=-5000, ShowActivated=false };
            try
            {
                cluster.SetSample(Sample(0)); clusterWindow.Show(); PumpDispatcher();
                var rasterCount = typeof(AvanteClusterView).GetField("_faceRasterizations",flags)!;
                int count = (int)rasterCount.GetValue(cluster)!; AssertTrue(count>0);
                cluster.SetSample(Sample(1)); PumpDispatcher();
                AssertEqual(count,(int)rasterCount.GetValue(cluster)!);
                Console.WriteLine("PROOF RPM target does not rasterize the static face again");
                int GearRebuilds() => (int)typeof(AvanteClusterView).GetField("_gearRebuilds",flags)!.GetValue(cluster)!;
                int StatusRebuilds() => (int)typeof(AvanteClusterView).GetField("_statusRebuilds",flags)!.GetValue(cluster)!;
                int SpeedRebuilds() => (int)typeof(AvanteClusterView).GetField("_speedRebuilds",flags)!.GetValue(cluster)!;
                var observed = new DrivingTelemetrySample(start,1,0,.2,.3,0,0,41,4,false,.2,5000,8000);
                cluster.SetSample(observed); int gearCount=GearRebuilds(),statusCount=StatusRebuilds(),speedCount=SpeedRebuilds();
                cluster.SetSample(new DrivingTelemetrySample(start.AddMilliseconds(17),1,0,.2,.3,0,0,42,4,false,.2,5100,8000));
                AssertEqual(gearCount,GearRebuilds()); AssertEqual(statusCount,StatusRebuilds()); AssertEqual(speedCount+1,SpeedRebuilds());
                cluster.SetSample(new DrivingTelemetrySample(start.AddMilliseconds(34),1,0,.2,.3,0,0,42,4,false,.2,5200,8000));
                AssertEqual(speedCount+1,SpeedRebuilds());
                Console.WriteLine("PROOF speed-only change preserves gear/status retained layers; same displayed speed does not rebuild");

            }
            finally { clusterWindow.Close(); }
            var gaugeView = new PedalTelemetryView(true) { Width=140,Height=120 };
            var gaugeWindow = new Window { Content=gaugeView,Width=140,Height=120,Left=-5000,Top=-5000,ShowActivated=false };
            try
            {
                gaugeWindow.Show(); PumpDispatcher(); var gaugeHistory=new DrivingTelemetryHistory();
                gaugeHistory.Add(new DrivingTelemetrySample(start,1,0,0,0,0,0,40,4));gaugeView.SetHistory(gaugeHistory);
                gaugeHistory.Add(new DrivingTelemetrySample(start.AddMilliseconds(17),1,0,1,1,1,1,40,4));gaugeView.SetHistory(gaugeHistory);
                var graphObject=typeof(PedalTelemetryView).GetField("_graph",flags)!.GetValue(gaugeView)!;
                var barMotions=(Array)graphObject.GetType().GetField("_barMotion",flags)!.GetValue(graphObject)!;
                AssertTrue(barMotions.Cast<object>().All(m=>(bool)motionType.GetProperty("IsActive",flags)!.GetValue(m)!));
                AssertEqual(1.0,gaugeHistory.Current!.Pedals[0]!.Value);
                gaugeWindow.Hide();
                AssertTrue(barMotions.Cast<object>().All(m=>!(bool)motionType.GetProperty("IsActive",flags)!.GetValue(m)!));
                Console.WriteLine("PROOF pedal display interpolates independently of observed values and hiding stops all bar followers");
            }
            finally { gaugeWindow.Close(); }
            WithTemporaryDirectory(directory=>{
                using var logger = new FileLogger(directory);
                var overlay = new OverlayWindow(false,Path.Combine(directory,"layout.json"));
                var coordinator = new PlayerOverlayCoordinator(overlay,new ClientStatusViewModel(),logger,false);
                var coordinatorType = typeof(PlayerOverlayCoordinator);
                var subscription = coordinatorType.GetMethod("UpdateDrivingSubscription",flags)!;
                bool Subscribed() => (bool)coordinatorType.GetField("_drivingSubscribed",flags)!.GetValue(coordinator)!;
                try
                {
                    foreach (var key in OverlayComponentKeys.All) overlay.SetComponentEnabled(key,key==OverlayComponentKeys.PedalGauge);
                    overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false),false);
                    overlay.ShowDemoAt(-5000,-5000,96); PumpDispatcher();
                    coordinatorType.GetField("_processId",flags)!.SetValue(coordinator,1);
                    subscription.Invoke(coordinator,new object?[]{null,EventArgs.Empty}); AssertTrue(Subscribed());
                    var gauge = (PedalTelemetryView)typeof(OverlayWindow).GetField("_pedalGaugeView",flags)!.GetValue(overlay)!;
                    AssertNull(typeof(PedalTelemetryView).GetField("_wheel",flags)!.GetValue(gauge));
                    overlay.SetComponentOpacity(OverlayComponentKeys.PedalGauge,0);
                    subscription.Invoke(coordinator,new object?[]{null,EventArgs.Empty});
                    AssertFalse(overlay.WantsDrivingTelemetry); AssertFalse(Subscribed());
                    overlay.SetComponentOpacity(OverlayComponentKeys.PedalGauge,1);
                    subscription.Invoke(coordinator,new object?[]{null,EventArgs.Empty}); AssertTrue(Subscribed());
                    overlay.HideOverlay(); subscription.Invoke(coordinator,new object?[]{null,EventArgs.Empty}); AssertFalse(Subscribed());
                    overlay.StartVr(_=>{});
                    var timer=(DispatcherTimer)typeof(OverlayWindow).GetField("_vrTimer",flags)!.GetValue(overlay)!;
                    AssertFalse(timer.IsEnabled); overlay.StopVr(); overlay.StartVr(_=>{}); AssertFalse(timer.IsEnabled);
                    Console.WriteLine("PROOF gauge has no hidden wheel; transparency/hide suspend render subscription; Monitor VR timer off across restart (code fixture only)");
                }
                finally {coordinator.Dispose();overlay.Close();}
            });
            var gallery = new ClientStatusWindow(new ClientStatusViewModel()){Left=-5000,Top=-5000,ShowActivated=false};
            try
            {
                gallery.Show(); PumpDispatcher();
                var images=Descendants<Image>((FrameworkElement)gallery.Content).ToArray();
                AssertTrue(images.Length>0 && images.All(i=>i.Source!=null));
                gallery.Hide(); AssertTrue(images.All(i=>i.Source==null));
                gallery.Show(); PumpDispatcher(); AssertTrue(images.All(i=>i.Source!=null));
                gallery.WindowState=WindowState.Minimized; PumpDispatcher(); AssertTrue(images.All(i=>i.Source==null));
            }
            finally {gallery.Close();}
        }
    }
}
