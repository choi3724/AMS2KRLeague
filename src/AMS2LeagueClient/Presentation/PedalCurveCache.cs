using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    // Retained, bounded chunks: only the newest segment changes. Old paths stay frozen.
    internal sealed class PedalCurveCache
    {
        private DrivingTelemetryHistory? _history;
        private long _revision = -1;
        private double _width, _height;
        private DateTimeOffset _origin;
        private readonly TranslateTransform _offset = new TranslateTransform();
        private readonly Channel[] _channels = { new Channel(0), new Channel(1), new Channel(2), new Channel(3), new Channel(0, true) };
        internal DrawingGroup Drawing(int channel, Pen stroke)
        { _channels[channel].SetStroke(stroke); return _channels[channel].Drawing; }
        internal long SamplesProcessed { get; private set; }
        internal PedalCurveCache() { foreach (var channel in _channels) { channel.Drawing.Transform = _offset; } }
        internal void Update(DrivingTelemetryHistory history, double width, double height)
        {
            bool reset = _origin == default || !ReferenceEquals(history, _history) || width != _width || height != _height ||
                (history.Revision != _revision && history.Revision != _revision + 1) || history.LastObserved == null;
            if (!reset && _revision == history.Revision) return;
            _history = history; _width = width; _height = height;
            if (reset)
            {
                foreach (var channel in _channels) channel.Clear();
                _origin = history.LastObserved?.CapturedAt ?? default;
                foreach (var sample in history.Samples) Append(sample);
            }
            else if (history.LastObserved != null) Append(history.LastObserved);
            _revision = history.Revision;
            if (history.LastObserved != null)
                _offset.X = width * (1 - (history.LastObserved.CapturedAt - _origin).TotalSeconds / 10);
        }
        private void Append(DrivingTelemetrySample sample)
        {
            SamplesProcessed++;
            double x = (sample.CapturedAt - _origin).TotalSeconds * _width / 10;
            foreach (var channel in _channels) channel.Append(sample, x, _height);
        }
        internal void Clear()
        {
            _history = null; _revision = -1;
            foreach (var channel in _channels) channel.Clear();
        }
        private sealed class Channel
        {
            internal readonly DrawingGroup Drawing = new DrawingGroup();
            private Pen? _stroke;
            internal void SetStroke(Pen stroke)
            {
                if (ReferenceEquals(_stroke, stroke)) return;
                _stroke = stroke;
                int i = 0;
                foreach (var chunk in _sealed) Drawing.Children[i++] = Seal(chunk.Path, stroke);
                if (i < Drawing.Children.Count) ((GeometryDrawing)Drawing.Children[i]).Pen = stroke;
            }
            private static GeometryDrawing Seal(PathGeometry path, Pen? stroke)
            {
                // Keep source figures for settings changes; render a compiled filled outline.
                var geometry = stroke == null ? RetainedGeometry.Compile(path) : RetainedGeometry.Compile(path.GetWidenedPathGeometry(stroke));
                var drawing = new GeometryDrawing(stroke?.Brush, null, geometry);
                drawing.Freeze(); return drawing;
            }
            private readonly Queue<(PathGeometry Path, DateTimeOffset End)> _sealed = new Queue<(PathGeometry, DateTimeOffset)>();
            private PathGeometry? _open;
            private PathFigure? _figure;
            private LineSegment? _tail;
            private Point? _previous;
            private Point _end;
            private DateTimeOffset _time;
            private double _filtered;
            private bool _ready, _drawing;
            private int _segments;
            private readonly int _channel;
            private readonly bool _absOnly;
            internal Channel(int channel, bool absOnly = false) { _channel = channel; _absOnly = absOnly; }
            internal void Clear()
            {
                Drawing.Children.Clear(); _sealed.Clear(); _open = null; _figure = null; _tail = null;
                _previous = null; _ready = _drawing = false; _time = default; _segments = 0;
            }
            internal void Append(DrivingTelemetrySample sample, double x, double height)
            {
                double? value = sample.Pedals[_channel];
                bool gap = _time != default && (sample.CapturedAt - _time).TotalMilliseconds > 150;
                if (!value.HasValue || gap)
                { _previous = null; _ready = _drawing = false; _figure = null; _tail = null; }
                double dt = (sample.CapturedAt - _time).TotalSeconds; _time = sample.CapturedAt;
                while (_sealed.Count > 0 && (_sealed.Count > 64 || (sample.CapturedAt - _sealed.Peek().End).TotalSeconds > 10))
                { _sealed.Dequeue(); Drawing.Children.RemoveAt(0); }
                if (!value.HasValue) return;
                _filtered = _ready ? _filtered + (value.Value - _filtered) * (1 - Math.Exp(-dt / .1)) : value.Value;
                _ready = true;
                var point = new Point(x, 4 + (1 - _filtered) * height);
                bool include = !_absOnly || sample.AbsActive;
                var end = _previous.HasValue ? new Point((_previous.Value.X + point.X) / 2, (_previous.Value.Y + point.Y) / 2) : point;
                if (include)
                {
                    if (_tail != null) _figure?.Segments.Remove(_tail);
                    if (_open == null || _segments >= 64)
                    {
                        if (_open != null) { _open.Freeze(); Drawing.Children[Drawing.Children.Count-1] = Seal(_open, _stroke); _sealed.Enqueue((_open, sample.CapturedAt)); }
                        _open = new PathGeometry(); Drawing.Children.Add(new GeometryDrawing(null, _stroke, _open)); _segments = 0; _figure = null;
                    }
                    if (_figure == null || !_drawing)
                    { _figure = new PathFigure { StartPoint = _previous.HasValue ? _end : point, IsFilled = false }; _open.Figures.Add(_figure); }
                    if (_previous.HasValue)
                    { _figure.Segments.Add(new QuadraticBezierSegment(_previous.Value, end, true)); _segments++; }
                    _tail ??= new LineSegment(); _tail.Point = point; _figure.Segments.Add(_tail);
                }
                else { _tail = null; _figure = null; }
                _drawing = include; _previous = point; _end = end;
            }
        }
    }
}
