using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AMS2LeagueClient.Presentation
{
    /// <summary>
    /// Shared broadcast-style motion primitives for the overlay panels.
    /// Every helper animates a transform or the opacity of the *given* element
    /// only and always ends at the resting value (offset 0, scale 1, opacity 1).
    /// </summary>
    internal static class HudMotion
    {
        public const int AnimationFrameRate = 144;

        public static void ConfigureFrameRate()
        {
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(DoubleAnimation), new PropertyMetadata((int?)AnimationFrameRate));
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(DoubleAnimationUsingKeyFrames), new PropertyMetadata((int?)AnimationFrameRate));
        }

        public static void SlideIn(UIElement element, double fromX, double fromY, int durationMs)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            TranslateTransform transform = EnsureTranslate(element);
            var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            double startX = Math.Abs(transform.X) > 0.01 ? transform.X : fromX;
            double startY = Math.Abs(transform.Y) > 0.01 ? transform.Y : fromY;
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(startX, 0, duration) { EasingFunction = easing });
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startY, 0, duration) { EasingFunction = easing });
            double fromOpacity = DependencyPropertyHelper.GetValueSource(element, UIElement.OpacityProperty).IsAnimated
                && element.Opacity < 0.99 ? element.Opacity : 0;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromOpacity, 1, duration) { EasingFunction = easing });
        }

        public static void SlideOut(UIElement element, double toX, double toY, int durationMs)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            TranslateTransform transform = EnsureTranslate(element);
            var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(toX, duration) { EasingFunction = easing });
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(toY, duration) { EasingFunction = easing });
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = easing });
        }

        /// <summary>Vertical number roll used for lap and position counters.</summary>
        public static void Roll(UIElement element, bool upward, int durationMs)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            TranslateTransform transform = EnsureTranslate(element);
            var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(upward ? 12 : -12, 0, duration) { EasingFunction = easing });
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        }

        /// <summary>Scale pop used for badges, sector splits and lap values.</summary>
        public static void Pop(UIElement element, double fromScale, int durationMs)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            ScaleTransform scale = EnsureScale(element);
            var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var easing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            double startX = Math.Abs(scale.ScaleX - 1) > 0.01 ? scale.ScaleX : fromScale;
            double startY = Math.Abs(scale.ScaleY - 1) > 0.01 ? scale.ScaleY : fromScale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(startX, 1, duration) { EasingFunction = easing });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(startY, 1, duration) { EasingFunction = easing });
        }

        /// <summary>Left-to-right accent sweep that fades out (fastest lap, flag banner).</summary>
        public static void Sweep(UIElement element, double peakOpacity, int sweepMs, int fadeMs)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            ScaleTransform scale = EnsureScale(element);
            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(sweepMs))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var frames = new DoubleAnimationUsingKeyFrames();
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(peakOpacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(peakOpacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(sweepMs))));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(sweepMs + fadeMs)),
                new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            element.BeginAnimation(UIElement.OpacityProperty, frames);
        }

        /// <summary>Grows a bar from its origin (accent bars on cards).</summary>
        public static void GrowY(UIElement element, int durationMs)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            ScaleTransform scale = EnsureScale(element);
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(durationMs))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        public static int ParseLeadingNumber(string? value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int start = 0;
            while (start < value.Length && !char.IsDigit(value[start])) start++;
            int end = start;
            while (end < value.Length && char.IsDigit(value[end])) end++;
            return end > start && int.TryParse(value.Substring(start, end - start), out int parsed) ? parsed : 0;
        }

        // XAML-declared transforms can arrive frozen (template values); replace
        // them with an animatable copy once instead of throwing.
        private static TranslateTransform EnsureTranslate(UIElement element)
        {
            if (element.RenderTransform is TranslateTransform existing && !existing.IsFrozen) return existing;
            TranslateTransform created = element.RenderTransform is TranslateTransform frozen ? frozen.Clone() : new TranslateTransform();
            element.RenderTransform = created;
            return created;
        }

        private static ScaleTransform EnsureScale(UIElement element)
        {
            if (element.RenderTransform is ScaleTransform existing && !existing.IsFrozen) return existing;
            ScaleTransform created = element.RenderTransform is ScaleTransform frozen ? frozen.Clone() : new ScaleTransform(1, 1);
            element.RenderTransform = created;
            return created;
        }
    }
}
