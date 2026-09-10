using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void OfflineLayoutPreviewLifecycle()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-offline-layout-" + Guid.NewGuid() + ".json");
            var before = Application.Current.Windows.Cast<Window>().ToHashSet();
            var overlay = new OverlayWindow(false, path);
            var desktop = new GameWindowSnapshot(IntPtr.Zero, -5000, -5000, 1920, 1080, 96, true, false, 0);
            try
            {
                overlay.SetComponentEnabled(OverlayComponentKeys.Speed, true); overlay.SetComponentEnabled(OverlayComponentKeys.Gear, true);
                if (File.Exists(path)) File.Delete(path);
                // No ShowAt/SHM/game is required, including the existing edit button.
                AssertTrue(overlay.BeginLayoutEdit());
                AssertTrue(overlay.IsLayoutPreview && overlay.IsVisible);
                overlay.EndLayoutEdit(false);
                AssertFalse(overlay.IsVisible);
                AssertFalse(File.Exists(path));

                overlay.BeginLayoutPreview(false, desktop);
                PumpDispatcher();
                Window[] panels = Application.Current.Windows.Cast<Window>().Where(w => !before.Contains(w)).ToArray();
                AssertEqual(9, panels.Count(w => w.IsVisible));
                AssertTrue(overlay.IsEventCardSurfaceVisible);
                Window speed = panels.Single(w => w.Title == "AMS2 속도계");
                Window waiting = panels.Single(w => w.Title == "AMS2 멀티 대기 화면");
                AssertTrue(DescendantText(speed).Contains("123 km/h"));
                AssertFalse(waiting.IsVisible);
                AssertFalse(overlay.GetStyleState().ClickThrough);

                overlay.Width = 600; overlay.Height = 720; overlay.Left = -4930; overlay.Top = -4950;
                PumpDispatcher();
                OverlayBounds edited = OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(overlay).Handle);
                var live = DemoSnapshotFactory.CreateShell(false, OverlayEventType.PositionGained);
                overlay.SetViewModel(live, false);
                overlay.UpdateDrivingTelemetry(DemoSnapshotFactory.CreateSnapshot(), 16, 7);
                overlay.ShowWaitingAt(new GameWindowSnapshot(IntPtr.Zero, 0, 0, 2560, 1440, 144, true, false, 0),
                    new MultiplayerWaitingOverlayViewModel { SessionLabel = "LIVE" });
                overlay.ShowAt(desktop);
                overlay.HideOverlay();
                PumpDispatcher();
                AssertTrue(overlay.IsVisible && !waiting.IsVisible);
                AssertTrue(DescendantText(speed).Contains("123 km/h"));
                AssertTrue(DescendantText(panels.Single(w => w.Title == "AMS2 이벤트 카드")).Contains("이벤트 미리보기"));
                CaptureLayout((FrameworkElement)overlay.Content, "offline-gameplay-layout");
                overlay.EndLayoutEdit(true);
                AssertTrue(panels.All(w => !w.IsVisible));
                AssertFalse(overlay.IsLayoutPreview);
                AssertTrue(overlay.GetStyleState().ClickThrough);
                var saved = JsonSerializer.Deserialize<OverlayLayoutProfile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Components[OverlayComponentKeys.TimingTower];
                AssertTrue(Math.Abs(saved.X - (edited.X - desktop.Left) / 1920.0) < 0.001);
                AssertTrue(Math.Abs(saved.Height - edited.Height / 1080.0) < 0.001);
                AssertFalse(File.ReadAllText(path).Contains("이벤트 미리보기"));
                overlay.ShowAt(desktop);
                AssertTrue(overlay.IsVisible);
                AssertTrue(ReferenceEquals(live.EventCard,
                    FindDescendant<EventCardView>(panels.Single(w => w.Title == "AMS2 이벤트 카드"))!.DataContext));

                overlay.BeginLayoutPreview(true, desktop);
                PumpDispatcher();
                AssertTrue(waiting.IsVisible && !overlay.IsVisible);
                AssertEqual(1, panels.Count(w => w.IsVisible));
                AssertTrue(DescendantText(waiting).Contains("대기 화면 미리보기"));
                waiting.Width = 480;
                PumpDispatcher();
                CaptureLayout((FrameworkElement)waiting.Content, "offline-waiting-layout");
                overlay.BeginLayoutPreview(false, desktop); // Switching saves the outgoing surface.
                AssertTrue(JsonSerializer.Deserialize<OverlayLayoutProfile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Components.ContainsKey(OverlayComponentKeys.Waiting));
                AssertFalse(waiting.IsVisible);
                overlay.SetComponentEnabled(OverlayComponentKeys.Speed, false);
                AssertFalse(speed.IsVisible);
                overlay.SetComponentEnabled(OverlayComponentKeys.Speed, true);
                AssertTrue(speed.IsVisible);
                overlay.EndLayoutEdit(true);

                overlay.ShowWaitingAt(desktop, new MultiplayerWaitingOverlayViewModel { SessionLabel = "LIVE" });
                PumpDispatcher();
                AssertTrue(waiting.IsVisible && DescendantText(waiting).Contains("LIVE"));
                overlay.HideOverlay();
                AssertFalse(waiting.IsVisible);
                overlay.BeginLayoutPreview(false, desktop);
                overlay.ResetLayout();
                AssertTrue(overlay.IsLayoutPreview && overlay.IsVisible);
                overlay.EndLayoutEdit(false);
                AssertTrue(panels.All(w => !w.IsVisible));
            }
            finally { overlay.EndLayoutEdit(false); overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }

        private static void OfflineLayoutControls()
        {
            var window = new ClientStatusWindow(new ClientStatusViewModel());
            try
            {
                int gameplay = 0, waiting = 0;
                window.GameplayPreviewRequested += (_, __) => gameplay++;
                window.WaitingPreviewRequested += (_, __) => waiting++;
                var root = (FrameworkElement)window.Content;
                window.Width = window.MinWidth; window.Height = window.MinHeight;
                window.Left = -5000; window.Top = -5000; window.Show(); PumpDispatcher();
                foreach (Button button in Descendants<Button>(root))
                {
                    if (button.Content as string == "게임 없이 주행 UI 편집"
                        || button.Content as string == "게임 없이 대기 UI 편집")
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                AssertEqual(1, gameplay); AssertEqual(1, waiting);
                Named<Expander>(root, "ConnectionDetails").IsExpanded = true; PumpDispatcher();
                var mode = Named<TextBlock>(root, "SessionPlayModeLabel");
                AssertTrue(mode.IsVisible && mode.ActualHeight > 0);
                foreach (Button button in Descendants<Button>(root))
                {
                    Rect bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                    AssertTrue(bounds.Left >= 0 && bounds.Right <= root.ActualWidth);
                }
                CaptureLayout(root, "status-offline-layout");
            }
            finally { window.Close(); }
        }
    }
}
