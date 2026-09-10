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
        private static readonly BitmapImage Wheel = LoadWheel();
        private readonly Typeface _font = new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        public double RotationRange { get; set; } = 900;
        public SteeringWheelView()
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            IsVisibleChanged += (_, __) => { if (!IsVisible) _rotation.BeginAnimation(RotateTransform.AngleProperty, null); };
            Unloaded += (_, __) => _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
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
            double from = _rotation.Angle, to = sample?.SteeringDegrees(RotationRange) ?? 0;
            bool continuous = sample != null && _sample != null && sample.Generation == _sample.Generation
                && sample.ParticipantIndex == _sample.ParticipantIndex && sample.CapturedAt >= _sample.CapturedAt
                && (sample.CapturedAt - _sample.CapturedAt).TotalSeconds <= 1;
            _sample = sample;
            _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            _rotation.Angle = to;
            if (IsVisible && continuous && Math.Abs(to - from) > .01)
                _rotation.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(35)) { FillBehavior = FillBehavior.Stop });
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext drawing)
        {
            double? angle = _sample?.SteeringDegrees(RotationRange);
            var text = new FormattedText(angle.HasValue ? angle.Value.ToString("0", CultureInfo.InvariantCulture) + "°" : "—°",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _font, 20, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
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
