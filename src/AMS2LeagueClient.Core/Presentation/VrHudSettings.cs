using System;

namespace AMS2LeagueClient.Core.Presentation
{
    public enum OverlayOutputMode { Monitor, Vr, Both }

    public sealed class VrHudSettings
    {
        public OverlayOutputMode Output { get; set; } = OverlayOutputMode.Monitor;
        public double WidthMetres { get; set; } = 2;
        public double DistanceMetres { get; set; } = 2;
        public double HorizontalMetres { get; set; }
        public double VerticalMetres { get; set; }
        public double YawDegrees { get; set; }
        public double PitchDegrees { get; set; }
        public bool FollowHead { get; set; } = true;

        public VrHudSettings Normalize() => new VrHudSettings
        {
            Output = Enum.IsDefined(typeof(OverlayOutputMode), Output) ? Output : OverlayOutputMode.Monitor,
            WidthMetres = Bound(WidthMetres, 0.5, 4, 2),
            DistanceMetres = Bound(DistanceMetres, 0.5, 5, 2),
            HorizontalMetres = Bound(HorizontalMetres, -2, 2, 0),
            VerticalMetres = Bound(VerticalMetres, -2, 2, 0),
            YawDegrees = Bound(YawDegrees, -60, 60, 0),
            PitchDegrees = Bound(PitchDegrees, -60, 60, 0),
            FollowHead = FollowHead
        };

        private static double Bound(double value, double min, double max, double fallback)
            => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }
}
