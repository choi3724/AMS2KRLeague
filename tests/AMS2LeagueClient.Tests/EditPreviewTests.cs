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
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
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
    }
}
