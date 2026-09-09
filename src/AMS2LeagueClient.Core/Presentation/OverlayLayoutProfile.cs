using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Presentation
{
    public static class OverlayComponentKeys
    {
        public const string TimingTower = "timingTower";
        public const string RelativeDrivers = "relativeDrivers";
        public const string LapTiming = "lapTiming";
        public const string SessionInfo = "sessionInfo";
        public const string EventCard = "eventCard";
        public const string RaceControl = "raceControl";
        public const string Waiting = "waiting";
        public const string PedalTelemetry = "pedalTelemetry";
        public const string Speed = "speed";
        public const string Gear = "gear";
        public static readonly string[] All =
        {
            TimingTower,
            RelativeDrivers,
            LapTiming,
            SessionInfo,
            EventCard,
            RaceControl,
            Waiting,
            PedalTelemetry,
            Speed,
            Gear
        };
    }

    public sealed class NormalizedOverlayBounds
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public sealed class OverlayLayoutProfile
    {
        public int Schema { get; set; } = 1;
        public VrHudSettings VrHud { get; set; } = new VrHudSettings();
        public DrivingHudSettings DrivingHud { get; set; } = new DrivingHudSettings();
        // Missing in pre-0.4.1 profiles; expand only the saved tower width once.
        public int TowerDesignWidth { get; set; } = 520;
        public Dictionary<string, NormalizedOverlayBounds> Components { get; set; }
            = new Dictionary<string, NormalizedOverlayBounds>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> EnabledComponents { get; set; }
            = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public bool IsEnabled(string component)
            => string.IsNullOrWhiteSpace(component)
                || EnabledComponents == null
                || !EnabledComponents.TryGetValue(component, out bool enabled)
                || enabled;

        public void SetEnabled(string component, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(component)) throw new ArgumentException("Component key is required.", nameof(component));
            EnabledComponents ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            EnabledComponents[component] = enabled;
        }

        public OverlayBounds Resolve(string component, OverlayBounds fallback, int viewportWidth, int viewportHeight)
        {
            ValidateViewport(viewportWidth, viewportHeight);
            if (string.IsNullOrWhiteSpace(component)
                || Components == null
                || !Components.TryGetValue(component, out NormalizedOverlayBounds? saved)
                || !IsFinitePositive(saved.Width)
                || !IsFinitePositive(saved.Height)
                || !IsFinite(saved.X)
                || !IsFinite(saved.Y))
            {
                return fallback;
            }

            double widthRatio = component == OverlayComponentKeys.TimingTower && TowerDesignWidth == 520 ? OverlayUiMetrics.TowerWidth / 520.0 : 1.0;
            int width = Clamp((int)Math.Round(saved.Width * viewportWidth * widthRatio), 72, viewportWidth);
            double extraHeaderHeight = widthRatio != 1 ? 22 * saved.Width * viewportWidth / 520.0 : 0;
            int height = Clamp((int)Math.Round(saved.Height * viewportHeight + extraHeaderHeight), 48, viewportHeight);
            int x = Clamp((int)Math.Round(saved.X * viewportWidth), 0, Math.Max(0, viewportWidth - width));
            int y = Clamp((int)Math.Round(saved.Y * viewportHeight), 0, Math.Max(0, viewportHeight - height));
            return new OverlayBounds(x, y, width, height);
        }

        public void Capture(string component, OverlayBounds bounds, int viewportWidth, int viewportHeight)
        {
            if (string.IsNullOrWhiteSpace(component)) throw new ArgumentException("Component key is required.", nameof(component));
            ValidateViewport(viewportWidth, viewportHeight);

            int width = Clamp(bounds.Width, 72, viewportWidth);
            int height = Clamp(bounds.Height, 48, viewportHeight);
            int x = Clamp(bounds.X, 0, Math.Max(0, viewportWidth - width));
            int y = Clamp(bounds.Y, 0, Math.Max(0, viewportHeight - height));
            Components ??= new Dictionary<string, NormalizedOverlayBounds>(StringComparer.OrdinalIgnoreCase);
            if (component == OverlayComponentKeys.TimingTower) TowerDesignWidth = OverlayUiMetrics.TowerWidth;
            Components[component] = new NormalizedOverlayBounds
            {
                X = x / (double)viewportWidth,
                Y = y / (double)viewportHeight,
                Width = width / (double)viewportWidth,
                Height = height / (double)viewportHeight
            };
        }

        private static void ValidateViewport(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        }

        private static int Clamp(int value, int minimum, int maximum)
            => Math.Max(minimum, Math.Min(maximum, value));

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsFinitePositive(double value)
            => IsFinite(value) && value > 0;
    }
}
