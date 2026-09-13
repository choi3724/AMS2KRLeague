using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void AvanteRedlineChatter()
        {
            var view = new AvanteClusterView { Width=454, Height=375 };
            var now = DateTimeOffset.UtcNow;
            view.SetSession(new TelemetrySnapshot(now,14,3398,0,2,1,2,0,2,0,0,0,0,0,
                new[]{new ParticipantSnapshot(0,true,"",1,0,1,1,2,0,0,0,vehicleName:AvanteRpmScale.LolaSuperspeedway)},rootCarName:"chatter"));
            var settings = new DrivingHudSettings();
            settings.AvanteVehicles["chatter"] = new AvanteRpmCalibration {Maximum=16000,YellowStart=14800.0*5/6,RedStart=14800};
            view.ApplySettings(settings); AssertEqual(16000.0,view.RpmScale.Maximum);
            void Sample(double rpm) => view.SetSample(new DrivingTelemetrySample(DateTimeOffset.UtcNow,1,0,0,0,0,0,20,1,rpm:rpm,maxRpm:14800));
            Sample(15100);
            var host = new Window {Content=view,Width=474,Height=415,Left=-5000,Top=-5000,ShowActivated=false};
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input) {Interval=TimeSpan.FromMilliseconds(16)};
            try
            {
                host.Show(); PumpDispatcher();
                int builds=view.StaticFaceBuilds, count=0, dim=0, intermediate=0, bright=0, captures=0;
                var red=(System.Windows.Media.DrawingVisual)System.Windows.Media.VisualTreeHelper.GetChild(view,0);
                var rows=new System.Collections.Generic.List<string>{"elapsed_ms,raw_rpm,opacity"};
                var clock=System.Diagnostics.Stopwatch.StartNew();
                var frame=new System.Windows.Threading.DispatcherFrame();
                timer.Tick += (_,__) =>
                {
                    double rpm=count++%2==0 ? 14750 : 15100;
                    Sample(rpm);
                    double opacity=red.Opacity;
                    if(opacity<.25)dim++; else if(opacity>.9)bright++; else intermediate++;
                    rows.Add(FormattableString.Invariant($"{clock.Elapsed.TotalMilliseconds:F3},{rpm},{opacity:F6}"));
                    if(_layoutCaptureDirectory!=null && clock.ElapsedMilliseconds>=captures*100 && captures<12)
                        CaptureLayout(view,"redline-chatter-"+(captures++).ToString("D2"),1);
                    if(clock.ElapsedMilliseconds>=1200){timer.Stop();frame.Continue=false;}
                };
                timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
                if(_layoutCaptureDirectory!=null)System.IO.File.WriteAllLines(System.IO.Path.Combine(_layoutCaptureDirectory,"redline-chatter.csv"),rows);
                Console.WriteLine($"PROOF redline chatter samples={count} dim={dim} intermediate={intermediate} bright={bright} staticRebuilds={view.StaticFaceBuilds-builds}");
                AssertTrue(dim>0); AssertTrue(bright>0); AssertTrue(intermediate>0);
                AssertEqual(builds,view.StaticFaceBuilds);
                view.SetSample(null); AssertFalse(view.IsRedFlashing); AssertEqual(1.0,red.Opacity);
                Sample(15100); PumpDispatcher(); AssertTrue(view.IsRedFlashing);
                view.Opacity=0; PumpDispatcher(); Sample(15100); AssertFalse(view.IsRedFlashing);
                view.Opacity=1; Sample(15100); PumpDispatcher(); AssertTrue(view.IsRedFlashing);
                host.Hide(); AssertFalse(view.IsRedFlashing);
            }
            finally { timer.Stop(); host.Close(); }
        }

        private static void AvanteReferenceWarningsRestore()
        {
            var now=DateTimeOffset.UtcNow;
            var view=new AvanteClusterView {Width=908,Height=750};
            DrivingTelemetrySample Sample(double rpm,double max) => new DrivingTelemetrySample(now,1,0,0,0,0,0,329/3.6,4,rpm:rpm,maxRpm:max);
            void Session(string car) => view.SetSession(new TelemetrySnapshot(now,14,3398,0,2,1,2,0,2,0,0,0,0,0,
                new[]{new ParticipantSnapshot(0,true,"",1,0,1,1,2,0,0,0,vehicleName:car)},rootCarName:"current-root"));
            void Layout(){view.Measure(new Size(908,750));view.Arrange(new Rect(0,0,908,750));view.UpdateLayout();}
            foreach(var item in new[]{(AvanteRpmScale.LolaSuperspeedway,14800.0,16000.0,14356.0),(AvanteRpmScale.IvecoStralis,3800.0,5000.0,3686.0), (AvanteRpmScale.ArcCamaro,11500.0,13000.0,11155.0)})
            {
                view=new AvanteClusterView {Width=908,Height=750};
                Session(item.Item1);view.SetSample(Sample(0,item.Item2));Layout();
                var scale=view.RpmScale;
                AssertTrue(scale.HasWarningThresholds);AssertFalse(scale.VehicleProfile);
                AssertEqual(item.Item4,scale.RedStart);AssertEqual(item.Item3,scale.Maximum);AssertEqual(item.Item2*.90,scale.YellowStart);
                AssertEqual(scale,AvanteRpmScale.Resolve(item.Item2,null,"unlisted-car"));
                AssertEqual(19400.0,AvanteRpmScale.Resolve(20000,null,item.Item1).RedStart);
                AssertTrue(scale.SourceDescription.Contains("90%") && scale.SourceDescription.Contains("97%"));
                AssertTrue(scale.SourceDescription.Contains("게임이 직접 제공한 경계값이 아닙니다"));
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(908,750,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(view);var pixels=new byte[908*750*4];bitmap.CopyPixels(pixels,908*4,0);
                (int R,int G,int B) Pixel(double rpm)
                {
                    double angle=scale.Angle(rpm)*Math.PI/180;
                    int x=(int)Math.Round(454+240*Math.Cos(angle)),y=(int)Math.Round(397+240*Math.Sin(angle)),p=(y*908+x)*4;
                    return(pixels[p+2],pixels[p+1],pixels[p]);
                }
                var yellow=Pixel((scale.YellowStart+scale.RedStart)/2);
                var red=Pixel((scale.RedStart+scale.Maximum)/2);
                Console.WriteLine("PROOF warning pixels yellow="+yellow+" red="+red);
                CaptureLayout(view,"restored-"+item.Item2+"-background",1);
                var uncoloured = new AvanteClusterView {Width=908,Height=750};
                uncoloured.SetSample(Sample(0,0));uncoloured.Measure(new Size(908,750));uncoloured.Arrange(new Rect(0,0,908,750));uncoloured.UpdateLayout();
                bitmap.Clear();bitmap.Render(uncoloured);bitmap.CopyPixels(pixels,908*4,0);
                var originalYellow=Pixel((scale.YellowStart+scale.RedStart)/2);
                AssertTrue(yellow.R>originalYellow.R+20 && yellow.G>originalYellow.G);
                AssertTrue(red.R>red.G && red.R>red.B);
                int builds=view.StaticFaceBuilds;
                foreach(double rpm in new[]{scale.YellowStart-1,scale.YellowStart,scale.RedStart-1,scale.RedStart})
                {view.SetSample(Sample(rpm,item.Item2));Layout();AssertEqual(builds,view.StaticFaceBuilds);}
                AssertEqual(0,scale.Band(scale.YellowStart-1));AssertEqual(1,scale.Band(scale.YellowStart));
                AssertEqual(1,scale.Band(scale.RedStart-1));AssertEqual(2,scale.Band(scale.RedStart));
                var host=new Window {Content=view,Width=928,Height=790,Left=-5000,Top=-5000,ShowActivated=false};
                try
                {
                    host.Show();PumpDispatcher();AssertTrue(view.IsRedFlashing);
                    var phases=new System.Collections.Generic.HashSet<bool>();
                    var frame=new System.Windows.Threading.DispatcherFrame();var watch=System.Diagnostics.Stopwatch.StartNew();
                    var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
                    timer.Tick+=(_,__) => {
                        if(phases.Add(view.RedFlashOn))CaptureLayout(view,"restored-"+item.Item2+"-flash-"+view.RedFlashOn,1);
                        if(watch.ElapsedMilliseconds>=400){timer.Stop();frame.Continue=false;}
                    };
                    timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);AssertEqual(2,phases.Count);
                    host.Hide();AssertFalse(view.IsRedFlashing);
                }
                finally{host.Content=null;host.Close();}
                view.SetSample(Sample(item.Item2==14800 ? 13371 : item.Item2==11500 ? 9870 : 753,item.Item2==11500 ? 11500 : item.Item2));Layout();
                CaptureLayout(view,"restored-"+item.Item2+"-reference",1);
                Console.WriteLine("PROOF common policy warning red="+item.Item4+" max="+item.Item3+" yellow="+scale.YellowStart+" both backgrounds have coloured pixels, flash both phases, source approved 90/97 display policy; live game NOT TESTED");
            }
            Session(AvanteRpmScale.LolaSuperspeedway);view.SetSample(Sample(0,14800));Layout();
            var animationHost=new Window {Content=view,Width=928,Height=790,Left=-5000,Top=-5000,ShowActivated=false};
            try
            {
                animationHost.Show();PumpDispatcher();
                var property=(DependencyProperty)typeof(AvanteClusterView).GetField("RpmPositionProperty",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                var numbers=(System.Windows.Media.DrawingVisual)System.Windows.Media.VisualTreeHelper.GetChild(view,5);
                for(int numeral=1;numeral<=15;numeral++)
                {
                    // Synthetic consecutive presentation positions, not a claim about game FPS.
                    view.SetSample(null);view.SetSample(Sample(numeral*1000-250,14800));Layout();
                    view.SetValue(property,numeral*1000+250.0);
                    var node=(System.Windows.Media.DrawingVisual)numbers.Children[numeral];
                    AssertEqual(1.2,((System.Windows.Media.ScaleTransform)node.Transform).ScaleX);
                    CaptureLayout(view,"crossed-numeral-"+numeral,1);
                }
                var frame=new System.Windows.Threading.DispatcherFrame();
                var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(160)};
                timer.Tick+=(_,__)=>{timer.Stop();frame.Continue=false;};
                timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
                AssertEqual(1.0,((System.Windows.Media.ScaleTransform)((System.Windows.Media.DrawingVisual)numbers.Children[15]).Transform).ScaleX);
                view.SetSample(null);
                foreach(System.Windows.Media.DrawingVisual node in numbers.Children)
                    AssertEqual(1.0,((System.Windows.Media.ScaleTransform)node.Transform).ScaleX);
                Console.WriteLine("PROOF fast frame crossing numerals1..15 all reach1.2; pulse decays/reset; retained shared clock, not physical FPS");
            }
            finally{animationHost.Content=null;animationHost.Close();}
            // Detached hidden views intentionally defer raster work. Validate the switch on a live WPF surface.
            var switchHost=new Window {Content=view,Width=928,Height=790,Left=-5000,Top=-5000,ShowActivated=false};
            try
            {
                switchHost.Show();PumpDispatcher();
            var settings=new DrivingHudSettings();settings.AvanteVehicles["current-root"]=new AvanteRpmCalibration {Maximum=16000,YellowStart=13000,RedStart=15000};
            view.ApplySettings(settings);Session(AvanteRpmScale.LolaSuperspeedway);Layout();PumpDispatcher();
            AssertEqual(15000.0,view.RpmScale.RedStart);AssertEqual(16000.0,view.RpmScale.Maximum);
            Session(AvanteRpmScale.ArcCamaro);Layout();PumpDispatcher();AssertEqual(15000.0,view.RpmScale.RedStart);
            view.ApplySettings(new DrivingHudSettings());Session(AvanteRpmScale.ArcCamaro);
            view.SetSample(null);view.SetSample(Sample(9870,11500));Layout();PumpDispatcher();
            AssertEqual(11155.0,view.RpmScale.RedStart);AssertEqual(13000.0,view.RpmScale.Maximum);
            AssertTrue(Math.Abs(view.RpmScale.YellowStart-10350)<.00001);
            AssertEqual(view.RpmScale.Angle(9870),view.NeedleAngle);
            AssertEqual(14,((System.Windows.Media.DrawingVisual)System.Windows.Media.VisualTreeHelper.GetChild(view,5)).Children.Count);
            CaptureLayout(view,"arc-camaro-switched-9870",1);
            view.ApplySettings(new DrivingHudSettings());Session("unconfirmed-other");Layout();PumpDispatcher();AssertFalse(view.RpmScale.HasWarningThresholds);
            Session(AvanteRpmScale.LolaSuperspeedway);view.SetSample(Sample(13371,14800));Layout();PumpDispatcher();AssertEqual(14356.0,view.RpmScale.RedStart);
            view.SetSample(null);AssertEqual(14356.0,view.RpmScale.RedStart);AssertFalse(view.IsRedFlashing);
            }
            finally {switchHost.Content=null;switchHost.Close();}
        }

        private static void AvanteRecordedVehicleProfile()
        {
            // Name from the local 2026-09-12 capture; 1821 RPM and dial boundaries from
            // the user's same-car AMS2 HUD reference. This is NOT a live game measurement.
            const string car = AvanteRpmScale.AstonMartinLowDownforce;
            var now = DateTimeOffset.UtcNow;
            ParticipantSnapshot Participant(int index, string name) => new ParticipantSnapshot(index,true,"",1,0,1,1,2,0,0,0,vehicleName:name);
            TelemetrySnapshot Session(string name) => new TelemetrySnapshot(now,14,3398,0,2,1,2,0,2,0,0,0,0,0,
                new[] { Participant(1,car), Participant(0,name) },rootCarName:"root-alias");
            DrivingTelemetrySample Sample(double rpm, double engine = 7200, int generation = 1)
                => new DrivingTelemetrySample(now,generation,0,0,0,0,0,33/3.6,1,rpm:rpm,maxRpm:engine);
            var session = Session(car);
            AssertEqual(car,AvanteRpmScale.ProfileVehicleName(session));
            AssertEqual("unconfirmed",AvanteRpmScale.ProfileVehicleName(Session("unconfirmed")));
            foreach (var item in new[] { (6800.0,8000.0), (6999.99,8000.0), (7000.0,8000.0), (7000.01,9000.0), (7400.0,9000.0), (8400.0,10000.0), (10500.0,12000.0), (8000.0,9000.0), (14800.0,16000.0) })
            {
                var automatic = new AvanteRpmCalibration {AutomaticMaximum=true,Maximum=item.Item1 >= 12000 ? 20000 : 12000,YellowStart=6000,RedStart=item.Item1};
                AssertTrue(automatic.IsValid);
                var resolved=AvanteRpmScale.Resolve(7200,automatic);
                AssertEqual(item.Item2,resolved.Maximum); AssertEqual(6000.0,resolved.YellowStart);AssertEqual(item.Item1,resolved.RedStart);
                AssertTrue(resolved.Angle(resolved.RedStart)<390); AssertTrue(resolved.AutomaticMaximum);
                AssertTrue(resolved.Maximum - resolved.RedStart >= 1000);
                AssertTrue(resolved.Maximum - 1000 < resolved.RedStart + 1000);
                foreach (bool expanded in new[] { false, true })
                {
                    var rangeView = new AvanteClusterView(expanded) {Width=expanded ? 1024 : 454,Height=375};
                    var rangeSettings = new DrivingHudSettings(); rangeSettings.AvanteVehicles["root-alias"]=automatic;
                    rangeView.ApplySettings(rangeSettings);rangeView.SetSession(session);rangeView.SetSample(Sample(item.Item1));
                    rangeView.Measure(new Size(rangeView.Width,375));rangeView.Arrange(new Rect(0,0,rangeView.Width,375));rangeView.UpdateLayout();
                    AssertEqual(resolved.Angle(item.Item1),rangeView.NeedleAngle);
                    AssertEqual(5,rangeView.RpmScale.LitPairs(item.Item1));
                    int staticBuilds=rangeView.StaticFaceBuilds;
                    foreach(double rpm in new[]{item.Item1-1,item.Item1,item.Item1+1})
                    {rangeView.SetSample(Sample(rpm));AssertEqual(staticBuilds,rangeView.StaticFaceBuilds);}
                    rangeView.SetSample(Sample(item.Item1));
                    CaptureLayout(rangeView,"rpm-auto-red-"+item.Item1+"-max-"+item.Item2+(expanded?"-expanded":"-normal"),1);
                }
                automatic.AutomaticMaximum=false;
                AssertTrue(automatic.IsValid);
                AssertEqual(automatic.Maximum,AvanteRpmScale.Resolve(7200,automatic).Maximum);
                Console.WriteLine("PROOF autoRed="+item.Item1+" max="+resolved.Maximum+" unchangedYellow=6000 manualMax="+automatic.Maximum);
            }
            var legacy=JsonSerializer.Deserialize<AvanteRpmCalibration>("{\"Maximum\":12000,\"YellowStart\":6850,\"RedStart\":7400}")!;
            AssertFalse(legacy.AutomaticMaximum); AssertEqual(12000.0,AvanteRpmScale.Resolve(7200,legacy).Maximum);
            AssertFalse(new AvanteRpmCalibration {Maximum=8000,YellowStart=6500,RedStart=8000}.IsValid);
            AssertFalse(new AvanteRpmCalibration {AutomaticMaximum=true,RedStart=double.NaN}.IsValid);
            // Every name uses the approved common policy when engine maximum is valid.
            var fallback=AvanteRpmScale.Resolve(7200);AssertEqual(8000.0,fallback.Maximum);AssertTrue(fallback.HasWarningThresholds); AssertEqual(2,fallback.Band(7200));
            AssertTrue(fallback.AutomaticMaximum);AssertFalse(fallback.VehicleProfile);
            var expected = AvanteRpmScale.Resolve(7200,null,car);
            AssertFalse(expected.VehicleProfile); AssertFalse(expected.Calibrated);
            AssertEqual(8000.0,expected.Maximum); AssertEqual(6984.0,expected.RedStart);
            AssertEqual(6480.0,expected.YellowStart);
            AssertTrue(expected.SourceDescription.Contains("게임이 직접 제공한 경계값이 아닙니다"));
            AssertEqual(150+6984.0/8000*240,expected.Angle(expected.RedStart));
            AssertTrue(Math.Abs((150+1821.0/8000*240)-expected.Angle(1821)) < 1e-10);
            var view = new AvanteClusterView {Width=454,Height=375};
            view.SetSession(session); view.SetSample(Sample(1821));
            void Layout() { view.Measure(new Size(view.Width,view.Height)); view.Arrange(new Rect(0,0,view.Width,view.Height)); view.UpdateLayout(); }
            Layout(); AssertEqual(expected,view.RpmScale); AssertEqual(expected.Angle(1821),view.NeedleAngle);
            CaptureLayout(view,"rpm-recorded-aston-1821",1);
            int builds = view.StaticFaceBuilds;
            foreach(double rpm in new[]{0.0,expected.YellowStart-.01,expected.YellowStart+.01,6983.99,6984.0,6984.01,12000.0,13000.0})
            { view.SetSample(Sample(rpm)); Layout(); AssertEqual(expected.Angle(rpm),view.NeedleAngle); AssertEqual(builds,view.StaticFaceBuilds); }
            view.SetSample(Sample(6984)); Layout(); CaptureLayout(view,"rpm-recorded-aston-red-boundary",1);
            view.SetSample(null); view.SetSample(Sample(1821,0)); Layout(); AssertEqual(expected,view.RpmScale);
            foreach(double size in new[]{.35,1.0,1.5})
            { view.Width=908*size;view.Height=750*size;Layout();CaptureLayout(view,"rpm-recorded-aston-size-"+size,1); }
            var settings = new DrivingHudSettings();
            settings.AvanteVehicles["root-alias"] = new AvanteRpmCalibration {Maximum=10000,YellowStart=6500,RedStart=7500};
            view.ApplySettings(settings); AssertTrue(view.RpmScale.Calibrated); AssertEqual(10000.0,view.RpmScale.Maximum);
            view.ApplySettings(new DrivingHudSettings()); AssertEqual(expected,view.RpmScale);
            view.SetSession(Session("unconfirmed")); view.SetSample(Sample(1821));
            AssertFalse(view.RpmScale.VehicleProfile); AssertEqual(8000.0,view.RpmScale.Maximum);
            view.SetSession(session); AssertFalse(view.RpmScale.HasWarningThresholds); view.SetSample(Sample(1821)); AssertEqual(expected,view.RpmScale);
            view.SetSession(null); view.SetSample(Sample(1821,0,2)); AssertFalse(view.RpmScale.VehicleProfile);
            var dialog = new DrivingHudSettingsWindow(new DrivingHudSettings(),"root-alias",7200,car);
            try
            {
                var maximumBox = LogicalDescendants<System.Windows.Controls.TextBox>(dialog).Single(box => System.Windows.Automation.AutomationProperties.GetName(box)=="최대 표시 눈금 (RPM)");
                AssertEqual("8000",maximumBox.Text); AssertFalse(maximumBox.IsEnabled);
                AssertTrue(LogicalDescendants<System.Windows.Controls.TextBlock>(dialog).Any(text=>text.Text.Contains("게임이 직접 제공한 경계값이 아닙니다")));
            }
            finally { dialog.Close(); }
            var modeDialog = new DrivingHudSettingsWindow(new DrivingHudSettings(),"root-alias",7200,car) {Left=-5000,Top=-5000,WindowStartupLocation=WindowStartupLocation.Manual};
            Exception? modeFailure=null;
            modeDialog.ContentRendered += (_,__) =>
            {
                try
                {
                    var custom=LogicalDescendants<System.Windows.Controls.CheckBox>(modeDialog).Single();custom.IsChecked=true;
                    var mode=LogicalDescendants<System.Windows.Controls.ComboBox>(modeDialog).Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="최대 눈금 결정");
                    var boxes=LogicalDescendants<System.Windows.Controls.TextBox>(modeDialog).ToArray();
                    var red=boxes.Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="빨강 시작 (RPM)");
                    var maximum=boxes.Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="최대 표시 눈금 (RPM)");
                    var save=LogicalDescendants<System.Windows.Controls.Button>(modeDialog).Single(button=>button.Content as string=="저장");
                    red.Text="8000";AssertEqual("9000",maximum.Text);AssertFalse(maximum.IsEnabled);
                    mode.SelectedIndex=1;maximum.Text="8000";save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    AssertTrue(modeDialog.IsVisible);AssertTrue(maximum.IsEnabled);
                    maximum.Text="12000";mode.SelectedIndex=0;AssertEqual("9000",maximum.Text);
                    mode.SelectedIndex=1;AssertEqual("12000",maximum.Text);mode.SelectedIndex=0;
                    Descendants<System.Windows.Controls.ScrollViewer>(modeDialog).First().ScrollToEnd();modeDialog.UpdateLayout();
                    AssertFalse(LogicalDescendants<System.Windows.Controls.TextBlock>(modeDialog).Any(text=>text.Text.StartsWith("0 ≤")));
                    CaptureLayout((FrameworkElement)modeDialog.Content,"rpm-automatic-mode",1);
                    save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
                catch(Exception ex){modeFailure=ex;modeDialog.Close();}
            };
            try {AssertTrue(modeDialog.ShowDialog()==true);if(modeFailure!=null)throw modeFailure;}finally{modeDialog.Close();}
            var saved=JsonSerializer.Deserialize<DrivingHudSettings>(JsonSerializer.Serialize(modeDialog.Settings))!.Normalize();
            AssertTrue(saved.AvanteVehicles["root-alias"].AutomaticMaximum);
            AssertEqual(12000.0,saved.AvanteVehicles["root-alias"].Maximum);
            AssertEqual(9000.0,AvanteRpmScale.Resolve(7200,saved.AvanteVehicles["root-alias"]).Maximum);
            for(int i=0;i<1000;i++) AvanteRpmScale.Resolve(7200,null,car);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100000;i++) expected=AvanteRpmScale.Resolve(7200,null,car);
            allocated = GC.GetAllocatedBytesForCurrentThread()-allocated;
            AssertEqual(0L,allocated);
            Console.WriteLine("PROOF common vehicle policy: max=8000 red=6984 yellow="+expected.YellowStart+" rpm=1821 needle=204.63deg redAngle=359.52deg staticRebuildsOnRpmChange=0 resolve100000Allocation="+allocated+" liveGame=NOT_TESTED");
        }

        private static void AvanteVehicleRpmCalibration()
        {
            var now = DateTimeOffset.UtcNow;
            TelemetrySnapshot Session(string name) => new TelemetrySnapshot(now,14,0,0,2,1,2,0,0,0,0,0,0,0,
                Array.Empty<ParticipantSnapshot>(), rootCarName:name);
            DrivingTelemetrySample Sample(double rpm, double maximum = 8000, int generation = 1)
                => new DrivingTelemetrySample(now, generation,0,0,0,0,0,200/3.6,4,rpm:rpm,maxRpm:maximum);
            var settings = new DrivingHudSettings();
            settings.AvanteVehicles["reference"] = new AvanteRpmCalibration { Maximum=12000,YellowStart=6850,RedStart=7400 };
            settings.AvanteVehicles["other"] = new AvanteRpmCalibration { Maximum=10000,YellowStart=8125,RedStart=9350 };
            var roundTrip = JsonSerializer.Deserialize<DrivingHudSettings>(JsonSerializer.Serialize(settings))!.Normalize();
            AssertEqual(7400.0,roundTrip.AvanteVehicles["reference"].RedStart);
            settings.AvanteVehicles["reference"].RedStart=7500;
            AssertEqual(7400.0,roundTrip.AvanteVehicles["reference"].RedStart);
            settings.AvanteVehicles["bad"] = new AvanteRpmCalibration { Maximum=0 };
            AssertFalse(settings.Normalize().AvanteVehicles.ContainsKey("bad"));
            AssertEqual(8000.0,AvanteRpmScale.Resolve(double.NaN).Maximum);
            foreach (double maximum in new[] {8000.0,10000.0,12000.0})
            {
                var calibration = new AvanteRpmCalibration { Maximum=maximum,YellowStart=maximum-2150,RedStart=maximum-1600 };
                if(maximum==12000) {calibration.YellowStart=6850;calibration.RedStart=7400;}
                var scale=AvanteRpmScale.Resolve(7200,calibration);
                AssertTrue(scale.Calibrated); AssertEqual(maximum,scale.Maximum);
                AssertEqual(150.0,scale.Angle(0)); AssertEqual(390.0,scale.Angle(maximum+1000));
                AssertEqual(150.0,scale.Angle(double.NaN));
                AssertEqual(0,scale.Band(calibration.YellowStart-.01)); AssertEqual(1,scale.Band(calibration.YellowStart));
                AssertEqual(1,scale.Band(calibration.RedStart-.01)); AssertEqual(2,scale.Band(calibration.RedStart));
                AssertEqual(150+calibration.RedStart/maximum*240,scale.Angle(calibration.RedStart));
                if(maximum==12000) AssertEqual(298.0,scale.Angle(7400));
                roundTrip.AvanteVehicles["capture"] = calibration;
                foreach (bool expanded in new[]{false,true}) foreach (double size in new[]{.35,.5,1.0,1.5})
                {
                    var view = new AvanteClusterView(expanded) {Width=(expanded?2048:908)*size, Height=750*size};
                    view.ApplySettings(roundTrip); view.SetSession(Session("capture"));
                    void Layout(){view.Measure(new Size(view.Width,view.Height));view.Arrange(new Rect(0,0,view.Width,view.Height));view.UpdateLayout();}
                    view.SetSample(Sample(calibration.RedStart,7200)); Layout();
                    AssertEqual(scale,view.RpmScale); AssertEqual(scale.Angle(calibration.RedStart),view.NeedleAngle);
                    CaptureLayout(view,"rpm-"+maximum+"-"+(expanded?"expanded":"normal")+"-size-"+size,1);
                    int builds=view.StaticFaceBuilds;
                    foreach(double rpm in new[]{0.0,calibration.YellowStart-.01,calibration.YellowStart+.01,calibration.RedStart-.01,calibration.RedStart+.01,maximum,maximum+100})
                    { view.SetSample(Sample(rpm,7200));Layout();AssertEqual(scale.Angle(rpm),view.NeedleAngle);AssertEqual(builds,view.StaticFaceBuilds); }
                    view.SetSample(Sample(1000,0)); Layout(); AssertEqual(scale,view.RpmScale);
                    view.SetSample(null); Layout(); AssertEqual(scale,view.RpmScale); AssertFalse(view.IsRedFlashing);
                    view.SetSample(Sample(2000,double.NaN));Layout();AssertEqual(scale,view.RpmScale);
                    AssertEqual(builds,view.StaticFaceBuilds);
                }
                Console.WriteLine("PROOF RPM scale="+maximum+" yellow="+calibration.YellowStart+" red="+calibration.RedStart+" redAngle="+scale.Angle(calibration.RedStart)+" sizes=0.35/0.5/1/1.5 staticRebuildsOnRpmChange=0");
            }
            var switched = new AvanteClusterView {Width=454,Height=375};
            switched.ApplySettings(roundTrip); switched.SetSession(Session("reference"));switched.SetSample(Sample(7400,7200));
            AssertEqual(12000.0,switched.RpmScale.Maximum);AssertEqual(7400.0,switched.RpmScale.RedStart);
            switched.SetSession(Session("other"));switched.SetSample(Sample(7400,7200));
            AssertEqual(10000.0,switched.RpmScale.Maximum);AssertEqual(9350.0,switched.RpmScale.RedStart);
            switched.SetSession(Session("unconfigured"));switched.SetSample(Sample(7400,7200));
            AssertFalse(switched.RpmScale.Calibrated);AssertEqual(8000.0,switched.RpmScale.Maximum);
            switched.SetSample(Sample(2000,0)); AssertEqual(8000.0,switched.RpmScale.Maximum);
            switched.SetSession(null); switched.SetSample(Sample(2000,12000,2)); AssertEqual(13000.0,switched.RpmScale.Maximum);
            switched.SetSample(Sample(2000,0,3)); AssertEqual(8000.0,switched.RpmScale.Maximum);
            AssertTrue(AvanteClusterView.TickOuterRadius < 351);
            AssertTrue(AvanteClusterView.MajorTickInnerRadius < AvanteClusterView.MinorTickInnerRadius);
            // Display overflow clamps only the pointer; the immutable input is retained as received.
            var raw=Sample(13000,7200);switched.SetSample(raw);AssertEqual(13000.0,raw.Rpm!.Value);AssertEqual(7200.0,raw.MaxRpm!.Value);
            var dialog = new DrivingHudSettingsWindow(roundTrip,"reference",7200) {WindowStartupLocation=WindowStartupLocation.Manual,Left=-5000,Top=-5000};
            Exception? uiFailure = null;
            dialog.ContentRendered += (_, __) =>
            {
                try
                {
                    var redBox = LogicalDescendants<System.Windows.Controls.TextBox>(dialog).Single(box => System.Windows.Automation.AutomationProperties.GetName(box)=="빨강 시작 (RPM)");
                    var save = LogicalDescendants<System.Windows.Controls.Button>(dialog).Single(button => button.Content as string=="저장");
                    redBox.Text="13000"; save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    AssertTrue(dialog.IsVisible); AssertEqual(7400.0,dialog.Settings.AvanteVehicles["reference"].RedStart);
                    redBox.Text="7400";
                    Descendants<System.Windows.Controls.ScrollViewer>(dialog).First().ScrollToEnd(); dialog.UpdateLayout();
                    CaptureLayout((FrameworkElement)dialog.Content,"rpm-settings",1);
                    save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
                catch(Exception ex) { uiFailure=ex;dialog.Close(); }
            };
            try { AssertTrue(dialog.ShowDialog()==true); if(uiFailure!=null)throw uiFailure; }
            finally { dialog.Close(); }
            AssertEqual(7400.0,dialog.Settings.AvanteVehicles["reference"].RedStart);
            AssertTrue(dialog.Settings.AvanteVehicles.ContainsKey("other"));
            var flashing = new AvanteClusterView { Width=454,Height=375 };
            flashing.ApplySettings(roundTrip); flashing.SetSession(Session("reference")); flashing.SetSample(Sample(7400,7200));
            var host = new Window { Content=flashing,Width=474,Height=415,Left=-5000,Top=-5000,ShowActivated=false };
            try
            {
                host.Show(); PumpDispatcher(); AssertTrue(flashing.IsRedFlashing);
                var opacities = new System.Collections.Generic.HashSet<double>();
                var capturedPhases = new System.Collections.Generic.HashSet<int>();
                var frame = new System.Windows.Threading.DispatcherFrame(); var watch=System.Diagnostics.Stopwatch.StartNew();
                var timer = new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
                timer.Tick += (_,__) =>
                {
                    var red=(System.Windows.Media.DrawingVisual)System.Windows.Media.VisualTreeHelper.GetChild(flashing,0);
                    opacities.Add(red.Opacity);
                    int phase = red.Opacity < .25 ? 0 : red.Opacity > .9 ? 2 : 1;
                    if(capturedPhases.Add(phase)) CaptureLayout(flashing,"rpm-12000-flash-"+phase,1);
                    if(watch.ElapsedMilliseconds>=350){timer.Stop();frame.Continue=false;}
                };
                timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
                AssertTrue(opacities.All(value=>value>=.12 && value<=1));
                AssertTrue(opacities.Min()<.25 && opacities.Max()>.9 && opacities.Any(value=>value>.3 && value<.8)); host.Hide();AssertFalse(flashing.IsRedFlashing);
            }
            finally {host.Close();}
            Console.WriteLine("PROOF calibrated red-region retained smooth opacity .12..1, hidden stop; settings invalid rejected, valid saved, other vehicle preserved");
            Console.WriteLine("PROOF RPM calibration JSON/normalization, precedence, exact vehicle switch, invalid/stale/reconnect, source sample preservation PASS");
        }
    }
}
