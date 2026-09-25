using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using AMS2LeagueClient.Core.Presentation;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace AMS2LeagueClient.Presentation
{
    // Opt-in (--monitor-retained-n) native owner. One DComp device batches both N HUDs into a
    // single Commit; no HUD owns a swap chain or calls Present. The producer only
    // replaces a one-deep display state and never waits for GPU or recording.
    internal sealed class RetainedNPresenter : IDisposable
    {
        internal readonly struct Slot
        {
            internal readonly IntPtr Hwnd;
            internal readonly int Width, Height;
            internal readonly bool Expanded;
            internal readonly float Opacity;
            internal Slot(IntPtr hwnd, int width, int height, bool expanded, float opacity)
            { Hwnd = hwnd; Width = width; Height = height; Expanded = expanded; Opacity = opacity; }
        }

        private sealed class Target : IDisposable
        {
            internal Slot Slot;
            internal readonly IDCompositionTarget Root;
            internal readonly IDCompositionVisual Visual;
            internal readonly IDCompositionVisual FaceVisual;
            internal readonly IDCompositionVisual WarningVisual;
            internal readonly IDCompositionVisual FollowerVisual;
            internal readonly IDCompositionVisual NeedleVisual;
            internal readonly IDCompositionVisual DynamicVisual;
            internal readonly IDCompositionEffectGroup Effect;
            internal readonly IDCompositionEffectGroup WarningEffect, FollowerEffect, NeedleEffect;
            internal readonly IDCompositionRotateTransform NeedleRotation;
            internal readonly IDCompositionSurface FaceSurface;
            internal readonly IDCompositionSurface WarningSurface, FollowerSurface, NeedleSurface;
            internal readonly IDCompositionSurface DynamicSurface;
            internal readonly Vortice.RawRect DialRect;
            internal readonly CompositionAvanteHud Hud;
            internal bool FirstDraw = true;
            private float _angle=float.NaN,_flash=float.NaN,_needleOpacity=float.NaN;
            internal Target(IDCompositionDevice device, ID2D1DeviceContext1 dc, ID2D1Factory1 factory, Slot slot,
                ID2D1Bitmap1[] images, Func<string,ID2D1Bitmap1> sharedImage)
            {
                Slot = slot;
                DialRect=MapDial(slot);
                device.CreateTargetForHwnd(slot.Hwnd, true, out var root).CheckError(); Root = root;
                Visual = device.CreateVisual();
                device.CreateEffectGroup(out var effect).CheckError(); Effect = effect;
                Effect.SetOpacity(slot.Opacity).CheckError();
                Visual.SetEffect(Effect).CheckError();
                FaceVisual = device.CreateVisual();
                WarningVisual = device.CreateVisual();
                FollowerVisual = device.CreateVisual();
                NeedleVisual = device.CreateVisual();
                DynamicVisual = device.CreateVisual();
                device.CreateEffectGroup(out var warningEffect).CheckError(); WarningEffect=warningEffect;
                device.CreateEffectGroup(out var followerEffect).CheckError(); FollowerEffect=followerEffect;
                device.CreateEffectGroup(out var needleEffect).CheckError(); NeedleEffect=needleEffect;
                WarningVisual.SetEffect(WarningEffect).CheckError();
                FollowerVisual.SetEffect(FollowerEffect).CheckError();
                NeedleVisual.SetEffect(NeedleEffect).CheckError();
                WarningVisual.SetOffsetX(DialRect.Left).CheckError();
                WarningVisual.SetOffsetY(DialRect.Top).CheckError();
                FollowerVisual.SetOffsetX(DialRect.Left).CheckError();
                FollowerVisual.SetOffsetY(DialRect.Top).CheckError();
                device.CreateRotateTransform(out var rotation).CheckError(); NeedleRotation=rotation;
                NeedleVisual.SetTransform(NeedleRotation).CheckError();
                device.CreateSurface((uint)slot.Width, (uint)slot.Height, Format.B8G8R8A8_UNorm,
                    Vortice.DXGI.AlphaMode.Premultiplied, out var face).CheckError();
                device.CreateSurface((uint)(DialRect.Right-DialRect.Left), (uint)(DialRect.Bottom-DialRect.Top), Format.B8G8R8A8_UNorm,
                    Vortice.DXGI.AlphaMode.Premultiplied, out var warning).CheckError();
                device.CreateSurface((uint)(DialRect.Right-DialRect.Left), (uint)(DialRect.Bottom-DialRect.Top), Format.B8G8R8A8_UNorm,
                    Vortice.DXGI.AlphaMode.Premultiplied, out var follower).CheckError();
                device.CreateSurface((uint)slot.Width, (uint)slot.Height, Format.B8G8R8A8_UNorm,
                    Vortice.DXGI.AlphaMode.Premultiplied, out var needle).CheckError();
                device.CreateSurface((uint)slot.Width, (uint)slot.Height, Format.B8G8R8A8_UNorm,
                    Vortice.DXGI.AlphaMode.Premultiplied, out var dynamic).CheckError();
                FaceSurface = face; WarningSurface=warning; FollowerSurface=follower; NeedleSurface=needle; DynamicSurface = dynamic;
                FaceVisual.SetContent(FaceSurface).CheckError();
                WarningVisual.SetContent(WarningSurface).CheckError();
                FollowerVisual.SetContent(FollowerSurface).CheckError();
                NeedleVisual.SetContent(NeedleSurface).CheckError();
                DynamicVisual.SetContent(DynamicSurface).CheckError();
                Visual.AddVisual(FaceVisual, false, null!).CheckError();
                Visual.AddVisual(WarningVisual, true, FaceVisual).CheckError();
                Visual.AddVisual(FollowerVisual, true, WarningVisual).CheckError();
                Visual.AddVisual(NeedleVisual, true, FollowerVisual).CheckError();
                Visual.AddVisual(DynamicVisual, true, NeedleVisual).CheckError();
                Root.SetRoot(Visual).CheckError();
                Hud = new CompositionAvanteHud(dc, factory, slot.Expanded, images, sharedImage);
            }
            private static Vortice.RawRect MapDial(Slot slot)
            {
                double designWidth=slot.Expanded?2048:908,designLeft=slot.Expanded?0:570;
                double scale=Math.Min(slot.Width/designWidth,slot.Height/750.0);
                double offsetX=(slot.Width-designWidth*scale)/2-designLeft*scale;
                double offsetY=(slot.Height-750*scale)/2;
                int left=Math.Clamp((int)Math.Floor(620*scale+offsetX)-2,0,slot.Width-1);
                int top=Math.Clamp((int)Math.Floor(offsetY)-2,0,slot.Height-1);
                int right=Math.Clamp((int)Math.Ceiling(1428*scale+offsetX)+2,left+1,slot.Width);
                int bottom=Math.Clamp((int)Math.Ceiling(630*scale+offsetY)+2,top+1,slot.Height);
                return new Vortice.RawRect(left,top,right,bottom);
            }
            internal Vortice.RawRect? CropDial(Vortice.RawRect rect)
            {
                int left=Math.Max(rect.Left,DialRect.Left),top=Math.Max(rect.Top,DialRect.Top);
                int right=Math.Min(rect.Right,DialRect.Right),bottom=Math.Min(rect.Bottom,DialRect.Bottom);
                return right>left&&bottom>top?(Vortice.RawRect?)new Vortice.RawRect(left-DialRect.Left,top-DialRect.Top,
                    right-DialRect.Left,bottom-DialRect.Top):null;
            }
            internal void SetOpacity(Slot slot)
            { Effect.SetOpacity(slot.Opacity).CheckError(); Slot = slot; }
            internal bool SetMotion(long now)
            {
                bool changed=false;
                var center=Hud.NeedleCenter;
                float angle=Hud.NeedleAngle(now);
                if (float.IsNaN(_angle))
                { NeedleRotation.SetCenterX(center.X).CheckError(); NeedleRotation.SetCenterY(center.Y).CheckError(); }
                if (float.IsNaN(_angle)||Math.Abs(angle-_angle)>.05f)
                { NeedleRotation.SetAngle(angle).CheckError(); _angle=angle; changed=true; }
                float visible=Hud.NeedleVisible?1:0;
                if (visible!=_needleOpacity)
                { NeedleEffect.SetOpacity(visible).CheckError(); _needleOpacity=visible; changed=true; }
                float flash=Hud.FlashOpacity(now);
                if (float.IsNaN(_flash)||Math.Abs(flash-_flash)>.01f)
                { WarningEffect.SetOpacity(flash).CheckError(); FollowerEffect.SetOpacity(flash).CheckError(); _flash=flash; changed=true; }
                return changed;
            }
            public void Dispose()
            { Hud.Dispose(); DynamicSurface.Dispose(); NeedleSurface.Dispose(); FollowerSurface.Dispose(); WarningSurface.Dispose(); FaceSurface.Dispose(); DynamicVisual.Dispose(); NeedleVisual.Dispose(); FollowerVisual.Dispose(); WarningVisual.Dispose(); FaceVisual.Dispose(); NeedleRotation.Dispose(); NeedleEffect.Dispose(); FollowerEffect.Dispose(); WarningEffect.Dispose(); Effect.Dispose(); Visual.Dispose(); Root.Dispose(); }
        }

        private readonly object _gate = new object();
        private readonly AutoResetEvent _changed = new AutoResetEvent(false);
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly Action<Exception?> _ready;
        private readonly Action<string> _report;
        private readonly byte[][] _images;
        private Slot[] _slots = Array.Empty<Slot>();
        private CompositionHudFrame _frame;
        private DrivingTelemetrySample? _sample;
        private bool _disposed;
        private readonly Thread _thread;
        internal long Commits => Interlocked.Read(ref _commits);
        internal long Draws => Interlocked.Read(ref _draws);
        private long _commits, _draws;

        private readonly struct DrawTiming
        {
            internal readonly double Begin, Draw, End;
            internal DrawTiming(double begin, double draw, double end) { Begin=begin; Draw=draw; End=end; }
        }
        private enum HudLayer { Face, Warning, Follower, Needle, Dynamic }

        internal RetainedNPresenter(byte[][] images, DrivingHudSettings settings, Action<Exception?> ready, Action<string> report)
        {
            _images = images;
            _frame = new CompositionHudFrame(settings, null);
            _ready = ready;
            _report = report;
            _thread = new Thread(Run) { Name = "Monitor retained N owner", IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        internal void Publish(Slot[] slots, CompositionHudFrame frame, DrivingTelemetrySample? sample)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _slots = slots; _frame = frame; _sample = sample;
                _changed.Set();
            }
        }

        private void Run()
        {
            var resources = new List<IDisposable>();
            var targets = new Dictionary<IntPtr, Target>();
            Exception? failure = null;
            bool signaled = false;
            try
            {
                T Own<T>(T resource) where T : IDisposable { resources.Add(resource); return resource; }
                var d3d = Own(D3D11.D3D11CreateDevice(Vortice.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport));
                var dxgi = Own(d3d.QueryInterface<IDXGIDevice>());
                var factory = Own(D2D1.D2D1CreateFactory<ID2D1Factory1>());
                var d2d = Own(factory.CreateDevice(dxgi));
                var dc = Own(d2d.CreateDeviceContext(DeviceContextOptions.None).QueryInterface<ID2D1DeviceContext1>());
                var composition = Own(DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi));
                // Both N windows use one D2D device, so decoded native source
                // images and scale atlases can be shared for the owner's lifetime.
                var sharedCanvas=Own(new CompositionHudCanvas(dc,factory));
                var sharedBitmaps=new ID2D1Bitmap1[_images.Length];
                for(int i=0;i<sharedBitmaps.Length;i++)
                {
                    using var stream=new System.IO.MemoryStream(_images[i],false);
                    sharedBitmaps[i]=sharedCanvas.Image("native-avante-"+i,stream);
                }
                var handles = new WaitHandle[] { _stop, _changed };
                long alignedTick=0,nextAllowed=0;
                bool pendingCommit = false;
                long summaryAt = Stopwatch.GetTimestamp();
                long intervalCommits = 0, intervalDraws = 0, intervalSkips = 0, intervalDirtyPixels = 0, intervalTransformOnly = 0;
                long intervalStatsUnavailable=0;
                var beginTimes = new List<double>();
                var drawTimes = new List<double>();
                var endTimes = new List<double>();
                var commitTimes = new List<double>();
                var waitTimes = new List<double>();
                var phaseTimes = new List<double>();
                _report("tickMode=composition-aligned capHz=72");
                while (!_stop.WaitOne(0))
                {
                    // Only the owner waits. Publication from the UI/SHM path remains
                    // a one-entry replacement even when GPU completion is slow.
                    if (pendingCommit)
                    {
                        long waitStart = Stopwatch.GetTimestamp();
                        composition.WaitForCommitCompletion().CheckError();
                        waitTimes.Add(Milliseconds(waitStart, Stopwatch.GetTimestamp()));
                        pendingCommit = false;
                    }
                    Slot[] slots; CompositionHudFrame frame; DrivingTelemetrySample? sample;
                    lock (_gate) { slots = _slots; frame = _frame; sample = _sample; }
                    bool changed = false;
                    var live = new HashSet<IntPtr>();
                    foreach (var slot in slots)
                    {
                        if (slot.Hwnd == IntPtr.Zero || slot.Width <= 0 || slot.Height <= 0) continue;
                        live.Add(slot.Hwnd);
                        if (targets.TryGetValue(slot.Hwnd, out var current)
                            && (current.Slot.Width != slot.Width || current.Slot.Height != slot.Height || current.Slot.Expanded != slot.Expanded))
                        { current.Dispose(); targets.Remove(slot.Hwnd); }
                        if (!targets.ContainsKey(slot.Hwnd))
                        { targets.Add(slot.Hwnd, new Target(composition, dc, factory, slot, sharedBitmaps, sharedCanvas.Image)); changed = true; }
                        else if (targets[slot.Hwnd].Slot.Opacity != slot.Opacity)
                        { targets[slot.Hwnd].SetOpacity(slot); changed = true; }
                    }
                    foreach (var entry in new List<KeyValuePair<IntPtr, Target>>(targets))
                        if (!live.Contains(entry.Key)) { entry.Value.Dispose(); targets.Remove(entry.Key); changed = true; }

                    // 72 Hz is below the 144 Hz monitor period. The owner does not
                    // queue another draw before the preceding Commit completes.
                    long now = Stopwatch.GetTimestamp();
                    if(alignedTick==0)
                    {
                        if(TryCompositionFrame(composition,out var frameStats,out long period))
                        {
                            // Submit just after an estimated compositor frame
                            // boundary. The estimate is not a physical scanout.
                            long target=frameStats.NextEstimatedFrameTime+Stopwatch.Frequency/4000;
                            long earliest=Math.Max(now,nextAllowed);
                            if(target<earliest)target+=((earliest-target+period-1)/period)*period;
                            alignedTick=target;
                        }
                        else { alignedTick=Math.Max(now,nextAllowed); intervalStatsUnavailable++; }
                    }
                    bool due=now>=alignedTick;
                    if (due)
                    {
                        bool anyDraw=false, anyTransform=false;
                        foreach (var target in targets.Values)
                        {
                            target.Hud.Update(frame, sample, target.Slot.Width, target.Slot.Height);
                            bool face = target.FirstDraw || target.Hud.FaceDirty;
                            var full=new Vortice.RawRect(0,0,target.Slot.Width,target.Slot.Height);
                            var dynamicRects=target.FirstDraw ? new List<Vortice.RawRect>{full} : target.Hud.DynamicDirtyRects(now);
                            var followerRects=new List<Vortice.RawRect>();
                            foreach(var rect in target.FirstDraw ? new List<Vortice.RawRect>{full} : target.Hud.FollowerDirtyRects(now))
                                if(target.CropDial(rect) is { } cropped)followerRects.Add(cropped);
                            if (face)
                            {
                                foreach (var layer in new[] { (target.FaceSurface,HudLayer.Face), (target.WarningSurface,HudLayer.Warning), (target.NeedleSurface,HudLayer.Needle) })
                                {
                                    var timing=Draw(layer.Item1,null,target.Hud,dc,layer.Item2,now,
                                        layer.Item2==HudLayer.Warning?target.DialRect.Left:0,
                                        layer.Item2==HudLayer.Warning?target.DialRect.Top:0);
                                    Record(timing,beginTimes,drawTimes,endTimes);
                                    intervalDirtyPixels+=layer.Item2==HudLayer.Warning
                                        ?(long)(target.DialRect.Right-target.DialRect.Left)*(target.DialRect.Bottom-target.DialRect.Top)
                                        :(long)target.Slot.Width*target.Slot.Height;
                                    intervalDraws++;anyDraw=true;
                                }
                            }
                            foreach(var rect in followerRects)
                            {
                                var timing=Draw(target.FollowerSurface,rect,target.Hud,dc,HudLayer.Follower,now,
                                    target.DialRect.Left,target.DialRect.Top);
                                Record(timing, beginTimes, drawTimes, endTimes);
                                intervalDirtyPixels+=(long)(rect.Right-rect.Left)*(rect.Bottom-rect.Top);
                                intervalDraws++;anyDraw=true;
                            }
                            foreach(var rect in dynamicRects)
                            {
                                var timing=Draw(target.DynamicSurface,rect,target.Hud,dc,HudLayer.Dynamic,now);
                                Record(timing,beginTimes,drawTimes,endTimes);
                                intervalDirtyPixels+=(long)(rect.Right-rect.Left)*(rect.Bottom-rect.Top);
                                intervalDraws++;anyDraw=true;
                            }
                            target.FirstDraw = false;
                            target.Hud.Accepted(face,dynamicRects.Count>0,followerRects.Count>0,now);
                            Interlocked.Add(ref _draws,(face?3:0)+dynamicRects.Count+followerRects.Count);
                            anyTransform|=target.SetMotion(now);
                        }
                        changed|=anyDraw||anyTransform;
                        if (changed)
                        {
                            long commitStart = Stopwatch.GetTimestamp();
                            if(TryCompositionFrame(composition,out var phaseStats,out long phasePeriod))
                            {
                                double phase=((commitStart-phaseStats.NextEstimatedFrameTime)%(double)phasePeriod+phasePeriod)%phasePeriod;
                                phaseTimes.Add(phase*1000.0/Stopwatch.Frequency);
                            }
                            else intervalStatsUnavailable++;
                            composition.Commit().CheckError();
                            commitTimes.Add(Milliseconds(commitStart, Stopwatch.GetTimestamp()));
                            Interlocked.Increment(ref _commits);
                            intervalCommits++;
                            if (!anyDraw && anyTransform) intervalTransformOnly++;
                            pendingCommit = true;
                            if (!signaled && targets.Count > 0) { signaled = true; _ready(null); }
                        }
                        else if (targets.Count > 0) intervalSkips++;
                        nextAllowed=Stopwatch.GetTimestamp()+Stopwatch.Frequency/72;alignedTick=0;
                    }
                    long summaryNow = Stopwatch.GetTimestamp();
                    double summarySeconds = (summaryNow-summaryAt)/(double)Stopwatch.Frequency;
                    if (summarySeconds >= 10)
                    {
                        _report(FormattableString.Invariant(
                            $"summary seconds={summarySeconds:F2} tickMode=aligned commits={intervalCommits} commitsPerSecond={intervalCommits/summarySeconds:F2} draws={intervalDraws} skipped={intervalSkips} dirtyPixels={intervalDirtyPixels} dirtyPixelsPerCommit={(intervalCommits==0?0:intervalDirtyPixels/intervalCommits)} transformOnly={intervalTransformOnly} beginP50={Percentile(beginTimes,.5):F3} beginP95={Percentile(beginTimes,.95):F3} drawP50={Percentile(drawTimes,.5):F3} drawP95={Percentile(drawTimes,.95):F3} endP50={Percentile(endTimes,.5):F3} endP95={Percentile(endTimes,.95):F3} commitP50={Percentile(commitTimes,.5):F3} commitP95={Percentile(commitTimes,.95):F3} waitP50={Percentile(waitTimes,.5):F3} waitP95={Percentile(waitTimes,.95):F3} phaseP05={Percentile(phaseTimes,.05):F3} phaseP50={Percentile(phaseTimes,.5):F3} phaseP95={Percentile(phaseTimes,.95):F3} phaseSamples={phaseTimes.Count} frameStatsUnavailable={intervalStatsUnavailable}"));
                        summaryAt=summaryNow; intervalCommits=intervalDraws=intervalSkips=intervalDirtyPixels=intervalTransformOnly=0;
                        intervalStatsUnavailable=0;beginTimes.Clear(); drawTimes.Clear(); endTimes.Clear(); commitTimes.Clear(); waitTimes.Clear();phaseTimes.Clear();
                    }
                    System.Windows.Forms.Application.DoEvents();
                    int wait = targets.Count == 0 ? 1000
                        : Math.Max(1,(int)Math.Ceiling((alignedTick-Stopwatch.GetTimestamp())*1000.0/Stopwatch.Frequency));
                    WaitHandle.WaitAny(handles, Math.Min(wait, 1000));
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                foreach (var target in targets.Values) target.Dispose();
                for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
                if (failure != null) _ready(failure);
            }
        }

        private static double Milliseconds(long start, long end) => (end-start)*1000.0/Stopwatch.Frequency;

        private static bool TryCompositionFrame(IDCompositionDevice composition,out Vortice.DirectComposition.FrameStatistics statistics,out long period)
        {
            var result=composition.GetFrameStatistics(out statistics);
            period=0;
            if(!result.Success || statistics.TimeFrequency<=0 || statistics.NextEstimatedFrameTime<=0
                || statistics.CurrentCompositionRate.Numerator<=0 || statistics.CurrentCompositionRate.Denominator<=0)return false;
            double rate=statistics.CurrentCompositionRate.Numerator/(double)statistics.CurrentCompositionRate.Denominator;
            if(rate<30||rate>360)return false;
            period=(long)Math.Round(Stopwatch.Frequency/rate);
            return period>0;
        }

        private static double Percentile(List<double> values, double fraction)
        {
            if (values.Count == 0) return 0;
            var sorted = values.ToArray(); Array.Sort(sorted);
            return sorted[Math.Min(sorted.Length-1, (int)Math.Ceiling(fraction*sorted.Length)-1)];
        }

        private static void Record(DrawTiming timing, List<double> begin, List<double> draw, List<double> end)
        { begin.Add(timing.Begin); draw.Add(timing.Draw); end.Add(timing.End); }

        private static DrawTiming Draw(IDCompositionSurface surface, Vortice.RawRect? rect,
            CompositionAvanteHud hud, ID2D1DeviceContext1 dc, HudLayer layer, long now,int originX=0,int originY=0)
        {
            long beginStart = Stopwatch.GetTimestamp();
            using var dxgiSurface = surface.BeginDraw<IDXGISurface>(rect, out Int2 offset);
            long beginEnd = Stopwatch.GetTimestamp();
            long drawEnd = beginEnd;
            try
            {
                using var bitmap = dc.CreateBitmapFromDxgiSurface(dxgiSurface,
                    new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
                        BitmapOptions.Target | BitmapOptions.CannotDraw));
                dc.Target = bitmap;
                dc.SetDpi(96, 96);
                dc.BeginDraw();
                // updateOffset points to the requested rectangle's top-left on
                // the returned DXGI surface, which can be elsewhere in its atlas.
                dc.Transform = Matrix3x2.CreateTranslation(
                    offset.X - (rect?.Left ?? 0) - originX, offset.Y - (rect?.Top ?? 0) - originY);
                if (rect.HasValue)
                    dc.PushAxisAlignedClip(new Vortice.RawRectF(rect.Value.Left+originX, rect.Value.Top+originY,
                        rect.Value.Right+originX, rect.Value.Bottom+originY), AntialiasMode.Aliased);
                dc.Clear(new Color4(0, 0, 0, 0));
                switch(layer)
                {
                    case HudLayer.Face: hud.DrawFace(); break;
                    case HudLayer.Warning: hud.DrawWarning(); break;
                    case HudLayer.Follower: hud.DrawFollower(now); break;
                    case HudLayer.Needle: hud.DrawNeedle(); break;
                    default: hud.DrawDynamic(); break;
                }
                if (rect.HasValue) dc.PopAxisAlignedClip();
                dc.EndDraw().CheckError();
                drawEnd = Stopwatch.GetTimestamp();
                dc.Target = null;
            }
            finally { dc.Target = null; surface.EndDraw().CheckError(); }
            return new DrawTiming(Milliseconds(beginStart,beginEnd), Milliseconds(beginEnd,drawEnd), Milliseconds(drawEnd,Stopwatch.GetTimestamp()));
        }

        public void Dispose()
        {
            lock (_gate) { if (_disposed) return; _disposed = true; _stop.Set(); }
            // Never join on the UI/SHM/recording path: GPU or composition may block.
        }
    }
}
