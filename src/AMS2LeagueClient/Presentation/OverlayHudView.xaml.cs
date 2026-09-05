using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    /// <summary>
    /// Timing Tower with broadcast-style (F1 TV graphics) transitions:
    /// rows slide into place on reorder, the moving row flashes green/red with a
    /// rolling position number, a session-fastest-lap status sweeps purple across
    /// the row, status badges pop, and rows build in from the left when the tower
    /// first appears or a participant enters the visible window.
    /// Row opacity is never animated: an active participant must never look dimmed.
    /// </summary>
    public partial class OverlayHudView : UserControl
    {
        public const int ReorderDurationMs = 340;
        public const int RowEntryDurationMs = 320;
        public const int RowEntryStaggerMs = 38;
        public const string PositionGainFlashColor = "#82F1D0";
        public const string PositionLossFlashColor = "#FF7777";
        public const string FastestLapFlashColor = "#B68CFF";
        private static readonly string RecordSeparator = ((char)30).ToString();
        private static readonly string UnitSeparator = ((char)31).ToString();

        private static readonly DependencyProperty RowEnteredProperty = DependencyProperty.RegisterAttached(
            "RowEntered",
            typeof(bool),
            typeof(OverlayHudView),
            new PropertyMetadata(false));

        private string _rankingKey = string.Empty;
        private readonly TimingTowerTransitionTracker _transitionTracker = new TimingTowerTransitionTracker();
        private readonly ObservableCollection<RankingRowViewModel> _rankingRows = new ObservableCollection<RankingRowViewModel>();
        private long _entryBatchTick = long.MinValue;
        private int _entryBatchOrdinal;

        public OverlayHudView()
        {
            InitializeComponent();
            RankingItems.ItemsSource = _rankingRows;
        }

        public void SetViewModel(OverlayViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            double requiredHeight = LeftTowerLayoutMetrics.RequiredHeightForRows(viewModel.RankingRowCapacity, viewModel.IsDiagnostic);
            bool capacityChanged = Height != requiredHeight;
            Height = requiredHeight;
            DataContext = viewModel;

            string rankingKey = viewModel.RankingRangeText + RecordSeparator + string.Join(
                UnitSeparator,
                viewModel.RankingRows.Select(row => row.ParticipantIndex + "|" + row.Position + "|" + row.Name + "|"
                    + row.Class + "|" + row.CurrentTime + "|" + row.Status + "|" + row.IsPlayer + "|" + row.DisplayState
                    + "|" + row.ClassBackground + "|" + row.Foreground));
            if (rankingKey == _rankingKey)
            {
                return;
            }

            _rankingKey = rankingKey;
            IReadOnlyList<TimingTowerTransition> transitions = _transitionTracker.Observe(viewModel.RankingRows);
            SynchronizeRankingRows(viewModel.RankingRows);
            // Resizing the visible row window is not an on-track overtake.
            // Snap the pinned player to its new row instead of sliding from an
            // old, now out-of-bounds row during the live resize preview.
            if (capacityChanged)
            {
                for (int index = 0; index < _rankingRows.Count; index++)
                {
                    if (RankingItems.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter presenter
                        && presenter.RenderTransform is TranslateTransform transform && !transform.IsFrozen)
                    {
                        transform.BeginAnimation(TranslateTransform.YProperty, null);
                        transform.Y = 0;
                    }
                }
            }
            else if (transitions.Any(transition => transition.IsReorder
                || transition.PositionGained
                || transition.PositionLost
                || transition.StatusChanged))
            {
                AnimateChangedRows(viewModel.RankingRows, transitions);
            }
        }

        private void SynchronizeRankingRows(IReadOnlyList<RankingRowViewModel> rows)
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
                    _rankingRows[targetIndex].UpdateFrom(incoming);
                }
            }

            while (_rankingRows.Count > rows.Count)
            {
                _rankingRows.RemoveAt(_rankingRows.Count - 1);
            }
        }

        private void AnimateChangedRows(
            IReadOnlyList<RankingRowViewModel> rows,
            IReadOnlyList<TimingTowerTransition> transitions)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(ReorderDurationMs));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int index = 0; index < rows.Count; index++)
            {
                ContentPresenter? presenter = RankingItems.ItemContainerGenerator.ContainerFromIndex(index) as ContentPresenter;
                if (presenter == null) continue;
                RankingRowViewModel row = rows[index];
                TimingTowerTransition? transition = transitions.FirstOrDefault(item => item.ParticipantIndex == row.ParticipantIndex);
                if (transition != null)
                {
                    if (transition.IsReorder)
                    {
                        TranslateTransform transform = EnsureTranslate(presenter);
                        double fromY = transition.RowDelta * LeftTowerLayoutMetrics.RankingRowPitch + transform.Y;
                        transform.BeginAnimation(
                            TranslateTransform.YProperty,
                            new DoubleAnimation(fromY, 0, duration) { EasingFunction = easing });
                    }

                    if (transition.PositionGained || transition.PositionLost)
                    {
                        FlashRow(presenter, transition.PositionGained ? PositionGainFlashColor : PositionLossFlashColor, sweep: false);
                        RollPositionNumber(presenter, transition.PositionGained);
                    }

                    if (transition.StatusChanged)
                    {
                        PopStatusBadge(presenter);
                        if (transition.BecameFastestLap)
                        {
                            FlashRow(presenter, FastestLapFlashColor, sweep: true);
                        }
                    }
                }

            }
        }

        private void RankingRow_Loaded(object sender, RoutedEventArgs eventArgs)
        {
            if (!(sender is FrameworkElement row) || (bool)row.GetValue(RowEnteredProperty)) return;
            row.SetValue(RowEnteredProperty, true);
            if (!(VisualTreeHelper.GetParent(row) is ContentPresenter presenter)) return;

            // Rows that load within the same short window form one broadcast
            // "tower build" cascade; a single late joiner slides in immediately.
            long now = Environment.TickCount64;
            _entryBatchOrdinal = now - _entryBatchTick > 80 ? 0 : _entryBatchOrdinal + 1;
            _entryBatchTick = now;
            AnimateRowEntry(presenter, _entryBatchOrdinal);
        }

        public static void AnimateRowEntry(UIElement rowContainer, int staggerOrdinal)
        {
            if (rowContainer == null) throw new ArgumentNullException(nameof(rowContainer));
            TranslateTransform transform = EnsureTranslate(rowContainer);
            double fromX = -Math.Round(OverlayUiMetrics.TowerWidth * 0.35);
            TimeSpan delay = TimeSpan.FromMilliseconds(Math.Max(0, staggerOrdinal) * RowEntryStaggerMs);
            var frames = new DoubleAnimationUsingKeyFrames();
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(fromX, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(fromX, KeyTime.FromTimeSpan(delay)));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(RowEntryDurationMs)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            transform.BeginAnimation(TranslateTransform.XProperty, frames);
        }

        private static void FlashRow(ContentPresenter presenter, string color, bool sweep)
        {
            Border? layer = TemplateChild<Border>(presenter, "FlashLayer");
            if (layer == null) return;
            layer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            ScaleTransform scale = EnsureScale(layer);
            if (sweep)
            {
                scale.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(320))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.ScaleX = 1;
            }

            double peak = sweep ? 0.62 : 0.5;
            int hold = sweep ? 320 : 160;
            int total = sweep ? 1200 : 820;
            var frames = new DoubleAnimationUsingKeyFrames();
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(hold))));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(total)),
                new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            layer.BeginAnimation(OpacityProperty, frames);
        }

        private static void RollPositionNumber(ContentPresenter presenter, bool gained)
        {
            TextBlock? position = TemplateChild<TextBlock>(presenter, "PositionText");
            if (position == null) return;
            var duration = new Duration(TimeSpan.FromMilliseconds(280));
            TranslateTransform transform = EnsureTranslate(position);
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(gained ? 14 : -14, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            position.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        }

        private static void PopStatusBadge(ContentPresenter presenter)
        {
            TextBlock? status = TemplateChild<TextBlock>(presenter, "StatusText");
            if (status == null) return;
            ScaleTransform scale = EnsureScale(status);
            var duration = new Duration(TimeSpan.FromMilliseconds(300));
            var easing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.45, 1, duration) { EasingFunction = easing });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.45, 1, duration) { EasingFunction = easing });
        }

        // Transforms declared inside the row DataTemplate are frozen by the XAML
        // loader (shared template values). An animatable copy replaces them once.
        private static TranslateTransform EnsureTranslate(UIElement element)
        {
            if (element.RenderTransform is TranslateTransform existing && !existing.IsFrozen) return existing;
            TranslateTransform created = element.RenderTransform is TranslateTransform frozen ? frozen.Clone() : new TranslateTransform();
            element.RenderTransform = created;
            return created;
        }

        private static ScaleTransform EnsureScale(UIElement element)
        {
            if (element.RenderTransform is ScaleTransform existing && !existing.IsFrozen) return existing;
            ScaleTransform created = element.RenderTransform is ScaleTransform frozen ? frozen.Clone() : new ScaleTransform(1, 1);
            element.RenderTransform = created;
            return created;
        }

        private static T? TemplateChild<T>(ContentPresenter presenter, string name) where T : class
        {
            if (VisualTreeHelper.GetChildrenCount(presenter) == 0) return null;
            try
            {
                return presenter.ContentTemplate?.FindName(name, presenter) as T;
            }
            catch (InvalidOperationException)
            {
                // The template is not applied to this presenter yet; skip the accent.
                return null;
            }
        }
    }
}
