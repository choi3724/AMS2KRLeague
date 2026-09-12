using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void MainOverlayGallery()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-gallery-" + Guid.NewGuid() + ".json");
            var overlay = new OverlayWindow(false, path);
            var watch = Stopwatch.StartNew();
            var status = new ClientStatusWindow(new ClientStatusViewModel()) { Left = -5000, Top = -5000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false };
            Console.WriteLine("PROOF gallery construction ms=" + watch.ElapsedMilliseconds);
            try
            {
                var settings = new DrivingHudSettings { TowerDesign = "racing", TelemetryDesign = "legacy",
                    BrakeColor = "#FF6633", SpeedFont = "Bahnschrift", SteeringRangeDegrees = 1080 };
                overlay.SaveDrivingHudSettings(settings);
                overlay.SetComponentEnabled(OverlayComponentKeys.PedalGauge, false);
                status.SetDesignLabels(overlay.GetDrivingHudSettings());
                status.SetLayoutComponentStates(OverlayComponentKeys.All.ToDictionary(key => key, overlay.IsComponentEnabled));
                int designChanges = 0;
                status.OverlayDesignSelected += (_, e) =>
                {
                    var next = overlay.GetDrivingHudSettings();
                    if (e.Component == OverlayComponentKeys.TimingTower) next.TowerDesign = e.Design;
                    else next.TelemetryDesign = e.Design;
                    overlay.SaveDrivingHudSettings(next); status.SetDesignLabels(next); designChanges++;
                };
                status.LayoutComponentToggled += (_, e) => overlay.SetComponentEnabled(e.Component, e.Enabled);
                status.Show(); PumpDispatcher();
                var root = (FrameworkElement)status.Content;
                var choices = Descendants<RadioButton>(root).ToArray();
                AssertEqual(4, choices.Length);
                foreach (RadioButton choice in choices)
                {
                    var selection = ((string Component, string Design))choice.Tag;
                    string expected = selection.Design == "legacy" ? "기본" : "개량";
                    AssertEqual(expected, ((TextBlock)((StackPanel)choice.Content).Children[0]).Text);
                }
                var pairs = Named<Grid>(root, "OverlayPairGrid");
                AssertEqual(2, pairs.ColumnDefinitions.Count);
                AssertEqual(6, pairs.RowDefinitions.Count);
                AssertEqual(12, pairs.Children.Count);
                RadioButton Choice(string key, string design) => choices.Single(choice =>
                    ((string Component, string Design))choice.Tag == (key, design));
                AssertTrue(Choice(OverlayComponentKeys.TimingTower, "racing").IsChecked == true);
                var previews = Descendants<Image>(root).ToArray();
                AssertEqual(16, previews.Length);
                foreach (var image in previews)
                {
                    var bitmap = (BitmapSource)image.Source;
                    AssertTrue(bitmap.IsFrozen && bitmap.PixelWidth > 100 && bitmap.PixelHeight > 100);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    int bright = 0;
                    for (int i = 0; i < pixels.Length; i += 4)
                        if (pixels[i + 3] > 100 && Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) > 100) bright++;
                    if (bright <= 100) Console.WriteLine("EMPTY PREVIEW " + System.Windows.Automation.AutomationProperties.GetName(image) + " bright=" + bright);
                    AssertTrue(bright > 100); // Every entry contains rendered content, not an empty placeholder.
                }
                var sources = previews.Select(image => image.Source).ToArray();
                Choice(OverlayComponentKeys.PedalTelemetry, "racing").IsChecked = true;
                Choice(OverlayComponentKeys.TimingTower, "legacy").IsChecked = true;
                AssertEqual(2, designChanges);
                AssertEqual("legacy", overlay.GetDrivingHudSettings().TowerDesign);
                AssertEqual("racing", overlay.GetDrivingHudSettings().TelemetryDesign);
                AssertEqual("#FF6633", overlay.GetDrivingHudSettings().BrakeColor);
                AssertEqual(1080.0, overlay.GetDrivingHudSettings().SteeringRangeDegrees);
                AssertEqual("Bahnschrift", overlay.GetDrivingHudSettings().SpeedFont);
                AssertFalse(overlay.IsComponentEnabled(OverlayComponentKeys.PedalGauge));
                Named<CheckBox>(root, "PedalGaugeCheck").IsChecked = true;
                AssertTrue(overlay.IsComponentEnabled(OverlayComponentKeys.PedalGauge));
                status.SetDesignLabels(overlay.GetDrivingHudSettings());
                AssertEqual(2, designChanges);
                AssertTrue(previews.Select((image, index) => ReferenceEquals(image.Source, sources[index])).All(value => value));
                var scroll = Named<ScrollViewer>(root, "GalleryScroll");
                foreach (var size in new[] { new Size(1120, 900), new Size(860, 660) })
                {
                    status.Width = size.Width; status.Height = size.Height; scroll.ScrollToTop(); PumpDispatcher();
                    var notice = Named<TextBlock>(root, "UpdateNotice");
                    AssertFalse(Named<Expander>(root, "ConnectionDetails").IsExpanded);
                    foreach (string message in new[] { "업데이트: 최신 버전 확인 중",
                        "업데이트: 다운로드 중 · 50%",
                        "업데이트: 다운로드 완료 · 경기 기록 저장이 끝나면 자동 설치합니다",
                        "업데이트: 0.7.2 다운로드 완료 · 10초 후 설치를 위해 종료하며, 설치 후 자동으로 다시 실행됩니다." })
                    {
                        ((ClientStatusViewModel)status.DataContext).UpdateText = message;
                        PumpDispatcher();
                        AssertEqual(message, notice.Text);
                        AssertTrue(notice.IsVisible && notice.ActualHeight > 0);
                        Rect noticeBounds = notice.TransformToAncestor(root).TransformBounds(new Rect(notice.RenderSize));
                        AssertTrue(noticeBounds.Top >= 0 && noticeBounds.Bottom <= root.ActualHeight);
                        AssertTrue(noticeBounds.Left >= 0 && noticeBounds.Right <= root.ActualWidth);
                    }
                    AssertTrue(scroll.ViewportHeight > 100);
                    AssertEqual(0.0, scroll.ScrollableWidth);
                    for (int i = 0; i < pairs.Children.Count; i += 2)
                    {
                        var left = (FrameworkElement)pairs.Children[i];
                        var right = (FrameworkElement)pairs.Children[i + 1];
                        Rect a = left.TransformToAncestor(root).TransformBounds(new Rect(left.RenderSize));
                        Rect b = right.TransformToAncestor(root).TransformBounds(new Rect(right.RenderSize));
                        AssertTrue(Math.Abs(a.Top - b.Top) < .01 && Math.Abs(a.Width - b.Width) < .01);
                        AssertTrue(a.Right <= b.Left && b.Right <= root.ActualWidth + 1);
                        AssertEqual(i / 2, Grid.GetRow(left)); AssertEqual(i / 2, Grid.GetRow(right));
                        AssertEqual(0, Grid.GetColumn(left)); AssertEqual(1, Grid.GetColumn(right));
                    }
                    foreach (var choice in choices)
                    {
                        Rect bounds = choice.TransformToAncestor(root).TransformBounds(new Rect(choice.RenderSize));
                        AssertTrue(bounds.Left >= 0 && bounds.Right <= root.ActualWidth + 1);
                    }
                    CaptureLayout(status, "gallery-main-" + size.Width + "x" + size.Height, 1);
                    scroll.ScrollToVerticalOffset(450); PumpDispatcher();
                    CaptureLayout(status, "gallery-middle-" + size.Width + "x" + size.Height, 1);
                    scroll.ScrollToBottom(); PumpDispatcher();
                    AssertTrue(Named<CheckBox>(root, "GearCheck").TransformToAncestor(scroll).Transform(new Point()).Y < scroll.ActualHeight);
                    CaptureLayout(status, "gallery-bottom-" + size.Width + "x" + size.Height, 1);
                    foreach (Button button in Descendants<Button>(root))
                    {
                        Rect bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                        AssertTrue(bounds.Left >= 0 && bounds.Right <= root.ActualWidth + 1);
                        AssertTrue(bounds.Top >= 0 && bounds.Bottom <= root.ActualHeight + 1);
                    }
                }
                // The gallery owns only frozen bitmaps; waiting does not regenerate examples or run live HUDs.
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                timer.Tick += (_, __) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
                AssertTrue(previews.Select((image, index) => ReferenceEquals(image.Source, sources[index])).All(value => value));
                AssertFalse(Descendants<PedalTelemetryView>(status).Any());
                AssertFalse(Descendants<OverlayHudView>(status).Any());
                overlay.Close();
                overlay = new OverlayWindow(false, path);
                AssertEqual("legacy", overlay.GetDrivingHudSettings().TowerDesign);
                AssertEqual("racing", overlay.GetDrivingHudSettings().TelemetryDesign);
                AssertTrue(overlay.IsComponentEnabled(OverlayComponentKeys.PedalGauge));
                Console.WriteLine("PROOF 14 entries / 16 nonempty frozen previews; direct design choices persist independently of display toggles and colours; narrow window scrolling and footer remain accessible");
            }
            finally { status.Close(); overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
