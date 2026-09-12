using System.Windows;
using System.Windows.Media;

namespace AMS2LeagueClient.Presentation
{
    // PathGeometry serializes its figures again during WPF bounds walks, even when frozen.
    // StreamGeometry retains the encoded commands. Compile only immutable resources.
    internal static class RetainedGeometry
    {
        internal static StreamGeometry Compile(Geometry source)
        {
            var path = PathGeometry.CreateFromGeometry(source);
            var result = new StreamGeometry { FillRule = path.FillRule, Transform = path.Transform };
            using (var dc = result.Open())
                foreach (var f in path.Figures)
                {
                    dc.BeginFigure(f.StartPoint, f.IsFilled, f.IsClosed);
                    foreach (var s in f.Segments)
                        switch (s)
                        {
                            case LineSegment p: dc.LineTo(p.Point,p.IsStroked,p.IsSmoothJoin); break;
                            case QuadraticBezierSegment p: dc.QuadraticBezierTo(p.Point1,p.Point2,p.IsStroked,p.IsSmoothJoin); break;
                            case BezierSegment p: dc.BezierTo(p.Point1,p.Point2,p.Point3,p.IsStroked,p.IsSmoothJoin); break;
                            case ArcSegment p: dc.ArcTo(p.Point,p.Size,p.RotationAngle,p.IsLargeArc,p.SweepDirection,p.IsStroked,p.IsSmoothJoin); break;
                            case PolyLineSegment p: dc.PolyLineTo(p.Points,p.IsStroked,p.IsSmoothJoin); break;
                            case PolyBezierSegment p: dc.PolyBezierTo(p.Points,p.IsStroked,p.IsSmoothJoin); break;
                            case PolyQuadraticBezierSegment p: dc.PolyQuadraticBezierTo(p.Points,p.IsStroked,p.IsSmoothJoin); break;
                            default: throw new System.NotSupportedException(s.GetType().FullName);
                        }
                }
            result.Freeze(); return result;
        }
    }
}
