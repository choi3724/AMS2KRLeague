using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void GameFreeSettingsPreviewAndRollback()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-settings-preview-" + Guid.NewGuid() + ".json");
            var overlay = new OverlayWindow(false, path);
            try
            {
                overlay.BeginLayoutPreview(false, new GameWindowSnapshot(IntPtr.Zero, -5000, -5000, 1280, 720, 96, true, false, 0));
                PumpDispatcher();
                Window race = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "AMS2 레이스 컨트롤");
                var view = FindDescendant<RaceControlView>(race)!;
                var title = Descendants<TextBlock>(view).Single(text => text.Name == "TitleText");
                double baseline = title.FontSize;
                string? requested = null;
                overlay.ComponentSettingsRequested += (_, args) => requested = args.Component;
                var mouse = new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent };
                race.RaiseEvent(mouse);
                AssertEqual(OverlayComponentKeys.RaceControl, requested);
                requested = null;
                overlay.RaiseEvent(new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent });
                AssertEqual(OverlayComponentKeys.TimingTower, requested);

                DrivingHudSettings original = overlay.GetDrivingHudSettings();
                var dialog = new OverlayComponentSettingsWindow(OverlayComponentKeys.RaceControl, original)
                    { ShowActivated = false, Left = -5000, Top = -5000, WindowStartupLocation = WindowStartupLocation.Manual };
                int changes = 0;
                dialog.SettingsChanged += (_, __) => { changes++; overlay.PreviewDrivingHudSettings(dialog.Settings); };
                dialog.Show(); PumpDispatcher();
                Slider scale = Descendants<Slider>(dialog).Single(item =>
                    System.Windows.Automation.AutomationProperties.GetName(item) == "레이스 컨트롤 글자 크기");
                scale.Value = 150;
                PumpDispatcher();
                AssertTrue(changes > 0);
                AssertTrue(title.FontSize > baseline);
                AssertEqual(1.5, overlay.GetDrivingHudSettings().TextFor(OverlayComponentKeys.RaceControl).Scale);
                AssertFalse(File.Exists(path)); // An in-progress preview is never saved.
                dialog.Close();
                overlay.PreviewDrivingHudSettings(original);
                PumpDispatcher();
                AssertEqual(baseline, title.FontSize);
                AssertEqual(1.0, overlay.GetDrivingHudSettings().TextFor(OverlayComponentKeys.RaceControl).Scale);

                var towerDialog = new OverlayComponentSettingsWindow(OverlayComponentKeys.TimingTower, original)
                    { ShowActivated = false, Left = -5000, Top = -5000, WindowStartupLocation = WindowStartupLocation.Manual };
                towerDialog.SettingsChanged += (_, __) => overlay.PreviewDrivingHudSettings(towerDialog.Settings);
                towerDialog.Show(); PumpDispatcher();
                OverlayHudView towerView = FindDescendant<OverlayHudView>(overlay)!;
                TextBlock classText = Descendants<TextBlock>(towerView).First(text => (string?)text.Tag == "class");
                TextBlock driverText = Descendants<TextBlock>(towerView).First(text => (string?)text.Tag == "driver");
                double originalClassSize = classText.FontSize, originalDriverSize = driverText.FontSize;
                Slider classSize = Descendants<Slider>(towerDialog).Single(item =>
                    System.Windows.Automation.AutomationProperties.GetName(item) == "순위 타워 클래스 글자 크기");
                classSize.Value = 150;
                PumpDispatcher();
                AssertTrue(classText.FontSize > originalClassSize);
                AssertEqual(originalDriverSize, driverText.FontSize);
                AssertEqual(1.5, overlay.GetDrivingHudSettings().TextFor(OverlayComponentKeys.TimingTower).ElementScales["class"]);
                ComboBox towerDesign = Descendants<ComboBox>(towerDialog).Single(item =>
                    System.Windows.Automation.AutomationProperties.GetName(item) == "디자인");
                towerDesign.SelectedIndex = 1;
                AssertEqual("AMS2 순위 타워 (개량)", overlay.Title);
                towerDialog.Close();
                overlay.PreviewDrivingHudSettings(original);
                PumpDispatcher();
                AssertEqual("AMS2 순위 타워 (기본)", overlay.Title);
                AssertEqual(originalClassSize, Descendants<TextBlock>(towerView).First(text => (string?)text.Tag == "class").FontSize);

                var saved = overlay.GetDrivingHudSettings();
                saved.OverlayText[OverlayComponentKeys.RaceControl] = new DrivingHudSettings.TextAppearance { Scale = 1.5 };
                saved.OverlayText[OverlayComponentKeys.TimingTower] = new DrivingHudSettings.TextAppearance
                {
                    ElementScales = new System.Collections.Generic.Dictionary<string, double> { ["class"] = 1.5, ["driver"] = 0.8 }
                };
                overlay.SaveDrivingHudSettings(saved);
                AssertTrue(File.Exists(path));
                AssertEqual(1.5, overlay.GetDrivingHudSettings().TextFor(OverlayComponentKeys.TimingTower).ElementScales["class"]);
                overlay.EndLayoutEdit(true);
                requested = null;
                race.RaiseEvent(new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent });
                AssertTrue(requested == null);
                overlay.BeginLayoutPreview(true);
                PumpDispatcher();
                Window waiting = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "AMS2 멀티 대기 화면");
                waiting.RaiseEvent(new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent });
                AssertEqual(OverlayComponentKeys.Waiting, requested);
                Console.WriteLine("PROOF game-free right-click racing/waiting surfaces routed; live text/design changed and cancel restored; save persisted; locked click ignored");
            }
            finally { overlay.EndLayoutEdit(false); overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }

        private static void EmptyPanelsRemainEditableWithPreview()
        {
            string layout = Path.Combine(Path.GetTempPath(), "ams2-edit-preview-" + Guid.NewGuid() + ".json");
            var window = new OverlayWindow(false, layout);
            try
            {
                window.SetViewModel(DemoSnapshotFactory.CreateShell(false, OverlayEventType.PersonalBest), false);
                window.ShowDemoAt(-5000, -5000, 96);
                var empty = DemoSnapshotFactory.CreateShell(false);
                window.SetViewModel(empty, true);
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                timer.Tick += (_, __) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
                window.ShowDemoAt(-5000, -5000, 96);
                AssertFalse(window.IsEventCardSurfaceVisible);

                AssertTrue(window.BeginLayoutEdit());
                PumpDispatcher();
                Window eventWindow = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "AMS2 이벤트 카드");
                Window raceWindow = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "AMS2 레이스 컨트롤");
                var eventView = FindDescendant<EventCardView>(eventWindow)!;
                var raceView = FindDescendant<RaceControlView>(raceWindow)!;
                AssertTrue(eventWindow.IsVisible && raceWindow.IsVisible);
                AssertTrue(DescendantText(eventView).Contains("이벤트 미리보기"));
                AssertTrue(DescendantText(raceView).Contains("미리보기"));
                AssertTrue(DescendantText(raceView).Contains("황색기"));
                AssertFalse(empty.EventCard.IsVisible);
                AssertFalse(empty.RaceControl.IsVisible);
                AssertFalse(empty.EventCard.EventId.Contains("preview"));

                foreach (Window panel in new[] { window, eventWindow, raceWindow })
                {
                    var root = (Grid)panel.Content;
                    var chrome = (Grid)root.Children[root.Children.Count - 1];
                    AssertFalse(OverlayWindowInterop.ReadStyleState(new WindowInteropHelper(panel).Handle).ClickThrough);
                    foreach (Point point in new[] { new Point(12, panel.ActualHeight / 2), new Point(panel.ActualWidth / 2, panel.ActualHeight / 2), new Point(panel.ActualWidth - 25, panel.ActualHeight - 10) })
                    {
                        AssertTrue(root.InputHitTest(point) is Border hit && hit.Cursor == Cursors.SizeAll);
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(chrome.ActualWidth), (int)Math.Ceiling(chrome.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(chrome);
                        byte[] pixel = new byte[4];
                        bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
                        AssertTrue(pixel[3] > 0);
                        Console.WriteLine("PROOF edit-area " + panel.Title + " alpha=" + pixel[3] + " cursor=SizeAll clickThrough=false");
                    }
                    DependencyObject? gripHit = root.InputHitTest(new Point(root.ActualWidth - 5, root.ActualHeight - 5)) as DependencyObject;
                    while (gripHit != null && !(gripHit is ResizeGrip)) gripHit = VisualTreeHelper.GetParent(gripHit);
                    if (!(gripHit is ResizeGrip)) throw new InvalidOperationException("Resize grip hit missing: " + panel.Title);
                }
                CaptureLayout((FrameworkElement)eventWindow.Content, "event-edit-preview");
                CaptureLayout((FrameworkElement)raceWindow.Content, "race-control-edit-preview");
                double width = eventWindow.Width, height = eventWindow.Height;
                eventWindow.Width = width * 1.25; eventWindow.Height = height * 1.5;
                PumpDispatcher();
                AssertTrue(eventWindow.ActualWidth > width && eventWindow.ActualHeight > height);
                window.SetViewModel(DemoSnapshotFactory.CreateShell(false), true);
                PumpDispatcher();
                AssertTrue(DescendantText(eventView).Contains("이벤트 미리보기"));
                AssertEqual(1.0, Named<Border>(eventView, "Panel").Opacity);
                CaptureLayout((FrameworkElement)eventWindow.Content, "event-edit-resized");

                var live = DemoSnapshotFactory.CreateShell(false, OverlayEventType.PositionGained);
                window.SetViewModel(live, true);
                AssertTrue(ReferenceEquals(live.EventCard, eventView.DataContext));
                AssertFalse(live.EventCard.EventId.Contains("preview"));
                window.EndLayoutEdit(true);
                AssertTrue(ReferenceEquals(live.EventCard, eventView.DataContext));
                AssertTrue(OverlayWindowInterop.ReadStyleState(new WindowInteropHelper(eventWindow).Handle).ClickThrough);
                AssertFalse(File.ReadAllText(layout).Contains("미리보기"));
                window.SetViewModel(empty, false); window.ShowDemoAt(-5000, -5000, 96);
                AssertFalse(eventWindow.IsVisible);
                AssertTrue(window.BeginLayoutEdit()); window.EndLayoutEdit(false);
                AssertFalse(eventWindow.IsVisible || raceWindow.IsVisible);
                AssertTrue(ReferenceEquals(empty.EventCard, eventView.DataContext));
                AssertTrue(ReferenceEquals(empty.RaceControl, raceView.DataContext));
            }
            finally { window.EndLayoutEdit(false); window.Close(); if (File.Exists(layout)) File.Delete(layout); }
        }

        private static void TimingTowerEditDividersResizeAndPersist()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-tower-dividers-" + Guid.NewGuid() + ".json");
            var overlay = new OverlayWindow(false, path);
            try
            {
                overlay.BeginLayoutPreview(false, new GameWindowSnapshot(IntPtr.Zero, -5000, -5000, 1280, 720, 96, true, false, 0));
                PumpDispatcher();
                var tower = FindDescendant<OverlayHudView>(overlay)!;
                AssertTrue(Descendants<TextBlock>(tower).Any(text => text.Name == "PenaltyText"
                    && text.Text == "드라이브스루"));
                var canvas = Descendants<Canvas>(overlay).Single(item => item.Name == "TowerSizeHandles");
                Thumb[] handles = canvas.Children.OfType<Thumb>().ToArray();
                AssertEqual(7, handles.Length);
                AssertTrue(tower.TryGetResizeGeometry(out double[] boundaries, out _, out _));
                AssertEqual(6, boundaries.Length);
                AssertEqual(Visibility.Visible, handles[1].Visibility);
                Grid legacy = Descendants<Grid>(tower).First(grid => (string?)grid.Tag == "tower-columns");
                double classBefore = legacy.ColumnDefinitions[2].ActualWidth;
                double totalBefore = legacy.ColumnDefinitions.Sum(column => column.ActualWidth);
                handles[1].RaiseEvent(new DragDeltaEventArgs(-20, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                PumpDispatcher();
                AssertTrue(legacy.ColumnDefinitions[2].ActualWidth > classBefore + 10);
                AssertTrue(Math.Abs(legacy.ColumnDefinitions.Sum(column => column.ActualWidth) - totalBefore) < 2);
                double rowBefore = Descendants<Border>(tower).First(row => (string?)row.Tag == "tower-data-row").Height;
                double windowHeightBefore = overlay.Height;
                handles[6].RaiseEvent(new DragDeltaEventArgs(0, 8) { RoutedEvent = Thumb.DragDeltaEvent });
                PumpDispatcher();
                AssertTrue(Descendants<Border>(tower).Where(row => (string?)row.Tag == "tower-data-row")
                    .All(row => row.Height > rowBefore));
                AssertTrue(overlay.Height > windowHeightBefore);
                CaptureLayout((FrameworkElement)overlay.Content, "tower-direct-edit-legacy");

                var settings = overlay.GetDrivingHudSettings();
                settings.TowerDesign = "racing";
                overlay.PreviewDrivingHudSettings(settings);
                PumpDispatcher();
                Grid racing = Descendants<Grid>(tower).First(grid => (string?)grid.Tag == "tower-columns");
                AssertTrue(Descendants<TextBlock>(tower).Any(text => text.Name == "PenaltyText"
                    && text.Text == "드라이브스루"));
                double racingClassBefore = racing.ColumnDefinitions[2].ActualWidth;
                handles[1].RaiseEvent(new DragDeltaEventArgs(-12, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                PumpDispatcher();
                AssertTrue(racing.ColumnDefinitions[2].ActualWidth > racingClassBefore);
                AssertEqual(Visibility.Visible, handles[6].Visibility);
                CaptureLayout((FrameworkElement)overlay.Content, "tower-direct-edit-racing");
                overlay.EndLayoutEdit(true);
                var saved = new OverlayLayoutStore(path).Load().DrivingHud.TextFor(OverlayComponentKeys.TimingTower);
                AssertTrue(saved.TowerColumnScales["class"] > 1);
                AssertTrue(saved.TowerRowHeight > 36);
                Console.WriteLine("PROOF direct edit dividers resized both tower designs, retained total width and persisted row height");
            }
            finally { overlay.EndLayoutEdit(false); overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
