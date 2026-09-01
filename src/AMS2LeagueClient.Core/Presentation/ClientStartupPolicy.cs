using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class ClientStartupPolicy
    {
        private ClientStartupPolicy(bool diagnostic, bool showStatusWindow)
        {
            Diagnostic = diagnostic;
            ShowStatusWindow = showStatusWindow;
        }

        public bool Diagnostic { get; }
        public bool ShowStatusWindow { get; }
        public bool ShowStatusWindowActivated => false;
        public bool IsBackgroundStartup => !ShowStatusWindow;

        public static ClientStartupPolicy FromArguments(IEnumerable<string> arguments)
        {
            bool diagnostic = false;
            bool showStatus = false;
            foreach (string argument in arguments)
            {
                if (string.Equals(argument, "--diagnostic", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostic = true;
                    showStatus = true;
                }
                else if (string.Equals(argument, "--status", StringComparison.OrdinalIgnoreCase))
                {
                    showStatus = true;
                }
            }

            return new ClientStartupPolicy(diagnostic, showStatus);
        }
    }
}
