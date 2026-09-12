using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Presentation
{
    // Original HTML coordinates, background and font outlines; no browser or animated image sequence.
    public sealed class AvanteClusterView : FrameworkElement
    {
        private const double Cx = 1024, Cy = 397;
        private static readonly WeakReference<BitmapSource[]> SharedImages = new WeakReference<BitmapSource[]>(null!);
        private readonly BitmapSource[] _images = GetImages();
        private static BitmapSource[] GetImages()
        {
            lock (SharedImages)
            {
                if (SharedImages.TryGetTarget(out BitmapSource[]? images)) return images;
                BitmapImage background = LoadImage();
                images = new[] { background, CreateWarningLights(background, Colors.White),
                    CreateWarningLights(background, Color.FromRgb(255, 192, 48)),
                    CreateWarningLights(background, Color.FromRgb(255, 65, 40)), LoadImage("avante-tickless.png") };
                SharedImages.SetTarget(images);
                return images;
            }
        }
        private static readonly Geometry[] LightClips = CreateLightClips();
        private static Geometry[] CreateLightClips()
        {
            // Boundaries fall in the original PNG's gaps; pairs fill from the bottom toward the top.
            double[] leftEnds = { 169, 194, 219, 244, 270 }, rightStarts = { 372, 347, 322, 296, 270 };
            var clips = new Geometry[5];
            for (int i = 0; i < clips.Length; i++)
            {
                var pair = new GeometryGroup();
                pair.Children.Add(Sector(360, 395, 140, leftEnds[i]));
                pair.Children.Add(Sector(360, 395, rightStarts[i], 400));
                pair.Freeze(); clips[i] = pair;
            }
            return clips;
        }
        private static Int32Rect LightImageCrop(BitmapSource background)
        {
            int left = (int)Math.Floor((Cx - 395) * background.PixelWidth / 2048);
            int right = (int)Math.Ceiling((Cx + 395) * background.PixelWidth / 2048);
            int bottom = (int)Math.Ceiling(623 * background.PixelHeight / 750.0);
            return new Int32Rect(left, 0, right - left, bottom);
        }
        private Rect LightImageBounds()
        {
            var crop = LightImageCrop(_images[0]);
            return new Rect(crop.X * 2048.0 / _images[0].PixelWidth, 0,
                crop.Width * 2048.0 / _images[0].PixelWidth, crop.Height * 750.0 / _images[0].PixelHeight);
        }
        private static BitmapSource CreateWarningLights(BitmapSource background, Color color)
        {
            Int32Rect crop = LightImageCrop(background);
            var source = new FormatConvertedBitmap(new CroppedBitmap(background, crop), PixelFormats.Bgra32, null, 0);
            int width = source.PixelWidth, height = source.PixelHeight;
            var raw = new byte[width * height * 4]; source.CopyPixels(raw, width * 4, 0);
            var output = new byte[raw.Length];
            // Trace the original chrome highlight near its measured contour, once per cached bitmap.
            // The local brightness ridge follows source asymmetry instead of clipping a perfect circle.
            var ridge = new double[261];
            for (int angle = 140; angle <= 400; angle++)
            {
                double radians = angle * Math.PI / 180, cosine = Math.Cos(radians), sine = Math.Sin(radians);
                double expected = 173 - 2 * cosine + 5 * sine, best = double.NegativeInfinity, found = expected;
                for (int step = -6; step <= 6; step++)
                {
                    double radius = expected + step * .5;
                    int px = (int)Math.Round((Cx + radius * cosine) * background.PixelWidth / 2048) - crop.X;
                    int py = (int)Math.Round((Cy + radius * sine) * background.PixelHeight / 750) - crop.Y;
                    int index = (py * width + px) * 4;
                    double neutral = Math.Min(raw[index], Math.Min(raw[index + 1], raw[index + 2]));
                    double score = neutral - 25 * (radius - expected) * (radius - expected);
                    if (score > best) { best = score; found = radius; }
                }
                ridge[angle - 140] = found;
            }
            var contour = new double[ridge.Length];
            for (int i = 0; i < ridge.Length; i++)
                contour[i] = (ridge[Math.Max(0,i-2)] + 2*ridge[Math.Max(0,i-1)] + 3*ridge[i]
                    + 2*ridge[Math.Min(ridge.Length-1,i+1)] + ridge[Math.Min(ridge.Length-1,i+2)]) / 9;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                double dx = (x + crop.X + .5) * 2048 / background.PixelWidth - Cx, dy = (y + crop.Y + .5) * 750 / background.PixelHeight - Cy;
                double radiusSquared = dx * dx + dy * dy;
                bool inner = radiusSquared >= 160 * 160 && radiusSquared <= 187 * 187;
                double coverage = 1;
                if (inner)
                {
                    double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                    if (angle < 140) angle += 360;
                    if (angle > 400) continue;
                    double position = angle - 140; int i = (int)position;
                    double radius = contour[i] + (contour[Math.Min(i+1,contour.Length-1)] - contour[i]) * (position-i);
                    coverage = Math.Clamp(2.2 - Math.Abs(Math.Sqrt(radiusSquared) - radius), 0, 1);
                    if (coverage == 0) continue;
                }
                else if (radiusSquared < 365 * 365 || radiusSquared > 392 * 392) continue;
                int p = (y * width + x) * 4;
                int high = Math.Max(raw[p], Math.Max(raw[p+1], raw[p+2])), low = Math.Min(raw[p], Math.Min(raw[p+1], raw[p+2]));
                // Follow source chrome pixels, not a circle painted over the blue texture.
                if (!inner && (high - low > 75 || high < 20)) continue;
                double alpha = raw[p+3] / 255.0 * coverage * (inner ? 1 : Math.Min(1, high / 60.0)), light = .65 + high / 255.0 * .35;
                output[p] = (byte)(color.B * light * alpha); output[p+1] = (byte)(color.G * light * alpha);
                output[p+2] = (byte)(color.R * light * alpha); output[p+3] = (byte)(255 * alpha);
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, output, width * 4);
            bitmap.Freeze(); return bitmap;
        }
        private static readonly Typeface DisplayFont = Font("Display", "AvanteN Display");
        private static readonly Typeface UiFont = Font("UI", "AvanteN UI");
        private static readonly Typeface ReadoutFont = Font("Readout", "AvanteN Readout");
        private static readonly Brush Digits = Gradient("#FFFFFF", "#BAE6FF");
        private static readonly Brush GearRim = Gradient("#FFF5BC", "#718AAF");
        private static readonly Brush NumberRim = Gradient("#E4FAFF", "#496BBA");
        private static readonly Pen ShadowPen = FrozenPen(Brushes.MidnightBlue, 1.8);
        private static readonly Pen GearPen = FrozenPen(GearRim, 1.1), DigitPen = FrozenPen(NumberRim, 1.1);
        private static Pen FrozenPen(Brush brush, double thickness) { var pen = new Pen(brush, thickness); pen.Freeze(); return pen; }
        private int _faceRasterizations;
        private readonly Dictionary<(string, double, bool, bool, bool), (Geometry Shape, Rect Bounds, Geometry? Shadow, Geometry? Rim)> _glyphs = new Dictionary<(string, double, bool, bool, bool), (Geometry, Rect, Geometry?, Geometry?)>();
        private readonly Dictionary<(char, double, bool, bool), (Geometry Shape, double Advance, Rect Bounds)> _characters = new Dictionary<(char, double, bool, bool), (Geometry, double, Rect)>();
        private readonly Dictionary<(string, bool), double> _textWidths = new Dictionary<(string, bool), double>();
        private string? _valuesKey, _gearKey, _speedKey;
        private readonly DrawingVisual _gearValues = new DrawingVisual(), _speedValues = new DrawingVisual();
        private int _gearRebuilds, _speedRebuilds, _statusRebuilds;
        private (int Band, int Pairs, bool HasRpm)? _motionKey;
        private readonly HudTargetMotion _rpmMotion;
        private readonly DrawingVisual _follower = new DrawingVisual();
        private readonly PathGeometry _followerPath = new PathGeometry();
        private readonly ArcSegment _outerArc = new ArcSegment { Size = new Size(348, 348), SweepDirection = SweepDirection.Clockwise };
        private readonly LineSegment _endLine = new LineSegment();
        private readonly ArcSegment _innerArc = new ArcSegment { Size = new Size(304, 304), SweepDirection = SweepDirection.Counterclockwise };
        private readonly RadialGradientBrush _followerBrush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = new Point(Cx,Cy), GradientOrigin = new Point(Cx,Cy), RadiusX = 348, RadiusY = 348 };
        // Original HTML sweep gradient, with the same light/dark structure in warning colours.
        private static readonly Color[][] FollowerColors = {
            new[] { Color.FromArgb(0,109,143,163), Color.FromArgb(92,137,169,185), Color.FromArgb(230,199,218,224), Color.FromRgb(237,248,248), Color.FromRgb(163,191,204) },
            new[] { Color.FromArgb(0,255,158,14), Color.FromArgb(92,239,133,8), Color.FromArgb(230,255,190,40), Color.FromRgb(255,239,153), Color.FromRgb(190,110,12) },
            new[] { Color.FromArgb(0,255,48,25), Color.FromArgb(92,207,28,15), Color.FromArgb(230,255,76,42), Color.FromRgb(255,212,185), Color.FromRgb(172,37,24) }
        };
        private static readonly Geometry MotionClip = new RectangleGeometry(new Rect(0, 0, 2048, 623));
        private static readonly Geometry InnerRing = Sector(160, 187, 140, 400);
        private readonly DrawingVisual _rpmNumbers = new DrawingVisual();
        private readonly List<ScaleTransform> _numberScales = new List<ScaleTransform>();
        private int _activeNumber = -1, _pulseNumber = -1;
        private double? _previousNumberRpm;
        private double _numberPulseAmount;
        private readonly HudTargetMotion _numberPulse;
        public static double NumberEmphasis(double rpm, int numeral) => double.IsFinite(rpm) ? 1 + .2 * Math.Max(0, 1 - Math.Abs(rpm - numeral * 1000.0) / 100) : 1;
        private void ApplyNumberScale(int index)
        {
            if (index < 0 || index >= _numberScales.Count) return;
            double scale = _sample?.Rpm != null && _previousNumberRpm is double rpm
                ? Math.Max(NumberEmphasis(rpm, index), index == _pulseNumber ? 1 + .2 * _numberPulseAmount : 1) : 1;
            if (_numberScales[index].ScaleX != scale) _numberScales[index].ScaleX = _numberScales[index].ScaleY = scale;
        }
        private void UpdateNumberEmphasis(double rpm)
        {
            double? previous = _previousNumberRpm;
            _previousNumberRpm = _sample?.Rpm != null && double.IsFinite(rpm) && rpm >= 0 ? (double?)rpm : null;
            int next = _previousNumberRpm.HasValue && rpm <= _rpmScale.Maximum ? (int)Math.Round(rpm / 1000) : -1;
            if (next >= _numberScales.Count) next = -1;
            if (_activeNumber != next) ApplyNumberScale(_activeNumber);
            _activeNumber = next;
            // A fast frame can skip the entire +/-100 RPM band. Preserve a brief visible
            // peak for that crossed numeral; slow travel keeps the exact RPM-based scaling.
            if (IsVisible && previous is double from && _previousNumberRpm.HasValue
                && Math.Abs(rpm - from) > 100 && Math.Abs(rpm - from) < 2000)
            {
                int crossed = (int)(rpm > from ? Math.Floor(rpm / 1000) : Math.Ceiling(rpm / 1000));
                double boundary = crossed * 1000.0;
                if (crossed >= 1 && crossed < _numberScales.Count
                    && (from < boundary && rpm >= boundary || from > boundary && rpm <= boundary)
                    && NumberEmphasis(rpm, crossed) < 1.15)
                {
                    _numberPulse.Set(0, false);
                    _pulseNumber = crossed;
                    _numberPulse.Set(1, false);
                    _numberPulse.Set(0, true);
                }
            }
            ApplyNumberScale(next);
            if (_pulseNumber != next) ApplyNumberScale(_pulseNumber);
        }
        private readonly DrawingVisual _motion = new DrawingVisual(), _scale = new DrawingVisual(), _values = new DrawingVisual(), _needle = new DrawingVisual();
        private bool? _needleVisible;
        private readonly MatrixTransform _transform = new MatrixTransform();
        private readonly RotateTransform _rotation = new RotateTransform(150, Cx, Cy);
        private DrivingTelemetrySample? _sample;
        private TelemetrySnapshot? _session;
        private AvanteRpmScale _rpmScale = AvanteRpmScale.Resolve(null);
        private double? _engineMaximum;
        private string _vehicleName = "", _profileVehicleName = "";
        private int? _generation, _participant;
        private DrivingHudSettings _settings = new DrivingHudSettings();
        private readonly DrawingVisual _redZone = new DrawingVisual();
        public AvanteRpmScale RpmScale => _rpmScale;
        public int StaticFaceBuilds => _faceRasterizations;
        // The 348..350 outline borders the face/sectors (r=348), inside the separate LEDs (r>=360).
        public const double TickOuterRadius = 349, MajorTickInnerRadius = 331, MinorTickInnerRadius = 340;
        private static readonly Pen MajorTickPen = FrozenPen(Brushes.AliceBlue, 3.2), MinorTickPen = FrozenPen(Brushes.AliceBlue, 1.7);
        private static readonly Geometry TickRing = Sector(315, 362, 140, 400);
        private static readonly Geometry OriginalWithoutTicks = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, 2048, 750)), new CombinedGeometry(GeometryCombineMode.Intersect, TickRing, MotionClip));
        private bool _preview;
        private long _flashStarted;
        private bool _flashSubscribed, _flashMonitorClock, _stoppingMotion;
        private double _flashOpacity = 1;
        public bool RedFlashOn => _flashOpacity >= .56;
        public bool IsRedFlashing => _flashSubscribed;
        private static readonly DependencyProperty RpmPositionProperty = DependencyProperty.Register("RpmPosition", typeof(double),
            typeof(AvanteClusterView), new PropertyMetadata(0.0, (s, e) => ((AvanteClusterView)s).DrawMotion()));
        public bool Expanded { get; }
        public string GearText => _sample?.Gear == 0 ? "N" : _sample?.Gear == -1 ? "R" : _sample?.GearText ?? "—";
        public string SpeedText => _sample?.SpeedKmh?.ToString("0", CultureInfo.InvariantCulture) ?? "—";
        public int RpmBand => _rpmScale.Band(_sample?.Rpm ?? 0);
        public double NeedleAngle => _rotation.Angle;
        public AvanteClusterView(bool expanded = false)
        {
            Expanded = expanded;
            _rpmMotion = new HudTargetMotion(65, rpm => SetValue(RpmPositionProperty, rpm), this);
            _numberPulse = new HudTargetMotion(100, value => { _numberPulseAmount = value; ApplyNumberScale(_pulseNumber); }, this);
            var figure = new PathFigure { StartPoint = PointAt(348, 150), IsClosed = true, IsFilled = true };
            figure.Segments.Add(_outerArc); figure.Segments.Add(_endLine); figure.Segments.Add(_innerArc);
            _innerArc.Point = PointAt(304, 150); _followerPath.Figures.Add(figure);
            foreach (double offset in new[] { 304.0/348, (304+44*.22)/348, (304+44*.63)/348, (304+44*.93)/348, 1.0 }) _followerBrush.GradientStops.Add(new GradientStop(Colors.White, offset));
            AddVisualChild(_redZone); _redZone.Transform = _transform;
            AddVisualChild(_follower); _follower.Transform = _transform;
            using (var drawing = _follower.RenderOpen()) { drawing.PushClip(MotionClip); drawing.DrawGeometry(_followerBrush, null, _followerPath); drawing.Pop(); }
            foreach (var visual in new[] { _motion, _scale, _values, _rpmNumbers, _needle, _gearValues, _speedValues }) { AddVisualChild(visual); visual.Transform = _transform; }

            Loaded += (_, __) => UpdateFlash();
            IsVisibleChanged += (_, __) => { if (!IsVisible) StopMotion(); else { UpdateFlash(); DrawMotion(); } };
            Unloaded += (_, __) => StopMotion();
        }
        protected override int VisualChildrenCount => 9;
        protected override Visual GetVisualChild(int index) => index == 0 ? _redZone : index == 1 ? _follower : index == 2 ? _motion : index == 3 ? _scale : index == 4 ? _values : index == 5 ? _rpmNumbers : index == 6 ? _needle : index == 7 ? _gearValues : index == 8 ? _speedValues : throw new ArgumentOutOfRangeException(nameof(index));
        private static BitmapImage LoadImage(string name = "avante-background.png")
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/AMS2LeagueClient;component/Assets/Hud/" + name)).Stream;
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
            image.Freeze(); return image;
        }
        private static Typeface Font(string file, string family) => new Typeface(new FontFamily(
            new Uri("pack://application:,,,/AMS2LeagueClient;component/"), "./Assets/Fonts/#" + family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static Brush Gradient(string top, string bottom)
        {
            var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 90);
            brush.Freeze(); return brush;
        }
        private static Point PointAt(double radius, double angle) => new Point(Cx + radius * Math.Cos(angle * Math.PI / 180), Cy + radius * Math.Sin(angle * Math.PI / 180));
        private static Geometry Sector(double inner, double outer, double start, double end)
        {
            end = Math.Max(start + .001, end);
            var path = new StreamGeometry();
            using (var g = path.Open())
            {
                g.BeginFigure(PointAt(outer, start), true, true);
                g.ArcTo(PointAt(outer, end), new Size(outer, outer), 0, end - start > 180, SweepDirection.Clockwise, true, false);
                g.LineTo(PointAt(inner, end), true, false);
                g.ArcTo(PointAt(inner, start), new Size(inner, inner), 0, end - start > 180, SweepDirection.Counterclockwise, true, false);
            }
            path.Freeze(); return path;
        }
        private double Angle(double rpm) => _rpmScale.Angle(rpm);
        private void Text(DrawingContext dc, string value, double x, double y, double size, bool styled = false, bool gear = false, Brush? color = null, bool readout = false, bool tabular = false)
        {
            var key = (value, size, styled, readout, tabular);
            if (!_glyphs.TryGetValue(key, out var glyph))
            {
                var group = new GeometryGroup();
                double advance = 0;
                foreach (char c in value)
                {
                    var characterKey = (c, size, styled, readout);
                    if (!_characters.TryGetValue(characterKey, out var character))
                    {
                        var text = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            styled ? DisplayFont : readout ? ReadoutFont : UiFont, size, Brushes.White, 1);
                        var shape = text.BuildGeometry(new Point()); shape.Freeze();
                        character = (shape, text.WidthIncludingTrailingWhitespace, shape.Bounds);
                        if (_characters.Count >= 512) _characters.Clear();
                        _characters[characterKey] = character;
                    }
                    // Keep the font's fixed advance and height; center each digit's ink in its cell.
                    // Cached character bounds avoid per-frame Bounds/serialization work.
                    double bearing = tabular && c >= '0' && c <= '9' ? (character.Advance - character.Bounds.Width) / 2 - character.Bounds.X : 0;
                    var placed = new GeometryGroup { Transform = new TranslateTransform(advance + bearing, 0) };
                    placed.Children.Add(character.Shape); group.Children.Add(placed);
                    advance += character.Advance - (styled ? size * .025 : 0);
                }
                if (styled) group.Transform = new SkewTransform(-10, 0);
                group.Freeze();
                var glyphShape = RetainedGeometry.Compile(group);
                Geometry? shadow = styled ? RetainedGeometry.Compile(glyphShape.GetWidenedPathGeometry(ShadowPen)) : null;
                Geometry? rim = styled ? RetainedGeometry.Compile(glyphShape.GetWidenedPathGeometry(DigitPen)) : null;
                glyph = (glyphShape, glyphShape.Bounds, shadow, rim);
                if (_glyphs.Count > 256) _glyphs.Clear();
                _glyphs[key] = glyph;
            }
            Geometry geometry = glyph.Shape; Rect b = glyph.Bounds;
            dc.PushTransform(new TranslateTransform(x - b.X - b.Width / 2, y - b.Y - b.Height / 2));
            if (styled)
            {
                dc.PushTransform(new TranslateTransform(0, 2));
                dc.DrawGeometry(Brushes.MidnightBlue, null, geometry);
                dc.DrawGeometry(Brushes.MidnightBlue, null, glyph.Shadow); dc.Pop();
            }
            dc.DrawGeometry(color ?? (styled ? Digits : Brushes.AliceBlue), null, geometry);
            if (styled) dc.DrawGeometry(gear ? GearRim : NumberRim, null, glyph.Rim);
            dc.Pop();
        }
        public void ApplySettings(DrivingHudSettings settings)
        { _settings = settings.Normalize(); ResolveScale(); DrawMotion(); }
        private void ResolveScale()
        {
            _settings.AvanteVehicles.TryGetValue(_vehicleName, out var calibration);
            var scale = AvanteRpmScale.Resolve(_engineMaximum, calibration, _profileVehicleName);
            if (_rpmScale == scale) return;
            StopFlash(true);
            _rpmScale = scale; _motionKey = null; InvalidateVisual();
        }
        public void SetSession(TelemetrySnapshot? session)
        {
            string vehicle = session?.RootCarName ?? "";
            string profileVehicle = AvanteRpmScale.ProfileVehicleName(session);
            if (session == null || vehicle != _vehicleName || profileVehicle != _profileVehicleName)
            {
                _engineMaximum = null; _sample = null; _rpmMotion.Set(0, false); DrawNeedle();
                if (session == null) _generation = _participant = null;
            }
            _vehicleName = vehicle; _profileVehicleName = profileVehicle; _session = session;
            double? engine = session?.ViewedVehicleTelemetry?.MaxRpm;
            if (AvanteRpmScale.IsValidEngineMaximum(engine)) _engineMaximum = engine;
            ResolveScale(); DrawMotion(); DrawValues();
        }
        public void SetSample(DrivingTelemetrySample? sample, bool preview = false)
        {
            bool continuous = _sample != null && sample != null && _sample.Generation == sample.Generation
                && _sample.ParticipantIndex == sample.ParticipantIndex && sample.CapturedAt >= _sample.CapturedAt
                && (sample.CapturedAt - _sample.CapturedAt).TotalSeconds < 1;
            if (sample != null)
            {
                bool changed = _generation.HasValue && (_generation != sample.Generation || _participant != sample.ParticipantIndex);
                if (changed)
                {
                    // A matching full snapshot may already have refreshed this vehicle's basis.
                    // Do not erase it when the following fast sample has a transient invalid maximum.
                    if (_session == null || _session.ViewedParticipantIndex != sample.ParticipantIndex
                        || Math.Abs((_session.CapturedAt - sample.CapturedAt).TotalSeconds) > .1)
                    { _engineMaximum = null; _session = null; _vehicleName = _profileVehicleName = ""; }
                }
                _generation = sample.Generation; _participant = sample.ParticipantIndex;
                if (AvanteRpmScale.IsValidEngineMaximum(sample.MaxRpm)) _engineMaximum = sample.MaxRpm;
            }
            if (!continuous) { StopFlash(true); _numberPulse.Set(0, false); _previousNumberRpm = null; }
            _sample = sample; _preview = preview;
            ResolveScale();
            double rpm = sample?.Rpm ?? 0;
            _rpmMotion.Set(rpm, continuous && IsVisible);
            DrawMotion(); DrawValues(); DrawNeedle();
        }
        private void UpdateFlash()
        {
            // Warning eligibility follows observed RPM immediately; the needle keeps its
            // presentation interpolation. Threshold crossings never reset elapsed flash phase.
            bool active = !_stoppingMotion && IsLoaded && MonitorPresentationClock.HasVisibleContent(this)
                && _sample?.Rpm != null && RpmBand == 2;
            if (!active) { StopFlash(); return; }
            if (_flashMonitorClock && !MonitorPresentationClock.CanUse(this)) StopFlash();
            if (!_flashSubscribed)
            {
                if (_flashStarted == 0) _flashStarted = Stopwatch.GetTimestamp();
                _flashSubscribed = true;
                _flashMonitorClock = MonitorPresentationClock.CanUse(this) && MonitorPresentationClock.Subscribe(FlashFrame);
                if (!_flashMonitorClock) CompositionTarget.Rendering += FlashFrame;
            }
            // Five cycles/sec, smoothly 1 -> .12 -> 1. Absolute phase survives brief
            // threshold exits; hiding, discontinuities and scale changes reset it.
            double phase = Stopwatch.GetElapsedTime(_flashStarted).TotalSeconds % .2 / .2;
            SetFlashOpacity(.12 + .88 * (.5 + .5 * Math.Cos(2 * Math.PI * phase)));
        }
        private void FlashFrame(object? sender, EventArgs args) => UpdateFlash();
        private void SetFlashOpacity(double opacity)
        {
            _flashOpacity = opacity;
            _follower.Opacity = _sample?.Rpm != null ? opacity : 0;
            _motion.Opacity = _redZone.Opacity = opacity;
        }
        private void StopFlash(bool resetPhase = false)
        {
            if (_flashSubscribed)
            {
                if (_flashMonitorClock) MonitorPresentationClock.Unsubscribe(FlashFrame);
                else CompositionTarget.Rendering -= FlashFrame;
            }
            _flashSubscribed = _flashMonitorClock = false;
            if (resetPhase) _flashStarted = 0;
            SetFlashOpacity(1);
        }
        private void StopMotion()
        {
            _stoppingMotion = true;
            StopFlash(true);
            _numberPulse.Set(0, false); _previousNumberRpm = null;
            _rpmMotion.Set(_sample?.Rpm ?? 0, false);
            _stoppingMotion = false;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            double width = Expanded ? 2048 : 908, left = Expanded ? 0 : 570;
            double scale = Math.Min(finalSize.Width / width, finalSize.Height / 750);
            _transform.Matrix = new Matrix(scale, 0, 0, scale, (finalSize.Width - width * scale) / 2 - left * scale, (finalSize.Height - 750 * scale) / 2);
            Geometry outline;
            if (Expanded) outline = new RectangleGeometry(new Rect(0, 0, 2048, 750));
            else
            {
                var dial = new CombinedGeometry(GeometryCombineMode.Intersect,
                    new EllipseGeometry(new Point(Cx, Cy), 392, 392), new RectangleGeometry(new Rect(570, 0, 908, 623)));
                var footer = Geometry.Parse("M570,665 L645,632 Q665,623 700,623 L1348,623 Q1383,623 1403,632 L1478,665 L1478,750 L570,750 Z");
                outline = new CombinedGeometry(GeometryCombineMode.Union, dial, footer);
            }
            outline.Transform = _transform.Clone(); Clip = outline;
            return base.ArrangeOverride(finalSize);
        }
        // Rasterize only the fixed face at the current physical display size. The original
        // source remains intact for resize/DPI/vehicle-scale changes; moving layers stay separate.
        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            var face = new DrawingVisual();
            using (var drawing = face.RenderOpen()) DrawFace(drawing);
            dc.DrawImage(Rasterize(face), new Rect(RenderSize));
            _faceRasterizations++;
            _redZone.Transform = _transform; _redZone.Opacity = 1;
            using (var red = _redZone.RenderOpen())
            {
                red.PushClip(MotionClip);
                if (_rpmScale.HasWarningThresholds)
                {
                    // Darken toward the high-RPM end while retaining the source texture.
                    var tint = new LinearGradientBrush(Color.FromArgb(205, 120, 3, 8), Color.FromArgb(225, 38, 0, 3),
                        PointAt(280, Angle(_rpmScale.RedStart)), PointAt(280, Angle(_rpmScale.Maximum))) { MappingMode = BrushMappingMode.Absolute };
                    tint.Freeze();
                    red.DrawGeometry(tint, null, Sector(210,348,Angle(_rpmScale.RedStart),Angle(_rpmScale.Maximum)));
                }
                red.Pop();
            }
            var redBitmap = Rasterize(_redZone); _redZone.Transform = Transform.Identity;
            using (var red = _redZone.RenderOpen()) red.DrawImage(redBitmap, new Rect(RenderSize));
            DrawScale(); DrawMotion(); DrawValues(); DrawNeedle();
        }
        private BitmapSource Rasterize(Visual source)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(ActualWidth*dpi.DpiScaleX)),
                Math.Max(1,(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(source); bitmap.Freeze(); return bitmap;
        }
        private void DrawFace(DrawingContext dc)
        {
            dc.PushTransform(_transform);
            // Replace only the baked tick ring with the cleaned texture, never paint over it.
            // All other source pixels (including the decorative LED rim) retain the original image.
            dc.PushClip(OriginalWithoutTicks); dc.DrawImage(_images[0], new Rect(0, 0, 2048, 750)); dc.Pop();
            dc.PushClip(MotionClip); dc.PushClip(TickRing);
            dc.DrawImage(_images[4], new Rect(0, 0, 2048, 750)); dc.Pop(); dc.Pop();
            dc.PushClip(MotionClip);
            if (_rpmScale.HasWarningThresholds) dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(90, 248, 212, 111)), null,
                Sector(210, 348, Angle(_rpmScale.YellowStart), Angle(_rpmScale.RedStart)));
            dc.Pop();
            if (Expanded)
            {
                Text(dc, "오일 온도", 319, 206, 37); Text(dc, "냉각수 온도", 319, 418, 37);
                Text(dc, "터보", 1724, 206, 37); Text(dc, "토크", 1724, 418, 37);
                Text(dc, "E", 132, 635, 38); Text(dc, "F", 478, 635, 38);
                Text(dc, "C", 1587, 635, 38); Text(dc, "H", 1933, 635, 38);
            }
            dc.Pop();
        }
        private void DrawMotion()
        {
            double rpm = (double)GetValue(RpmPositionProperty), angle = Angle(rpm);
            _rotation.Angle = angle;
            UpdateNumberEmphasis(rpm);
            _outerArc.Point = PointAt(348, angle); _endLine.Point = PointAt(304, angle);
            _outerArc.IsLargeArc = _innerArc.IsLargeArc = angle - 150 > 180;
            int band = _rpmScale.Band(rpm);
            int pairs = _sample?.Rpm == null ? 0 : _rpmScale.LitPairs(_sample.Rpm.Value);
            UpdateFlash();
            var key = (band, pairs, _sample?.Rpm != null);
            if (_motionKey == key) return;
            _motionKey = key;
            for (int i = 0; i < _followerBrush.GradientStops.Count; i++)
                _followerBrush.GradientStops[i].Color = FollowerColors[band][i];
            using (var dc = _motion.RenderOpen())
            {
                dc.PushClip(MotionClip);
                if (band > 0) { dc.PushClip(InnerRing); dc.DrawImage(_images[band + 1], LightImageBounds()); dc.Pop(); }
                if (pairs > 0) { dc.PushClip(LightClips[pairs - 1]); dc.DrawImage(_images[band + 1], LightImageBounds()); dc.Pop(); }
                dc.Pop();
            }
        }
        private void DrawScale()
        {
            _scale.Transform = _transform;
            _numberPulse.Set(0, false); _pulseNumber = -1; _previousNumberRpm = null;
            _rpmNumbers.Children.Clear(); _numberScales.Clear(); _activeNumber = -1;
            using (var dc = _scale.RenderOpen())
            {
                dc.PushClip(MotionClip);
                dc.DrawGeometry(Brushes.AliceBlue, null, Sector(TickOuterRadius - 1, TickOuterRadius + 1, Angle(0), Angle(_rpmScale.Maximum)));
                if (_rpmScale.HasWarningThresholds)
                    dc.DrawGeometry(Brushes.Firebrick, null, Sector(TickOuterRadius - 11, TickOuterRadius - 3, Angle(_rpmScale.RedStart), Angle(_rpmScale.Maximum)));
                for (double tick = 0; tick <= _rpmScale.Maximum; tick += 200)
                {
                    bool major = tick % 1000 == 0;
                    dc.DrawLine(major ? MajorTickPen : MinorTickPen, PointAt(major ? MajorTickInnerRadius : MinorTickInnerRadius, Angle(tick)), PointAt(TickOuterRadius, Angle(tick)));
                }
                double count = _rpmScale.Maximum / 1000;
                double size = Math.Min(60, 276 * 240 / count * Math.PI / 180 / (count >= 10 ? 1.5 : .8));
                for (int i = 0; i <= count; i++)
                {
                    var p = PointAt(276, Angle(i * 1000));
                    var transform = new ScaleTransform(1, 1, p.X, p.Y);
                    var numeral = new DrawingVisual { Transform = transform };
                    using (var text = numeral.RenderOpen()) Text(text, i.ToString(), p.X, p.Y, size, true);
                    _rpmNumbers.Children.Add(numeral); _numberScales.Add(transform);
                }
                dc.Pop();
                Text(dc, "km/h", 1168, 589, 17); Text(dc, "x1000", 805, 591, 23); Text(dc, "rpm", 805, 613, 20);
            }
            var bitmap = Rasterize(_scale);
            _scale.Transform = Transform.Identity;
            using (var dc = _scale.RenderOpen()) dc.DrawImage(bitmap,new Rect(RenderSize));
        }
        private void DrawNeedle()
        {
            bool visible = _sample?.Rpm != null;
            if (_needleVisible == visible) return;
            _needleVisible = visible;
            using (var dc = _needle.RenderOpen())
            {
                if (_sample?.Rpm == null) return;
                dc.PushTransform(_rotation);
                var path = Geometry.Parse("M 1138,397 Q 1146,389 1170,390 L 1377,395.9 1377,398.1 1170,404 Q 1146,405 1138,397 Z");
                dc.DrawGeometry(Gradient("#FFFDF2", "#FF713B"), new Pen(Brushes.OrangeRed, 2), path);
                dc.Pop();
            }
        }
        private static string Value(double? n, double low, double high, string format = "0") => n.HasValue && double.IsFinite(n.Value) && n >= low && n <= high ? n.Value.ToString(format, CultureInfo.InvariantCulture) : "—";
        private void DrawValues()
        {
            var valid = _sample != null && _session != null && _sample.ParticipantIndex == _session.ViewedParticipantIndex
                && Math.Abs((_sample.CapturedAt - _session.CapturedAt).TotalSeconds) <= 1;
            var v = valid ? _session!.ViewedVehicleTelemetry : null;
            // Compare displayed facts, including validity, before replacing the retained drawing.
            string gearText = GearText, speedText = SpeedText;
            if (_gearKey != gearText)
            { _gearKey = gearText; using var dc = _gearValues.RenderOpen(); Text(dc,gearText,Cx,Cy,164,true,true); _gearRebuilds++; }
            if (_speedKey != speedText)
            { _speedKey = speedText; using var dc = _speedValues.RenderOpen(); Text(dc,speedText,Cx,562,108,true,tabular:true); _speedRebuilds++; }
            string key = string.Join("|", _preview, valid,
                Value(valid ? _session!.AmbientTemperature : (double?)null, -80, 80), _sample?.AbsActive, v?.CarFlagsRaw,
                Expanded ? string.Join("|", v?.OilTemperatureCelsius, v?.WaterTemperatureCelsius,
                    v?.EngineTorqueNewtonMetres, v?.FuelLevel, v?.FuelCapacityLitres, v?.OdometerKilometres) : "");
            if (_valuesKey == key) return;
            _valuesKey = key; _statusRebuilds++;
            using (var dc = _values.RenderOpen())
            {
                Text(dc, Value(_preview ? 23 : valid ? _session!.AmbientTemperature : (double?)null, -80, 80) + "°C", 668, 690, 42);
                Status(dc, "ABS", _sample == null ? (bool?)null : _sample.AbsActive, 795);
                Status(dc, "TCS", v != null ? (v.CarFlagsRaw & (1u << 6)) != 0 : _preview ? false : (bool?)null, 992);
                Status(dc, "PIT LIMITER", v != null ? (v.CarFlagsRaw & (1u << 3)) != 0 : _preview ? false : (bool?)null, 1266);
                if (Expanded)
                {
                    Readout(dc, Value(_preview ? 74 : v?.OilTemperatureCelsius, -40, 300), "°C", 319, 297);
                    Readout(dc, Value(_preview ? 89 : v?.WaterTemperatureCelsius, -40, 200), "°C", 319, 510);
                    // DATA_DICTIONARY marks turboBoostPressure unit/scale pending; do not label the raw float as bar.
                    Readout(dc, Value(_preview ? 1.1 : (double?)null, 0, 9, "0.0"), "bar", 1724, 297);
                    Readout(dc, Value(_preview ? 285 : v?.EngineTorqueNewtonMetres, -4000, 4000), "Nm", 1724, 510);
                    // The source PNG has painted gauge levels. Cover them before showing real levels.
                    Gauge(dc, 170, 615, _preview ? .7 : (double?)v?.FuelLevel);
                    Gauge(dc, 1620, 615, null); // No authoritative C/H scale: keep the numeric water temperature above.
                    Text(dc, v != null ? Value(v.FuelLevel * v.FuelCapacityLitres, 0, 2000) + " L" : _preview ? "35 L" : "— L", 275, 700, 45);
                    Text(dc, Value(_preview ? 123 : v?.OdometerKilometres, 0, 9999999) + " km", 1820, 710, 42);
                }
            }
        }
        private void Readout(DrawingContext dc, string number, string unit, double x, double y)
        {
            Text(dc, number, x, y, 140, readout: true);
            Text(dc, unit, x + TextWidth(number, true) / 2 + 28, y + 45, 33);
        }
        private double TextWidth(string value, bool readout)
        {
            var key = (value, readout);
            if (_textWidths.TryGetValue(key, out double width)) return width;
            width = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                readout ? ReadoutFont : UiFont, readout ? 140 : 33, Brushes.White, 1).Width;
            if (_textWidths.Count >= 256) _textWidths.Clear();
            _textWidths[key] = width;
            return width;
        }
        private void Status(DrawingContext dc, string label, bool? active, double x)
        {
            Brush color = active == true ? Brushes.Tomato : active == false ? Brushes.AliceBlue : Brushes.SlateGray;
            const double size = 33;
            double labelWidth = TextWidth(label, false);
            double left = x - (33 + 16 + labelWidth) / 2;
            for (int i = 0; i < 3; i++) dc.DrawRoundedRectangle(color, null, new Rect(left, 673 + i * 10, 33, 5), 2.5, 2.5);
            Text(dc, label, left + 49 + labelWidth / 2, 686, size, false, false, color);
        }
        private static void Gauge(DrawingContext dc, double x, double y, double? level)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(5,15,25)), null, new Rect(x,y,265,27));
            if (level.HasValue && double.IsFinite(level.Value) && level >= 0 && level <= 1)
                dc.DrawRectangle(Gradient("#E4FFFF", "#3BADD1"), null, new Rect(x,y,265*level.Value,27));
        }
    }
}
