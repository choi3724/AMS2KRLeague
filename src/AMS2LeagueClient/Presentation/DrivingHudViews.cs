using System;
using System.Globalization;
using System.Collections.Generic;
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
        private readonly SteeringWheelView? _wheel;
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
                _wheel = new SteeringWheelView(); grid.Children.Add(_wheel);
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
            if (_wheel != null) { _wheel.RotationRange = settings.Normalize().SteeringRangeDegrees; _wheel.InvalidateVisual(); }
        }

        public void SetHistory(DrivingTelemetryHistory history)
        {
            _graph.SetHistory(history);
            _wheel?.SetSample(history.Current);
        }

        private sealed class PedalGraph : FrameworkElement
        {
            private DrivingTelemetryHistory _history = new DrivingTelemetryHistory();
            private DrivingTelemetrySample? _current;
            private readonly Stopwatch _scrollClock = new Stopwatch();
            private readonly TranslateTransform _scroll = new TranslateTransform();
            private int _renderCount;
            private readonly PedalCurveCache _cache = new PedalCurveCache();
            private readonly Pen?[] _pens = new Pen?[5];
            private readonly HudHistoryScroll _motion;
            private static readonly Pen GridPen = FrozenPen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
            private static readonly Brush BarBackground = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255));
            private static Pen FrozenPen(Brush brush, double width)
            { var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; pen.Freeze(); return pen; }
            private Pen Stroke(int channel)
            { Brush color = channel == 4 ? Brushes.Gold : Colors[channel]; if (_pens[channel]?.Brush != color) _pens[channel] = FrozenPen(color, 3); return _pens[channel]!; }
            private readonly bool _gaugesOnly;
            private readonly DrawingVisual _gaugeLabels = new DrawingVisual();
            private readonly RectangleGeometry[] _barShapes = { new RectangleGeometry(), new RectangleGeometry(), new RectangleGeometry(), new RectangleGeometry() };
            private readonly HudTargetMotion[] _barMotion = new HudTargetMotion[4];
            private readonly double[] _barLevels = new double[4];
            private readonly bool[] _barValid = new bool[4];
            private readonly string?[] _barLabelValues = new string?[4];
            private bool _labelsDirty = true;
            protected override int VisualChildrenCount => _gaugesOnly ? 1 : 0;
            protected override Visual GetVisualChild(int index) => _gaugesOnly && index == 0 ? _gaugeLabels : throw new ArgumentOutOfRangeException(nameof(index));
            private void PlaceBar(int channel)
            {
                int column = channel < 2 ? 1-channel : channel;
                double height = Math.Max(1,ActualHeight-36), width = Math.Max(1,ActualWidth/4-10);
                double level = _barValid[channel] ? _barLevels[channel] : 0;
                _barShapes[channel].Rect = new Rect(column*ActualWidth/4+5,20+height*(1-level),width,height*level);
            }
            private void UpdateGauge(DrivingTelemetrySample? sample, bool continuous)
            {
                for (int i=0;i<4;i++)
                {
                    double? value = sample?.Pedals[i]; _barValid[i] = value.HasValue;
                    _barMotion[i].Set(value ?? 0,continuous && value.HasValue && IsVisible);
                    string label = value.HasValue ? (value.Value*100).ToString("0",CultureInfo.InvariantCulture) : "—";
                    if (_barLabelValues[i] != label) { _barLabelValues[i] = label; _labelsDirty = true; }
                }
                DrawGaugeLabels();
            }
            private void DrawGaugeLabels()
            {
                if (!_labelsDirty || ActualWidth <= 0) return;
                _labelsDirty = false;
                using var dc = _gaugeLabels.RenderOpen();
                for (int i=0;i<4;i++)
                {
                    int column = i<2 ? 1-i : i;
                    double width=Math.Max(1,ActualWidth/4-10), x=column*ActualWidth/4+5;
                    DrawLabel(dc,_barLabelValues[i] ?? "—",Brushes.White,x,width,0);
                    DrawLabel(dc,"BACH"[i].ToString(),Colors[i],x,width,22+Math.Max(1,ActualHeight-36));
                }
            }
            private void StopBars() { foreach(var motion in _barMotion) motion.Stop(); }

            private readonly Dictionary<string, (Geometry Shape, double Width)> _labels = new Dictionary<string, (Geometry, double)>();
            private double _labelDpi;
            private void DrawLabel(DrawingContext dc, string value, Brush brush, double barX, double barWidth, double y)
            {
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                if (_labelDpi != dpi) { _labels.Clear(); _labelDpi = dpi; }
                if (!_labels.TryGetValue(value, out var label))
                {
                    var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 12, Brushes.White, dpi);
                    var shape = RetainedGeometry.Compile(text.BuildGeometry(new Point()));
                    label = (shape, text.Width); _labels[value] = label;
                }
                dc.PushTransform(new TranslateTransform(barX + (barWidth - label.Width) / 2, y));
                dc.DrawGeometry(brush, null, label.Shape); dc.Pop();
            }
            private readonly Typeface _typeface = new Typeface(DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName),
                FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            public void SetHistory(DrivingTelemetryHistory history)
            {
                bool changed = !ReferenceEquals(_current, history.Current) || !ReferenceEquals(_history, history);
                bool continuous = _current != null && history.Current != null && _current.Generation == history.Current.Generation
                    && _current.ParticipantIndex == history.Current.ParticipantIndex && history.Current.CapturedAt >= _current.CapturedAt
                    && (history.Current.CapturedAt-_current.CapturedAt).TotalMilliseconds <= 150;
                _history = history;
                if (changed) { _current = history.Current; if (_current != null) _scrollClock.Restart(); }
                if (!_gaugesOnly) _cache.Update(history, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight - 8));
                UpdateRendering();
                if (_gaugesOnly && changed) UpdateGauge(_current,continuous);
            }
            public Brush[] Colors { get; } = { Brushes.Red, Brushes.Lime, Brushes.DodgerBlue, Brushes.MediumPurple };
            public PedalGraph(bool gaugesOnly)
            {
                _motion = new HudHistoryScroll(_scroll, _scrollClock, this);
                _gaugesOnly = gaugesOnly;
                for (int i=0;i<4;i++) { int channel=i; _barMotion[i] = new HudTargetMotion(35,level=>{_barLevels[channel]=level;PlaceBar(channel);},this); }
                if (_gaugesOnly) AddVisualChild(_gaugeLabels);
                ClipToBounds = true;
                SizeChanged += (s, e) => { _labelsDirty = true; UpdateRendering(); InvalidateVisual(); };
                IsVisibleChanged += (s, e) => { if (!IsVisible) StopBars(); UpdateRendering(); };
                Loaded += (s, e) => UpdateRendering();
                Unloaded += (s, e) => { _motion.Stop(); StopBars(); _cache.Clear(); _labels.Clear(); _labelsDirty = true; };
            }

            private void UpdateRendering()
            {
                _motion.Update(Math.Max(1, ActualWidth), !_gaugesOnly && IsLoaded && IsVisible && _current != null);
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                _renderCount++;
                double plotWidth = Math.Max(1, ActualWidth), plotHeight = Math.Max(1, ActualHeight - (_gaugesOnly ? 36 : 8));
                var gridPen = GridPen;
                for (int row = 0; !_gaugesOnly && row < 3; row++)
                    dc.DrawLine(gridPen, new Point(0, row * plotHeight / 2 + 4), new Point(plotWidth, row * plotHeight / 2 + 4));
                if (!_gaugesOnly)
                    for (int column = 1; column < 8; column++)
                        dc.DrawLine(gridPen, new Point(plotWidth * column / 8, 4), new Point(plotWidth * column / 8, 4 + plotHeight));
                DrivingTelemetrySample? current = _history.LastObserved;
                if (!_gaugesOnly)
                {
                    _cache.Update(_history, plotWidth, plotHeight);
                    // Reuse observed curves until the next sample; only their screen position changes each frame.
                    dc.PushTransform(_scroll);
                }
                for (int channel = 0; channel < 4; channel++)
                {
                    if (!_gaugesOnly)
                    {
                        dc.DrawDrawing(_cache.Drawing(channel, Stroke(channel)));
                        if (channel == 0)
                            dc.DrawDrawing(_cache.Drawing(4, Stroke(4)));
                    }
                    if (!_gaugesOnly) continue;
                    // Input history remains B/A/C/H; display the bars in A/B/C/H order.
                    int column = channel < 2 ? 1 - channel : channel;
                    double columnWidth = ActualWidth / 4;
                    double barWidth = Math.Max(1, columnWidth - 10);
                    double barX = column * columnWidth + 5;
                    dc.DrawRectangle(BarBackground, null, new Rect(barX, 20, barWidth, plotHeight));
                    PlaceBar(channel);
                    dc.DrawGeometry(Colors[channel],null,_barShapes[channel]);
                }
                if (!_gaugesOnly) dc.Pop();
                else { _labelsDirty = true; DrawGaugeLabels(); }
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
