using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class ClientStartupPolicy
    {
        private ClientStartupPolicy(bool diagnostic, bool showStatusWindow, bool afterUpdate,
            bool useGlass, bool useRetainedN, bool layeredRequested)
        {
            Diagnostic = diagnostic;
            AfterUpdate = afterUpdate;
            ShowStatusWindow = showStatusWindow;
            UseGlass = useGlass;
            UseRetainedN = useRetainedN;
            LayeredRequested = layeredRequested;
        }

        public bool Diagnostic { get; }
        public bool ShowStatusWindow { get; }
        public bool AfterUpdate { get; }
        public bool ShowStatusWindowActivated => ShowStatusWindow && !AfterUpdate;
        public bool IsBackgroundStartup => !ShowStatusWindow;
        public bool UseGlass { get; }
        public bool UseRetainedN { get; }
        public bool LayeredRequested { get; }

        public static ClientStartupPolicy FromArguments(IEnumerable<string> arguments)
        {
            bool diagnostic = false;
            bool showStatus = true;
            bool afterUpdate = false;
            bool layeredRequested = false;
            bool retainedNRequested = false;
            foreach (string argument in arguments)
            {
                if (string.Equals(argument, "--after-update", StringComparison.OrdinalIgnoreCase))
                {
                    afterUpdate = true;
                }
                else if (string.Equals(argument, "--diagnostic", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostic = true;
                    showStatus = true;
                }
                else if (string.Equals(argument, "--status", StringComparison.OrdinalIgnoreCase))
                {
                    showStatus = true;
                }
                else if (string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase))
                {
                    showStatus = false;
                }
                else if (string.Equals(argument, "--monitor-layered", StringComparison.OrdinalIgnoreCase))
                {
                    layeredRequested = true;
                }
                else if (string.Equals(argument, "--monitor-retained-n", StringComparison.OrdinalIgnoreCase))
                {
                    retainedNRequested = true;
                }
            }

            return new ClientStartupPolicy(diagnostic, showStatus || afterUpdate, afterUpdate,
                !layeredRequested, retainedNRequested && !layeredRequested, layeredRequested);
        }
    }
}
