using System;
using System.Windows.Controls;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    /// <summary>
    /// Current/last/best lap panel. A completed lap pops the last-lap value, a
    /// new personal best pops the best-lap value harder, and each sector split
    /// pops once when it first appears for the current lap. The running current
    /// lap time changes every frame and is not animated.
    /// </summary>
    public partial class LapTimingView : UserControl
    {
        private const string Unavailable = "—";
        private string _lastLap = string.Empty;
        private string _bestLap = string.Empty;
        private readonly string[] _sectors = { string.Empty, string.Empty, string.Empty };

        public LapTimingView() => InitializeComponent();

        public void SetViewModel(OverlayViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;

            if (Changed(ref _lastLap, viewModel.LastLapText) && IsTime(viewModel.LastLapText))
            {
                HudMotion.Pop(LastLapValue, 1.18, 320);
            }
            if (Changed(ref _bestLap, viewModel.BestLapText) && IsTime(viewModel.BestLapText))
            {
                HudMotion.Pop(BestLapValue, 1.32, 380);
            }

            string[] incoming = { viewModel.Sector1Text, viewModel.Sector2Text, viewModel.Sector3Text };
            TextBlock[] targets = { Sector1Value, Sector2Value, Sector3Value };
            for (int index = 0; index < incoming.Length; index++)
            {
                string previous = _sectors[index];
                if (Changed(ref _sectors[index], incoming[index]) && !IsTime(previous) && IsTime(incoming[index]))
                {
                    HudMotion.Pop(targets[index], 1.2, 260);
                }
            }
        }

        private static bool Changed(ref string field, string value)
        {
            if (string.Equals(field, value, StringComparison.Ordinal)) return false;
            bool first = field.Length == 0;
            field = value;
            return !first;
        }

        private static bool IsTime(string value)
            => !string.IsNullOrWhiteSpace(value) && value != Unavailable && value != "--";
    }
}
