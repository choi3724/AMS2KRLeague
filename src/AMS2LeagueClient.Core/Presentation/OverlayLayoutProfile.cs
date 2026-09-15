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
        public const string PedalGauge = "pedalGauge";
        public const string Speed = "speed";
        public const string Gear = "gear";
        public const string DrivingDashboard = "drivingDashboard";
        public const string AvanteCluster = "avanteCluster";
        public const string AvanteClusterExpanded = "avanteClusterExpanded";
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
            PedalGauge,
            Speed,
            Gear,
            DrivingDashboard,
            AvanteCluster,
            AvanteClusterExpanded
        };
    }

    public sealed class NormalizedOverlayBounds
    {
        // Legacy schema-1 positions retain their clamp; newly edited positions may cross the game viewport.
        public bool AllowOutsideViewport { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        // Physical viewport used to encode all four coordinates. Restore the
        // saved pixel offsets and size, then the caller adds the game-client origin.
        public int ReferenceWidth { get; set; }
        public int ReferenceHeight { get; set; }
    }

    public sealed class OverlayPreviewViewport
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public uint Dpi { get; set; } = 96;
    }

    public sealed class OverlayLayoutProfile
    {
        public int Schema { get; set; } = 1;
        // Physical game-client reference for game-free editing only; never a monitor index.
        public OverlayPreviewViewport? PreviewViewport { get; set; }
        public VrHudSettings VrHud { get; set; } = new VrHudSettings();
        public DrivingHudSettings DrivingHud { get; set; } = new DrivingHudSettings();
        // Missing in pre-0.4.1 profiles; expand only the saved tower width once.
        public int TowerDesignWidth { get; set; } = 520;
        public Dictionary<string, NormalizedOverlayBounds> Components { get; set; }
            = new Dictionary<string, NormalizedOverlayBounds>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> EnabledComponents { get; set; }
            = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public void SetDrivingPanelDefaults()
        {
            EnabledComponents ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            EnabledComponents.TryAdd(OverlayComponentKeys.DrivingDashboard, false);
            EnabledComponents.TryAdd(OverlayComponentKeys.PedalGauge, false);
            EnabledComponents.TryAdd(OverlayComponentKeys.AvanteCluster, false);
            EnabledComponents.TryAdd(OverlayComponentKeys.AvanteClusterExpanded, false);
        }

        public Dictionary<string, double> ComponentOpacities { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public double GetOpacity(string component)
            => ComponentOpacities != null && ComponentOpacities.TryGetValue(component, out double value) && double.IsFinite(value)
                ? Math.Clamp(value, 0, 1) : 1;
        public void SetOpacity(string component, double value)
        {
            if (Array.IndexOf(OverlayComponentKeys.All, component) < 0 || !double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(component));
            ComponentOpacities ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            ComponentOpacities[component] = Math.Clamp(value, 0, 1);
        }

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
            bool hasReference = saved.ReferenceWidth > 0 && saved.ReferenceHeight > 0;
            bool hasPreview = PreviewViewport?.Width > 0 && PreviewViewport.Height > 0;
            int sizeWidth = hasReference ? saved.ReferenceWidth : hasPreview ? PreviewViewport!.Width : viewportWidth;
            int sizeHeight = hasReference ? saved.ReferenceHeight : hasPreview ? PreviewViewport!.Height : viewportHeight;
            int width = Clamp((int)Math.Round(saved.Width * sizeWidth * widthRatio), 72, viewportWidth);
            double extraHeaderHeight = widthRatio != 1 ? 22 * saved.Width * sizeWidth / 520.0 : 0;
            int height = Clamp((int)Math.Round(saved.Height * sizeHeight + extraHeaderHeight), 48, viewportHeight);
            int x = Pixel(saved.X * sizeWidth);
            int y = Pixel(saved.Y * sizeHeight);
            if (!saved.AllowOutsideViewport)
            {
                x = Clamp(x, 0, Math.Max(0, viewportWidth - width));
                y = Clamp(y, 0, Math.Max(0, viewportHeight - height));
            }
            return new OverlayBounds(x, y, width, height);
        }

        public void Capture(string component, OverlayBounds bounds, int viewportWidth, int viewportHeight)
        {
            if (string.IsNullOrWhiteSpace(component)) throw new ArgumentException("Component key is required.", nameof(component));
            ValidateViewport(viewportWidth, viewportHeight);

            PreserveSizeReferences();
            int width = Clamp(bounds.Width, 72, viewportWidth);
            int height = Clamp(bounds.Height, 48, viewportHeight);
            int x = bounds.X;
            int y = bounds.Y;
            Components ??= new Dictionary<string, NormalizedOverlayBounds>(StringComparer.OrdinalIgnoreCase);
            if (component == OverlayComponentKeys.TimingTower) TowerDesignWidth = OverlayUiMetrics.TowerWidth;
            Components[component] = new NormalizedOverlayBounds
            {
                AllowOutsideViewport = true,
                X = x / (double)viewportWidth,
                Y = y / (double)viewportHeight,
                Width = width / (double)viewportWidth,
                Height = height / (double)viewportHeight,
                ReferenceWidth = viewportWidth,
                ReferenceHeight = viewportHeight
            };
        }

        // Pin every legacy component before another editing session can replace
        // PreviewViewport, including hidden/disabled HUDs that were not recaptured.
        public void PreserveSizeReferences()
        {
            if (PreviewViewport == null || PreviewViewport.Width <= 0 || PreviewViewport.Height <= 0 || Components == null) return;
            foreach (var saved in Components.Values)
            {
                if (saved == null || (saved.ReferenceWidth > 0 && saved.ReferenceHeight > 0)) continue;
                saved.ReferenceWidth = PreviewViewport.Width;
                saved.ReferenceHeight = PreviewViewport.Height;
            }
        }

        private static int Pixel(double value) => (int)Math.Clamp(Math.Round(value), int.MinValue / 2.0, int.MaxValue / 2.0);

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
