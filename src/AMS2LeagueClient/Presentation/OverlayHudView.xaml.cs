using System;
using System.Collections.ObjectModel;
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
        private readonly ObservableCollection<RankingRowViewModel> _rankingRows = new ObservableCollection<RankingRowViewModel>();

        public OverlayHudView()
        {
            InitializeComponent();
            RankingItems.ItemsSource = _rankingRows;
        }

        public void SetViewModel(OverlayViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;

            string rankingKey = viewModel.RankingRangeText + "\u001e" + string.Join(
                "\u001f",
                viewModel.RankingRows.Select(row => row.ParticipantIndex + "|" + row.Position + "|" + row.Name + "|"
                    + row.Class + "|" + row.CurrentTime + "|" + row.Status + "|" + row.IsPlayer + "|" + row.DisplayState
                    + "|" + row.ClassBackground + "|" + row.Foreground));
            if (rankingKey == _rankingKey)
            {
                return;
            }

            _rankingKey = rankingKey;
            System.Collections.Generic.IReadOnlyList<TimingTowerTransition> transitions = _transitionTracker.Observe(viewModel.RankingRows);
            SynchronizeRankingRows(viewModel.RankingRows);
            RankingItems.UpdateLayout();
            AnimateChangedRows(viewModel.RankingRows, transitions);
        }

        private void SynchronizeRankingRows(System.Collections.Generic.IReadOnlyList<RankingRowViewModel> rows)
        {
            var expected = rows.Select(row => row.ParticipantIndex).ToHashSet();
            for (int index = _rankingRows.Count - 1; index >= 0; index--)
            {
                if (!expected.Contains(_rankingRows[index].ParticipantIndex))
                {
                    _rankingRows.RemoveAt(index);
                }
            }

            for (int targetIndex = 0; targetIndex < rows.Count; targetIndex++)
            {
                RankingRowViewModel incoming = rows[targetIndex];
                int currentIndex = -1;
                for (int index = targetIndex; index < _rankingRows.Count; index++)
                {
                    if (_rankingRows[index].ParticipantIndex == incoming.ParticipantIndex)
                    {
                        currentIndex = index;
                        break;
                    }
                }

                if (currentIndex < 0)
                {
                    _rankingRows.Insert(targetIndex, incoming);
                }
                else
                {
                    if (currentIndex != targetIndex)
                    {
                        _rankingRows.Move(currentIndex, targetIndex);
                    }
                    _rankingRows[targetIndex] = incoming;
                }
            }

            while (_rankingRows.Count > rows.Count)
            {
                _rankingRows.RemoveAt(_rankingRows.Count - 1);
            }
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
                // Never animate the whole row opacity. A status update is not
                // evidence that an actively racing participant became inactive.
                presenter.BeginAnimation(OpacityProperty, null);
                presenter.Opacity = 1;
            }
        }
    }
}
