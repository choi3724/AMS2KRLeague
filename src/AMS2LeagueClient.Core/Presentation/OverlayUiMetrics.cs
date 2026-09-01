using System;

namespace AMS2LeagueClient.Core.Presentation
{
    /// <summary>
    /// One source of truth for the public overlay footprint.  These dimensions
    /// are logical pixels; Windows DPI scaling is applied exactly once by the
    /// physical window layout calculator.
    /// </summary>
    public static class OverlayUiMetrics
    {
        public const double TargetScale = 0.80;
        public const double FontScale = 0.86;

        public const int BaselineTowerWidth = 460;
        public const int BaselineTowerHeight = 700;
        public const int BaselineRowPitch = 27;
        public const int BaselineSessionWidth = 280;
        public const int BaselineSessionHeight = 150;
        public const int BaselineRaceControlCompactWidth = 360;
        public const int BaselineRaceControlExpandedWidth = 520;
        public const int BaselineRaceControlCompactHeight = 82;
        public const int BaselineRaceControlExpandedHeight = 190;
        public const int BaselineEventWidth = 650;
        public const int BaselineEventHeight = 105;

        public const int TowerWidth = 368;
        public const int TowerHeight = 560;
        public const int DiagnosticTowerHeight = 720;
        public const int RowPitch = 22;
        public const int HeaderAndFooterHeight = 230;
        public const int ComponentGap = 10;

        public const int SessionWidth = 224;
        public const int SessionHeight = 120;
        public const int WaitingWidth = 312;
        public const int WaitingHeight = 120;
        public const int RaceControlCompactWidth = 288;
        public const int RaceControlExpandedWidth = 416;
        public const int RaceControlCompactHeight = 66;
        public const int RaceControlExpandedHeight = 152;
        public const int EventWidth = 520;
        public const int EventHeight = 84;

        public const double FontMicro = 8;
        public const double FontTiny = 9;
        public const double FontSmall = 10.5;
        public const double FontBody = 12;
        public const double FontTitle = 13;
        public const double FontEmphasis = 15;
        public const double FontValue = 19;
        public const double FontHero = 25;

        public static double ScaleRatio(int compact, int baseline)
            => baseline <= 0 ? 0 : compact / (double)baseline;
    }

    public static class OverlayUiPalette
    {
        // Text stays opaque; only panel and row surfaces become more transparent.
        public const string MainPanelBackground = "#B00A111C";
        public const string SecondaryPanelBackground = "#94101C29";
        public const string TertiaryPanelBackground = "#86121D2B";
        public const string NormalRowBackground = "#78121D2A";
        public const string PlayerRowBackground = "#A82A554F";
    }

    public readonly struct OverlayBounds
    {
        public OverlayBounds(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => X + Width;
        public int Bottom => Y + Height;
    }

    public sealed class OverlayComponentLayout
    {
        public OverlayComponentLayout(
            OverlayBounds timing,
            OverlayBounds session,
            OverlayBounds eventCard,
            OverlayBounds raceControl,
            OverlayBounds waiting,
            int bottomInset)
        {
            Timing = timing;
            Session = session;
            EventCard = eventCard;
            RaceControl = raceControl;
            Waiting = waiting;
            BottomInset = bottomInset;
        }

        public OverlayBounds Timing { get; }
        public OverlayBounds Session { get; }
        public OverlayBounds EventCard { get; }
        public OverlayBounds RaceControl { get; }
        public OverlayBounds Waiting { get; }
        public int BottomInset { get; }
    }

    public static class OverlayComponentLayoutCalculator
    {
        public static OverlayComponentLayout Calculate(
            int viewportWidth,
            int viewportHeight,
            uint dpi,
            bool diagnostic,
            bool raceControlExpanded)
        {
            if (viewportWidth <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
            if (viewportHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewportHeight));
            double dpiScale = Math.Max(1, dpi) / 96.0;
            int leftInset = Math.Max(8, (int)Math.Round(viewportWidth * 0.004));
            int topInset = Math.Max(8, (int)Math.Round(viewportHeight * 0.008));
            int bottomInset = (int)Math.Round(viewportHeight * 0.09);
            int timingWidth = Scale(OverlayUiMetrics.TowerWidth, dpiScale);
            int timingHeight = Math.Min(
                Scale(diagnostic ? OverlayUiMetrics.DiagnosticTowerHeight : OverlayUiMetrics.TowerHeight, dpiScale),
                Math.Max(1, viewportHeight - (topInset * 2)));
            int sessionWidth = Scale(OverlayUiMetrics.SessionWidth, dpiScale);
            int sessionHeight = Scale(OverlayUiMetrics.SessionHeight, dpiScale);
            int eventWidth = Scale(OverlayUiMetrics.EventWidth, dpiScale);
            int eventHeight = Scale(OverlayUiMetrics.EventHeight, dpiScale);
            int raceWidth = Scale(
                raceControlExpanded ? OverlayUiMetrics.RaceControlExpandedWidth : OverlayUiMetrics.RaceControlCompactWidth,
                dpiScale);
            int raceHeight = Scale(
                raceControlExpanded ? OverlayUiMetrics.RaceControlExpandedHeight : OverlayUiMetrics.RaceControlCompactHeight,
                dpiScale);
            int auxiliaryLeft = leftInset + timingWidth + Scale(OverlayUiMetrics.ComponentGap, dpiScale);

            return new OverlayComponentLayout(
                new OverlayBounds(leftInset, topInset, timingWidth, timingHeight),
                new OverlayBounds(auxiliaryLeft, topInset, sessionWidth, sessionHeight),
                new OverlayBounds((viewportWidth - eventWidth) / 2, viewportHeight - bottomInset - eventHeight, eventWidth, eventHeight),
                new OverlayBounds(auxiliaryLeft, topInset + Scale(OverlayUiMetrics.SessionHeight + OverlayUiMetrics.ComponentGap, dpiScale), raceWidth, raceHeight),
                new OverlayBounds(leftInset, topInset, Scale(OverlayUiMetrics.WaitingWidth, dpiScale), Scale(OverlayUiMetrics.WaitingHeight, dpiScale)),
                bottomInset);
        }

        private static int Scale(int logicalPixels, double dpiScale)
            => Math.Max(1, (int)Math.Round(logicalPixels * dpiScale));
    }
}
