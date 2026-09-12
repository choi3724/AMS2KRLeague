using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using WGeometry = System.Windows.Media.Geometry;
using WBrush = System.Windows.Media.Brush;
using WRect = System.Windows.Rect;
using WColor = System.Windows.Media.Color;
using ArcSegment = System.Windows.Media.ArcSegment;
using BezierSegment = System.Windows.Media.BezierSegment;
using QuadraticBezierSegment = System.Windows.Media.QuadraticBezierSegment;
using BezierSegmentD2D = Vortice.Direct2D1.BezierSegment;
using QuadraticBezierSegmentD2D = Vortice.Direct2D1.QuadraticBezierSegment;
using FillMode = Vortice.Direct2D1.FillMode;

// Diagnostic adapter: the product's retained drawing, images and glyph outlines are
// the scene authority. No browser, game hook, per-frame WPF bitmap/readback or PNG sequence.
sealed class GpuSurface : IDisposable
{
    sealed class DeviceSet : IDisposable {
        public readonly ID3D11Device D3d=D3D11.D3D11CreateDevice(Vortice.Direct3D.DriverType.Hardware,DeviceCreationFlags.BgraSupport);
        public readonly IDXGIDevice Dxgi;public readonly IDXGIFactory2 DxFactory=DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        public readonly ID2D1Factory1 Factory=D2D1.D2D1CreateFactory<ID2D1Factory1>();public readonly ID2D1Device Device;public readonly ID2D1DeviceContext Dc;
        public DeviceSet(){Dxgi=D3d.QueryInterface<IDXGIDevice>();Device=Factory.CreateDevice(Dxgi);Dc=Device.CreateDeviceContext(DeviceContextOptions.None);}
        public void Dispose(){Dc.Dispose();Device.Dispose();Factory.Dispose();DxFactory.Dispose();Dxgi.Dispose();D3d.Dispose();}
    }
    static DeviceSet? shared;static int users;
    sealed class CachedGeometry {
        public ID2D1PathGeometry? Native;public bool Dirty=true;
        public void Changed(object? sender,EventArgs e)=>Dirty=true;
    }
    sealed class Host : System.Windows.Forms.Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override System.Windows.Forms.CreateParams CreateParams {
            get { var p=base.CreateParams; p.ExStyle |= 0x00200000|0x08000000|0x00000080|0x20; return p; }
        }
        protected override void WndProc(ref System.Windows.Forms.Message m) {
            if(m.Msg==0x84) {m.Result=new IntPtr(-1);return;} base.WndProc(ref m);
        }
    }
    readonly Host host;
    readonly ID3D11Device d3d;
    readonly IDXGIDevice dxgi;
    readonly IDXGIFactory2 dxFactory;
    readonly ID2D1Factory1 factory;
    readonly ID2D1Device device;
    readonly ID2D1DeviceContext dc;
    readonly IDCompositionDevice composition;
    readonly IDCompositionTarget target;
    readonly IDCompositionVisual visual;
    readonly IDXGISwapChain1 chain;
    readonly ID2D1Bitmap1 backbuffer;
    readonly Dictionary<BitmapSource,ID2D1Bitmap1> images=new();
    readonly Dictionary<WGeometry,CachedGeometry> geometries=new();
    readonly HashSet<WGeometry> usedGeometry=new();
    readonly Dictionary<Visual,(object? Content,Drawing? Drawing)> drawings=new();
    static readonly Dictionary<Type,FieldInfo?> contentFields=new();
    readonly List<IDisposable> transient=new();
    readonly Dictionary<uint,ID2D1SolidColorBrush> colors=new();
    public IntPtr Handle=>host.Handle;
    public int ImageUploads {get;private set;}
    public int GeometryBuilds {get;private set;}
    public int Submitted {get;private set;}
    public readonly List<object> Statistics=new();
    uint lastPresent;
    public GpuSurface(string name,int x,int y,int width,int height)
    {
        host=new Host {Text="Renderer D "+name,FormBorderStyle=System.Windows.Forms.FormBorderStyle.None,
            StartPosition=System.Windows.Forms.FormStartPosition.Manual,Bounds=new System.Drawing.Rectangle(x,y,width,height),TopMost=true,ShowInTaskbar=false};
        shared??=new DeviceSet();users++;d3d=shared.D3d;dxgi=shared.Dxgi;dxFactory=shared.DxFactory;factory=shared.Factory;device=shared.Device;dc=shared.Dc;
        chain=dxFactory.CreateSwapChainForComposition(d3d,new SwapChainDescription1 {Width=(uint)width,Height=(uint)height,
            Format=Format.B8G8R8A8_UNorm,BufferCount=2,BufferUsage=Usage.RenderTargetOutput,
            SampleDescription=new SampleDescription(1,0),SwapEffect=SwapEffect.FlipSequential,
            Scaling=Scaling.Stretch,AlphaMode=Vortice.DXGI.AlphaMode.Premultiplied});
        using(var surface=chain.GetBuffer<IDXGISurface>(0)) backbuffer=dc.CreateBitmapFromDxgiSurface(surface,new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,Vortice.DCommon.AlphaMode.Premultiplied),96,96,BitmapOptions.Target|BitmapOptions.CannotDraw));
        dc.Target=backbuffer;
        composition=DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi);
        composition.CreateTargetForHwnd(host.Handle,true,out target).CheckError();
        visual=composition.CreateVisual(); visual.SetContent(chain).CheckError(); target.SetRoot(visual).CheckError(); composition.Commit().CheckError();
        host.Show();
    }
    static Vector2 P(System.Windows.Point p)=>new((float)p.X,(float)p.Y);
    static Matrix3x2 M(Matrix m)=>new((float)m.M11,(float)m.M12,(float)m.M21,(float)m.M22,(float)m.OffsetX,(float)m.OffsetY);
    static Color4 C(WColor c)=>new(c.R/255f,c.G/255f,c.B/255f,c.A/255f);
    static Vortice.RawRectF R(WRect r)=>new((float)r.Left,(float)r.Top,(float)r.Right,(float)r.Bottom);
    ID2D1PathGeometry Geometry(WGeometry source)
    {
        usedGeometry.Add(source);
        if(!geometries.TryGetValue(source,out var cached)) {
            geometries.Add(source,cached=new CachedGeometry());if(!source.IsFrozen)source.Changed+=cached.Changed;
        }
        if(!cached.Dirty)return cached.Native!;
        var result=factory.CreatePathGeometry(); using(var sink=result.Open()) {
            // Flatten only geometries with arcs/combined operands; ordinary graph Beziers retain curves.
            var path=PathGeometry.CreateFromGeometry(source);
            if(path.Figures.Any(f=>f.Segments.Any(s=>s is ArcSegment)))path=source.GetFlattenedPathGeometry(.15,ToleranceType.Absolute);
            sink.SetFillMode(path.FillRule==FillRule.Nonzero?FillMode.Winding:FillMode.Alternate);
            Matrix transform=(path.Transform?.Value??Matrix.Identity);
            Vector2 Pt(System.Windows.Point p)=>P(transform.Transform(p));
            foreach(var f in path.Figures) {
                sink.BeginFigure(Pt(f.StartPoint),f.IsFilled?FigureBegin.Filled:FigureBegin.Hollow);
                foreach(var s in f.Segments) switch(s) {
                    case LineSegment l:sink.AddLine(Pt(l.Point));break;
                    case PolyLineSegment l:foreach(var p in l.Points)sink.AddLine(Pt(p));break;
                    case BezierSegment b:sink.AddBezier(new BezierSegmentD2D(Pt(b.Point1),Pt(b.Point2),Pt(b.Point3)));break;
                    case PolyBezierSegment b:for(int i=0;i<b.Points.Count;i+=3)sink.AddBezier(new BezierSegmentD2D(Pt(b.Points[i]),Pt(b.Points[i+1]),Pt(b.Points[i+2])));break;
                    case QuadraticBezierSegment q:sink.AddQuadraticBezier(new QuadraticBezierSegmentD2D {Point1=Pt(q.Point1),Point2=Pt(q.Point2)});break;
                    case PolyQuadraticBezierSegment q:for(int i=0;i<q.Points.Count;i+=2)sink.AddQuadraticBezier(new QuadraticBezierSegmentD2D {Point1=Pt(q.Points[i]),Point2=Pt(q.Points[i+1])});break;
                    default:throw new NotSupportedException(s.GetType().Name);
                }
                sink.EndFigure(f.IsClosed?FigureEnd.Closed:FigureEnd.Open);
            }
            sink.Close();
        }
        GeometryBuilds++;
        cached.Native?.Dispose();cached.Native=result;cached.Dirty=false;
        return result;
    }
    ID2D1Brush Brush(WBrush source,WRect bounds)
    {
        if(source is SolidColorBrush solid) {
            uint key=(uint)(solid.Color.A<<24|solid.Color.R<<16|solid.Color.G<<8|solid.Color.B);
            if(!colors.TryGetValue(key,out var brush))colors.Add(key,brush=dc.CreateSolidColorBrush(C(solid.Color)));
            brush.Opacity=(float)source.Opacity;return brush;
        }
        if(source is GradientBrush gradient) {
            using var stops=dc.CreateGradientStopCollection(gradient.GradientStops.Select(s=>new Vortice.Direct2D1.GradientStop((float)s.Offset,C(s.Color))).ToArray(),
                Gamma.StandardRgb,gradient.SpreadMethod==GradientSpreadMethod.Pad?ExtendMode.Clamp:gradient.SpreadMethod==GradientSpreadMethod.Reflect?ExtendMode.Mirror:ExtendMode.Wrap);
            Vector2 At(System.Windows.Point p)=>P(gradient.MappingMode==BrushMappingMode.Absolute?p:new System.Windows.Point(bounds.X+p.X*bounds.Width,bounds.Y+p.Y*bounds.Height));
            ID2D1Brush b;
            if(gradient is LinearGradientBrush l)b=dc.CreateLinearGradientBrush(new LinearGradientBrushProperties(At(l.StartPoint),At(l.EndPoint)),stops);
            else if(gradient is RadialGradientBrush r)b=dc.CreateRadialGradientBrush(new RadialGradientBrushProperties(At(r.Center),At(r.GradientOrigin)-At(r.Center),
                (float)(r.RadiusX*(r.MappingMode==BrushMappingMode.Absolute?1:bounds.Width)),(float)(r.RadiusY*(r.MappingMode==BrushMappingMode.Absolute?1:bounds.Height))),stops);
            else throw new NotSupportedException(gradient.GetType().Name);
            b.Opacity=(float)source.Opacity; b.Transform=M((source.Transform?.Value??Matrix.Identity));transient.Add(b);return b;
        }
        throw new NotSupportedException("Brush "+source.GetType().Name);
    }
    unsafe ID2D1Bitmap1 Image(BitmapSource source)
    {
        if(images.TryGetValue(source,out var cached))return cached;
        var bitmap=source.Format==PixelFormats.Pbgra32?source:new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
        if(Environment.GetEnvironmentVariable("MATRIX_ASSET_DUMP") is string dump) {
            Directory.CreateDirectory(dump);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(Path.Combine(dump,host.Text+"-"+ImageUploads+".png"));encoder.Save(output);
        }
        var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0);
        fixed(byte* p=bytes) cached=dc.CreateBitmap(new SizeI(bitmap.PixelWidth,bitmap.PixelHeight),(IntPtr)p,(uint)bitmap.PixelWidth*4,
            new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,Vortice.DCommon.AlphaMode.Premultiplied),96,96));
        images.Add(source,cached);ImageUploads++;return cached;
    }
    void Push(WGeometry? clip,double opacity) {
        var layer=new LayerParameters1 { ContentBounds=new Vortice.RawRectF(-1e6f,-1e6f,1e6f,1e6f),
            GeometricMask=clip==null?null:Geometry(clip),MaskTransform=Matrix3x2.Identity,
            MaskAntialiasMode=AntialiasMode.PerPrimitive,Opacity=(float)opacity };
        dc.PushLayer(layer,null!);
    }
    void Draw(Drawing? drawing) {
        if(drawing==null)return;
        switch(drawing) {
            case DrawingGroup g:
                if(g.OpacityMask!=null)throw new NotSupportedException("OpacityMask");
                var old=dc.Transform;dc.Transform=M((g.Transform?.Value??Matrix.Identity))*old;
                bool layer=g.ClipGeometry!=null||g.Opacity!=1;
                if(layer)Push(g.ClipGeometry,g.Opacity);
                foreach(var child in g.Children)Draw(child);
                if(layer)dc.PopLayer();dc.Transform=old;break;
            case GeometryDrawing g:
                var geometry=Geometry(g.Geometry);
                if(g.Brush!=null)dc.FillGeometry(geometry,Brush(g.Brush,g.Geometry.Bounds));
                if(g.Pen!=null) {
                    using var stroke=factory.CreateStrokeStyle(new StrokeStyleProperties {StartCap=(CapStyle)g.Pen.StartLineCap,EndCap=(CapStyle)g.Pen.EndLineCap,
                        LineJoin=g.Pen.LineJoin==PenLineJoin.Round?LineJoin.Round:g.Pen.LineJoin==PenLineJoin.Bevel?LineJoin.Bevel:LineJoin.Miter,MiterLimit=(float)g.Pen.MiterLimit});
                    dc.DrawGeometry(geometry,Brush(g.Pen.Brush,g.Geometry.Bounds),(float)g.Pen.Thickness,stroke);
                }break;
            case ImageDrawing i when i.ImageSource is BitmapSource bitmap:
                dc.DrawBitmap(Image(bitmap),R(i.Rect),1,InterpolationMode.HighQualityCubic,null,null);break;
            case GlyphRunDrawing g:
                var outline=g.GlyphRun.BuildGeometry();dc.FillGeometry(Geometry(outline),Brush(g.ForegroundBrush,outline.Bounds));break;
            default:throw new NotSupportedException("Drawing "+drawing.GetType().Name);
        }
    }
    void DrawVisual(Visual v) {
        var old=dc.Transform; var offset=VisualTreeHelper.GetOffset(v);var transform=VisualTreeHelper.GetTransform(v)?.Value??Matrix.Identity;
        transform.Translate(offset.X,offset.Y);dc.Transform=M(transform)*old;
        var clip=VisualTreeHelper.GetClip(v);double opacity=VisualTreeHelper.GetOpacity(v);bool layer=clip!=null||opacity!=1;
        if(VisualTreeHelper.GetEffect(v)!=null)throw new NotSupportedException("Visual effect "+v.GetType().Name);
        if(layer)Push(clip,opacity);
        if(!contentFields.TryGetValue(v.GetType(),out var field)) {
            for(Type? t=v.GetType();t!=null;t=t.BaseType) {
                field=t.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).FirstOrDefault(f=>f.FieldType.Name=="RenderData");
                if(field!=null)break;
            }
            contentFields.Add(v.GetType(),field);
        }
        object? content=field?.GetValue(v);
        if(field==null)Draw(VisualTreeHelper.GetDrawing(v));
        else {
            if(!drawings.TryGetValue(v,out var entry)||!ReferenceEquals(entry.Content,content))
                drawings[v]=entry=(content,VisualTreeHelper.GetDrawing(v));
            Draw(entry.Drawing);
        }
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(v);i++)if(VisualTreeHelper.GetChild(v,i) is Visual child)DrawVisual(child);
        if(layer)dc.PopLayer();dc.Transform=old;
    }
    public void Render(Visual scene,string? capture=null) {
        usedGeometry.Clear();
        dc.Target=backbuffer;dc.BeginDraw();dc.Transform=Matrix3x2.Identity;dc.Clear(new Color4(0,0,0,0));
        DrawVisual(scene);dc.EndDraw().CheckError();if(capture!=null)Save(capture);chain.Present(0,PresentFlags.None).CheckError();Submitted++;
        foreach(var resource in transient)resource.Dispose();transient.Clear();
        if(Submitted%120==0)foreach(var key in geometries.Keys.Where(k=>!usedGeometry.Contains(k)).ToArray()) {
            var cached=geometries[key];if(!key.IsFrozen)key.Changed-=cached.Changed;cached.Native?.Dispose();geometries.Remove(key);
        }
        var result=chain.GetFrameStatistics(out var stats);
        if(result.Success && stats.PresentCount!=lastPresent) {lastPresent=stats.PresentCount;Statistics.Add(new {utc=DateTimeOffset.UtcNow,stats.PresentCount,stats.PresentRefreshCount,stats.SyncRefreshCount,stats.SyncQPCTime});}
    }
    public void Save(string path) {
        using var cpu=dc.CreateBitmap(backbuffer.PixelSize,IntPtr.Zero,0,new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,Vortice.DCommon.AlphaMode.Premultiplied),96,96,BitmapOptions.CpuRead|BitmapOptions.CannotDraw));
        cpu.CopyFromBitmap(backbuffer).CheckError();var mapped=cpu.Map(MapOptions.Read);
        try {
            var bytes=new byte[mapped.Pitch*backbuffer.PixelSize.Height];Marshal.Copy(mapped.Bits,bytes,0,bytes.Length);
            var bitmap=BitmapSource.Create(backbuffer.PixelSize.Width,backbuffer.PixelSize.Height,96,96,PixelFormats.Pbgra32,null,bytes,(int)mapped.Pitch);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
        }finally {cpu.Unmap();}
    }
    public void Dispose() {
        host.Close();foreach(var x in transient)x.Dispose();foreach(var x in images.Values)x.Dispose();foreach(var x in geometries){if(!x.Key.IsFrozen)x.Key.Changed-=x.Value.Changed;x.Value.Native?.Dispose();}foreach(var x in colors.Values)x.Dispose();
        dc.Target=null;visual.Dispose();target.Dispose();composition.Dispose();backbuffer.Dispose();chain.Dispose();host.Dispose();if(--users==0){shared!.Dispose();shared=null;}
    }
}
