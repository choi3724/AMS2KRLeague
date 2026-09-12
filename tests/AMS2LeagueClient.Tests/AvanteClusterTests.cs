using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
                private static void AvantePairedSegments()
        {
            var view = new AvanteClusterView { Width = 908, Height = 750 };
            ConfigureReferenceN(view);
            var now = DateTimeOffset.UtcNow;
            byte[] Render(int rpm)
            {
                view.SetSample(new DrivingTelemetrySample(now, 1, 0, 0, 0, 0, 0, 0, 1, rpm: rpm, maxRpm: 8000));
                view.Measure(new Size(908,750)); view.Arrange(new Rect(0,0,908,750)); view.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(908,750,96,96,PixelFormats.Pbgra32);
                bitmap.Render(view); var pixels = new byte[908*750*4]; bitmap.CopyPixels(pixels,908*4,0); return pixels;
            }
            byte[] baseline = Render(0);
            double[] angles = { 156,181,206,231,257,384,359,334,309,283 };
            foreach (int rpm in new[] { 3999,4000,4250,4500,4999,5000,5999,6000,6500 })
            {
                byte[] pixels = Render(rpm); int pairs = new AvanteRpmScale(8000,5000,6000,true).LitPairs(rpm);
                for (int i=0;i<angles.Length;i++)
                {
                    double a=angles[i]*Math.PI/180; int cx=(int)(454+378*Math.Cos(a)), cy=(int)(397+378*Math.Sin(a));
                    int delta=0;
                    for(int y=cy-3;y<=cy+3;y++) for(int x=cx-3;x<=cx+3;x++) for(int c=0;c<3;c++)
                    { int p=(y*908+x)*4+c; delta=Math.Max(delta,Math.Abs(pixels[p]-baseline[p])); }
                    AssertTrue(i%5 < pairs ? delta > 25 : delta == 0);
                }
                CaptureLayout(view,"avante-pairs-"+rpm,1);
                Console.WriteLine("PROOF paired segment pixels rpm="+rpm+" left="+pairs+" right="+pairs+" band="+new AvanteRpmScale(8000,5000,6000,true).Band(rpm));
            }

        }

        private static void AvanteOuterGaugeCompletesAtRedline()
        {
            var view = new AvanteClusterView {Width=908,Height=750};
            ConfigureReferenceN(view,16000,14800.0*5/6,14800);
            byte[] Render(double rpm)
            {
                view.SetSample(new DrivingTelemetrySample(DateTimeOffset.UtcNow,1,0,0,0,0,0,20,1,rpm:rpm,maxRpm:14800));
                view.Measure(new Size(908,750));view.Arrange(new Rect(0,0,908,750));view.UpdateLayout();
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(908,750,96,96,PixelFormats.Pbgra32);
                bitmap.Render(view);var pixels=new byte[908*750*4];bitmap.CopyPixels(pixels,908*4,0);return pixels;
            }
            var dark=Render(0);
            var lit=Render(14800);
            CaptureLayout(view,"outer-gauge-16000-red14800",1);
            // Both last (top) blocks must light at redline even though the pointer has headroom.
            foreach(double angle in new[]{257.0,283.0})
            {
                int x=(int)(454+378*Math.Cos(angle*Math.PI/180)),y=(int)(397+378*Math.Sin(angle*Math.PI/180)),delta=0;
                for(int yy=y-3;yy<=y+3;yy++)for(int xx=x-3;xx<=x+3;xx++)for(int c=0;c<3;c++)
                {int offset=(yy*908+xx)*4+c;delta=Math.Max(delta,Math.Abs(lit[offset]-dark[offset]));}
                Console.WriteLine($"PROOF final outer block angle={angle} pixelDelta={delta}");
                AssertTrue(delta>25);
            }
            int builds=view.StaticFaceBuilds;
            foreach(double rpm in new[]{14799.0,14800,15000,16000,17000})Render(rpm);
            AssertEqual(builds,view.StaticFaceBuilds);
            foreach(double red in new[]{7400.0,14800})
            foreach(double headroom in new[]{200.0,1200,5200})
            {
                var scale=new AvanteRpmScale(red+headroom,red*5/6,red,true);
                AssertEqual(0,scale.LitPairs(0));
                AssertEqual(3,scale.LitPairs(scale.YellowStart));
                AssertEqual(4,scale.LitPairs((scale.YellowStart+red)/2));
                AssertEqual(4,scale.LitPairs(red-1));
                AssertEqual(5,scale.LitPairs(red));AssertEqual(5,scale.LitPairs(red+100));
                AssertEqual(5,scale.LitPairs(scale.Maximum));
                AssertTrue(scale.Angle(red)<390);AssertEqual(390.0,scale.Angle(scale.Maximum));
                AssertEqual(0,scale.LitPairs(double.NaN));AssertEqual(0,scale.LitPairs(-1));
            }
            var unknown=AvanteRpmScale.Resolve(null);
            AssertFalse(unknown.HasWarningThresholds);AssertEqual(2,unknown.LitPairs(4000));AssertEqual(5,unknown.LitPairs(8000));
            Console.WriteLine("PROOF outer gauge full at known redline independent of display maximum; pointer headroom/unknown fallback/static resources preserved");
        }

        private static void ConfigureReferenceN(AvanteClusterView view, double maximum=8000, double yellow=5000, double red=6000)
        {
            var settings = new DrivingHudSettings();
            settings.AvanteVehicles["reference-n"] = new AvanteRpmCalibration {Maximum=maximum,YellowStart=yellow,RedStart=red};
            view.ApplySettings(settings);
            view.SetSession(new TelemetrySnapshot(DateTimeOffset.UtcNow,14,3398,0,2,1,2,0,2,0,0,0,0,0,
                Array.Empty<ParticipantSnapshot>(),rootCarName:"reference-n"));
        }

        private static void AvanteReferenceEffects()
        {
            foreach (double maximum in new[]{8000.0,10000.0,12000.0})
            {
                var view=new AvanteClusterView {Width=908,Height=750};
                ConfigureReferenceN(view,maximum,6850,7400);
                var now=DateTimeOffset.UtcNow;
                void Set(double rpm)
                {
                    view.SetSample(new DrivingTelemetrySample(now,1,0,0,0,0,0,21/3.6,1,rpm:rpm,maxRpm:maximum));
                    view.Measure(new Size(908,750));view.Arrange(new Rect(0,0,908,750));view.UpdateLayout();
                }
                Set(3800);
                var numbers=(DrawingVisual)VisualTreeHelper.GetChild(view,5);
                var numeral=(DrawingVisual)numbers.Children[4];
                int builds=view.StaticFaceBuilds;
                foreach(double rpm in new[]{3900.0,3950,4000,4050,4100})
                {
                    Set(rpm);
                    AssertTrue(ReferenceEquals(numeral,numbers.Children[4]));
                    var scale=(ScaleTransform)numeral.Transform;
                    AssertTrue(Math.Abs(scale.ScaleX-(1+.2*Math.Max(0,1-Math.Abs(rpm-4000)/100)))<1e-9);
                    AssertEqual(scale.ScaleX,scale.ScaleY);
                    AssertEqual(builds,view.StaticFaceBuilds);
                    AssertEqual(view.RpmScale.Angle(rpm),view.NeedleAngle);
                    CaptureLayout(view,"n-emphasis-"+maximum+"-"+rpm,1);
                }
                foreach(double rpm in new[]{7000.0,7500})
                { Set(rpm);CaptureLayout(view,"n-source-ring-"+maximum+"-"+rpm,1); }
                view.SetSample(null);
                AssertEqual(1.0,((ScaleTransform)((DrawingVisual)numbers.Children[4]).Transform).ScaleX);
                AssertEqual(1.0,((ScaleTransform)((DrawingVisual)numbers.Children[8]).Transform).ScaleX);
            }
            var animated = new AvanteClusterView {Width=454,Height=375};
            ConfigureReferenceN(animated);
            var host=new Window {Content=animated,Width=474,Height=415,Left=-5000,Top=-5000,ShowActivated=false};
            try
            {
                host.Show();PumpDispatcher();
                var now=DateTimeOffset.UtcNow;
                animated.SetSample(new DrivingTelemetrySample(now,1,0,0,0,0,0,0,1,rpm:3900,maxRpm:8000));
                animated.SetSample(new DrivingTelemetrySample(now.AddMilliseconds(50),1,0,0,0,0,0,0,1,rpm:4100,maxRpm:8000));
                var clock=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(2)};
                var frame=new DispatcherFrame();var watch=Stopwatch.StartNew();int observations=0;
                clock.Tick+=(_,__) =>
                {
                    double shown=(animated.NeedleAngle-150)/240*8000;
                    var numbers=(DrawingVisual)VisualTreeHelper.GetChild(animated,5);
                    var transform=(ScaleTransform)((DrawingVisual)numbers.Children[4]).Transform;
                    AssertTrue(Math.Abs(transform.ScaleX-AvanteClusterView.NumberEmphasis(shown,4))<1e-8);
                    observations++;
                    if(watch.ElapsedMilliseconds>=180){clock.Stop();frame.Continue=false;}
                };
                clock.Start();Dispatcher.PushFrame(frame);AssertTrue(observations>1);
                AssertTrue(Math.Abs(animated.NeedleAngle-273)<1e-6);
                Console.WriteLine("PROOF live WPF interpolation needle/numeral agree; observations="+observations+"; not physical FPS");
            }
            finally {host.Close();}
            foreach(double maximum in new[]{3800.0,7200,14800})
            {
                var unknown=AvanteRpmScale.Resolve(maximum);
                AssertTrue(unknown.HasWarningThresholds);
                AssertEqual(maximum*.90,unknown.YellowStart);AssertEqual(maximum*.97,unknown.RedStart);
                AssertEqual(2,unknown.Band(maximum));
                AssertEqual(2,unknown.Band(maximum*2));
                AssertEqual(0,unknown.LitPairs(double.NaN));
                AssertEqual(5,unknown.LitPairs(unknown.Maximum));
            }
            AssertEqual(1.0,AvanteClusterView.NumberEmphasis(double.NaN,4));
            Console.WriteLine("PROOF 3900/3950/4000/4050/4100 = 1/1.1/1.2/1.1/1 retained glyph transforms, 8k/10k/12k, static rebuilds=0; valid engine maximum uses approved 90/97 policy");
        }

        private static void AvanteSpeedCenterAndFollower()
        {
            var view=new AvanteClusterView {Width=454,Height=375};
            ConfigureReferenceN(view);
            var now=DateTimeOffset.UtcNow;
            double height=0;
            foreach(int speed in new[]{0,1,2,3,4,5,6,7,8,9,10,11,29,33,100,101,111,113,123,214,329,888})
            {
                view.SetSample(new DrivingTelemetrySample(now,1,0,0,0,0,0,speed/3.6,1,rpm:2500,maxRpm:8000));
                view.Measure(new Size(454,375));view.Arrange(new Rect(0,0,454,375));view.UpdateLayout();
                var ink=((DrawingVisual)VisualTreeHelper.GetChild(view,8)).ContentBounds;
                AssertTrue(Math.Abs(ink.X+ink.Width/2-1024)<1.5);
                // Includes the existing 2px drop shadow below the centered ink.
                AssertTrue(Math.Abs(ink.Y+ink.Height/2-562)<2);
                if(height==0)height=ink.Height;else AssertTrue(Math.Abs(ink.Height-height)<.01);
                CaptureLayout(view,"speed-centered-"+speed,1);
                if(speed==214 || speed==329 || speed==29)
                {
                    foreach(double zoom in new[]{.5,1.5,2})
                    {
                        view.Width=454*zoom; view.Height=375*zoom;
                        view.Measure(new Size(view.Width,view.Height));view.Arrange(new Rect(0,0,view.Width,view.Height));view.UpdateLayout();
                        CaptureLayout(view,"speed-centered-"+speed+"-zoom-"+zoom.ToString(System.Globalization.CultureInfo.InvariantCulture),1);
                    }
                    view.Width=454;view.Height=375;
                    view.Measure(new Size(454,375));view.Arrange(new Rect(0,0,454,375));view.UpdateLayout();
                }
            }
            int builds=view.StaticFaceBuilds;
            foreach(int rpm in new[]{2500,4500,5500,6500})
            {
                view.SetSample(new DrivingTelemetrySample(now,1,0,0,0,0,0,123/3.6,1,rpm:rpm,maxRpm:8000));
                view.UpdateLayout();AssertEqual(builds,view.StaticFaceBuilds);
                CaptureLayout(view,"follower-gradient-"+rpm,1);
            }
            Console.WriteLine("PROOF speed 0..9/10/11/33/100/101/111/113/123/888 ink centers stable; equalHeight="+height+" fixedFontSize=108; follower RPM changes staticRebuilds=0");
        }

        private static void AvanteRetainedReadouts()
        {
            var view = new AvanteClusterView { Width = 454, Height = 375 };
            ConfigureReferenceN(view);
            var now = DateTimeOffset.UtcNow;
            var sample = new DrivingTelemetrySample(now, 1, 0, 0, 0, 0, 0, 100 / 3.6, 4, rpm: 2500, maxRpm: 8000);
            byte[] Render()
            {
                view.Measure(new Size(454,375)); view.Arrange(new Rect(0,0,454,375)); view.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(454,375,96,96,PixelFormats.Pbgra32);
                bitmap.Render(view); var pixels = new byte[454*375*4]; bitmap.CopyPixels(pixels,454*4,0); return pixels;
            }
            view.SetSample(sample); byte[] before = Render();
            for (int i=0; i<20; i++) view.SetSample(sample);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i=0; i<500; i++) view.SetSample(sample);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            AssertTrue(allocated < 2_000_000);
            byte[] unchanged = Render();
            AssertTrue(System.Linq.Enumerable.SequenceEqual(before, unchanged));
            view.SetSample(new DrivingTelemetrySample(now.AddMilliseconds(20), 1, 0, 0, 0, 0, 0, 101 / 3.6, 4, rpm: 2500, maxRpm: 8000));
            AssertFalse(System.Linq.Enumerable.SequenceEqual(before, Render()));
            view.SetSample(null);
            AssertEqual("—", view.SpeedText); AssertEqual("—", view.GearText);
            AssertFalse(System.Linq.Enumerable.SequenceEqual(before, Render()));
            Console.WriteLine("PROOF unchanged readout updates=500 allocatedBytes=" + allocated + " pixelsStable=true changedAndMissingValuesRendered=true");
        }

        private static void AvanteClusterLayoutsAndMotion()
        {
            AvanteRetainedReadouts();
            AvantePairedSegments();
            AssertEqual(0, new AvanteRpmScale(8000,5000,6000,true).Band(4999));
            AssertEqual(1, new AvanteRpmScale(8000,5000,6000,true).Band(5000));
            AssertEqual(1, new AvanteRpmScale(8000,5000,6000,true).Band(5999));
            AssertEqual(2, new AvanteRpmScale(8000,5000,6000,true).Band(6000));
            var defaults = new OverlayLayoutProfile(); defaults.SetDrivingPanelDefaults();
            AssertFalse(defaults.IsEnabled(OverlayComponentKeys.AvanteCluster));
            AssertFalse(defaults.IsEnabled(OverlayComponentKeys.AvanteClusterExpanded));
            defaults.SetEnabled(OverlayComponentKeys.AvanteCluster, true);
            defaults.Capture(OverlayComponentKeys.AvanteCluster, new OverlayBounds(100, 120, 454, 375), 1920, 1080);
            defaults.Capture(OverlayComponentKeys.AvanteClusterExpanded, new OverlayBounds(300, 450, 820, 300), 1920, 1080);
            AssertTrue(defaults.IsEnabled(OverlayComponentKeys.AvanteCluster));
            AssertFalse(defaults.IsEnabled(OverlayComponentKeys.AvanteClusterExpanded));
            AssertEqual(454, defaults.Resolve(OverlayComponentKeys.AvanteCluster, new OverlayBounds(0,0,72,48), 1920,1080).Width);
            AssertEqual(820, defaults.Resolve(OverlayComponentKeys.AvanteClusterExpanded, new OverlayBounds(0,0,72,48), 1920,1080).Width);
            foreach (bool expanded in new[] { false, true })
            {
                var view = new AvanteClusterView(expanded) { Width = expanded ? 1024 : 454, Height = 375 };
            ConfigureReferenceN(view);
                var now = DateTimeOffset.UtcNow;
                foreach (int rpm in new[] { 4500, 5500, 6500 })
                {
                    view.SetSample(new DrivingTelemetrySample(now, 1, 0, 0, .8, 0, 0, 200 / 3.6, 4, rpm: rpm, maxRpm: 8000), true);
                    view.Measure(new Size(view.Width, view.Height)); view.Arrange(new Rect(0,0,view.Width,view.Height)); view.UpdateLayout();
                    AssertEqual(expanded, view.Clip.FillContains(new Point(1, 1)));
                    AssertTrue(view.Clip.FillContains(new Point(view.Width / 2, view.Height / 2)));
                    AssertTrue(view.Clip.FillContains(new Point(view.Width / 2, view.Height - 10)));
                    var pixels = new System.Windows.Media.Imaging.RenderTargetBitmap((int)view.Width, (int)view.Height, 96, 96, PixelFormats.Pbgra32);
                    pixels.Render(view);
                    var corner = new byte[4]; pixels.CopyPixels(new Int32Rect(1, 1, 1, 1), corner, 4, 0);
                    AssertEqual(expanded ? (byte)255 : (byte)0, corner[3]);
                    AssertEqual("200", view.SpeedText); AssertEqual("4", view.GearText);
                    AssertEqual(150 + rpm / 8000.0 * 240, view.NeedleAngle);
                    CaptureLayout(view, "avante-" + (expanded ? "expanded" : "normal") + "-" + rpm, 2);
                }
                view.SetSample(new DrivingTelemetrySample(now, 1, 0, 0, 0, 0, 0, 0, 0, rpm: 900, maxRpm: 8000));
                AssertEqual("N", view.GearText);
                CaptureLayout(view, "avante-neutral-" + expanded, 2);
                view.SetSample(null); AssertEqual("—", view.SpeedText); AssertEqual("—", view.GearText);
                CaptureLayout(view, "avante-missing-" + expanded, 2);
            }
            var animated = new AvanteClusterView { Width = 454, Height = 375 };
            ConfigureReferenceN(animated);
            var host = new Window { Content = animated, Width = 474, Height = 415, Left = -5000, Top = -5000, ShowActivated = false };
            try
            {
                host.Show(); PumpDispatcher();
                var start = DateTimeOffset.UtcNow;
                animated.SetSample(new DrivingTelemetrySample(start, 1, 0, 0, 0, 0, 0, 0, 1, rpm: 1000, maxRpm: 8000));
                animated.SetSample(new DrivingTelemetrySample(start.AddMilliseconds(50), 1, 0, 0, 1, 0, 0, 20, 2, rpm: 6000, maxRpm: 8000));
                AssertTrue(animated.NeedleAngle < 330);
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (_, __) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
                AssertTrue(Math.Abs(animated.NeedleAngle - 330) < .01);
                AssertTrue(animated.IsRedFlashing);
                var phases = new System.Collections.Generic.HashSet<bool>();
                var flashFrame = new DispatcherFrame();
                var flashWatch = Stopwatch.StartNew();
                var probe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                probe.Tick += (_, __) =>
                {
                    if (phases.Add(animated.RedFlashOn)) CaptureLayout(animated, "avante-flash-" + animated.RedFlashOn, 1);
                    if (flashWatch.ElapsedMilliseconds >= 500) { probe.Stop(); flashFrame.Continue = false; }
                };
                probe.Start(); Dispatcher.PushFrame(flashFrame);
                AssertEqual(2, phases.Count);
                host.Hide(); AssertFalse(animated.IsRedFlashing);
                host.Show(); AssertTrue(animated.IsRedFlashing);
                animated.SetSample(new DrivingTelemetrySample(start.AddMilliseconds(180), 1, 0, 0, 1, 0, 0, 50, 4, rpm: 5999, maxRpm: 8000));
                AssertFalse(animated.IsRedFlashing);
                Console.WriteLine("PROOF red flash both phases; hidden/below-red stop; normal outline corners transparent and footer retained");
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 600; i++)
                    animated.SetSample(new DrivingTelemetrySample(start.AddMilliseconds(200+i*17), 1, 0, 0, 1, 0, 0, 50, 4, rpm: 4000+i%2000, maxRpm:8000));
                Console.WriteLine("PROOF Avante 600 display updates CPU meanMs=" + (watch.Elapsed.TotalMilliseconds/600).ToString("F3") + "; not game-load FPS");
            }
            finally { host.Close(); }
            Console.WriteLine("PROOF Avante both layouts, missing values, N gear, 4999/5000/5999/6000 bands and animated needle checked");
        }
    }
}
