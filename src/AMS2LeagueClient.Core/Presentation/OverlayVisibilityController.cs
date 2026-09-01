using AMS2LeagueClient.Core.Process;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class OverlayVisibilityDecision
    {
        public OverlayVisibilityDecision(bool shouldShow, string reason)
        {
            ShouldShow = shouldShow;
            Reason = reason;
        }

        public bool ShouldShow { get; }
        public string Reason { get; }
    }

    public sealed class OverlayVisibilityController
    {
        public OverlayVisibilityDecision Evaluate(bool processAttached, GameWindowSnapshot? window, bool hasValidGameplaySnapshot)
        {
            if (!processAttached)
            {
                return new OverlayVisibilityDecision(false, "WAIT_PROCESS");
            }

            if (window == null || !window.HasValidClientRect)
            {
                return new OverlayVisibilityDecision(false, "WAIT_WINDOW");
            }

            if (window.IsMinimized)
            {
                return new OverlayVisibilityDecision(false, "MINIMIZED");
            }

            if (!window.IsForeground)
            {
                return new OverlayVisibilityDecision(false, "NOT_FOREGROUND");
            }

            if (!hasValidGameplaySnapshot)
            {
                return new OverlayVisibilityDecision(false, "INVALID_GAMEPLAY_SNAPSHOT");
            }

            return new OverlayVisibilityDecision(true, "SHOW");
        }
    }
}
