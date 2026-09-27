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

        // The source PNG has dark marks baked into its old, thin colour strip.
        // Replace that entire inner strip with a taller inset behind the glass.
        internal const double Top = 618, Bottom = 639;
        internal static readonly Outline Fuel = new Outline(164, 175, 434, 443);
        internal static readonly Outline Coolant = new Outline(1627, 1616, 1888, 1877);
        internal static Geometry Matte()
        {
            var both = new GeometryGroup();
            both.Children.Add(Track(Fuel));
            both.Children.Add(Track(Coolant));
            both.Freeze();
            return both;
        }

        // The colour sweep belongs to the *visible fill*, not to the full track.
        // A partly filled acrylic bar still traverses every stop from 0 to 1.
        internal static readonly (double Offset, string Color)[] FillStops =
        {
            (0, "#258BAD"), (.35, "#51BBDD"), (.72, "#A1EAF3"), (1, "#E7FCFF")
        };
        internal static readonly (double Offset, string Color)[] GlassStops =
        {
            (0, "#D62C4A5A"), (.32, "#B9102533"), (1, "#D51D3948")
        };
        internal static readonly (double Offset, string Color)[] SheenStops =
        {
            (0, "#87F5FDFF"), (.18, "#39D9F8FF"), (.62, "#00FFFFFF"), (1, "#35000B13")
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
