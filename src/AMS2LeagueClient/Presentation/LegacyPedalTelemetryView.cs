using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public sealed class LegacyPedalTelemetryView : UserControl
    {
        private readonly PedalGraph _graph = new PedalGraph();
        public LegacyPedalTelemetryView()
        {
            FontFamily = DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName);
            var grid = new Grid { Margin = new Thickness(10, 6, 10, 6) };
            grid.Children.Add(_graph);
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
            private readonly TranslateTransform _scroll = new TranslateTransform();
            private int _renderCount;
            private readonly StreamGeometry?[] _curves = new StreamGeometry?[4];
            private bool _curvesDirty = true;
            private Size _curveSize;
            public void SetHistory(DrivingTelemetryHistory history)
            {
                _history = history;
                if (!ReferenceEquals(_current, history.Current))
                {
                    _current = history.Current;
                    _curvesDirty = true;
                    if (_current != null) _scrollClock.Restart();
                }
                UpdateRendering();
                InvalidateVisual();
            }
            public Brush[] Colors { get; } = { Brushes.Red, Brushes.Lime, Brushes.DodgerBlue, Brushes.MediumPurple };
            public PedalGraph()
            {
                ClipToBounds = true;
                SizeChanged += (s, e) => { UpdateRendering(); InvalidateVisual(); };
                IsVisibleChanged += (s, e) => UpdateRendering();
                Loaded += (s, e) => UpdateRendering();
                Unloaded += (s, e) => _scroll.BeginAnimation(TranslateTransform.XProperty, null);
            }

            private void UpdateRendering()
            {
                HudMotion.ScrollHistory(_scroll, Math.Max(1, ActualWidth), _scrollClock.Elapsed.TotalSeconds,
                    IsLoaded && IsVisible && _current != null);
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                _renderCount++;
                double plotWidth = Math.Max(1, ActualWidth), plotHeight = Math.Max(1, ActualHeight - 8);
                var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), 1);
                for (int row = 0; row < 3; row++)
                    dc.DrawLine(gridPen, new Point(0, row * plotHeight / 2 + 4), new Point(plotWidth, row * plotHeight / 2 + 4));
                DrivingTelemetrySample? current = _history.LastObserved;
                if (current != null && (_curvesDirty || _curveSize != RenderSize))
                {
                    for (int i = 0; i < 4; i++) _curves[i] = PedalCurveBuilder.Create(_history, i, current.CapturedAt, plotWidth, plotHeight);
                    _curvesDirty = false; _curveSize = RenderSize;
                }
                for (int channel = 0; channel < 4; channel++)
                {
                    if (current != null)
                    {
                        StreamGeometry? geometry = _curves[channel];
                        dc.PushTransform(_scroll);
                        dc.DrawGeometry(null, new Pen(Colors[channel], 3) {
                            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
                        }, geometry);
                        dc.Pop();
                    }
                }
            }
        }
    }

}
