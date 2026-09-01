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
        public bool ShowStatusWindowActivated => ShowStatusWindow;
        public bool IsBackgroundStartup => !ShowStatusWindow;

        public static ClientStartupPolicy FromArguments(IEnumerable<string> arguments)
        {
            bool diagnostic = false;
            bool showStatus = true;
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
                else if (string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase))
                {
                    showStatus = false;
                }
            }

            return new ClientStartupPolicy(diagnostic, showStatus);
        }
    }
}
