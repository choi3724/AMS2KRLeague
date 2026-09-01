using AMS2LeagueClient.Core.Events;

namespace AMS2LeagueClient.Core.RaceControl
{
    public static class EventSuppressionPolicy
    {
        public static bool ShouldSuppress(BroadcastOverlayState state, OverlayEventType type)
        {
            if ((state & BroadcastOverlayState.RedFlag) != 0)
            {
                return IsBattle(type) || IsPosition(type) || type == OverlayEventType.PersonalBest;
            }

            if ((state & (BroadcastOverlayState.Yellow | BroadcastOverlayState.DoubleYellow | BroadcastOverlayState.FullCourseYellow)) != 0)
            {
                return IsBattle(type) || IsPosition(type) || type == OverlayEventType.OpeningStart;
            }

            if ((state & BroadcastOverlayState.PlayerPit) != 0 && IsPosition(type))
            {
                return true;
            }

            if ((state & BroadcastOverlayState.Chequered) != 0)
            {
                return IsBattle(type) || IsPosition(type) || type == OverlayEventType.PersonalBest;
            }

            return (state & BroadcastOverlayState.PlayerDsq) != 0
                && (IsBattle(type) || IsPosition(type));
        }

        private static bool IsBattle(OverlayEventType type)
            => type == OverlayEventType.Battle;

        private static bool IsPosition(OverlayEventType type)
            => type == OverlayEventType.PositionGained
                || type == OverlayEventType.PositionLost
                || type == OverlayEventType.PodiumEntry
                || type == OverlayEventType.PodiumExit;
    }
}
