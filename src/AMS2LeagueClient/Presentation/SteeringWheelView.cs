using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public sealed class SteeringWheelView : FrameworkElement
    {
        private DrivingTelemetrySample? _sample;
        private readonly RotateTransform _rotation = new RotateTransform();
        private static readonly WeakReference<BitmapImage> SharedWheel = new WeakReference<BitmapImage>(null!);
        private readonly BitmapImage Wheel = GetWheel();
        private readonly HudTargetMotion _motion;
        private string? _angleText;
        private FormattedText? _text;
        private double _textDpi;
        private static BitmapImage GetWheel()
        { if (SharedWheel.TryGetTarget(out var image)) return image; image = LoadWheel(); SharedWheel.SetTarget(image); return image; }
        private readonly Typeface _font = new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        public double RotationRange { get; set; } = 900;
        public SteeringWheelView()
        {
            _motion = new HudTargetMotion(35, angle => _rotation.Angle = angle, this);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            IsVisibleChanged += (_, __) => { if (!IsVisible) _motion.Stop(); };
            Unloaded += (_, __) => _motion.Stop();
        }
        private static BitmapImage LoadWheel()
        {
            var image = new BitmapImage();
            image.BeginInit(); image.UriSource = new Uri("pack://application:,,,/AMS2LeagueClient;component/Assets/Hud/steering-wheel.png");
            image.DecodePixelWidth = 256; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
            return image;
        }
        public void SetSample(DrivingTelemetrySample? sample)
        {
            double to = sample?.SteeringDegrees(RotationRange) ?? 0;
            bool continuous = sample != null && _sample != null && sample.Generation == _sample.Generation
                && sample.ParticipantIndex == _sample.ParticipantIndex && sample.CapturedAt >= _sample.CapturedAt
                && (sample.CapturedAt - _sample.CapturedAt).TotalSeconds <= 1;
            _sample = sample;
            _motion.Set(to, IsVisible && continuous);
            string text = (sample?.SteeringDegrees(RotationRange)?.ToString("0", CultureInfo.InvariantCulture) ?? "—") + "°";
            if (text != _angleText) { _angleText = text; _text = null; InvalidateVisual(); }
        }
        protected override void OnRender(DrawingContext drawing)
        {
            double? angle = _sample?.SteeringDegrees(RotationRange);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (_text == null || dpi != _textDpi)
            { _textDpi = dpi; _text = new FormattedText(angle.HasValue ? angle.Value.ToString("0", CultureInfo.InvariantCulture) + "°" : "—°",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _font, 20, Brushes.White, dpi); }
            var text = _text;
            const double gap = 2;
            double radius = Math.Max(0, Math.Min(ActualWidth - 10, ActualHeight - text.Height - gap) / 2);
            double top = Math.Max(0, (ActualHeight - radius * 2 - gap - text.Height) / 2);
            Point center = new Point(ActualWidth / 2, top + radius);
            _rotation.CenterX = center.X; _rotation.CenterY = center.Y;
            drawing.PushTransform(_rotation);
            drawing.DrawImage(Wheel, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
            drawing.Pop();
            double textScale = Math.Min(1.15, Math.Max(0, ActualWidth - 4) / text.Width);
            drawing.PushTransform(new ScaleTransform(textScale, 1, ActualWidth / 2, 0));
            drawing.DrawText(text, new Point((ActualWidth - text.Width) / 2, top + radius * 2 + gap));
            drawing.Pop();
        }
    }
}
