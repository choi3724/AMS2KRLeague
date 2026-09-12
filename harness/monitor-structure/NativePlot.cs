using System;
using System.Collections.Generic;
using System.Numerics;
using AMS2LeagueClient.Core.Presentation;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;

// Native diagnostic scene: observed samples -> native paths at DATA updates only.
// Cached realizations are drawn at presentation ticks; no WPF Drawing translation.
sealed partial class GpuSurface
{
    sealed class Curve : IDisposable
    {
        internal readonly List<(Vector2 Start,Vector2 Control,Vector2 End,bool Begin,bool HasPrevious)> Segments=new();
        internal readonly Queue<(ID2D1GeometryRealization Geometry,double End)> Sealed=new();
        internal ID2D1GeometryRealization? Open;
        internal Vector2? Previous;internal Vector2 End,Point;
        internal double Filter,Time;internal bool Ready,Drawing;
        public void Dispose(){Open?.Dispose();foreach(var x in Sealed)x.Geometry.Dispose();}
    }
    readonly Curve[] plotCurves={new(),new(),new(),new(),new()};
    ID2D1DeviceContext1? plotDc;ID2D1StrokeStyle? plotStroke;
    ID2D1GeometryRealization? plotGrid;
    readonly List<ID2D1SolidColorBrush> plotColors=new();
    double plotWidth,plotHeight;
    internal void InitializePlot(double width,double height)
    {
        plotWidth=width;plotHeight=height-8;plotDc=dc.QueryInterface<ID2D1DeviceContext1>();
        plotStroke=factory.CreateStrokeStyle(new StrokeStyleProperties{StartCap=CapStyle.Round,EndCap=CapStyle.Round,LineJoin=LineJoin.Round});
        var settings=new DrivingHudSettings();
        foreach(var color in new[]{settings.BrakeColor,settings.ThrottleColor,settings.ClutchColor,settings.HandBrakeColor,"#FFD700"})
            plotColors.Add(dc.CreateSolidColorBrush(C((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color))));
        plotColors.Add(dc.CreateSolidColorBrush(new Color4(1,1,1,30/255f)));
        using var grid=factory.CreatePathGeometry();using(var sink=grid.Open()){
            for(int row=0;row<3;row++){sink.BeginFigure(new Vector2(0,(float)(row*plotHeight/2+4)),FigureBegin.Hollow);sink.AddLine(new Vector2((float)width,(float)(row*plotHeight/2+4)));sink.EndFigure(FigureEnd.Open);}
            for(int col=1;col<8;col++){sink.BeginFigure(new Vector2((float)(width*col/8),4),FigureBegin.Hollow);sink.AddLine(new Vector2((float)(width*col/8),(float)(4+plotHeight)));sink.EndFigure(FigureEnd.Open);}sink.Close();
        }
        plotGrid=plotDc.CreateStrokedGeometryRealization(grid,.25f,1,plotStroke);
    }
    ID2D1GeometryRealization BuildPlot(Curve curve,bool tail)
    {
        using var path=factory.CreatePathGeometry();using(var sink=path.Open()){
            bool figure=false;
            foreach(var s in curve.Segments){
                if(s.Begin){if(figure)sink.EndFigure(FigureEnd.Open);sink.BeginFigure(s.Start,FigureBegin.Hollow);figure=true;}
                if(s.HasPrevious)sink.AddQuadraticBezier(new Vortice.Direct2D1.QuadraticBezierSegment{Point1=s.Control,Point2=s.End});
            }
            if(figure){if(tail)sink.AddLine(curve.Point);sink.EndFigure(FigureEnd.Open);}sink.Close();
        }
        GeometryBuilds++;return plotDc!.CreateStrokedGeometryRealization(path,.25f,3,plotStroke!);
    }
    internal void AppendPlot(DrivingTelemetrySample sample,double seconds)
    {
        for(int i=0;i<5;i++){
            var c=plotCurves[i];double? value=sample.Pedals[i==4?0:i];bool gap=c.Ready&&seconds-c.Time>.150;
            if(!value.HasValue||gap){c.Previous=null;c.Ready=c.Drawing=false;}
            double dt=seconds-c.Time;c.Time=seconds;
            while(c.Sealed.Count>0&&(seconds-c.Sealed.Peek().End>10||c.Sealed.Count>64))c.Sealed.Dequeue().Geometry.Dispose();
            if(!value.HasValue)continue;
            c.Filter=c.Ready?c.Filter+(value.Value-c.Filter)*(1-Math.Exp(-dt/.1)):value.Value;c.Ready=true;
            var point=new Vector2((float)(seconds*plotWidth/10),(float)(4+(1-c.Filter)*plotHeight));
            bool include=i!=4||sample.AbsActive;var end=c.Previous.HasValue?(c.Previous.Value+point)/2:point;
            if(include){
                bool boundary=c.Segments.Count>=64;
                if(boundary){c.Sealed.Enqueue((BuildPlot(c,false),seconds));c.Segments.Clear();}
                c.Segments.Add((c.Previous.HasValue?c.End:point,c.Previous??point,end,boundary||!c.Drawing||c.Segments.Count==0,c.Previous.HasValue));
                c.Point=point;c.Open?.Dispose();c.Open=BuildPlot(c,true);
            }
            c.Drawing=include;c.Previous=point;c.End=end;
        }
    }
    internal void RenderPlot(double time,string? screenshot=null)
    {
        dc.Target=backbuffer;dc.BeginDraw();dc.Transform=Matrix3x2.Identity;dc.Clear(new Color4(0,0,0,0));
        plotDc!.DrawGeometryRealization(plotGrid!,plotColors[5]);
        dc.PushAxisAlignedClip(new Vortice.RawRectF(0,0,(float)plotWidth,(float)(plotHeight+8)),AntialiasMode.PerPrimitive);
        dc.Transform=Matrix3x2.CreateTranslation((float)(plotWidth*(1-time/10)),0);
        foreach(int i in new[]{0,4,1,2,3}){var c=plotCurves[i];foreach(var x in c.Sealed)plotDc.DrawGeometryRealization(x.Geometry,plotColors[i]);if(c.Open!=null)plotDc.DrawGeometryRealization(c.Open,plotColors[i]);}
        dc.PopAxisAlignedClip();dc.EndDraw().CheckError();if(screenshot!=null)Save(screenshot);
        chain.Present(0,PresentFlags.None).CheckError();Submitted++;
        var result=chain.GetFrameStatistics(out var stats);if(result.Success&&stats.PresentCount!=lastPresent){lastPresent=stats.PresentCount;Statistics.Add(new{utc=DateTimeOffset.UtcNow,stats.PresentCount,stats.PresentRefreshCount,stats.SyncRefreshCount,stats.SyncQPCTime});}
    }
    void DisposePlot(){foreach(var c in plotCurves)c.Dispose();foreach(var c in plotColors)c.Dispose();plotGrid?.Dispose();plotStroke?.Dispose();plotDc?.Dispose();}
}
