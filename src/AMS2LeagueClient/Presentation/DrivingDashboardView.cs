using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public sealed class DrivingDashboardView : FrameworkElement
    {
        private DrivingTelemetrySample? _sample;
        private readonly DrawingVisual _inputVisual = new DrawingVisual();
        private readonly MatrixTransform _layoutTransform = new MatrixTransform();
        private static readonly BitmapImage Housing = LoadHousing();
        private static readonly Brush Body = Frozen(new ImageBrush(Housing) { Viewbox = new Rect(.38, .40, .35, .22), ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill });
        private static readonly Brush Dial = Frozen(new SolidColorBrush(Color.FromRgb(40, 43, 43)));
        private readonly Brush _glow = CreateGlow();
        private readonly ScaleTransform _gearMotion = new ScaleTransform(1, 1, 55, 59);
        private int _renderCount;
        private static readonly Pen Rim = Frozen(new Pen(Brushes.DimGray, 3.5));
        private readonly FormattedText?[] _text = new FormattedText?[9];
        private Typeface _speedFont = Font(DrivingHudSettings.DefaultFontName);
        private Typeface _gearFont = Font(DrivingHudSettings.DefaultFontName);
        private double _dpi;
        private double _inputTarget;
        private bool _braking;
        private string _temperature = "노면 —";
        private string _remaining = "남은 —";
                public static readonly DependencyProperty InputLevelProperty = MotionProperty("InputLevel", 0);
        public ScaleTransform GearMotion => _gearMotion;
        public double GearScale => _gearMotion.ScaleX;
        public double ShiftGlow => _glow.Opacity;
        public bool IsShifting => _gearMotion.HasAnimatedProperties || _glow.HasAnimatedProperties;
        public double InputLevel => (double)GetValue(InputLevelProperty);
        public string GearText => _sample?.GearText ?? "—";
        public string SpeedText => _sample?.SpeedKmh?.ToString("0", CultureInfo.InvariantCulture) ?? "—";
        public string RpmText => _sample?.RpmText ?? "—";
        public string PositionText { get; private set; } = "P—";
        public int LitRpmLights => _sample?.Rpm != null && _sample.MaxRpm.HasValue
            ? (int)Math.Clamp(Math.Floor((_sample.Rpm.Value / _sample.MaxRpm.Value - .75) / .20 * 15), 0, 15) : 0;

        public DrivingDashboardView()
        {
            AddVisualChild(_inputVisual); _inputVisual.Transform = _layoutTransform;
            IsVisibleChanged += (_, __) => { if (!IsVisible) ResetMotion(); };
            Unloaded += (_, __) => ResetMotion();
        }

        private static DependencyProperty MotionProperty(string name, double value)
            => DependencyProperty.Register(name, typeof(double), typeof(DrivingDashboardView),
                new FrameworkPropertyMetadata(value, (sender, args) => ((DrivingDashboardView)sender).DrawInputArc()));
        private static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
        private static BitmapImage LoadHousing()
        {
            var image = new BitmapImage();
            image.BeginInit(); image.UriSource = new Uri("pack://application:,,,/AMS2LeagueClient;component/Assets/Hud/dashboard-housing.png");
            image.DecodePixelWidth = 1360; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
            return image;
        }
        private static Brush CreateGlow()
        {
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, .40));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(165, 240, 243, 243), .83));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            brush.Opacity = 0;
            return brush;
        }
        public void ApplySettings(DrivingHudSettings settings)
        {
            _speedFont = Font(settings.SpeedFont); _gearFont = Font(settings.GearFont);
            Array.Clear(_text, 0, _text.Length); InvalidateVisual();
        }
        public void SetSession(double trackTemperature, string remaining)
        {
            string previousTemperature = _temperature, previousRemaining = _remaining;
            _temperature = double.IsFinite(trackTemperature) && trackTemperature >= -60 && trackTemperature <= 100
                ? "노면 " + trackTemperature.ToString("0", CultureInfo.InvariantCulture) + "°C" : "노면 —";
            _remaining = "남은 " + (string.IsNullOrWhiteSpace(remaining) ? "—" : remaining);
            if (previousTemperature != _temperature || previousRemaining != _remaining) InvalidateVisual();
        }
        public void SetSample(DrivingTelemetrySample? sample, string position)
        {
            bool continuous = _sample != null && sample != null && sample.Generation == _sample.Generation
                && sample.ParticipantIndex == _sample.ParticipantIndex
                && sample.CapturedAt >= _sample.CapturedAt && (sample.CapturedAt - _sample.CapturedAt).TotalSeconds <= 1;
            bool shifted = continuous && sample!.Gear.HasValue && _sample!.Gear.HasValue && sample.Gear != _sample.Gear;
            _sample = sample;
            if (sample == null) { _temperature = "노면 —"; _remaining = "남은 —"; }
            PositionText = string.IsNullOrWhiteSpace(position) ? "P—" : position;
            bool priorBraking = _braking;
            _braking = (sample?.Pedals[0] ?? 0) > .01;
            if (priorBraking != _braking) DrawInputArc();
            double input = (_braking ? sample?.Pedals[0] : sample?.Pedals[1]) ?? 0;
            if (!continuous || !IsVisible) { _inputTarget = input; ResetMotion(); }
            else if (Math.Abs(input - _inputTarget) > .005)
            {
                _inputTarget = input;
                double start = InputLevel;
                SetValue(InputLevelProperty, input);
                BeginAnimation(InputLevelProperty, new DoubleAnimation(start, input, TimeSpan.FromMilliseconds(90))
                    { FillBehavior = FillBehavior.Stop });
            }
            if (shifted && IsVisible)
            {
                // Reference motion: smaller incoming digit, a soft pop, and a delayed radial flash.
                // Animation affects display only; captured gear/RPM/input data stays untouched.
                var scale = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                scale.KeyFrames.Add(new DiscreteDoubleKeyFrame(.72, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                scale.Completed += (_, __) =>
                {
                    _gearMotion.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    _gearMotion.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                };
                _gearMotion.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
                _gearMotion.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
                var glow = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                glow.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                glow.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(580))));
                glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400))));
                glow.Completed += (_, __) => _glow.BeginAnimation(Brush.OpacityProperty, null);
                _glow.BeginAnimation(Brush.OpacityProperty, glow);
            }
            AutomationProperties.SetName(this, "기어 " + GearText + ", 속도 " + SpeedText + " km/h, RPM " + RpmText + ", " + PositionText);
            InvalidateVisual();
        }
        private void ResetMotion()
        {
            _gearMotion.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _gearMotion.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _glow.BeginAnimation(Brush.OpacityProperty, null);
            BeginAnimation(InputLevelProperty, null); SetValue(InputLevelProperty, _inputTarget);
        }
        private static Typeface Font(string name) => new Typeface(DrivingNumberView.ResolveFont(name), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        protected override void OnRender(DrawingContext drawing)
        {
            _renderCount++;
            double scale = Math.Min(ActualWidth / 340, ActualHeight / 120);
            _layoutTransform.Matrix = new Matrix(scale, 0, 0, scale, (ActualWidth - 340 * scale) / 2, (ActualHeight - 120 * scale) / 2);
            drawing.PushTransform(_layoutTransform);
            drawing.DrawRoundedRectangle(Body, new Pen(Brushes.DimGray, .8), new Rect(47, 18, 282, 82), 41, 41);
            var center = new Point(55, 59);
            drawing.DrawEllipse(Dial, Rim, center, 43, 43);
            drawing.DrawEllipse(_glow, null, center, 45, 45);
            for (int i = 0; i < 15; i++)
            {
                Brush color = i >= LitRpmLights ? Brushes.Gray : i < 10 ? Brushes.LightGreen : i < 13 ? Brushes.Gold : Brushes.OrangeRed;
                drawing.DrawEllipse(color, null, new Point(112 + i * 11, 33), 3.5, 3.5);
            }
            drawing.PushTransform(_gearMotion);
            Text(drawing, 0, GearText, 30, 55, 59, Brushes.White, _gearFont, true);
            drawing.Pop();
            Text(drawing, 1, "km/h", 12, 112, 49, Brushes.LightGray, _speedFont);
            Text(drawing, 2, "RPM", 12, 163, 49, Brushes.LightGray, _speedFont);
            Text(drawing, 3, SpeedText, 18, 112, 65, Brushes.White, _speedFont);
            Text(drawing, 4, RpmText, 18, 163, 65, Brushes.White, _speedFont);
            Text(drawing, 5, PositionText, 26, 285, 64, Brushes.White, _speedFont, true);
            drawing.DrawRoundedRectangle(Brushes.DimGray, null, new Rect(47, 99, 224, 19), 8, 8);
            Text(drawing, 6, _temperature, 11, 62, 101, Brushes.White, _speedFont);
            Text(drawing, 7, _remaining, 11, 177, 101, Brushes.White, _speedFont);
            drawing.Pop();
        }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? _inputVisual : throw new ArgumentOutOfRangeException(nameof(index));
        private void DrawInputArc()
        {
            using var drawing = _inputVisual.RenderOpen();
            if (InputLevel > .001)
            {
                double sweep = 300 * Math.Clamp(InputLevel, 0, 1);
                Point ArcPoint(double degrees) => new Point(55 + 43 * Math.Cos(degrees * Math.PI / 180), 59 + 43 * Math.Sin(degrees * Math.PI / 180));
                var arc = new StreamGeometry();
                using (var context = arc.Open())
                {
                    context.BeginFigure(ArcPoint(150), false, false);
                    context.ArcTo(ArcPoint(150 + sweep), new Size(43, 43), 0, sweep > 180, SweepDirection.Clockwise, true, false);
                }
                arc.Freeze();
                drawing.DrawGeometry(null, new Pen(_braking ? Brushes.OrangeRed : Brushes.LimeGreen, 3.5), arc);
            }
        }
        private void Text(DrawingContext drawing, int slot, string value, double size, double x, double y, Brush color, Typeface font, bool centered = false)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (_dpi != dpi) { Array.Clear(_text, 0, _text.Length); _dpi = dpi; }
            FormattedText? text = _text[slot];
            if (text == null || text.Text != value)
                _text[slot] = text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, size, color, dpi);
            drawing.DrawText(text, new Point(centered ? x - text.Width / 2 : x, centered ? y - text.Height / 2 : y));
        }
    }
}
