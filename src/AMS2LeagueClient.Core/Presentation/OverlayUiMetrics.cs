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
        public const double TargetScale = 1.00;
        public const double FontScale = 1.00;

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

        public const int TowerWidth = 520;
        public const int TowerHeight = 586;
        public const int DiagnosticTowerHeight = 690;
        public const int RowPitch = 38;
        public const int HeaderAndFooterHeight = 16;
        public const int ComponentGap = 10;

        public const int RelativeWidth = 520;
        public const int RelativeHeight = 104;
        public const int LapTimingWidth = 380;
        public const int LapTimingHeight = 112;
        public const int SessionWidth = 250;
        public const int SessionHeight = 140;
        public const int WaitingWidth = 336;
        public const int WaitingHeight = 140;
        public const int RaceControlCompactWidth = 288;
        public const int RaceControlExpandedWidth = 416;
        public const int RaceControlCompactHeight = 66;
        public const int RaceControlExpandedHeight = 152;
        public const int EventWidth = 520;
        public const int EventHeight = 84;

        public const double FontMicro = 10;
        public const double FontTiny = 11.5;
        public const double FontSmall = 14.5;
        public const double FontBody = 16.5;
        public const double FontTitle = 18;
        public const double FontEmphasis = 20;
        public const double FontDriverName = 24;
        public const double FontClass = 17;
        public const double FontTiming = 18;
        public const double FontValue = 24;
        public const double FontHero = 30;

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
        public const string InactiveRowBackground = "#70141B24";
        public const string ActiveText = "#F4F7FB";
        public const string InactiveText = "#8C9AA8";
        public const string ActiveTime = "#FFFFFF";
        public const string InactiveTime = "#9AA6B2";
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
            OverlayBounds relative,
            OverlayBounds lapTiming,
            OverlayBounds session,
            OverlayBounds eventCard,
            OverlayBounds raceControl,
            OverlayBounds waiting,
            int bottomInset)
        {
            Timing = timing;
            Relative = relative;
            LapTiming = lapTiming;
            Session = session;
            EventCard = eventCard;
            RaceControl = raceControl;
            Waiting = waiting;
            BottomInset = bottomInset;
        }

        public OverlayBounds Timing { get; }
        public OverlayBounds Relative { get; }
        public OverlayBounds LapTiming { get; }
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
            int relativeWidth = Scale(OverlayUiMetrics.RelativeWidth, dpiScale);
            int relativeHeight = Scale(OverlayUiMetrics.RelativeHeight, dpiScale);
            int lapTimingWidth = Scale(OverlayUiMetrics.LapTimingWidth, dpiScale);
            int lapTimingHeight = Scale(OverlayUiMetrics.LapTimingHeight, dpiScale);
            int eventWidth = Scale(OverlayUiMetrics.EventWidth, dpiScale);
            int eventHeight = Scale(OverlayUiMetrics.EventHeight, dpiScale);
            int raceWidth = Scale(
                raceControlExpanded ? OverlayUiMetrics.RaceControlExpandedWidth : OverlayUiMetrics.RaceControlCompactWidth,
                dpiScale);
            int raceHeight = Scale(
                raceControlExpanded ? OverlayUiMetrics.RaceControlExpandedHeight : OverlayUiMetrics.RaceControlCompactHeight,
                dpiScale);
            int auxiliaryLeft = leftInset + timingWidth + Scale(OverlayUiMetrics.ComponentGap, dpiScale);
            int componentGap = Scale(OverlayUiMetrics.ComponentGap, dpiScale);

            return new OverlayComponentLayout(
                new OverlayBounds(leftInset, topInset, timingWidth, timingHeight),
                new OverlayBounds(leftInset, topInset + timingHeight + componentGap, relativeWidth, relativeHeight),
                new OverlayBounds(auxiliaryLeft, topInset + sessionHeight + componentGap, lapTimingWidth, lapTimingHeight),
                new OverlayBounds(auxiliaryLeft, topInset, sessionWidth, sessionHeight),
                new OverlayBounds((viewportWidth - eventWidth) / 2, viewportHeight - bottomInset - eventHeight, eventWidth, eventHeight),
                new OverlayBounds(auxiliaryLeft, topInset + sessionHeight + lapTimingHeight + (componentGap * 2), raceWidth, raceHeight),
                new OverlayBounds(leftInset, topInset, Scale(OverlayUiMetrics.WaitingWidth, dpiScale), Scale(OverlayUiMetrics.WaitingHeight, dpiScale)),
                bottomInset);
        }

        private static int Scale(int logicalPixels, double dpiScale)
            => Math.Max(1, (int)Math.Round(logicalPixels * dpiScale));
    }
}
