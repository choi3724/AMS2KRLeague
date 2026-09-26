using System;
using System.Windows;
using System.Windows.Media;

namespace AMS2LeagueClient.Presentation
{
    // Design coordinates trace the inner colour areas of avante-background.png.
    // The slanted chrome caps and red lower rim stay in the source image.
    internal static class AvanteBarGauge
    {
        internal readonly struct Outline
        {
            internal Outline(double topLeft, double bottomLeft, double topRight, double bottomRight)
            {
                TopLeft = topLeft; BottomLeft = bottomLeft;
                TopRight = topRight; BottomRight = bottomRight;
            }
            internal double TopLeft { get; }
            internal double BottomLeft { get; }
            internal double TopRight { get; }
            internal double BottomRight { get; }
        }

        internal const double Top = 621, Bottom = 635;
        internal static readonly Outline Fuel = new Outline(169, 176, 432, 439);
        internal static readonly Outline Coolant = new Outline(1627, 1621, 1883, 1877);

        // The colour sweep belongs to the *visible fill*, not to the full track.
        // A partly filled acrylic bar still traverses every stop from 0 to 1.
        internal static readonly (double Offset, string Color)[] FillStops =
        {
            (0, "#258BAD"), (.35, "#51BBDD"), (.72, "#A1EAF3"), (1, "#E7FCFF")
        };
        internal static readonly (double Offset, string Color)[] GlassStops =
        {
            (0, "#C4283E50"), (.32, "#E1081723"), (1, "#C2152A39")
        };
        internal static readonly (double Offset, string Color)[] SheenStops =
        {
            (0, "#55E8FCFF"), (.2, "#18D4F7FF"), (.7, "#00FFFFFF"), (1, "#30010C17")
        };

        internal static double? FuelLevel(double? ratio) => ratio.HasValue && double.IsFinite(ratio.Value)
            && ratio >= 0 && ratio <= 1 ? ratio.Value : (double?)null;

        // Display-only C/H scale. AMS2 exposes a water temperature, not C/H boundaries.
        internal static double? CoolantLevel(double? celsius) => celsius.HasValue && double.IsFinite(celsius.Value)
            && celsius >= -40 && celsius <= 200 ? Math.Clamp((celsius.Value - 40) / 80, 0, 1) : (double?)null;

        internal static Geometry Track(Outline outline) => Fill(outline, 1);

        internal static Geometry Fill(Outline outline, double fraction)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            var shape = new StreamGeometry();
            using (var path = shape.Open())
            {
                path.BeginFigure(new Point(outline.TopLeft, Top), true, true);
                path.LineTo(new Point(outline.TopLeft + (outline.TopRight - outline.TopLeft) * fraction, Top), true, false);
                path.LineTo(new Point(outline.BottomLeft + (outline.BottomRight - outline.BottomLeft) * fraction, Bottom), true, false);
                path.LineTo(new Point(outline.BottomLeft, Bottom), true, false);
            }
            shape.Freeze();
            return shape;
        }

        internal static PathGeometry MutableFill(Outline outline)
        {
            var figure = new PathFigure { StartPoint = new Point(outline.TopLeft, Top), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(new Point(outline.TopLeft, Top), true));
            figure.Segments.Add(new LineSegment(new Point(outline.BottomLeft, Bottom), true));
            figure.Segments.Add(new LineSegment(new Point(outline.BottomLeft, Bottom), true));
            return new PathGeometry(new[] { figure });
        }

        internal static void SetLevel(PathGeometry shape, Outline outline, double? fraction)
        {
            double level = fraction ?? 0;
            var figure = shape.Figures[0];
            var top = (LineSegment)figure.Segments[0];
            var bottom = (LineSegment)figure.Segments[1];
            var topPoint = new Point(outline.TopLeft + (outline.TopRight - outline.TopLeft) * level, Top);
            var bottomPoint = new Point(outline.BottomLeft + (outline.BottomRight - outline.BottomLeft) * level, Bottom);
            if (top.Point != topPoint) top.Point = topPoint;
            if (bottom.Point != bottomPoint) bottom.Point = bottomPoint;
        }
    }
}
