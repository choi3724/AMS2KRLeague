using System;
using System.Windows.Controls;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    /// <summary>
    /// Session card. The lap counter rolls upward when a new lap starts and the
    /// position value rolls in the direction of the change, like a broadcast
    /// lap/position counter. The remaining-time value ticks every second and is
    /// deliberately not animated.
    /// </summary>
    public partial class SessionInfoView : UserControl
    {
        private string _lapValue = string.Empty;
        private string _positionValue = string.Empty;

        public SessionInfoView() => InitializeComponent();

        public void SetViewModel(SessionInfoViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;

            if (!string.Equals(viewModel.LapValue, _lapValue, StringComparison.Ordinal))
            {
                bool first = _lapValue.Length == 0;
                _lapValue = viewModel.LapValue;
                if (!first) HudMotion.Roll(LapValueText, upward: true, durationMs: 280);
            }

            if (!string.Equals(viewModel.PositionValue, _positionValue, StringComparison.Ordinal))
            {
                int previous = HudMotion.ParseLeadingNumber(_positionValue);
                int current = HudMotion.ParseLeadingNumber(viewModel.PositionValue);
                bool first = _positionValue.Length == 0;
                _positionValue = viewModel.PositionValue;
                if (!first && previous > 0 && current > 0 && previous != current)
                {
                    HudMotion.Roll(PositionValueText, upward: current < previous, durationMs: 280);
                }
            }
        }
    }
}
