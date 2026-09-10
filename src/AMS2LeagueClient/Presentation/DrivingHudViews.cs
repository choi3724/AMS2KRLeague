using System;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public sealed class PedalTelemetryView : UserControl
    {
        private readonly PedalGraph _graph;
        private readonly SteeringWheelView _wheel = new SteeringWheelView();
        public PedalTelemetryView(bool gaugesOnly = false)
        {
            _graph = new PedalGraph(gaugesOnly);
            FontFamily = DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName);
            var grid = new Grid { Margin = new Thickness(10, 6, 10, 6) };
            grid.Children.Add(_graph);
            if (!gaugesOnly)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(_graph, 1);
                grid.Children.Add(_wheel);
            }
            var background = new SolidColorBrush(Color.FromArgb(245, 14, 20, 25));
            background.Freeze();
            Content = new Border { Background = background, BorderBrush = new SolidColorBrush(Color.FromArgb(45, 149, 172, 179)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = grid };
            AutomationProperties.SetName(this, gaugesOnly ? "악셀, 브레이크, 클러치, 핸드브레이크 페달 게이지" : "브레이크, 악셀, 클러치, 핸드브레이크 입력 그래프");
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
            _wheel.RotationRange = settings.Normalize().SteeringRangeDegrees;
            _wheel.InvalidateVisual();
        }

        public void SetHistory(DrivingTelemetryHistory history)
        {
            _graph.SetHistory(history);
            _wheel.SetSample(history.Current);
        }

        private sealed class PedalGraph : FrameworkElement
        {
            private DrivingTelemetryHistory _history = new DrivingTelemetryHistory();
            private DrivingTelemetrySample? _current;
            private readonly Stopwatch _scrollClock = new Stopwatch();
            private readonly TranslateTransform _scroll = new TranslateTransform();
            private int _renderCount;
            private bool _curvesDirty = true;
            private Size _curveSize;
            private readonly StreamGeometry?[] _curves = new StreamGeometry?[5];
            private readonly bool _gaugesOnly;
            private readonly Typeface _typeface = new Typeface(DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName),
                FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            public void SetHistory(DrivingTelemetryHistory history)
            {
                if (!ReferenceEquals(_history, history) || !ReferenceEquals(_current, history.Current)) _curvesDirty = true;
                _history = history;
                if (!ReferenceEquals(_current, history.Current))
                {
                    _current = history.Current;
                    if (_current != null) _scrollClock.Restart();
                }
                UpdateRendering();
                InvalidateVisual();
            }
            public Brush[] Colors { get; } = { Brushes.Red, Brushes.Lime, Brushes.DodgerBlue, Brushes.MediumPurple };
            public PedalGraph(bool gaugesOnly)
            {
                _gaugesOnly = gaugesOnly;
                ClipToBounds = true;
                SizeChanged += (s, e) => { UpdateRendering(); InvalidateVisual(); };
                IsVisibleChanged += (s, e) => UpdateRendering();
                Loaded += (s, e) => UpdateRendering();
                Unloaded += (s, e) => _scroll.BeginAnimation(TranslateTransform.XProperty, null);
            }

            private void UpdateRendering()
            {
                HudMotion.ScrollHistory(_scroll, Math.Max(1, ActualWidth), _scrollClock.Elapsed.TotalSeconds,
                    !_gaugesOnly && IsLoaded && IsVisible && _current != null);
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                _renderCount++;
                double plotWidth = Math.Max(1, ActualWidth), plotHeight = Math.Max(1, ActualHeight - (_gaugesOnly ? 36 : 8));
                var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
                for (int row = 0; !_gaugesOnly && row < 3; row++)
                    dc.DrawLine(gridPen, new Point(0, row * plotHeight / 2 + 4), new Point(plotWidth, row * plotHeight / 2 + 4));
                if (!_gaugesOnly)
                    for (int column = 1; column < 8; column++)
                        dc.DrawLine(gridPen, new Point(plotWidth * column / 8, 4), new Point(plotWidth * column / 8, 4 + plotHeight));
                DrivingTelemetrySample? current = _history.LastObserved;
                if (!_gaugesOnly && current != null)
                {
                    if (_curvesDirty || _curveSize != RenderSize)
                    {
                        for (int i = 0; i < 4; i++) _curves[i] = PedalCurveBuilder.Create(_history, i, current.CapturedAt, plotWidth, plotHeight);
                        _curves[4] = PedalCurveBuilder.Create(_history, 0, current.CapturedAt, plotWidth, plotHeight, absOnly: true);
                        _curvesDirty = false; _curveSize = RenderSize;
                    }
                    // Reuse observed curves until the next sample; only their screen position changes each frame.
                    dc.PushTransform(_scroll);
                }
                for (int channel = 0; channel < 4; channel++)
                {
                    if (!_gaugesOnly && current != null)
                    {
                        StreamGeometry? geometry = _curves[channel];
                        dc.DrawGeometry(null, new Pen(Colors[channel], 3) {
                            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
                        }, geometry);
                        if (channel == 0)
                            dc.DrawGeometry(null, new Pen(Brushes.Gold, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                                _curves[4]);
                    }
                    if (!_gaugesOnly) continue;
                    // Input history remains B/A/C/H; display the bars in A/B/C/H order.
                    int column = channel < 2 ? 1 - channel : channel;
                    double columnWidth = ActualWidth / 4;
                    double barWidth = Math.Max(1, columnWidth - 10);
                    double barX = column * columnWidth + 5;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), null, new Rect(barX, 20, barWidth, plotHeight));
                    double? level = _history.Current?.Pedals[channel];
                    if (level.HasValue)
                        dc.DrawRectangle(Colors[channel], null, new Rect(barX, 20 + plotHeight * (1 - level.Value), barWidth, plotHeight * level.Value));
                    var text = new FormattedText(level.HasValue ? (level.Value * 100).ToString("0", CultureInfo.InvariantCulture) : "—",
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 12, Brushes.White,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(text, new Point(barX + (barWidth - text.Width) / 2, 0));
                    var label = new FormattedText("BACH"[channel].ToString(), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, _typeface, 12, Colors[channel], VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(label, new Point(barX + (barWidth - label.Width) / 2, 22 + plotHeight));
                }
                if (!_gaugesOnly && current != null) dc.Pop();
            }
        }
    }

    public sealed class DrivingNumberView : UserControl
    {
        private readonly bool _gear;
        private readonly TextBlock? _title;
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
                _title = new TextBlock { Text = "기어", Foreground = Brushes.White, FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                grid.Children.Add(_title);
            }
            var box = new Viewbox { Stretch = Stretch.Uniform,
                Child = new Border { Padding = new Thickness(6), Child = ValueText }, Margin = new Thickness(4) };
            if (gear) Grid.SetRow(box, 1);
            grid.Children.Add(box);
            Content = new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(gear ? 2 : 0), Child = grid };
            AutomationProperties.SetName(this, gear ? "기어" : "속도");
            ApplyShadow("#000000");
        }

        public void SetSample(DrivingTelemetrySample? sample)
            => ValueText.Text = _gear ? sample?.GearText ?? "—" : sample?.SpeedText ?? "— km/h";

        public void ApplyFont(string name) => ValueText.FontFamily = ResolveFont(name);

        public void ApplyShadow(string color)
        {
            var shadow = new DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString(color),
                BlurRadius = 6, ShadowDepth = 2, Direction = 315, Opacity = 1
            };
            shadow.Freeze();
            ValueText.Effect = shadow;
            if (_title != null) _title.Effect = shadow;
        }

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
