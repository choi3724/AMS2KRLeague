using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Rect = System.Windows.Rect;

namespace AMS2LeagueClient.Presentation
{

// Native-owner resource cache. Font outlines and source images are prepared once;
// there is no WPF visual, Drawing, render target, or per-frame visual translation.
internal sealed class CompositionHudCanvas : IDisposable
{
    internal readonly ID2D1DeviceContext1 Dc;
    internal readonly ID2D1Factory1 Factory;
    private readonly Dictionary<uint, ID2D1SolidColorBrush> _colors = new Dictionary<uint, ID2D1SolidColorBrush>();
    private readonly Dictionary<string,uint> _colorNames = new Dictionary<string,uint>(StringComparer.Ordinal);
    private readonly Dictionary<string, ID2D1PathGeometry> _paths = new Dictionary<string, ID2D1PathGeometry>();
    private readonly Dictionary<string, ID2D1Bitmap1> _images = new Dictionary<string, ID2D1Bitmap1>();
    private readonly Dictionary<(string, double, string, int), (ID2D1PathGeometry Shape, Rect Bounds, double Width, double Height)> _texts = new Dictionary<(string, double, string, int), (ID2D1PathGeometry, Rect, double, double)>();
    private readonly Stack<Matrix3x2> _transforms = new Stack<Matrix3x2>();
    private readonly Dictionary<string, ID2D1Brush> _gradients = new Dictionary<string, ID2D1Brush>();
    private readonly Dictionary<(string,double,string,int), (double Width,double Height)> _measurements = new Dictionary<(string,double,string,int), (double Width,double Height)>();
    private readonly Dictionary<(string,double,double,int), (ID2D1PathGeometry Shape,double Height)> _blocks = new Dictionary<(string,double,double,int), (ID2D1PathGeometry Shape,double Height)>();
    internal long TextBuilds { get; private set; }
    private ID2D1DeviceContext? _shadowContext;
    private Vortice.Direct2D1.Effects.Shadow? _shadowEffect;
    private ID2D1CommandList? _shadowCommands;
    private (string,double,string,int,string) _shadowKey;
    private readonly Queue<(string,double,string,int)> _textOrder=new Queue<(string,double,string,int)>();
    private void AddText((string,double,string,int) key,(ID2D1PathGeometry Shape,Rect Bounds,double Width,double Height) item)
    {
        while(_texts.Count>=1024){var oldest=_textOrder.Dequeue();_texts[oldest].Shape.Dispose();_texts.Remove(oldest);}
        _texts.Add(key,item);_textOrder.Enqueue(key);TextBuilds++;
    }
    internal void ForgetScaleResources()
    {
        foreach(var key in new List<string>(_paths.Keys))if(key.StartsWith("redTicks-",StringComparison.Ordinal)){_paths[key].Dispose();_paths.Remove(key);}
        foreach(var key in new List<string>(_gradients.Keys))if(key.StartsWith("red-",StringComparison.Ordinal)){_gradients[key].Dispose();_gradients.Remove(key);}
    }
    internal CompositionHudCanvas(ID2D1DeviceContext1 dc, ID2D1Factory1 factory) { Dc = dc; Factory = factory; }
    internal ID2D1SolidColorBrush Color(string value)
    {if(!_colorNames.TryGetValue(value,out var argb)){argb=Parse(value);_colorNames.Add(value,argb);}return Color(argb);}
    internal static uint Parse(string value)
    {
        var c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(value);
        return (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
    }
    internal ID2D1SolidColorBrush Color(uint value)
    {
        if (!_colors.TryGetValue(value, out var brush))
            _colors.Add(value, brush = Dc.CreateSolidColorBrush(new Color4((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f, (value >> 24) / 255f)));
        return brush;
    }
    internal void Push(Matrix3x2 matrix) { _transforms.Push(Dc.Transform); Dc.Transform = matrix * Dc.Transform; }
    internal void Pop() => Dc.Transform = _transforms.Pop();
    internal void Clip(ID2D1Geometry? geometry, float opacity=1) => Dc.PushLayer(new LayerParameters1 {
        ContentBounds=new Vortice.RawRectF(-1e6f,-1e6f,1e6f,1e6f), GeometricMask=geometry,
        MaskTransform=Matrix3x2.Identity,MaskAntialiasMode=AntialiasMode.PerPrimitive,Opacity=opacity },null!);
    internal void Unclip()=>Dc.PopLayer();
    internal ID2D1Brush Gradient(string key, string first, string last, Vector2 start, Vector2 end)
    {
        if(_gradients.TryGetValue(key,out var brush))return brush;
        using var stops=Dc.CreateGradientStopCollection(new[]{new Vortice.Direct2D1.GradientStop(0,Color(first).Color),new Vortice.Direct2D1.GradientStop(1,Color(last).Color)},Gamma.StandardRgb,ExtendMode.Clamp);
        brush=Dc.CreateLinearGradientBrush(new LinearGradientBrushProperties(start,end),stops);_gradients.Add(key,brush);return brush;
    }
    internal ID2D1Brush Radial(string key, Vector2 center, float radius, float[] offsets, string[] colors)
    {
        if(_gradients.TryGetValue(key,out var brush))return brush;
        var values=new Vortice.Direct2D1.GradientStop[offsets.Length];
        for(int i=0;i<values.Length;i++)values[i]=new Vortice.Direct2D1.GradientStop(offsets[i],Color(colors[i]).Color);
        using var stops=Dc.CreateGradientStopCollection(values,Gamma.StandardRgb,ExtendMode.Clamp);
        brush=Dc.CreateRadialGradientBrush(new RadialGradientBrushProperties {Center=center,RadiusX=radius,RadiusY=radius},stops);
        _gradients.Add(key,brush);return brush;
    }
    internal (double Width,double Height) Measure(string value,double size,string font="Pretendard",bool bold=true,int weight=0)
    {
        int actualWeight=weight==0?(bold?700:400):weight;var key=(value,size,font,actualWeight);
        if(_measurements.TryGetValue(key,out var result))return result;
        var family=font=="Pretendard"||font.StartsWith("AvanteN",StringComparison.Ordinal)
            ?new FontFamily(new Uri("pack://application:,,,/AMS2LeagueClient;component/"),"./Assets/Fonts/#"+font):new FontFamily(font);
        var text=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(family,FontStyles.Normal,FontWeight.FromOpenTypeWeight(actualWeight),FontStretches.Normal),size,Brushes.White,1);
        result=(text.WidthIncludingTrailingWhitespace,text.Height);
        if(_measurements.Count>=2048)_measurements.Clear();
        _measurements.Add(key,result);return result;
    }
    internal void CenterText(string value,double x,double y,double size,string color="AliceBlue",string font="AvanteN UI")
    {
        Text(value,x,y,size,color,font,false,1,centerInk:true);
    }
    internal double BlockHeight(string value,double size,double width,int weight=700)
        => Block(value,size,width,weight).Height;
    private (ID2D1PathGeometry Shape,double Height) Block(string value,double size,double width,int weight)
    {
        var key=(value,size,width,weight);if(_blocks.TryGetValue(key,out var result))return result;
        var family=new FontFamily(new Uri("pack://application:,,,/AMS2LeagueClient;component/"),"./Assets/Fonts/#Pretendard");
        var text=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(family,FontStyles.Normal,FontWeight.FromOpenTypeWeight(Math.Abs(weight)),FontStretches.Normal),size,Brushes.White,1){MaxTextWidth=Math.Max(1,width)};
        if(weight<0){text.MaxLineCount=1;text.Trimming=TextTrimming.CharacterEllipsis;}
        result=(Convert(text.BuildGeometry(new Point())),text.Height);
        if(_blocks.Count>=128){foreach(var entry in _blocks.Values)entry.Shape.Dispose();_blocks.Clear();}
        _blocks.Add(key,result);TextBuilds++;return result;
    }
    internal void BlockText(string value,double x,double y,double size,double width,string color,int weight=700)
    {if(value.Length==0)return;var block=Block(value,size,width,weight);Push(Matrix3x2.CreateTranslation((float)x,(float)y));Dc.FillGeometry(block.Shape,Color(color));Pop();}
    internal void StyledText(string value,double x,double y,double size,bool readout=false,bool tabular=false)
    {
        string font=readout?"AvanteN Readout":"AvanteN Display";var key=(value,size,font,tabular?1001:1000);
        if(!_texts.TryGetValue(key,out var item))
        {
            var family=new FontFamily(new Uri("pack://application:,,,/AMS2LeagueClient;component/"),"./Assets/Fonts/#"+font);
            var group=new GeometryGroup();double advance=0;
            foreach(char ch in value)
            {
                var text=new FormattedText(ch.ToString(),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),size,Brushes.White,1);
                var shape=text.BuildGeometry(new Point());var b=shape.Bounds;
                double bearing=tabular&&char.IsDigit(ch)?(text.WidthIncludingTrailingWhitespace-b.Width)/2-b.X:0;
                var placed=new GeometryGroup{Transform=new TranslateTransform(advance+bearing,0)};placed.Children.Add(shape);group.Children.Add(placed);
                advance+=text.WidthIncludingTrailingWhitespace-(readout?0:size*.025);
            }
            if(!readout)group.Transform=new SkewTransform(-10,0);
            item=(Convert(group),group.Bounds,advance,size);AddText(key,item);
        }
        var bounds=item.Bounds;Push(Matrix3x2.CreateTranslation((float)(x-bounds.X-bounds.Width/2),(float)(y-bounds.Y-bounds.Height/2)));
        if(!readout){Push(Matrix3x2.CreateTranslation(0,2));Dc.FillGeometry(item.Shape,Color("MidnightBlue"));Dc.DrawGeometry(item.Shape,Color("MidnightBlue"),1.8f);Pop();}
        var fill=Gradient("digit-"+size,"White","#BAE6FF",new Vector2(0,(float)bounds.Top),new Vector2(0,(float)bounds.Bottom));
        Dc.FillGeometry(item.Shape,readout?Color("AliceBlue"):fill);
        if(!readout)Dc.DrawGeometry(item.Shape,Color("#718AAF"),1.1f);Pop();
    }
    internal void Rect(double x, double y, double width, double height, string fill, double radius = 0, string? stroke = null, double thickness = 1)
    {
        var r = new Vortice.RawRectF((float)x, (float)y, (float)(x + width), (float)(y + height));
        if (radius == 0) { Dc.FillRectangle(r, Color(fill)); if (stroke != null) Dc.DrawRectangle(r, Color(stroke), (float)thickness); }
        else { var rounded = new RoundedRectangle(r, (float)radius, (float)radius); Dc.FillRoundedRectangle(rounded, Color(fill)); if (stroke != null) Dc.DrawRoundedRectangle(rounded, Color(stroke), (float)thickness); }
    }
    internal void Line(double x, double y, double x2, double y2, string color, double thickness = 1) =>
        Dc.DrawLine(new Vector2((float)x, (float)y), new Vector2((float)x2, (float)y2), Color(color), (float)thickness);
    internal void Ellipse(double x, double y, double rx, double ry, string fill, string? stroke = null, double thickness = 1)
    {
        var e = new Ellipse(new Vector2((float)x, (float)y), (float)rx, (float)ry);
        Dc.FillEllipse(e, Color(fill)); if (stroke != null) Dc.DrawEllipse(e, Color(stroke), (float)thickness);
    }
    internal ID2D1PathGeometry Geometry(string key, Func<System.Windows.Media.Geometry> make)
    {
        if (_paths.TryGetValue(key, out var cached)) return cached;
        var shape = Convert(make()); _paths.Add(key, shape); return shape;
    }
    internal ID2D1PathGeometry Sector(Vector2 center,double inner,double outer,double start,double end)
    {
        Vector2 P(double r,double a)=>center+new Vector2((float)(r*Math.Cos(a*Math.PI/180)),(float)(r*Math.Sin(a*Math.PI/180)));
        end=Math.Max(start+.001,end);var shape=Factory.CreatePathGeometry();
        try
        {
            using var sink=shape.Open();sink.BeginFigure(P(outer,start),FigureBegin.Filled);
            sink.AddArc(new Vortice.Direct2D1.ArcSegment {Point=P(outer,end),Size=new Vortice.Mathematics.Size((float)outer,(float)outer),SweepDirection=Vortice.Direct2D1.SweepDirection.Clockwise,ArcSize=end-start>180?ArcSize.Large:ArcSize.Small});
            sink.AddLine(P(inner,end));
            sink.AddArc(new Vortice.Direct2D1.ArcSegment {Point=P(inner,start),Size=new Vortice.Mathematics.Size((float)inner,(float)inner),SweepDirection=Vortice.Direct2D1.SweepDirection.CounterClockwise,ArcSize=end-start>180?ArcSize.Large:ArcSize.Small});
            sink.EndFigure(FigureEnd.Closed);sink.Close();return shape;
        }
        catch{shape.Dispose();throw;}
    }
    internal ID2D1PathGeometry Convert(System.Windows.Media.Geometry source)
    {
        // CPU font/vector preparation only, never applied to a WPF Drawing or visual.
        var flat = source.GetFlattenedPathGeometry(.08, ToleranceType.Absolute);
        var shape = Factory.CreatePathGeometry();
        try
        {
            using var sink = shape.Open();
            sink.SetFillMode(flat.FillRule == FillRule.EvenOdd ? FillMode.Alternate : FillMode.Winding);
            var matrix = flat.Transform?.Value ?? Matrix.Identity;
            Vector2 Point(System.Windows.Point p) { p = matrix.Transform(p); return new Vector2((float)p.X, (float)p.Y); }
            foreach (var f in flat.Figures)
            {
                sink.BeginFigure(Point(f.StartPoint), f.IsFilled ? FigureBegin.Filled : FigureBegin.Hollow);
                foreach (var s in f.Segments)
                {
                    if (s is PolyLineSegment poly) foreach (var p in poly.Points) sink.AddLine(Point(p));
                    else if (s is LineSegment line) sink.AddLine(Point(line.Point));
                    else throw new InvalidOperationException("Unflattened HUD resource.");
                }
                sink.EndFigure(f.IsClosed ? FigureEnd.Closed : FigureEnd.Open);
            }
            sink.Close(); return shape;
        }
        catch { shape.Dispose(); throw; }
    }
    internal void Text(string value, double x, double y, double size, string color = "White", string font = "Pretendard", bool bold = true, int align = 0, double maxWidth = double.PositiveInfinity, bool centerInk=false, int weight=0)
    {
        if (value.Length == 0) return;
        int actualWeight=weight==0?(bold?700:400):weight; var key = (value, size, font, actualWeight);
        if (!_texts.TryGetValue(key, out var item))
        {
            var family = font == "Pretendard" || font.StartsWith("AvanteN", StringComparison.Ordinal)
                ? new FontFamily(new Uri("pack://application:,,,/AMS2LeagueClient;component/"), "./Assets/Fonts/#" + font) : new FontFamily(font);
            var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeight.FromOpenTypeWeight(actualWeight), FontStretches.Normal), size, Brushes.White, 1);
            var geometry = text.BuildGeometry(new Point());
            item = (Convert(geometry), geometry.Bounds, text.WidthIncludingTrailingWhitespace, text.Height);
            AddText(key,item);
        }
        double scale = Math.Min(1, maxWidth / Math.Max(.01, item.Width));
        double left = align == 1 ? x - item.Width * scale / 2 : align == 2 ? x - item.Width * scale : x;
        if(centerInk){left=x-(item.Bounds.X+item.Bounds.Width/2)*scale;y-=(item.Bounds.Y+item.Bounds.Height/2)*scale;}
        Push(Matrix3x2.CreateScale((float)scale) * Matrix3x2.CreateTranslation((float)left, (float)y));
        Dc.FillGeometry(item.Shape, Color(color)); Pop();
    }
    internal void ShadowText(string value,double x,double y,double size,string shadowColor,string font,int weight=600)
    {
        // Cache a native command list for the current string; effect input never crosses through a CPU bitmap.
        var key=(value,size,font,weight,shadowColor);
        if(_shadowEffect==null || key!=_shadowKey)
        {
            // Prepare the same glyph resource without visible output on the current target.
            _cullText(value,size,font,weight);
            if(_shadowContext==null){using var device=Dc.Device;_shadowContext=device.CreateDeviceContext(DeviceContextOptions.None);}
            if(_shadowEffect==null)_shadowEffect=new Vortice.Direct2D1.Effects.Shadow(Dc);
            var commands=_shadowContext.CreateCommandList();_shadowContext.Target=commands;
            _shadowContext.BeginDraw();_shadowContext.FillGeometry(_texts[(value,size,font,weight)].Shape,Color("White"));_shadowContext.EndDraw().CheckError();
            _shadowContext.Target=null;commands.Close().CheckError();
            _shadowEffect.SetInput(0,commands,true);_shadowEffect.BlurStandardDeviation=2;_shadowEffect.Color=Color(shadowColor).Color;
            _shadowCommands?.Dispose();_shadowCommands=commands;_shadowKey=key;
        }
        using var output=_shadowEffect.Output;Dc.DrawImage(output,new Vector2((float)(x+Math.Sqrt(2)),(float)(y+Math.Sqrt(2))));
    }
    private void _cullText(string value,double size,string font,int weight)
    {
        if(_texts.ContainsKey((value,size,font,weight)))return;
        // A zero-opacity native layer prepares only the cached outline; no WPF rendering occurs.
        Clip(null,0);Text(value,0,0,size,font:font,weight:weight);Unclip();
    }
    internal ID2D1Bitmap1 Image(string name)
    {
        if (_images.TryGetValue(name, out var result)) return result;
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/AMS2LeagueClient;component/Assets/Hud/" + name)).Stream;
        return Image(name,stream);
    }
    internal ID2D1Bitmap1 Image(string name,System.IO.Stream stream)
    {
        if(_images.TryGetValue(name,out var result))return result;
        BitmapSource source;
        if(name=="tower-material.png"||name=="dashboard-housing.png")
        {var decoded=new BitmapImage();decoded.BeginInit();decoded.StreamSource=stream;decoded.CacheOption=BitmapCacheOption.OnLoad;decoded.DecodePixelWidth=name=="tower-material.png"?512:1360;decoded.EndInit();source=decoded;}
        else source = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var pixels = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        byte[] bytes = new byte[pixels.PixelWidth * pixels.PixelHeight * 4]; pixels.CopyPixels(bytes, pixels.PixelWidth * 4, 0);
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            result = Dc.CreateBitmap(new SizeI(pixels.PixelWidth, pixels.PixelHeight), pinned.AddrOfPinnedObject(), (uint)(pixels.PixelWidth * 4),
                new BitmapProperties1(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)));
        }
        finally { pinned.Free(); }
        _images.Add(name, result); return result;
    }
    internal void DrawImage(string name, double x, double y, double width, double height, float opacity = 1) =>
        Dc.DrawBitmap(Image(name), new Vortice.RawRectF((float)x, (float)y, (float)(x + width), (float)(y + height)), opacity, InterpolationMode.HighQualityCubic, null, null);
    public void Dispose()
    {
        _shadowEffect?.Dispose();_shadowCommands?.Dispose();_shadowContext?.Dispose();
        foreach (var resource in _texts.Values) resource.Shape.Dispose();
        foreach (var resource in _paths.Values) resource.Dispose();
        foreach (var resource in _colors.Values) resource.Dispose();
        foreach (var resource in _images.Values) resource.Dispose();
        foreach (var resource in _gradients.Values) resource.Dispose();
        foreach (var resource in _blocks.Values) resource.Shape.Dispose();
        _texts.Clear(); _paths.Clear(); _colors.Clear(); _images.Clear();
    }
}
}
