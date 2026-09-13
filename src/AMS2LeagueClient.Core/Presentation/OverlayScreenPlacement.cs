using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Presentation
{
    // All rectangles are virtual-desktop physical pixels. Monitor holes are not screens.
    public static class OverlayScreenPlacement
    {
        public static OverlayBounds Recover(OverlayBounds desired, IReadOnlyList<OverlayBounds> monitors, IReadOnlyList<OverlayBounds> workAreas)
        {
            if (monitors.Count != workAreas.Count) throw new ArgumentException("Monitor/work area count mismatch.");
            int nearest = -1;
            double nearestDistance = double.MaxValue;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                if ((long)desired.X < (long)m.X + m.Width && (long)desired.X + desired.Width > m.X
                    && (long)desired.Y < (long)m.Y + m.Height && (long)desired.Y + desired.Height > m.Y)
                    return desired;
                double x = desired.X + desired.Width / 2.0, y = desired.Y + desired.Height / 2.0;
                double dx = x - Math.Clamp(x, m.X, (double)m.X + m.Width);
                double dy = y - Math.Clamp(y, m.Y, (double)m.Y + m.Height);
                double distance = dx * dx + dy * dy;
                if (distance < nearestDistance) { nearest = i; nearestDistance = distance; }
            }
            if (nearest < 0) return desired; // Transient enumeration failure is not a new layout.
            var work = workAreas[nearest];
            int width = Math.Min(desired.Width, work.Width), height = Math.Min(desired.Height, work.Height);
            return new OverlayBounds(Math.Clamp(desired.X, work.X, work.Right - width),
                Math.Clamp(desired.Y, work.Y, work.Bottom - height), width, height);
        }
    }
}
