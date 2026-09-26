using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using WGeometry=System.Windows.Media.Geometry;

namespace AMS2LeagueClient.Presentation
{
    internal sealed class CompositionAvanteHud : IDisposable
    {
        private const double Cx=1024,Cy=397;
        private readonly CompositionHudCanvas _c;
        private readonly bool _expanded;
        private AvanteRpmScale _scale=AvanteRpmScale.Resolve(null);
        private CompositionHudFrame? _frame;
        private DrivingTelemetrySample? _sample;
        private string _vehicle="",_profile="";
        private double? _engineMaximum;
        private TelemetrySnapshot? _sessionSource,_session;
        private int? _generation,_participant;
        private object? _dialKey, _gearKey, _speedKey, _statusKey, _oilKey, _waterKey, _fuelKey, _torqueKey, _odoKey;
        private bool _faceDirty=true, _dialDirty=true, _gearDirty=true, _speedDirty=true, _statusDirty=true;
        private bool _oilDirty=true, _waterDirty=true, _fuelDirty=true, _torqueDirty=true, _odoDirty=true;
        private double _renderedRpm = double.NaN;
        private int _renderedBand = -1, _renderedPairs = -1;
        private bool _followerDrawn;
        private long _ignitionAt;
        private bool _ignitionSettled;
        private readonly Vortice.RawRect?[] _fixedSlots=new Vortice.RawRect?[8];
        private Vortice.RawRect? _coolantGaugeSlot;
        private readonly Vortice.RawRect?[] _numberSlots=new Vortice.RawRect?[21];
        internal bool FaceDirty => _faceDirty;
        internal void Accepted(bool face, bool dynamic, bool follower, long now)
        {
            if (face) _faceDirty=false;
            if (dynamic) _gearDirty=_speedDirty=_statusDirty=_oilDirty=_waterDirty=_fuelDirty=_torqueDirty=_odoDirty=false;
            if (follower)
            {
                _dialDirty=false; _followerDrawn=true;
                if (_ignitionAt != 0 && (now-_ignitionAt)/(double)Stopwatch.Frequency >= AvanteIgnitionSweep.DurationSeconds)
                    _ignitionSettled=true;
                _renderedRpm=Rpm(now);
                _renderedBand=_scale.Band(_renderedRpm);
                _renderedPairs=_sample?.Rpm==null?0:_scale.LitPairs(_sample.Rpm.Value);
            }
        }
        private Vortice.RawRect? MapRect(System.Windows.Rect r)
        {
            double designWidth=_expanded?2048:908, designLeft=_expanded?0:570;
            double scale=Math.Min(_width/designWidth,_height/750);
            double offsetX=(_width-designWidth*scale)/2-designLeft*scale;
            double offsetY=(_height-750*scale)/2;
            int left=Math.Clamp((int)Math.Floor(r.Left*scale+offsetX)-2,0,(int)_width);
            int top=Math.Clamp((int)Math.Floor(r.Top*scale+offsetY)-2,0,(int)_height);
            int right=Math.Clamp((int)Math.Ceiling(r.Right*scale+offsetX)+2,0,(int)_width);
            int bottom=Math.Clamp((int)Math.Ceiling(r.Bottom*scale+offsetY)+2,0,(int)_height);
            return right>left&&bottom>top?(Vortice.RawRect?)new Vortice.RawRect(left,top,right,bottom):null;
        }
        private void BuildSlots()
        {
            var design=new[] {
                new System.Windows.Rect(932,320,184,160), // gear
                new System.Windows.Rect(889,500,270,125), // speed
                new System.Windows.Rect(570,640,908,110), // status
                new System.Windows.Rect(100,245,440,105), // oil
                new System.Windows.Rect(100,457,440,105), // water
                new System.Windows.Rect(100,602,440,110), // fuel
                new System.Windows.Rect(1540,457,400,105), // torque
                new System.Windows.Rect(1490,645,520,75) // odometer
            };
            for(int i=0;i<design.Length;i++)_fixedSlots[i]=MapRect(design[i]);
            _coolantGaugeSlot=MapRect(new System.Windows.Rect(1600,610,320,40));
            for(int i=0;i<_numberSlots.Length;i++)
            {
                if(i>_scale.Maximum/1000){_numberSlots[i]=null;continue;}
                var p=P(276,_scale.Angle(i*1000));
                _numberSlots[i]=MapRect(new System.Windows.Rect(p.X-55,p.Y-55,110,110));
            }
        }
        internal (float X,float Y) NeedleCenter
        {
            get
            {
                double designWidth=_expanded?2048:908, designLeft=_expanded?0:570;
                double s=Math.Min(_width/designWidth,_height/750);
                return ((float)((_width-designWidth*s)/2+(Cx-designLeft)*s),(float)((_height-750*s)/2+Cy*s));
            }
        }
        internal float NeedleAngle(long now) => (float)_scale.Angle(Rpm(now));
        internal bool NeedleVisible => _sample?.Rpm!=null;
        internal float FlashOpacity(long now)
        {
            bool flashing=_sample?.Rpm!=null&&_scale.Band(_sample.Rpm.Value)==2;
            if (flashing && _flashAt==0) _flashAt=now;
            return flashing?(float)(.12+.88*(.5+.5*Math.Cos(2*Math.PI*(now-_flashAt)/(double)Stopwatch.Frequency/.2))):1;
        }
        internal System.Collections.Generic.List<Vortice.RawRect> FollowerDirtyRects(long now)
        {
            var result=new System.Collections.Generic.List<Vortice.RawRect>();
            if (!_followerDrawn || _faceDirty)
            { result.Add(new Vortice.RawRect(0,0,(int)_width,(int)_height)); return result; }
            if (!_ignitionSettled && _ignitionAt != 0)
            { if (MapRect(new System.Windows.Rect(620,0,808,630)) is { } sweep) result.Add(sweep); return result; }
            double rpm=Rpm(now);
            int band=_scale.Band(rpm), pairs=_sample?.Rpm==null?0:_scale.LitPairs(_sample.Rpm.Value);
            if (band!=_renderedBand || pairs!=_renderedPairs || !double.IsFinite(_renderedRpm))
            { if (MapRect(new System.Windows.Rect(620,0,808,630)) is { } full) result.Add(full); return result; }
            if (!_dialDirty && Math.Abs(rpm-_renderedRpm)<1) return result;
            var old=P(326,_scale.Angle(_renderedRpm));var current=P(326,_scale.Angle(rpm));
            if (MapRect(new System.Windows.Rect(Math.Min(old.X,current.X)-60,Math.Min(old.Y,current.Y)-60,
                Math.Abs(current.X-old.X)+120,Math.Abs(current.Y-old.Y)+120)) is { } edge) result.Add(edge);
            return result;
        }
        internal System.Collections.Generic.List<Vortice.RawRect> DynamicDirtyRects(long now)
        {
            var result=new System.Collections.Generic.List<Vortice.RawRect>();
            // Scale changes move every numeral. Clear the previous scale's entire
            // dynamic surface before drawing the new one; the old slots no longer
            // describe where the previous numerals were placed.
            if (_faceDirty)
            {
                result.Add(new Vortice.RawRect(0,0,(int)_width,(int)_height));
                return result;
            }
            void Add(Vortice.RawRect? r) { if(r is { } mapped) result.Add(mapped); }
            if (_gearDirty) Add(_fixedSlots[0]);
            if (_speedDirty) Add(_fixedSlots[1]);
            if (_statusDirty) Add(_fixedSlots[2]);
            if (_expanded)
            {
                if (_oilDirty) Add(_fixedSlots[3]);
                if (_waterDirty) { Add(_fixedSlots[4]); Add(_coolantGaugeSlot); }
                if (_fuelDirty) Add(_fixedSlots[5]);
                if (_torqueDirty) Add(_fixedSlots[6]);
                if (_odoDirty) Add(_fixedSlots[7]);
            }
            double rpm=Rpm(now);
            if (_dialDirty || _from!=_to&&now<_at+Stopwatch.Frequency*.065 || _pulseNumber>=0&&now<_pulseAt+Stopwatch.Frequency*.1)
            {
                int previous=double.IsFinite(_renderedRpm)?(int)Math.Round(_renderedRpm/1000):-1;
                int current=(int)Math.Round(rpm/1000);
                foreach(int i in new[]{previous,current,_pulseNumber})
                {
                    if(i<0 || i>_scale.Maximum/1000)continue;
                    if(i!=_pulseNumber && Math.Abs(rpm-i*1000)>150 && (!double.IsFinite(_renderedRpm)||Math.Abs(_renderedRpm-i*1000)>150))continue;
                    Add(_numberSlots[i]);
                }
            }
            return result;
        }
        private double _from,_to;
        private long _at,_flashAt;
        private double? _previousNumberRpm;
        private int _pulseNumber=-1;
        private long _pulseAt;
        private static readonly string[][] FollowerColors={
            new[]{"#006D8FA3","#5C89A9B9","#E6C7DAE0","#EDF8F8","#A3BFCC"},
            new[]{"#00FF9E0E","#5CEF8508","#E6FFBE28","#FFEF99","#BE6E0C"},
            new[]{"#00FF3019","#5CCF1C0F","#E6FF4C2A","#FFD4B9","#AC2518"}};
        private double _width,_height;
        private readonly ID2D1PathGeometry _clip,_withoutTicks,_tickRing,_innerRing,_needle,_outline;
        private readonly ID2D1PathGeometry? _fuelTrack,_coolantTrack;
        private ID2D1PathGeometry? _fuelFill,_coolantFill;
        private double? _fuelFillLevel,_coolantFillLevel;
        private readonly ID2D1PathGeometry[] _lightClips=new ID2D1PathGeometry[5];
        private ID2D1PathGeometry? _red,_yellow,_ticks;
        private readonly ID2D1Bitmap1[] _images=new ID2D1Bitmap1[5];
        private readonly ID2D1PathGeometry[] _logoShapes;
        private readonly ID2D1PathGeometry _headlightShape;
        private readonly Func<string,ID2D1Bitmap1> _sharedImage;
        private ID2D1Bitmap1? _scaleAtlas;
        internal CompositionAvanteHud(ID2D1DeviceContext1 context, ID2D1Factory1 factory,bool expanded,
            ID2D1Bitmap1[] images, Func<string,ID2D1Bitmap1> sharedImage)
        {
            _expanded=expanded;_c=new CompositionHudCanvas(context,factory);_sharedImage=sharedImage;
            _logoShapes=Array.ConvertAll(AvanteNLogo.Geometries,_c.Convert);
            _headlightShape=_c.Convert(WGeometry.Parse("M0,9 L15,11 L15,27 L0,29 Q5,20 0,9 Z M22,9 L42,5 M22,18 L45,18 M22,27 L42,31"));
            _outline=_c.Convert(expanded ? (WGeometry)new RectangleGeometry(new System.Windows.Rect(0,0,2048,750)) : new CombinedGeometry(GeometryCombineMode.Union,
                new CombinedGeometry(GeometryCombineMode.Intersect,new EllipseGeometry(new Point(Cx,Cy),392,392),new RectangleGeometry(new System.Windows.Rect(570,0,908,623))),
                WGeometry.Parse("M570,665 L645,632 Q665,623 700,623 L1348,623 Q1383,623 1403,632 L1478,665 L1478,750 L570,750 Z")));
            _clip=_c.Convert(new RectangleGeometry(new System.Windows.Rect(0,0,2048,623)));
            var ring=SectorGeometry(315,362,140,400);_tickRing=_c.Convert(ring);
            _withoutTicks=_c.Convert(new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new System.Windows.Rect(0,0,2048,750)),new CombinedGeometry(GeometryCombineMode.Intersect,ring,new RectangleGeometry(new System.Windows.Rect(0,0,2048,623)))));
            _innerRing=_c.Convert(SectorGeometry(160,187,140,400));
            _needle=_c.Convert(WGeometry.Parse("M1138,397 Q1146,389 1170,390 L1377,395.9 1377,398.1 1170,404 Q1146,405 1138,397 Z"));
            if(expanded)
            {
                _fuelTrack=_c.Convert(AvanteBarGauge.Track(AvanteBarGauge.Fuel));
                _coolantTrack=_c.Convert(AvanteBarGauge.Track(AvanteBarGauge.Coolant));
            }
            double[] left={169,194,219,244,270},right={372,347,322,296,270};
            for(int i=0;i<5;i++){var pair=new GeometryGroup();pair.Children.Add(SectorGeometry(360,395,140,left[i]));pair.Children.Add(SectorGeometry(360,395,right[i],400));_lightClips[i]=_c.Convert(pair);_images[i]=images[i];}
            Rebuild();
        }
        private static Point P(double r,double a)=>new Point(Cx+r*Math.Cos(a*Math.PI/180),Cy+r*Math.Sin(a*Math.PI/180));
        private static WGeometry SectorGeometry(double inner,double outer,double start,double end)
        {
            end=Math.Max(start+.001,end);var shape=new StreamGeometry();using(var g=shape.Open())
            {g.BeginFigure(P(outer,start),true,true);g.ArcTo(P(outer,end),new System.Windows.Size(outer,outer),0,end-start>180,System.Windows.Media.SweepDirection.Clockwise,true,false);g.LineTo(P(inner,end),true,false);g.ArcTo(P(inner,start),new System.Windows.Size(inner,inner),0,end-start>180,System.Windows.Media.SweepDirection.Counterclockwise,true,false);}return shape;
        }
        private void Rebuild()
        {
            _c.ForgetScaleResources();
            // Match the WPF scale sprites. The unknown-maximum fallback remains
            // procedural until a telemetry maximum arrives, avoiding a second
            // full atlas allocation during startup.
            bool generated=AppContext.TryGetSwitch("AMS2KRLeague.Avante.UseGeneratedScales",out bool enabled)&&enabled;
            _scaleAtlas=!generated&&_scale.HasWarningThresholds&&_scale.Maximum>=4000&&_scale.Maximum<=20000
                &&_scale.Maximum%1000==0?_sharedImage("avante-scale-"+_scale.Maximum.ToString(CultureInfo.InvariantCulture)+".png"):null;
            _red?.Dispose();_yellow?.Dispose();_ticks?.Dispose();
            _red=_scale.HasWarningThresholds?_c.Convert(SectorGeometry(210,348,_scale.Angle(_scale.RedStart),_scale.Angle(_scale.Maximum))):null;
            _yellow=_scale.HasWarningThresholds?_c.Convert(SectorGeometry(210,348,_scale.Angle(_scale.YellowStart),_scale.Angle(_scale.RedStart))):null;
            var ticks=new GeometryGroup();ticks.Children.Add(SectorGeometry(348,350,_scale.Angle(0),_scale.Angle(_scale.Maximum)));
            for(double rpm=0;rpm<=_scale.Maximum;rpm+=200)
            {bool major=rpm%1000==0;var line=new LineGeometry(P(major?331:340,_scale.Angle(rpm)),P(349,_scale.Angle(rpm)));ticks.Children.Add(line.GetWidenedPathGeometry(new Pen(Brushes.White,major?3.2:1.7)));}
            _ticks=_c.Convert(ticks);_flashAt=0;_previousNumberRpm=null;_pulseNumber=-1;
            _faceDirty=_dialDirty=_gearDirty=_speedDirty=_statusDirty=true;
            _oilDirty=_waterDirty=_fuelDirty=_torqueDirty=_odoDirty=true;
            _followerDrawn=false;_renderedRpm=double.NaN;_renderedBand=_renderedPairs=-1;
        }
        internal void Update(CompositionHudFrame frame,DrivingTelemetrySample? sample,double width,double height)
        {
            if (_ignitionAt == 0) _ignitionAt = Stopwatch.GetTimestamp();
            bool geometryChanged=_width!=width||_height!=height;
            _width=width;_height=height;
            if(!ReferenceEquals(frame.Session,_sessionSource))
            {
                _sessionSource=frame.Session;string vehicle=frame.Session?.RootCarName??"",profile=AvanteRpmScale.ProfileVehicleName(frame.Session);
                if(frame.Session==null||_vehicle!=vehicle||_profile!=profile){_engineMaximum=null;_sample=null;_from=_to=0;_flashAt=0;if(frame.Session==null)_generation=_participant=null;}
                _vehicle=vehicle;_profile=profile;_session=frame.Session;
                if(AvanteRpmScale.IsValidEngineMaximum(_session?.ViewedVehicleTelemetry?.MaxRpm))_engineMaximum=_session!.ViewedVehicleTelemetry!.MaxRpm;
            }
            if(!ReferenceEquals(_sample,sample))
            {
                long now=Stopwatch.GetTimestamp();bool continuous=sample!=null&&_sample!=null&&sample.Generation==_sample.Generation&&sample.ParticipantIndex==_sample.ParticipantIndex&&sample.CapturedAt>=_sample.CapturedAt&&(sample.CapturedAt-_sample.CapturedAt).TotalSeconds<1;
                if(sample!=null)
                {
                    if(_generation.HasValue&&(_generation!=sample.Generation||_participant!=sample.ParticipantIndex)
                        &&(_session==null||_session.ViewedParticipantIndex!=sample.ParticipantIndex||Math.Abs((_session.CapturedAt-sample.CapturedAt).TotalSeconds)>.1))
                    {_engineMaximum=null;_session=null;_vehicle=_profile="";}
                    _generation=sample.Generation;_participant=sample.ParticipantIndex;
                    if(AvanteRpmScale.IsValidEngineMaximum(sample.MaxRpm))_engineMaximum=sample.MaxRpm;
                }
                if(!continuous||_to!=(sample?.Rpm??0)){_from=continuous?Rpm(now):sample?.Rpm??0;_to=sample?.Rpm??0;_at=now;}
                if(!continuous){_flashAt=0;_previousNumberRpm=null;_pulseNumber=-1;}
                _sample=sample;
            }
            frame.Settings.AvanteVehicles.TryGetValue(_vehicle,out var calibration);
            var scale=AvanteRpmScale.Resolve(_engineMaximum,calibration,_profile);
            bool changed=!scale.Maximum.Equals(_scale.Maximum)||!scale.RedStart.Equals(_scale.RedStart)||!scale.YellowStart.Equals(_scale.YellowStart);
            _scale=scale;if(changed)Rebuild();
            if(geometryChanged||changed)BuildSlots();
            bool valid=sample!=null&&_session!=null&&sample.ParticipantIndex==_session.ViewedParticipantIndex&&Math.Abs((sample.CapturedAt-_session.CapturedAt).TotalSeconds)<=1;var v=valid?_session!.ViewedVehicleTelemetry:null;
            void Changed(object? key, ref object? previous, ref bool dirty)
            { if(!Equals(key,previous)){previous=key;dirty=true;} }
            Changed((sample?.Rpm,_scale.Maximum,_scale.YellowStart,_scale.RedStart),ref _dialKey,ref _dialDirty);
            Changed(sample?.Gear,ref _gearKey,ref _gearDirty);
            Changed(sample?.SpeedText,ref _speedKey,ref _speedDirty);
            Changed((AvanteIndicators.Abs(v,sample),AvanteIndicators.Tcs(v),AvanteIndicators.Headlights(v),
                valid?(double?)_session!.AmbientTemperature:null,v?.CarFlagsRaw),ref _statusKey,ref _statusDirty);
            Changed(v?.OilTemperatureCelsius,ref _oilKey,ref _oilDirty);
            Changed(v?.WaterTemperatureCelsius,ref _waterKey,ref _waterDirty);
            Changed((v?.FuelLevel,v?.FuelCapacityLitres),ref _fuelKey,ref _fuelDirty);
            Changed(v?.EngineTorqueNewtonMetres,ref _torqueKey,ref _torqueDirty);
            Changed(v?.OdometerKilometres,ref _odoKey,ref _odoDirty);
            _frame=frame;
        }
        private double Rpm(long now)=>_from+(_to-_from)*Math.Clamp((now-_at)/(double)Stopwatch.Frequency/.065,0,1);
        private void BeginCanvas()
        {
            double width=_expanded?2048:908,left=_expanded?0:570;
            float s=(float)Math.Min(_width/width,_height/750);_c.Push(Matrix3x2.CreateScale(s)*Matrix3x2.CreateTranslation((float)((_width-width*s)/2-left*s),(float)((_height-750*s)/2)));
        }
        internal void DrawFace()
        {
            if(_frame==null)return;
            BeginCanvas();var dc=_c.Dc;
            _c.Clip(_outline);_c.Clip(_withoutTicks);Bitmap(0);_c.Unclip();
            _c.Clip(_clip);_c.Clip(_tickRing);Bitmap(4);_c.Unclip();
            if(_scale.HasWarningThresholds)dc.FillGeometry(_yellow!,_c.Color("#5AF8D46F"));
            _c.Unclip();_c.Unclip();_c.Pop();
        }
        internal void DrawWarning()
        {
            if(_frame==null || !_scale.HasWarningThresholds)return;
            BeginCanvas();var dc=_c.Dc;_c.Clip(_outline);_c.Clip(_clip);
            var start=P(280,_scale.Angle(_scale.RedStart));var end=P(280,_scale.Angle(_scale.Maximum));
            dc.FillGeometry(_red!,_c.Gradient("red-"+_scale.Maximum+"-"+_scale.RedStart,"#CD780308","#E1260003",new Vector2((float)start.X,(float)start.Y),new Vector2((float)end.X,(float)end.Y)));
            _c.Unclip();_c.Unclip();_c.Pop();
        }
        internal void DrawNeedle()
        {
            if(_frame==null)return;
            BeginCanvas();var dc=_c.Dc;_c.Clip(_outline);
            dc.FillGeometry(_needle,_c.Gradient("needle","#FFFDF2","#FF713B",new Vector2(0,389),new Vector2(0,405)));
            dc.DrawGeometry(_needle,_c.Color("OrangeRed"),2);
            _c.Unclip();_c.Pop();
        }
        internal void DrawFollower(long now)
        {
            if(_frame==null)return;
            BeginCanvas();var dc=_c.Dc;_c.Clip(_outline);_c.Clip(_clip);
            double rpm=Rpm(now),angle=_scale.Angle(rpm);int band=_scale.Band(rpm),pairs=_sample?.Rpm==null?0:_scale.LitPairs(_sample.Rpm.Value);
            if(_sample?.Rpm!=null)
            {
                using var follower=_c.Sector(new Vector2(1024,397),304,348,150,angle);
                dc.FillGeometry(follower,_c.Radial("follower-"+band,new Vector2(1024,397),348,new[]{304/348f,(304+44*.22f)/348,(304+44*.63f)/348,(304+44*.93f)/348,1},FollowerColors[band]));
            }
            if(band>0){_c.Clip(_innerRing);Lights(band+1);_c.Unclip();}if(pairs>0){_c.Clip(_lightClips[pairs-1]);Lights(band+1);_c.Unclip();}
            if (_ignitionAt != 0)
            {
                double progress=Math.Clamp((now-_ignitionAt)/(double)Stopwatch.Frequency/AvanteIgnitionSweep.DurationSeconds,0,1);
                float opacity=(float)AvanteIgnitionSweep.Opacity(progress);
                if (opacity>0)
                {
                    double end=AvanteIgnitionSweep.Angle(progress);
                    double blueEnd=AvanteIgnitionSweep.Angle(Math.Min(1,progress*1.16));
                    using var blue=_c.Sector(new Vector2((float)Cx,(float)Cy),213,245,AvanteIgnitionSweep.StartAngle,blueEnd);
                    _c.Clip(_outline,opacity);
                    dc.FillGeometry(blue,_c.Radial("avante-ignition-blue",new Vector2((float)Cx,(float)Cy),245,
                        new[]{213/245f,221/245f,231/245f,1f},
                        new[]{"#0007C6FF","#9100DAFF","#E66BF9FF","#00078CF3"}));
                    using(var reveal=_c.Sector(new Vector2((float)Cx,(float)Cy),335,399,AvanteIgnitionSweep.StartAngle,end))
                    {
                        _c.Clip(reveal);
                        _c.DrawImage("avante-ignition-flame.png",Cx-486,Cy-486,972,972);
                        _c.Unclip();
                    }
                    var head=P(378,end);
                    _c.Ellipse(head.X,head.Y,8,8,"#FFFFB52D");
                    _c.Ellipse(head.X,head.Y,3,3,"White");
                    _c.Unclip();
                }
            }
            _c.Unclip();_c.Unclip();_c.Pop();
        }
        internal void DrawDynamic()
        {
            if(_frame==null)return;
            BeginCanvas();var dc=_c.Dc;_c.Clip(_outline);_c.Clip(_clip);
            long now=Stopwatch.GetTimestamp();double rpm=Rpm(now);
            if(_scale.HasWarningThresholds){var warning=_c.Geometry("redTicks-"+_scale.Maximum+"-"+_scale.RedStart,()=>SectorGeometry(338,346,_scale.Angle(_scale.RedStart),_scale.Angle(_scale.Maximum)));dc.FillGeometry(warning,_c.Color("Firebrick"));}
            if(_scaleAtlas is { } atlas)
                dc.DrawBitmap(atlas,new Vortice.RawRectF(570,0,1478,623),1,InterpolationMode.Linear,
                    new Vortice.RawRectF(0,0,1816,1246),null);
            else dc.FillGeometry(_ticks!,_c.Color("AliceBlue"));
            if(_previousNumberRpm is double previous && Math.Abs(rpm-previous)>100 && Math.Abs(rpm-previous)<2000)
            {
                int crossed=(int)(rpm>previous?Math.Floor(rpm/1000):Math.Ceiling(rpm/1000));double boundary=crossed*1000.0;
                if(crossed>=1&&boundary<=_scale.Maximum&&(previous<boundary&&rpm>=boundary||previous>boundary&&rpm<=boundary)&&AvanteClusterView.NumberEmphasis(rpm,crossed)<1.15){_pulseNumber=crossed;_pulseAt=now;}
            }
            _previousNumberRpm=_sample?.Rpm!=null?(double?)rpm:null;
            double count=_scale.Maximum/1000,size=Math.Min(60,276*240/count*Math.PI/180/(count>=10?1.5:.8));
            for(int i=0;i<=count;i++){var p=P(276,_scale.Angle(i*1000));double emphasis=Math.Max(AvanteClusterView.NumberEmphasis(rpm,i),i==_pulseNumber?1+.2*Math.Max(0,1-(now-_pulseAt)/(double)Stopwatch.Frequency/.1):1);_c.Push(Matrix3x2.CreateScale((float)emphasis,new Vector2((float)p.X,(float)p.Y)));if(_scaleAtlas is { } numbers){int sx=(i%8)*256,sy=1246+(i/8)*256;dc.DrawBitmap(numbers,new Vortice.RawRectF((float)(p.X-64),(float)(p.Y-64),(float)(p.X+64),(float)(p.Y+64)),1,InterpolationMode.Linear,new Vortice.RawRectF(sx,sy,sx+256,sy+256),null);}else _c.StyledText(i.ToString(CultureInfo.InvariantCulture),p.X,p.Y,size);_c.Pop();}
            _c.Unclip();if(_scaleAtlas==null){_c.CenterText("km/h",1168,589,17);_c.CenterText("x1000",805,591,23);_c.CenterText("rpm",805,613,20);}
            _c.StyledText(_sample?.Gear==0?"N":_sample?.Gear==-1?"R":_sample?.GearText??"—",1024,397,164);
            _c.StyledText(_sample?.SpeedKmh?.ToString("0",CultureInfo.InvariantCulture)??"—",1024,562,108,tabular:true);
            Values();_c.Unclip();_c.Pop();
        }
        private void Bitmap(int index)=>_c.Dc.DrawBitmap(_images[index],new Vortice.RawRectF(0,0,2048,750),1,InterpolationMode.HighQualityCubic,null,null);
        private void Lights(int index)
        {
            double pixelScale=2048.0/_images[0].PixelSize.Width,left=Math.Floor((Cx-395)/pixelScale)*pixelScale;
            _c.Dc.DrawBitmap(_images[index],new Vortice.RawRectF((float)left,0,(float)(left+_images[index].PixelSize.Width*pixelScale),(float)(_images[index].PixelSize.Height*750.0/_images[0].PixelSize.Height)),1,InterpolationMode.HighQualityCubic,null,null);
        }
        private static string Value(double? n,double low,double high,string format="0")=>n.HasValue&&double.IsFinite(n.Value)&&n>=low&&n<=high?n.Value.ToString(format,CultureInfo.InvariantCulture):"—";
        private void BarGauge(string key,ID2D1PathGeometry track,AvanteBarGauge.Outline outline,double? level,
            ref double? previous,ref ID2D1PathGeometry? fill)
        {
            if(previous!=level)
            {
                fill?.Dispose();
                fill=level is double amount && amount>0?_c.Convert(AvanteBarGauge.Fill(outline,amount)):null;
                previous=level;
            }
            var top=new Vector2(0,(float)AvanteBarGauge.Top);
            var bottom=new Vector2(0,(float)AvanteBarGauge.Bottom);
            _c.Dc.FillGeometry(track,_c.Gradient(key+"-glass",AvanteBarGauge.GlassStops,top,bottom));
            if(fill!=null)
            {
                float left=(float)((outline.TopLeft+outline.BottomLeft)/2);
                float right=(float)(left+((outline.TopRight+outline.BottomRight)/2-left)*level!.Value);
                var sweep=_c.Gradient(key+"-fill",AvanteBarGauge.FillStops,new Vector2(left,0),new Vector2(right,0));
                var end=new Vector2(right,0);
                if(sweep.EndPoint!=end)sweep.EndPoint=end;
                _c.Dc.FillGeometry(fill,sweep);
            }
            _c.Dc.FillGeometry(track,_c.Gradient(key+"-sheen",AvanteBarGauge.SheenStops,top,bottom));
        }
        private void Values()
        {
            var session=_session;bool valid=_sample!=null&&session!=null&&_sample.ParticipantIndex==session.ViewedParticipantIndex&&Math.Abs((_sample.CapturedAt-session.CapturedAt).TotalSeconds)<=1;
            var v=valid?session!.ViewedVehicleTelemetry:null;
            _c.Text(Value(valid?session!.AmbientTemperature:(double?)null,-80,80)+"°C",668,663,42,"AliceBlue","AvanteN UI",false,1);
            Status("ABS",AvanteIndicators.Abs(v,_sample),795);Status("TCS",AvanteIndicators.Tcs(v),992);Status("PIT LIMITER",v==null?(bool?)null:(v.CarFlagsRaw&(1u<<3))!=0,1266);
            if(AvanteIndicators.Headlights(v))
            {
                _c.Push(Matrix3x2.CreateTranslation(_expanded?1885:1350,30));
                _c.Dc.DrawGeometry(_headlightShape,_c.Color("#51FF64"),3);
                _c.Pop();
            }
            if(!_expanded)return;
            _c.Text("오일 온도",319,182,37,"AliceBlue","AvanteN UI",false,1);_c.Text("냉각수 온도",319,394,37,"AliceBlue","AvanteN UI",false,1);
            _c.Text("터보",1724,182,37,"AliceBlue","AvanteN UI",false,1);_c.Text("토크",1724,394,37,"AliceBlue","AvanteN UI",false,1);
            Readout(Value(v?.OilTemperatureCelsius,-40,300),"°C",319,297);Readout(Value(v?.WaterTemperatureCelsius,-40,200),"°C",319,510);
            Readout("—","bar",1724,297);Readout(Value(v?.EngineTorqueNewtonMetres,-4000,4000),"Nm",1724,510);
            BarGauge("avante-fuel",_fuelTrack!,AvanteBarGauge.Fuel,AvanteBarGauge.FuelLevel(v?.FuelLevel),ref _fuelFillLevel,ref _fuelFill);
            BarGauge("avante-coolant",_coolantTrack!,AvanteBarGauge.Coolant,AvanteBarGauge.CoolantLevel(v?.WaterTemperatureCelsius),ref _coolantFillLevel,ref _coolantFill);
            _c.Text("E",132,612,38,"AliceBlue","AvanteN UI",false,1);_c.Text("F",478,612,38,"AliceBlue","AvanteN UI",false,1);_c.Text("C",1587,612,38,"AliceBlue","AvanteN UI",false,1);_c.Text("H",1933,612,38,"AliceBlue","AvanteN UI",false,1);
            _c.Text(v==null?"— L":Value(v.FuelLevel*v.FuelCapacityLitres,0,2000)+" L",275,672,45,"AliceBlue","AvanteN UI",false,1);_c.Text(Value(v?.OdometerKilometres,0,9999999)+" km",1820,684,42,"AliceBlue","AvanteN UI",false,1);
            _c.Push(Matrix3x2.CreateScale(68f/76,29f/32)*Matrix3x2.CreateTranslation(1600,678));
            for(int i=0;i<_logoShapes.Length;i++)_c.Dc.FillGeometry(_logoShapes[i],_c.Color(AvanteNLogo.Colours[i]));
            _c.Pop();
        }
        private void Readout(string value,string unit,double x,double y){_c.StyledText(value,x,y,140,true);_c.CenterText(unit,x+_c.Measure(value,140,"AvanteN Readout",false).Width/2+28,y+45,33);}
        private void Status(string text,bool? active,double x)=>Status(text,active==true?AvanteIndicatorState.Active:active==false?AvanteIndicatorState.Off:AvanteIndicatorState.Unknown,x);
        private void Status(string text,AvanteIndicatorState state,double x)
        {string color=state==AvanteIndicatorState.Active?"#FF5146":state==AvanteIndicatorState.On?"#FFDE56":state==AvanteIndicatorState.Off?"AliceBlue":"SlateGray";double width=_c.Measure(text,33,"AvanteN UI",false).Width,left=x-(49+width)/2;for(int i=0;i<3;i++)_c.Rect(left,673+i*10,33,5,color,2.5);_c.CenterText(text,left+49+width/2,690,33,color);}
        public void Dispose(){_red?.Dispose();_yellow?.Dispose();_ticks?.Dispose();_fuelTrack?.Dispose();_coolantTrack?.Dispose();_fuelFill?.Dispose();_coolantFill?.Dispose();_clip.Dispose();_withoutTicks.Dispose();_tickRing.Dispose();_innerRing.Dispose();_needle.Dispose();_outline.Dispose();_headlightShape.Dispose();foreach(var logo in _logoShapes)logo.Dispose();foreach(var clip in _lightClips)clip.Dispose();_c.Dispose();}
    }
}
