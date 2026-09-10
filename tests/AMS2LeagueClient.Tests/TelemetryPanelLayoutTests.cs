using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void TelemetryPanelLayouts()
        {
            IEnumerable<Drawing> Drawings(Drawing drawing)
            {
                yield return drawing;
                if (drawing is DrawingGroup group)
                    foreach (Drawing child in group.Children)
                        foreach (Drawing item in Drawings(child)) yield return item;
            }
            var history = new DrivingTelemetryHistory();
            for (int i = 0; i <= 200; i++)
                history.Add(new DrivingTelemetrySample(FixedTime().AddSeconds(i * .05), 1, 3,
                    (1 + Math.Sin(i * .07)) / 2, (1 + Math.Cos(i * .07)) / 2,
                    (1 + Math.Sin(i * .09)) / 2, .1, 30, 3, i % 40 < 20, -.25));
            void CheckGraph(FrameworkElement view, bool legacy)
            {
                AssertFalse(Descendants<TextBlock>(view).Any(text => text.Text == "텔레메트리"));
                var graph = Descendants<FrameworkElement>(view).Single(item => item.GetType().Name == "PedalGraph");
                var drawings = Drawings(VisualTreeHelper.GetDrawing(graph)).ToArray();
                AssertEqual(0, drawings.OfType<GlyphRunDrawing>().Count());
                AssertFalse(drawings.OfType<GeometryDrawing>().Any(item => item.Geometry is RectangleGeometry));
                var curves = drawings.OfType<GeometryDrawing>().Where(item => item.Geometry is StreamGeometry).ToArray();
                AssertEqual(legacy ? 4 : 5, curves.Length);
                AssertTrue(curves.All(item => item.Pen.Thickness == 3));
                AssertTrue(Math.Abs(curves[0].Geometry.Bounds.Right - graph.ActualWidth) < .01);
                AssertTrue(curves.All(item => item.Geometry.Bounds.Top >= 3.99 && item.Geometry.Bounds.Bottom <= graph.ActualHeight - 3.99));
                if (legacy) AssertTrue(Math.Abs(graph.ActualWidth - view.ActualWidth + 20) < .01);
                else AssertTrue(curves.Any(item => item.Pen.Brush == Brushes.Gold));
            }
            void CheckWheel(SteeringWheelView wheel)
            {
                var drawings = Drawings(VisualTreeHelper.GetDrawing(wheel)).ToArray();
                Rect image = drawings.OfType<ImageDrawing>().Single().Rect;
                var glyphs = drawings.OfType<GlyphRunDrawing>().ToArray();
                Rect ink = Rect.Empty;
                foreach (var glyph in glyphs)
                {
                    ink.Union(glyph.Bounds);
                    AssertEqual(20.0, glyph.GlyphRun.FontRenderingEmSize);
                }
                AssertTrue(ink.Top - image.Bottom >= 2 && ink.Top - image.Bottom <= 10);
                AssertTrue(image.Top >= 0 && ink.Bottom <= wheel.ActualHeight + .01);
                AssertTrue(Math.Abs(image.Width - image.Height) < .01);
                var scale = drawings.OfType<DrawingGroup>().Select(item => item.Transform).OfType<ScaleTransform>().Single();
                AssertEqual(1.15, scale.ScaleX); AssertEqual(1.0, scale.ScaleY);
                Rect scaled = scale.TransformBounds(ink);
                AssertTrue(scaled.Left >= 0 && scaled.Right <= wheel.ActualWidth);
            }
            string path = Path.Combine(Path.GetTempPath(), "ams2-telemetry-layout-" + Guid.NewGuid() + ".json");
            var overlay = new OverlayWindow(false, path);
            try
            {
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.SetComponentEnabled(OverlayComponentKeys.PedalTelemetry, true);
                overlay.SetComponentEnabled(OverlayComponentKeys.PedalGauge, true);
                overlay.ShowDemoAt(-5000, -5000, 96);
                foreach (string design in new[] { "legacy", "racing" })
                {
                    overlay.SaveDrivingHudSettings(new DrivingHudSettings { TelemetryDesign = design });
                    PumpDispatcher();
                    var panel = Application.Current.Windows.Cast<Window>().Single(item =>
                        item.Title == (design == "legacy" ? "AMS2 텔레메트리 (기본)" : "AMS2 텔레메트리 (개량)"));
                    var view = design == "legacy" ? (FrameworkElement)FindDescendant<LegacyPedalTelemetryView>(panel)!
                        : FindDescendant<PedalTelemetryView>(panel)!;
                    if (view is LegacyPedalTelemetryView original) original.SetHistory(history);
                    else ((PedalTelemetryView)view).SetHistory(history);
                    foreach (var size in new[] { new Size(420, 160), new Size(420, 64), new Size(420, 48), new Size(240, 240), new Size(180, 48) })
                    {
                        panel.Width = size.Width; panel.Height = size.Height; PumpDispatcher();
                        CheckGraph(view, design == "legacy");
                        if (design == "racing") CheckWheel(FindDescendant<SteeringWheelView>(view)!);
                        CaptureLayout((FrameworkElement)panel.Content, "telemetry-" + design + "-" + size.Width + "x" + size.Height, 1);
                    }
                }
                var gaugePanel = Application.Current.Windows.Cast<Window>().Single(item => item.Title == "AMS2 페달 게이지");
                var gaugeView = FindDescendant<PedalTelemetryView>(gaugePanel)!;
                gaugeView.SetHistory(history); PumpDispatcher();
                var gauge = Descendants<FrameworkElement>(gaugeView).Single(item => item.GetType().Name == "PedalGraph");
                var gaugeDrawings = Drawings(VisualTreeHelper.GetDrawing(gauge)).ToArray();
                AssertEqual(8, gaugeDrawings.OfType<GlyphRunDrawing>().Count());
                AssertEqual(8, gaugeDrawings.OfType<GeometryDrawing>().Count(item => item.Geometry is RectangleGeometry));
                CaptureLayout((FrameworkElement)gaugePanel.Content, "telemetry-independent-gauges", 1);
                var wheel = new SteeringWheelView { Width = 84, Height = 148, RotationRange = 1440 };
                var host = new Window { Content = wheel, Left = -5000, Top = -5000, Width = 100, Height = 190, ShowActivated = false };
                try
                {
                    host.Show();
                    foreach (double angle in new[] { -1.0, 0, 1.0, double.NaN })
                    {
                        wheel.SetSample(new DrivingTelemetrySample(FixedTime(), 2, 3, 0, 0, 0, 0, 0, 0, steering: angle));
                        PumpDispatcher(); CheckWheel(wheel);
                        CaptureLayout(wheel, "telemetry-angle-" + angle, 1);
                    }
                }
                finally { host.Close(); }
                var status = new ClientStatusWindow(new ClientStatusViewModel()) { Left = -5000, Top = -5000, ShowActivated = false };
                try
                {
                    status.SetLayoutComponentStates(new Dictionary<string, bool> {
                        [OverlayComponentKeys.PedalTelemetry] = true, [OverlayComponentKeys.PedalGauge] = false });
                    status.Show(); PumpDispatcher();
                    var check = Named<CheckBox>(status, "PedalGaugeCheck");
                    AssertTrue(check.IsVisible && check.IsEnabled);
                    AssertEqual("페달 게이지", check.Content);
                    var toggles = new List<string>();
                    status.LayoutComponentToggled += (_, e) => toggles.Add(e.Component);
                    check.IsChecked = true;
                    AssertTrue(status.GetLayoutComponentStates()[OverlayComponentKeys.PedalTelemetry]);
                    AssertTrue(status.GetLayoutComponentStates()[OverlayComponentKeys.PedalGauge]);
                    AssertEqual(1, toggles.Count); AssertEqual(OverlayComponentKeys.PedalGauge, toggles[0]);
                    CaptureLayout((FrameworkElement)status.Content, "telemetry-main-selection", 1);
                }
                finally { status.Close(); }
                Console.WriteLine("PROOF both telemetry styles: no heading/bars, full-width 3px curves; five actual window aspect ratios; compact 20px/115% angle; independent main-screen pedal toggle");
            }
            finally { overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
