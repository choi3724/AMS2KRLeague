using System;
using System.Windows;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    internal static class PedalCurveBuilder
    {
        // Keep the first, last and both extrema in each pixel column. ABS
        // transitions and gaps split columns; captured samples remain untouched.
        internal static StreamGeometry Create(DrivingTelemetryHistory history, int channel,
            DateTimeOffset rightEdge, double width, double height, bool absOnly = false)
        {
            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            Point? previous = null; Point curveEnd = default;
            bool drawing = false, haveBucket = false, bucketAbs = false;
            DateTimeOffset previousTime = default;
            double filtered = 0; bool filterReady = false;
            int column = 0, ordinal = 0;
            Node first = default, last = default, min = default, max = default;
            void Draw(Point point, bool active, bool reduced)
            {
                bool include = !absOnly || active;
                if (!previous.HasValue)
                {
                    curveEnd = point;
                    if (include) { context.BeginFigure(point, false, false); drawing = true; }
                }
                else
                {
                    var end = new Point((previous.Value.X + point.X) / 2, (previous.Value.Y + point.Y) / 2);
                    if (include)
                    {
                        if (!drawing) context.BeginFigure(curveEnd, false, false);
                        if (reduced)
                        {
                            // A reduced column must pass through its extrema;
                            // another Bezier average would erase narrow peaks.
                            context.LineTo(previous.Value, true, false);
                            context.LineTo(point, true, false);
                            end = point;
                        }
                        else context.QuadraticBezierTo(previous.Value, end, true, false);
                    }
                    drawing = include; curveEnd = end;
                }
                previous = point;
            }
            void Flush()
            {
                if (!haveBucket) return;
                Span<Node> selected = stackalloc Node[4] { first, min, max, last };
                for (int a = 1; a < 4; a++)
                    for (int b = a; b > 0 && selected[b].Index < selected[b-1].Index; b--)
                    { Node swap = selected[b]; selected[b] = selected[b-1]; selected[b-1] = swap; }
                int prior = -1;
                foreach (Node node in selected)
                { if (node.Index != prior) Draw(node.Point, bucketAbs, last.Index - first.Index >= 4); prior = node.Index; }
                haveBucket = false;
            }
            foreach (DrivingTelemetrySample sample in history.Samples)
            {
                double? value = sample.Pedals[channel];
                bool gap = previousTime != default && (sample.CapturedAt - previousTime).TotalMilliseconds > 150;
                if (!value.HasValue || gap)
                {
                    Flush();
                    if (drawing && previous.HasValue) context.LineTo(previous.Value, true, false);
                    previous = null; drawing = false; filterReady = false;
                    if (!value.HasValue) { previousTime = sample.CapturedAt; continue; }
                }
                filtered = filterReady ? filtered + (value!.Value - filtered) *
                    (1 - Math.Exp(-(sample.CapturedAt - previousTime).TotalSeconds / .1)) : value!.Value;
                filterReady = true; previousTime = sample.CapturedAt;
                var point = new Point(width * (1 - (rightEdge - sample.CapturedAt).TotalSeconds / DrivingTelemetryHistory.DurationSeconds),
                    4 + (1 - filtered) * height);
                int nextColumn = (int)Math.Floor(point.X);
                if (haveBucket && (nextColumn != column || bucketAbs != sample.AbsActive)) Flush();
                var node = new Node { Index = ordinal++, Point = point };
                if (!haveBucket) { first = min = max = node; column = nextColumn; bucketAbs = sample.AbsActive; haveBucket = true; }
                last = node;
                if (node.Point.Y < min.Point.Y) min = node;
                if (node.Point.Y > max.Point.Y) max = node;
            }
            Flush();
            if (drawing && previous.HasValue) context.LineTo(previous.Value, true, false);
            context.Close(); geometry.Freeze(); return geometry;
        }
        private struct Node { public int Index; public Point Point; }
    }
}
