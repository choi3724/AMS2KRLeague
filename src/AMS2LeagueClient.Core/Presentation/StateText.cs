using System;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public static class StateText
    {
        public static string Game(uint raw)
        {
            return Enum.IsDefined(typeof(GameState), raw)
                ? ((GameState)raw).ToString().ToUpperInvariant()
                : "UNKNOWN(" + raw + ")";
        }

        public static string Session(uint raw)
        {
            return Enum.IsDefined(typeof(SessionState), raw)
                ? ((SessionState)raw).ToString().ToUpperInvariant()
                : "UNKNOWN(" + raw + ")";
        }

        public static string Race(uint raw)
        {
            return Enum.IsDefined(typeof(RaceState), raw)
                ? ((RaceState)raw).ToString().ToUpperInvariant()
                : "UNKNOWN(" + raw + ")";
        }

        public static string Pit(uint raw)
        {
            return Enum.IsDefined(typeof(PitMode), raw)
                ? ((PitMode)raw).ToString().ToUpperInvariant()
                : "UNKNOWN(" + raw + ")";
        }

        public static string GapSourceText(GapSource source)
        {
            switch (source)
            {
                case GapSource.GameSplit:
                    return "GAME_SPLIT";
                case GapSource.Estimated:
                    return "ESTIMATED";
                case GapSource.LapDelta:
                    return "LAP_DELTA";
                case GapSource.Status:
                    return "STATUS";
                default:
                    return "UNKNOWN";
            }
        }
    }
}
