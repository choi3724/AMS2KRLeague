using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Presentation
{
    // Only immutable display inputs cross from the WPF owner to the native owner.
    // Acquisition and activity recording never wait for this frame.
    internal sealed class CompositionHudFrame
    {
        internal DrivingHudSettings Settings { get; }
        internal TelemetrySnapshot? Session { get; }

        internal CompositionHudFrame(DrivingHudSettings settings, TelemetrySnapshot? session)
        {
            Settings = settings;
            Session = session;
        }
    }
}
