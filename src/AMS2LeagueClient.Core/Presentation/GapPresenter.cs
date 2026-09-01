using System;
using System.Globalization;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class GapDisplay
    {
        public GapDisplay(string text, GapSource source)
        {
            Text = text;
            Source = source;
        }

        public string Text { get; }
        public GapSource Source { get; }
    }

    public sealed class GapPresenter
    {
        public GapDisplay Present(float directSplitSeconds, ParticipantSnapshot local, ParticipantSnapshot? relative)
        {
            if (relative == null)
            {
                return new GapDisplay("—", GapSource.Unknown);
            }

            long lapDelta = (long)relative.LapsCompleted - local.LapsCompleted;
            if (lapDelta != 0)
            {
                string sign = lapDelta > 0 ? "+" : string.Empty;
                return new GapDisplay(sign + lapDelta.ToString(CultureInfo.InvariantCulture) + " LAP", GapSource.LapDelta);
            }

            if (!float.IsNaN(directSplitSeconds) && !float.IsInfinity(directSplitSeconds) && directSplitSeconds >= 0.0f)
            {
                return new GapDisplay("+" + directSplitSeconds.ToString("0.000", CultureInfo.InvariantCulture), GapSource.GameSplit);
            }

            return new GapDisplay("—", GapSource.Unknown);
        }
    }
}
