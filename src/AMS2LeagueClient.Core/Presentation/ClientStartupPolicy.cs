using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class ClientStartupPolicy
    {
        private ClientStartupPolicy(bool diagnostic, bool showStatusWindow, bool afterUpdate,
            bool useGlass, bool useRetainedN, bool layeredRequested, bool hardwareRequested, bool threadedNRequested)
        {
            Diagnostic = diagnostic;
            AfterUpdate = afterUpdate;
            ShowStatusWindow = showStatusWindow;
            UseGlass = useGlass;
            UseRetainedN = useRetainedN;
            LayeredRequested = layeredRequested;
            HardwareRequested = hardwareRequested;
            ThreadedNRequested = threadedNRequested;
        }

        public bool Diagnostic { get; }
        public bool ShowStatusWindow { get; }
        public bool AfterUpdate { get; }
        public bool ShowStatusWindowActivated => ShowStatusWindow && !AfterUpdate;
        public bool IsBackgroundStartup => !ShowStatusWindow;
        public bool UseGlass { get; }
        public bool UseRetainedN { get; }
        public bool LayeredRequested { get; }
        public bool HardwareRequested { get; }
        // Monitor HUDs rasterize on the CPU by default. The layered hardware path waits on
        // the GPU readback behind a GPU-bound game; DWM glass and DComp remain GPU paths.
        public bool UseSoftwareRendering => !UseGlass && !HardwareRequested;
        public bool ThreadedNRequested { get; }
        // Experimental: each N HUD rasterizes on its own thread into a native layered window.
        // It replaces the layered WPF surface, so glass and the DComp N take precedence.
        public bool UseThreadedN => ThreadedNRequested && !UseGlass;

        public static ClientStartupPolicy FromArguments(IEnumerable<string> arguments)
        {
            bool diagnostic = false;
            bool showStatus = true;
            bool afterUpdate = false;
            bool layeredRequested = false;
            bool glassRequested = false;
            bool retainedNRequested = false;
            bool hardwareRequested = false;
            bool threadedNRequested = false;
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
                else if (string.Equals(argument, "--monitor-glass", StringComparison.OrdinalIgnoreCase))
                {
                    glassRequested = true;
                }
                else if (string.Equals(argument, "--monitor-retained-n", StringComparison.OrdinalIgnoreCase))
                {
                    retainedNRequested = true;
                }
                else if (string.Equals(argument, "--monitor-hardware", StringComparison.OrdinalIgnoreCase))
                {
                    hardwareRequested = true;
                }
                else if (string.Equals(argument, "--monitor-threaded-n", StringComparison.OrdinalIgnoreCase))
                {
                    threadedNRequested = true;
                }
            }

            return new ClientStartupPolicy(diagnostic, showStatus || afterUpdate, afterUpdate,
                (glassRequested || retainedNRequested) && !layeredRequested,
                retainedNRequested && !layeredRequested, layeredRequested, hardwareRequested, threadedNRequested);
        }
    }
}
