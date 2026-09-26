using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace AMS2LeagueClient.Presentation
{
    // Vector mark from Hyundai N's official public site, not extracted from a
    // dashboard photo. Prepared once for both WPF and the retained D2D path.
    internal static class AvanteNLogo
    {
        private static readonly Lazy<Geometry[]> Shapes = new Lazy<Geometry[]>(Load);
        internal static Geometry[] Geometries => Shapes.Value;
        internal static readonly string[] Colours = { "#EF4123", "#FFFFFF", "#231F20" };

        private static Geometry[] Load()
        {
            using var source = Application.GetResourceStream(new Uri(
                "pack://application:,,,/AMS2LeagueClient;component/Assets/Hud/hyundai-n-logo.svg")).Stream;
            var svg = XDocument.Load(source);
            var shapes = svg.Root!.Elements(XName.Get("path", "http://www.w3.org/2000/svg"))
                .Select(element => Geometry.Parse((string)element.Attribute("d")!)).ToArray();
            if (shapes.Length != Colours.Length) throw new InvalidOperationException("Unexpected Hyundai N logo shape count.");
            foreach (var shape in shapes) shape.Freeze();
            return shapes;
        }

        internal static void Draw(DrawingContext dc, double x, double y, double width, double height)
        {
            dc.PushTransform(new MatrixTransform(new Matrix(width / 76, 0, 0, height / 32, x, y)));
            for (int i = 0; i < Colours.Length; i++)
                dc.DrawGeometry(new SolidColorBrush((Color)ColorConverter.ConvertFromString(Colours[i])), null, Geometries[i]);
            dc.Pop();
        }
    }
}
