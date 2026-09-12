using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Presentation
{
    internal static class OverlayGalleryPreview
    {
        // Render once without opening an overlay window or starting a preview timer.
        public static BitmapSource Create(string component, string design, DrivingHudSettings settings)
        {
            var shell = DemoSnapshotFactory.CreateShell(false, OverlayEventType.BattleBehind);
            var history = DemoSnapshotFactory.CreateDrivingPreview();
            FrameworkElement view;
            double width, height;
            switch (component)
            {
                case OverlayComponentKeys.TimingTower:
                    shell.Timing.RankingRows = shell.Timing.RankingRows.Take(5).ToArray();
                    shell.Timing.RankingRowCapacity = 5;
                    var tower = new OverlayHudView();
                    tower.SetRacingDesign(design == "racing"); tower.SetViewModel(shell.Timing);
                    view = tower; width = OverlayUiMetrics.TowerWidth; height = tower.Height; break;
                case OverlayComponentKeys.PedalTelemetry:
                    if (design == "legacy")
                    {
                        var legacy = new LegacyPedalTelemetryView(); legacy.ApplySettings(settings); legacy.SetHistory(history); view = legacy;
                    }
                    else
                    {
                        var telemetry = new PedalTelemetryView(); telemetry.ApplySettings(settings); telemetry.SetHistory(history); view = telemetry;
                    }
                    width = OverlayUiMetrics.PedalWidth; height = OverlayUiMetrics.PedalHeight; break;
                case OverlayComponentKeys.PedalGauge:
                    var gauge = new PedalTelemetryView(true); gauge.ApplySettings(settings); gauge.SetHistory(history);
                    view = gauge; width = OverlayUiMetrics.PedalGaugeWidth; height = OverlayUiMetrics.PedalHeight; break;
                case OverlayComponentKeys.DrivingDashboard:
                    var dashboard = new DrivingDashboardView(); dashboard.ApplySettings(settings);
                    dashboard.SetSample(history.Current, "P12"); dashboard.SetSession(27, "14:03");
                    view = dashboard; width = OverlayUiMetrics.DashboardWidth; height = OverlayUiMetrics.DashboardHeight; break;
                case OverlayComponentKeys.AvanteCluster:
                case OverlayComponentKeys.AvanteClusterExpanded:
                    bool expanded = component == OverlayComponentKeys.AvanteClusterExpanded;
                    var avante = new AvanteClusterView(expanded);
                    avante.SetSample(new DrivingTelemetrySample(DateTimeOffset.UtcNow, 1, 0, 0, .5, 0, 0, 200 / 3.6, 4, rpm: 5500, maxRpm: 8000), true);
                    view = avante; width = expanded ? 820 : 454; height = expanded ? 300 : 375; break;
                case OverlayComponentKeys.Speed:
                case OverlayComponentKeys.Gear:
                    bool gear = component == OverlayComponentKeys.Gear;
                    var number = new DrivingNumberView(gear); number.SetSample(history.Current);
                    number.ApplyFont(gear ? settings.GearFont : settings.SpeedFont);
                    number.ApplyShadow(gear ? settings.GearShadowColor : settings.SpeedShadowColor);
                    view = number; width = gear ? OverlayUiMetrics.GearSize : OverlayUiMetrics.SpeedWidth;
                    height = gear ? OverlayUiMetrics.GearSize : OverlayUiMetrics.SpeedHeight; break;
                case OverlayComponentKeys.RelativeDrivers:
                    view = new RelativeDriversView { DataContext = shell.Timing };
                    width = OverlayUiMetrics.RelativeWidth; height = OverlayUiMetrics.RelativeHeight; break;
                case OverlayComponentKeys.LapTiming:
                    view = new LapTimingView { DataContext = shell.Timing };
                    width = OverlayUiMetrics.LapTimingWidth; height = OverlayUiMetrics.LapTimingHeight; break;
                case OverlayComponentKeys.SessionInfo:
                    view = new SessionInfoView { DataContext = shell.Session };
                    width = OverlayUiMetrics.SessionWidth; height = OverlayUiMetrics.SessionHeight; break;
                case OverlayComponentKeys.EventCard:
                    var eventCard = new EventCardView(); eventCard.SetViewModel(shell.EventCard, false);
                    view = eventCard; width = OverlayUiMetrics.EventWidth; height = OverlayUiMetrics.EventHeight; break;
                case OverlayComponentKeys.RaceControl:
                    var control = new RaceControlView();
                    control.SetViewModel(new RaceControlViewModel { IsVisible = true, IsExpanded = true, Title = "레이스 컨트롤",
                        DriverLine = "플레이어", Message = "트랙 제한 · 랩타임 삭제", StateLabel = "황색기" }, false);
                    view = control; width = OverlayUiMetrics.RaceControlExpandedWidth; height = OverlayUiMetrics.RaceControlExpandedHeight; break;
                case OverlayComponentKeys.Waiting:
                    view = new MultiplayerWaitingOverlayView { DataContext = new MultiplayerWaitingOverlayViewModel {
                        Title = "멀티플레이어 대기", SessionLabel = "예선 준비", ParticipantCountText = "참가자 24명",
                        RemainingLabel = "시작까지", RemainingValue = "00:45" } };
                    width = OverlayUiMetrics.WaitingWidth; height = OverlayUiMetrics.WaitingHeight; break;
                default: throw new ArgumentOutOfRangeException(nameof(component));
            }
            view.Width = width; view.Height = height;
            view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
            // Two pixels per logical unit keep the example readable on high-DPI displays.
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(view); bitmap.Freeze();
            return bitmap;
        }
    }
}
