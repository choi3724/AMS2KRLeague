using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public partial class OverlayHudView : UserControl
    {
        private string _rankingKey = string.Empty;
        private readonly TimingTowerTransitionTracker _transitionTracker = new TimingTowerTransitionTracker();

        public OverlayHudView()
        {
            InitializeComponent();
        }

        public void SetViewModel(OverlayViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;

            string rankingKey = viewModel.RankingRangeText + "\u001e" + string.Join(
                "\u001f",
                viewModel.RankingRows.Select(row => row.ParticipantIndex + "|" + row.Position + "|" + row.Name + "|" + row.Status + "|" + row.IsPlayer));
            if (rankingKey == _rankingKey)
            {
                return;
            }

            _rankingKey = rankingKey;
            System.Collections.Generic.IReadOnlyList<TimingTowerTransition> transitions = _transitionTracker.Observe(viewModel.RankingRows);
            RankingRange.Text = viewModel.RankingRangeText;
            RankingItems.ItemsSource = viewModel.RankingRows;
            RankingItems.UpdateLayout();
            AnimateChangedRows(viewModel.RankingRows, transitions);
        }

        private void AnimateChangedRows(
            System.Collections.Generic.IReadOnlyList<RankingRowViewModel> rows,
            System.Collections.Generic.IReadOnlyList<TimingTowerTransition> transitions)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(340));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int index = 0; index < rows.Count; index++)
            {
                ContentPresenter? presenter = RankingItems.ItemContainerGenerator.ContainerFromIndex(index) as ContentPresenter;
                if (presenter == null) continue;
                RankingRowViewModel row = rows[index];
                TimingTowerTransition? transition = transitions.FirstOrDefault(item => item.ParticipantIndex == row.ParticipantIndex);
                if (transition != null && transition.IsReorder)
                {
                    var transform = new TranslateTransform(0, transition.RowDelta * LeftTowerLayoutMetrics.RankingRowPitch);
                    presenter.RenderTransform = transform;
                    transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(transform.Y, 0, duration) { EasingFunction = easing });
                }
                else if (transition != null && transition.FromIndex < 0)
                {
                    presenter.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
                }

                if (transition != null && transition.StatusChanged)
                {
                    presenter.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, new Duration(TimeSpan.FromMilliseconds(420))) { AutoReverse = true });
                }
            }
        }
    }
}
