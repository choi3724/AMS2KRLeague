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
        private static void DrivingAbsSteeringAndRpm()
        {
            var defaults = new OverlayLayoutProfile();
            defaults.SetDrivingPanelDefaults();
            AssertFalse(defaults.IsEnabled(OverlayComponentKeys.DrivingDashboard));
            AssertFalse(defaults.IsEnabled(OverlayComponentKeys.PedalGauge));
            AssertTrue(defaults.IsEnabled(OverlayComponentKeys.Speed)); AssertTrue(defaults.IsEnabled(OverlayComponentKeys.Gear));
            var separate = new OverlayLayoutProfile();
            separate.EnabledComponents[OverlayComponentKeys.Speed] = true; separate.SetDrivingPanelDefaults();
            AssertTrue(separate.IsEnabled(OverlayComponentKeys.Speed)); AssertFalse(separate.IsEnabled(OverlayComponentKeys.DrivingDashboard));
            foreach (string asset in new[] { "steering-wheel.png", "dashboard-housing.png" })
            {
                var resource = Application.GetResourceStream(new Uri("pack://application:,,,/AMS2LeagueClient;component/Assets/Hud/" + asset));
                using (resource.Stream)
                {
                    var frame = System.Windows.Media.Imaging.BitmapFrame.Create(resource.Stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    var pixels = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    var corner = new byte[4]; pixels.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
                    AssertEqual((byte)0, corner[3]);
                }
            }
            var tower = new OverlayHudView();
            tower.SetRacingDesign(true);
            tower.SetViewModel(AMS2LeagueClient.Runtime.DemoSnapshotFactory.CreateShell(false).Timing);
            var nextTower = AMS2LeagueClient.Runtime.DemoSnapshotFactory.CreateShell(false).Timing;
            nextTower.RankingRows[0].PenaltyText = "+5초"; nextTower.RankingRows[0].TimeForeground = "#E765F4";
            tower.SetViewModel(nextTower);
            var towerContent = (System.Windows.Controls.ContentControl)tower.FindName("TowerContent");
            var displayed = (RankingRowViewModel)((System.Windows.Controls.ItemsControl)towerContent.Template.FindName("RankingItems", towerContent)).Items[0];
            AssertEqual("+5초", displayed.PenaltyText); AssertEqual("#E765F4", displayed.TimeForeground);
            var towerHost = new Window { Content = tower, Width = 668, Height = 660, Left = -5000, Top = -5000, ShowActivated = false };
            try
            {
                towerHost.Show(); PumpDispatcher(); CaptureLayout(tower, "tower-artwork");
                tower.SetRacingDesign(false); PumpDispatcher();
                AssertTrue(Descendants<System.Windows.Controls.TextBlock>(tower).Any(text => text.Text == "P1"));
                AssertFalse(Descendants<System.Windows.Controls.TextBlock>(tower).Any(text => text.Foreground is SolidColorBrush brush && brush.Color == (Color)ColorConverter.ConvertFromString("#E765F4")));
                CaptureLayout(tower, "tower-original-0.6.1");
                tower.SetRacingDesign(true); PumpDispatcher();
                AssertTrue(Descendants<System.Windows.Controls.TextBlock>(tower).Any(text => text.Text == "1"));
            }
            finally { towerHost.Close(); }
            var fixture = new RawFixtureBuilder().SetViewedVehicleTelemetry();
            fixture.Buffer[SharedMemoryLayout.AntiLockActive] = 1;
            Array.Copy(BitConverter.GetBytes(-0.25f), 0, fixture.Buffer, SharedMemoryLayout.UnfilteredSteering, 4);
            Array.Copy(BitConverter.GetBytes(5941f), 0, fixture.Buffer, SharedMemoryLayout.Rpm, 4);
            Array.Copy(BitConverter.GetBytes(8000f), 0, fixture.Buffer, SharedMemoryLayout.MaxRpm, 4);
            var sample = DrivingTelemetrySample.FromSnapshot(Parse(fixture), 3, 1)!;
            AssertTrue(sample.AbsActive); AssertEqual(-112.5, sample.SteeringDegrees(900)!.Value);
            AssertEqual("5941", sample.RpmText); AssertEqual(8000.0, sample.MaxRpm!.Value);
            AssertNull(DrivingTelemetrySample.FromSnapshot(Parse(fixture), 2, 1));
            var invalid = new DrivingTelemetrySample(FixedTime(), 1, 3, 0, 0, 0, 0, 0, 1, true, 2, -1, 0);
            AssertFalse(invalid.AbsActive); AssertNull(invalid.Steering); AssertNull(invalid.Rpm); AssertNull(invalid.MaxRpm);
            var dashboard = new DrivingDashboardView { Width = 340, Height = 120 };
            dashboard.SetSample(sample, "P12");
            AssertEqual("5941", dashboard.RpmText); AssertEqual(0, dashboard.LitRpmLights);
            AssertEqual("260", dashboard.SpeedText); AssertEqual("4", dashboard.GearText);
            var dashboardHost = new Window { Content = dashboard, Width = 360, Height = 160, Left = -5000, Top = -5000, ShowActivated = false };
            try
            {
                dashboard.SetSession(27, "14:03");
                dashboardHost.Show(); PumpDispatcher(); CaptureLayout(dashboard, "combined-dashboard");
                var shift = new DrivingTelemetrySample(sample.CapturedAt.AddMilliseconds(50), 1, 3, .2, 0, 0, 0, 65, 3, false, 0, 6466, 8000);
                dashboard.SetSample(shift, "P12");
                AssertEqual(4, dashboard.LitRpmLights);
                AssertTrue(dashboard.IsShifting);
                AssertEqual(1.0, (double)dashboard.GearMotion.GetAnimationBaseValue(ScaleTransform.ScaleXProperty));
                foreach (int frame in new[] { 0, 1, 2, 3 })
                {
                    var wait = new System.Windows.Threading.DispatcherFrame();
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(frame == 0 ? 30 : 480) };
                    timer.Tick += (_, __) => { timer.Stop(); wait.Continue = false; };
                    timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(wait);
                    CaptureLayout(dashboard, "dashboard-shift-" + frame, settleMs: 1);
                }
                AssertFalse(dashboard.IsShifting);
                dashboard.SetSample(new DrivingTelemetrySample(shift.CapturedAt.AddMilliseconds(50), 2, 3, 0, 1, 0, 0, 65, 5, false, 0, 7500, 8000), "P12");
                AssertFalse(dashboard.HasAnimatedProperties || dashboard.IsShifting);
                dashboard.SetSample(invalid, "");
                AssertEqual("—", dashboard.RpmText); AssertEqual(0, dashboard.LitRpmLights); AssertEqual("P—", dashboard.PositionText);
                dashboard.SetSample(new DrivingTelemetrySample(FixedTime(), 1, 3, 0, 0, 0, 0, 0, 1, false, 0, 9000, 8000), "P64");
                AssertEqual(15, dashboard.LitRpmLights);
                dashboard.Width = 510; dashboard.Height = 180;
                dashboardHost.Width = 530; dashboardHost.Height = 220;
                PumpDispatcher(); CaptureLayout(dashboard, "combined-dashboard-resized");
                dashboard.SetSample(null, "");
                AssertEqual("—", dashboard.GearText); AssertEqual("—", dashboard.SpeedText); AssertFalse(dashboard.HasAnimatedProperties || dashboard.IsShifting);
            }
            finally { dashboardHost.Close(); }
            var view = new PedalTelemetryView { Width = 540, Height = 120 };
            var host = new Window { Content = view, Width = 560, Height = 160, Left = -5000, Top = -5000, ShowActivated = false };
            try
            {
                host.Show(); PumpDispatcher();
                var graph = Descendants<FrameworkElement>(view).Single(item => item.GetType().Name == "PedalGraph");
                var curve = typeof(PedalTelemetryView).Assembly.GetType("AMS2LeagueClient.Presentation.PedalCurveBuilder")!.GetMethod("Create", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                foreach (bool abs in new[] { false, true })
                {
                    var history = new DrivingTelemetryHistory();
                    for (int i = 0; i <= 200; i++)
                        history.Add(new DrivingTelemetrySample(FixedTime().AddSeconds(i * 0.05), 1, 3,
                            0.4 + Math.Sin(i * 0.05) * 0.3, 0.5 + Math.Cos(i * 0.05) * 0.4, 0, 0, 128 / 3.6, 3,
                            abs && i > 75 && i < 125, -0.25, 5941, 8000));
                    var highlight = (StreamGeometry)curve.Invoke(null, new object[] { history, 0, history.Current!.CapturedAt, 300.0, 100.0, true })!;
                    AssertEqual(!abs, highlight.Bounds.IsEmpty);
                    view.SetHistory(history); PumpDispatcher(); CaptureLayout(view, abs ? "telemetry-abs-yellow" : "telemetry-abs-off");
                }
            }
            finally { host.Close(); }
            Console.WriteLine("PROOF ABS active-only history segments; actual SHM steering/RPM; invalid values rejected");
        }
    }
}
