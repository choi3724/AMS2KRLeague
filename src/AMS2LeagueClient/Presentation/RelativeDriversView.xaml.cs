using System;
using System.Windows;
using System.Windows.Controls;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    /// <summary>
    /// Ahead/behind panel. When the car ahead or behind changes the row slides in
    /// from its own direction (ahead from above, behind from below) and the
    /// distance readout pops whenever its favourable/unfavourable colour flips.
    /// </summary>
    public partial class RelativeDriversView : UserControl
    {
        private string _aheadKey = string.Empty;
        private string _behindKey = string.Empty;
        private string _aheadColor = string.Empty;
        private string _behindColor = string.Empty;

        public RelativeDriversView() => InitializeComponent();

        public void SetViewModel(OverlayViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;

            if (!string.Equals(viewModel.AheadParticipantKey, _aheadKey, StringComparison.Ordinal))
            {
                _aheadKey = viewModel.AheadParticipantKey;
                if (_aheadKey.Length > 0) HudMotion.SlideIn(AheadRow, 0, -14, 280);
            }
            if (!string.Equals(viewModel.BehindParticipantKey, _behindKey, StringComparison.Ordinal))
            {
                _behindKey = viewModel.BehindParticipantKey;
                if (_behindKey.Length > 0) HudMotion.SlideIn(BehindRow, 0, 14, 280);
            }

            if (!string.Equals(viewModel.AheadDistanceColor, _aheadColor, StringComparison.Ordinal))
            {
                if (_aheadColor.Length > 0) HudMotion.Pop(AheadDistancePanel, 1.22, 240);
                _aheadColor = viewModel.AheadDistanceColor;
            }
            if (!string.Equals(viewModel.BehindDistanceColor, _behindColor, StringComparison.Ordinal))
            {
                if (_behindColor.Length > 0) HudMotion.Pop(BehindDistancePanel, 1.22, 240);
                _behindColor = viewModel.BehindDistanceColor;
            }
        }
    }
}
