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
        public GapDisplay Present(float directSplitSeconds, ParticipantSnapshot? relative)
        {
            if (relative == null)
            {
                return new GapDisplay("—", GapSource.Unknown);
            }

            if (!float.IsNaN(directSplitSeconds) && !float.IsInfinity(directSplitSeconds) && directSplitSeconds >= 0.0f)
            {
                return new GapDisplay("+" + directSplitSeconds.ToString("0.000", CultureInfo.InvariantCulture), GapSource.GameSplit);
            }

            return new GapDisplay("—", GapSource.Unknown);
        }
    }
}
