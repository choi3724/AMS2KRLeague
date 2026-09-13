using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void AvanteCommonRpmPolicy()
        {
            var cases=new (double Engine,double Yellow,double Red,double Maximum)[]{(7600.0,6840.0,7372.0,9000.0),
                (10000.0,9000.0,9700.0,11000.0),(11500.0,10350.0,11155.0,13000.0),(16000.0,14400.0,15520.0,17000.0)};
            DrivingTelemetrySample Sample(double rpm,double? maximum,int generation=1,DateTimeOffset? at=null)
                => new DrivingTelemetrySample(at??DateTimeOffset.UtcNow,generation,0,0,0,0,0,100/3.6,3,rpm:rpm,maxRpm:maximum??double.NaN);
            TelemetrySnapshot Session(string root,string name,double? maximum=null)
            {
                ViewedVehicleTelemetrySnapshot? vehicle=null;
                if(maximum.HasValue)
                {
                    vehicle=(ViewedVehicleTelemetrySnapshot)Activator.CreateInstance(typeof(ViewedVehicleTelemetrySnapshot),true)!;
                    typeof(ViewedVehicleTelemetrySnapshot).GetProperty("MaxRpm")!.SetValue(vehicle,(float)maximum.Value);
                }
                return new TelemetrySnapshot(DateTimeOffset.UtcNow,14,3398,0,2,1,2,0,2,0,0,0,0,0,
                    new[]{new ParticipantSnapshot(0,true,"",1,0,1,1,2,0,0,0,vehicleName:name)},rootCarName:root,viewedVehicleTelemetry:vehicle);
            }
            foreach(var item in cases)
            {
                var expected=AvanteRpmScale.Resolve(item.Engine);
                AssertEqual(item.Yellow,expected.YellowStart);AssertEqual(item.Red,expected.RedStart);AssertEqual(item.Maximum,expected.Maximum);
                foreach(string name in new[]{"unlisted",AvanteRpmScale.AstonMartinLowDownforce,AvanteRpmScale.LolaSuperspeedway,AvanteRpmScale.IvecoStralis,AvanteRpmScale.ArcCamaro})
                {AssertEqual(expected,AvanteRpmScale.Resolve(item.Engine,null,name));AssertFalse(expected.VehicleProfile);}
                foreach(double delta in new[]{-.01,0,.01})
                {
                    AssertEqual(delta<0?0:1,expected.Band(item.Yellow+delta));
                    AssertEqual(delta<0?1:2,expected.Band(item.Red+delta));
                    AssertEqual(delta<0?4:5,expected.LitPairs(item.Red+delta));
                }
                foreach(bool expanded in new[]{false,true})
                {
                    var view=new AvanteClusterView(expanded){Width=(expanded?2048:908)*.5,Height=375};
                    view.SetSession(Session("matrix","matrix",item.Engine));view.SetSample(Sample(0,item.Engine));
                    void Layout(){view.Measure(new Size(view.Width,view.Height));view.Arrange(new Rect(0,0,view.Width,view.Height));view.UpdateLayout();}
                    Layout();int builds=view.StaticFaceBuilds;
                    foreach(double rpm in new[]{0,item.Yellow-.01,item.Yellow,item.Yellow+.01,item.Red-.01,item.Red,item.Red+.01,item.Maximum,item.Maximum+100})
                    {
                        var sample=Sample(rpm,item.Engine);view.SetSample(sample);Layout();
                        AssertEqual(expected,view.RpmScale);AssertEqual(expected.Angle(rpm),view.NeedleAngle);
                        AssertEqual(rpm,sample.Rpm!.Value);AssertEqual(item.Engine,sample.MaxRpm!.Value);
                        AssertEqual(builds,view.StaticFaceBuilds);
                    }
                    view.SetSample(Sample(item.Red,item.Engine));Layout();
                    AssertEqual((int)(item.Maximum/1000)+1,((DrawingVisual)VisualTreeHelper.GetChild(view,5)).Children.Count);
                    CaptureLayout(view,"policy-"+item.Engine+"-"+(expanded?"expanded":"normal"),1);
                    // Near-limit input starts the warning immediately, not after the needle's interpolation.
                    var host=new Window {Content=view,Width=view.Width+20,Height=415,Left=-5000,Top=-5000,ShowActivated=false};
                    try
                    {
                        host.Show();PumpDispatcher();view.SetSample(null);view.SetSample(Sample(item.Red-1,item.Engine));
                        AssertFalse(view.IsRedFlashing);view.SetSample(Sample(item.Red,item.Engine));AssertTrue(view.IsRedFlashing);
                        AssertEqual(5,view.RpmScale.LitPairs(item.Red));
                        view.SetSample(Sample(item.Red-1,item.Engine));AssertFalse(view.IsRedFlashing);
                    }
                    finally {host.Close();}
                }
                Console.WriteLine($"PROOF approved policy engine={item.Engine} yellow={item.Yellow} red={item.Red} max={item.Maximum}; normal/expanded exact boundaries, raw values unchanged, static rebuilds=0");
            }
            foreach(bool expanded in new[]{false,true})
            {
                var view=new AvanteClusterView(expanded);
                view.SetSession(Session("same-root","A",7600));view.SetSample(Sample(1000,7600));
                var original=view.RpmScale;
                foreach(double? invalid in new double?[]{null,0,-1,double.NaN,double.PositiveInfinity,double.NegativeInfinity,100001})
                {
                    AssertFalse(AvanteRpmScale.Resolve(invalid).HasWarningThresholds);
                    view.SetSession(Session("same-root","A",invalid));view.SetSample(Sample(1500,invalid));AssertEqual(original,view.RpmScale);
                }
                view.SetSession(Session("same-root","B"));AssertFalse(view.RpmScale.HasWarningThresholds);
                view.SetSample(Sample(1000,double.NaN));AssertFalse(view.RpmScale.HasWarningThresholds);
                view.SetSample(Sample(1000,11500));AssertEqual(11155.0,view.RpmScale.RedStart);
                view.SetSession(Session("same-root","B",16000));view.SetSample(Sample(1000,double.NaN,2));
                AssertEqual(15520.0,view.RpmScale.RedStart); // Fresh full snapshot survives an invalid fast read.
                view.SetSample(Sample(1000,0,3,DateTimeOffset.UtcNow.AddSeconds(2)));AssertFalse(view.RpmScale.HasWarningThresholds);
                view.SetSession(Session("manual","C",11500));
                var settings=new DrivingHudSettings();settings.AvanteVehicles["manual"]=new AvanteRpmCalibration{Maximum=20000,YellowStart=9000,RedStart=11000};
                view.ApplySettings(settings);AssertEqual(20000.0,view.RpmScale.Maximum);AssertEqual(11000.0,view.RpmScale.RedStart);AssertEqual(5,view.RpmScale.LitPairs(11000));
                view.ApplySettings(new DrivingHudSettings());AssertEqual(AvanteRpmScale.Resolve(11500),view.RpmScale);
                view.SetSession(null);AssertFalse(view.RpmScale.HasWarningThresholds);
            }
            var configured=new DrivingHudSettings();configured.AvanteVehicles["manual"]=new AvanteRpmCalibration{Maximum=20000,YellowStart=9000,RedStart=11000};
            var dialog=new DrivingHudSettingsWindow(configured,"manual",11500);
            try
            {
                LogicalDescendants<System.Windows.Controls.CheckBox>(dialog).Single().IsChecked=false;
                var boxes=LogicalDescendants<System.Windows.Controls.TextBox>(dialog).ToArray();
                string Value(string name)=>boxes.Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)==name).Text;
                AssertEqual("13000",Value("최대 표시 눈금 (RPM)"));AssertEqual("10350",Value("노랑 시작 (RPM)"));AssertEqual("11155",Value("빨강 시작 (RPM)"));
            }
            finally{dialog.Close();}
            Console.WriteLine("PROOF transient invalid maxima retain basis; root/participant vehicle switch clears basis; fresh snapshot/fast sample handoff; manual clear and both layouts PASS");
        }
    }
}
