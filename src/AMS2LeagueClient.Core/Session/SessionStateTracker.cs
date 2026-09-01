using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Session
{
    public sealed class SessionStateTracker
    {
        private uint? _lastGameState;
        private uint? _lastSessionState;

        public int Generation { get; private set; }

        public bool Observe(TelemetrySnapshot snapshot)
        {
            bool transition = _lastGameState.HasValue
                && (_lastGameState.Value != snapshot.GameStateRaw || _lastSessionState != snapshot.SessionStateRaw);

            if (transition)
            {
                Generation++;
            }

            _lastGameState = snapshot.GameStateRaw;
            _lastSessionState = snapshot.SessionStateRaw;
            return transition;
        }

        public void Reset()
        {
            _lastGameState = null;
            _lastSessionState = null;
            Generation++;
        }
    }
}
