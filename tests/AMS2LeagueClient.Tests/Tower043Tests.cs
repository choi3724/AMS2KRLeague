using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void TowerShrinksAndRestoresWithParticipants()
        {
            string layout = Path.Combine(Path.GetTempPath(), "ams2-auto-height-" + Guid.NewGuid() + ".json");
            var window = new OverlayWindow(false, layout);
            try
            {
                double originalWidth = 0;
                foreach (int count in new[] { 15, 2, 1, 5, 15, 2 })
                {
                    var rows = Enumerable.Range(1, count).Select(i => Row(i, "P" + i, "참가자 " + i, "1:40.123")).ToArray();
                    rows[0].Status = "BEST";
                    var timing = TimingRows(rows);
                    timing.AllRankingRows = rows;
                    window.SetViewModel(new OverlayShellViewModel { Timing = timing }, false);
                    window.ShowDemoAt(-5000, -5000, 96);
                    PumpDispatcher();
                    var hud = FindDescendant<OverlayHudView>(window)!;
                    var items = FindDescendant<ItemsControl>(hud)!;
                    int height = LeftTowerLayoutMetrics.RequiredHeightForRows(count, false);
                    AssertEqual((double)height, hud.Height);
                    AssertEqual(15, timing.RankingRowCapacity);
                    AssertEqual(count, items.Items.Count);
                    if (originalWidth == 0) originalWidth = window.ActualWidth;
                    AssertEqual(originalWidth, window.ActualWidth);
                    AssertTrue(Math.Abs(window.ActualHeight / window.ActualWidth - height / 648.0) < 0.01);
                    AssertTrue(Container(items, count - 1).TranslatePoint(new Point(0, 36), window).Y <= window.ActualHeight + 1);
                    AssertEqual("최고속 랩", Named<TextBlock>(Container(items, 0), "StatusText").Text);
                    Console.WriteLine("PROOF tower participants=" + count + " width=" + window.ActualWidth + " height=" + window.ActualHeight + " capacity=" + timing.RankingRowCapacity);
                    if (count == 2 || count == 15) CaptureLayout((FrameworkElement)window.Content, "tower-auto-" + count);
                }
                AssertFalse(File.Exists(layout)); // Automatic shrinking must not overwrite the user's maximum.
                AssertTrue(window.BeginLayoutEdit());
                PumpDispatcher();
                AssertTrue(window.ActualHeight / window.ActualWidth > 0.9);
                window.EndLayoutEdit(true);
                PumpDispatcher();
                AssertTrue(window.ActualHeight / window.ActualWidth < 0.2);
                var saved = System.Text.Json.JsonSerializer.Deserialize<OverlayLayoutProfile>(File.ReadAllText(layout), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })!;
                var bounds = saved.Resolve(OverlayComponentKeys.TimingTower, new OverlayBounds(0, 0, 648, 608), 1920, 1080);
                AssertEqual(15, LeftTowerLayoutMetrics.CalculateRankingRows(bounds.Width, bounds.Height, false));
            }
            finally { window.EndLayoutEdit(false); window.Close(); if (File.Exists(layout)) File.Delete(layout); }
        }
    }
}
