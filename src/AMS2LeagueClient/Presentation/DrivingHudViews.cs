using System;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public sealed class PedalTelemetryView : UserControl
    {
        private readonly PedalGraph _graph = new PedalGraph();
        public PedalTelemetryView()
        {
            FontFamily = DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName);
            var grid = new Grid { Margin = new Thickness(10, 6, 10, 6) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.Children.Add(new TextBlock { Text = "텔레메트리", FontSize = 13, Foreground = Brushes.White });
            Grid.SetRow(_graph, 1); grid.Children.Add(_graph);
            Content = new Border { Background = new SolidColorBrush(Color.FromArgb(185, 10, 17, 28)), Child = grid };
            AutomationProperties.SetName(this, "브레이크, 악셀, 클러치, 핸드브레이크 입력 그래프");
            ApplySettings(new DrivingHudSettings());
        }

        public void ApplySettings(DrivingHudSettings settings)
        {
            string[] colors = { settings.BrakeColor, settings.ThrottleColor, settings.ClutchColor, settings.HandBrakeColor };
            for (int i = 0; i < 4; i++)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
                brush.Freeze(); _graph.Colors[i] = brush;
            }
            _graph.InvalidateVisual();
        }

        public void SetHistory(DrivingTelemetryHistory history)
        {
            _graph.SetHistory(history);
        }

        private sealed class PedalGraph : FrameworkElement
        {
            private DrivingTelemetryHistory _history = new DrivingTelemetryHistory();
            private DrivingTelemetrySample? _current;
            private readonly Stopwatch _scrollClock = new Stopwatch();
            private bool _rendering;
            private readonly Typeface _typeface = new Typeface(DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName),
                FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            public void SetHistory(DrivingTelemetryHistory history)
            {
                _history = history;
                if (!ReferenceEquals(_current, history.Current))
                {
                    _current = history.Current;
                    _scrollClock.Restart();
                }
                UpdateRendering();
                InvalidateVisual();
            }
            public Brush[] Colors { get; } = { Brushes.Red, Brushes.Lime, Brushes.DodgerBlue, Brushes.MediumPurple };
            public PedalGraph()
            {
                ClipToBounds = true;
                SizeChanged += (s, e) => InvalidateVisual();
                IsVisibleChanged += (s, e) => UpdateRendering();
                Loaded += (s, e) => UpdateRendering();
                Unloaded += (s, e) => { CompositionTarget.Rendering -= OnFrame; _rendering = false; };
            }

            private void UpdateRendering()
            {
                bool needed = IsVisible && _current != null;
                if (needed == _rendering) return;
                _rendering = needed;
                if (needed) CompositionTarget.Rendering += OnFrame;
                else CompositionTarget.Rendering -= OnFrame;
            }

            private void OnFrame(object? sender, EventArgs args)
            {
                // Move observed points between 20 Hz samples; never invent pedal values.
                if (_scrollClock.Elapsed.TotalSeconds <= 1) InvalidateVisual();
            }

            private static StreamGeometry CreateCurve(DrivingTelemetryHistory history, int channel,
                DateTimeOffset rightEdge, double width, double height)
            {
                var geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    Point? previous = null;
                    DateTimeOffset previousTime = default;
                    double filtered = 0;
                    foreach (DrivingTelemetrySample sample in history.Samples)
                    {
                        double? value = sample.Pedals[channel];
                        if (!value.HasValue)
                        {
                            if (previous.HasValue) context.LineTo(previous.Value, true, false);
                            previous = null;
                            continue;
                        }
                        // Display-only 100 ms smoothing. Bars and recorded samples remain raw.
                        filtered = previous.HasValue
                            ? filtered + (value.Value - filtered) * (1 - Math.Exp(-(sample.CapturedAt - previousTime).TotalSeconds / 0.1))
                            : value.Value;
                        var point = new Point(width * (1 - (rightEdge - sample.CapturedAt).TotalSeconds / DrivingTelemetryHistory.DurationSeconds),
                            20 + (1 - filtered) * height);
                        if (!previous.HasValue) context.BeginFigure(point, false, false);
                        else context.QuadraticBezierTo(previous.Value,
                            new Point((previous.Value.X + point.X) / 2, (previous.Value.Y + point.Y) / 2), true, false);
                        previous = point;
                        previousTime = sample.CapturedAt;
                    }
                    if (previous.HasValue) context.LineTo(previous.Value, true, false);
                }
                geometry.Freeze();
                return geometry;
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                double plotWidth = Math.Max(1, ActualWidth - 160), plotHeight = Math.Max(1, ActualHeight - 42);
                var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), 1);
                for (int row = 0; row < 3; row++)
                    dc.DrawLine(gridPen, new Point(0, row * plotHeight / 2 + 20), new Point(plotWidth, row * plotHeight / 2 + 20));
                DrivingTelemetrySample? current = _history.Current;
                DateTimeOffset rightEdge = (current?.CapturedAt ?? DateTimeOffset.MinValue).AddSeconds(Math.Min(1, _scrollClock.Elapsed.TotalSeconds));
                for (int channel = 0; channel < 4; channel++)
                {
                    if (current != null)
                    {
                        StreamGeometry geometry = CreateCurve(_history, channel, rightEdge, plotWidth, plotHeight);
                        dc.DrawGeometry(null, new Pen(Colors[channel], 2) {
                            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
                        }, geometry);
                    }
                    // Input history remains B/A/C/H; display the bars in A/B/C/H order.
                    int column = channel < 2 ? 1 - channel : channel;
                    double barX = plotWidth + 16 + column * 36;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), null, new Rect(barX, 20, 24, plotHeight));
                    double? level = current?.Pedals[channel];
                    if (level.HasValue)
                        dc.DrawRectangle(Colors[channel], null, new Rect(barX, 20 + plotHeight * (1 - level.Value), 24, plotHeight * level.Value));
                    var text = new FormattedText(level.HasValue ? (level.Value * 100).ToString("0", CultureInfo.InvariantCulture) : "—",
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 12, Brushes.White,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(text, new Point(barX + (24 - text.Width) / 2, 0));
                    var label = new FormattedText("BACH"[channel].ToString(), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, _typeface, 12, Colors[channel], VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(label, new Point(barX + (24 - label.Width) / 2, 22 + plotHeight));
                }
            }
        }
    }

    public sealed class DrivingNumberView : UserControl
    {
        private readonly bool _gear;
        public TextBlock ValueText { get; }
        public DrivingNumberView(bool gear)
        {
            _gear = gear;
            FontFamily = ResolveFont(DrivingHudSettings.DefaultFontName);
            ValueText = new TextBlock { Text = gear ? "—" : "— km/h", FontSize = gear ? 64 : 36,
                FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center };
            var grid = new Grid();
            if (gear)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
                grid.RowDefinitions.Add(new RowDefinition());
                grid.Children.Add(new TextBlock { Text = "기어", Foreground = Brushes.White, FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            var box = new Viewbox { Stretch = Stretch.Uniform, Child = ValueText, Margin = new Thickness(4) };
            if (gear) Grid.SetRow(box, 1);
            grid.Children.Add(box);
            Content = new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(gear ? 2 : 0), Child = grid };
            AutomationProperties.SetName(this, gear ? "기어" : "속도");
        }

        public void SetSample(DrivingTelemetrySample? sample)
            => ValueText.Text = _gear ? sample?.GearText ?? "—" : sample?.SpeedText ?? "— km/h";

        public void ApplyFont(string name) => ValueText.FontFamily = ResolveFont(name);

        internal static FontFamily ResolveFont(string name)
        {
            // Resolve the bundled patch font locally; other choices must be installed families.
            if (!string.Equals(name, DrivingHudSettings.DefaultFontName, StringComparison.OrdinalIgnoreCase))
            {
                FontFamily? installed = Fonts.SystemFontFamilies.FirstOrDefault(font =>
                    string.Equals(font.Source, name, StringComparison.OrdinalIgnoreCase));
                if (installed != null) return installed;
            }
            return new FontFamily(new Uri("pack://application:,,,/AMS2LeagueClient;component/"),
                "./Assets/Fonts/#Pretendard");
        }
    }
}
