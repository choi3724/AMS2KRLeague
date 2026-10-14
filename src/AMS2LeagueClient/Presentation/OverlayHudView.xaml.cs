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

        private ItemsControl RankingItems = null!;
        private bool? _racingDesign;
        private DrivingHudSettings.TextAppearance _textAppearance = new DrivingHudSettings.TextAppearance();
        private OverlayViewModel? _lastViewModel;
        public OverlayHudView()
        {
            InitializeComponent();
            SetRacingDesign(false);
        }

        public void SetRacingDesign(bool racing)
        {
            if (_racingDesign == racing) return;
            _racingDesign = racing;
            TowerContent.Template = (ControlTemplate)FindResource(racing ? "RacingTowerTemplate" : "LegacyTowerTemplate");
            TowerContent.ApplyTemplate();
            RankingItems = (ItemsControl)TowerContent.Template.FindName("RankingItems", TowerContent);
            RankingItems.ItemsSource = _rankingRows;
            OverlayTextStyler.Apply(this, _textAppearance);
            Dispatcher.BeginInvoke(new Action(ApplyTowerSizing));
        }

        public void ApplyTypography(DrivingHudSettings.TextAppearance appearance)
        {
            _textAppearance = appearance;
            OverlayTextStyler.Apply(this, appearance);
            ApplyTowerSizing();
        }

        public void SetViewModel(OverlayViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            _lastViewModel = viewModel;
            int visibleRows = Math.Min(viewModel.RankingRowCapacity, viewModel.RankingRows.Count);
            double requiredHeight = LeftTowerLayoutMetrics.RequiredHeightForRows(visibleRows, viewModel.IsDiagnostic)
                + (_textAppearance.TowerRowHeight - 36) * visibleRows;
            bool capacityChanged = Height != requiredHeight;
            Height = requiredHeight;
            DataContext = viewModel;

            string rankingKey = viewModel.RankingRangeText + RecordSeparator + string.Join(
                UnitSeparator,
                viewModel.RankingRows.Select(row => row.ParticipantIndex + "|" + row.Position + "|" + row.Name + "|"
                    + row.Class + "|" + row.CurrentTime + "|" + row.TowerTimeText + "|" + row.Status + "|" + row.IsPlayer + "|" + row.DisplayState
                    + "|" + row.ClassBackground + "|" + row.Foreground + "|" + row.TimeForeground
                    + "|" + row.PenaltyText + "|" + row.TimeOrGridPenaltyText + "|" + row.StatusColor));
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
                        double fromY = transition.RowDelta * (LeftTowerLayoutMetrics.RankingRowPitch + _textAppearance.TowerRowHeight - 36) + transform.Y;
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
            if (sender is FrameworkElement textRow)
            {
                OverlayTextStyler.Apply(textRow, _textAppearance);
                textRow.Height = _textAppearance.TowerRowHeight;
                if (FindTagged<Grid>(textRow, "tower-columns") is { } columns) ApplyColumnWidths(columns);
                if (FindTagged<Grid>(textRow, "tower-background-racing") is { } background) ApplyBackgroundWidths(background);
            }
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

        private static readonly string[] LegacyColumnOrder =
            { "position", "driver", "class", "time", "status", "pitPenalty", "numericPenalty" };
        private static readonly string[] RacingColumnOrder =
            { "position", "driver", "class", "status", "time", "pitPenalty", "numericPenalty" };

        private double[] GetColumnWidths(double total)
        {
            bool racing = _racingDesign == true;
            double[] baseline = racing
                ? new[] { 24d, 0d, 100d, 60d, 104d, 94d, 88d }
                : new[] { 36d, 0d, 100d, 100d, 52d, 112d, 88d };
            baseline[1] = Math.Max(1, total - baseline.Sum());
            string[] order = racing ? RacingColumnOrder : LegacyColumnOrder;
            double[] widths = baseline.Select((width, index) => width *
                (_textAppearance.TowerColumnScales.TryGetValue(order[index], out double factor) ? factor : 1)).ToArray();
            double sum = widths.Sum();
            return widths.Select(width => width * total / sum).ToArray();
        }

        private static T? FindTagged<T>(DependencyObject root, string tag) where T : FrameworkElement
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T match && Equals(match.Tag, tag)) return match;
                if (FindTagged<T>(child, tag) is { } nested) return nested;
            }
            return null;
        }

        private Grid? FirstRowColumns()
        {
            if (!(RankingItems.ItemContainerGenerator.ContainerFromIndex(0) is ContentPresenter presenter)) return null;
            return FindTagged<Grid>(presenter, "tower-columns");
        }

        private void ApplyTowerSizing()
        {
            if (RankingItems == null) return;
            if (_lastViewModel != null)
            {
                int rows = Math.Min(_lastViewModel.RankingRowCapacity, _lastViewModel.RankingRows.Count);
                Height = LeftTowerLayoutMetrics.RequiredHeightForRows(rows, _lastViewModel.IsDiagnostic)
                    + (_textAppearance.TowerRowHeight - 36) * rows;
            }
            if (FindTagged<Grid>(TowerContent, _racingDesign == true ? "tower-header-racing" : "tower-header-legacy") is { } header)
                ApplyHeaderWidths(header);
            for (int index = 0; index < _rankingRows.Count; index++)
            {
                if (!(RankingItems.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter presenter)) continue;
                if (FindTagged<Border>(presenter, "tower-data-row") is { } row) row.Height = _textAppearance.TowerRowHeight;
                if (FindTagged<Grid>(presenter, "tower-columns") is { } columns) ApplyColumnWidths(columns);
                if (FindTagged<Grid>(presenter, "tower-background-racing") is { } background) ApplyBackgroundWidths(background);
            }
        }

        private void ApplyColumnWidths(Grid columns)
        {
            double[] widths = GetColumnWidths(columns.ActualWidth > 0 ? columns.ActualWidth : _racingDesign == true ? 616 : 616);
            for (int index = 0; index < columns.ColumnDefinitions.Count; index++)
                columns.ColumnDefinitions[index].Width = new GridLength(widths[index]);
            if (FindTagged<Grid>(TowerContent, _racingDesign == true ? "tower-header-racing" : "tower-header-legacy") is { } header)
                ApplyHeaderWidths(header);
        }

        private void ApplyHeaderWidths(Grid header)
        {
            double total = _racingDesign == true ? header.ActualWidth :
                FirstRowColumns()?.ActualWidth ?? 616;
            if (total <= 0) total = 616;
            double[] widths = GetColumnWidths(total);
            double[] headerWidths = _racingDesign == true
                ? new[] { widths[0] + widths[1], widths[2], widths[3], widths[4], widths[5], widths[6] }
                : new[] { widths[3], widths[4], widths[5], widths[6] };
            for (int index = 0; index < header.ColumnDefinitions.Count; index++)
                header.ColumnDefinitions[index].Width = new GridLength(headerWidths[index]);
        }

        private void ApplyBackgroundWidths(Grid background)
        {
            double[] widths = GetColumnWidths(background.ActualWidth > 0 ? background.ActualWidth : 616);
            double[] backgroundWidths = { widths[0] + widths[1] + widths[2], widths[3], widths[4], widths[5], widths[6] };
            for (int index = 0; index < background.ColumnDefinitions.Count; index++)
                background.ColumnDefinitions[index].Width = new GridLength(backgroundWidths[index]);
        }

        public bool TryGetResizeGeometry(out double[] columnBoundaries, out double top, out double rowBottom)
        {
            columnBoundaries = Array.Empty<double>(); top = rowBottom = 0;
            Grid? columns = FirstRowColumns();
            if (columns == null || columns.ActualWidth <= 0) return false;
            Border? row = FindTagged<Border>(RankingItems.ItemContainerGenerator.ContainerFromIndex(0)!, "tower-data-row");
            if (row == null) return false;
            Point rowOrigin, columnOrigin;
            try
            {
                rowOrigin = row.TransformToAncestor(this).Transform(new Point(0, 0));
                columnOrigin = columns.TransformToAncestor(this).Transform(new Point(0, 0));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            top = rowOrigin.Y - 22;
            rowBottom = rowOrigin.Y + row.ActualHeight;
            columnBoundaries = new double[6];
            double x = columnOrigin.X;
            for (int index = 0; index < columnBoundaries.Length; index++)
            {
                x += columns.ColumnDefinitions[index].ActualWidth;
                columnBoundaries[index] = x;
            }
            return true;
        }

        public bool ResizeColumn(int boundary, double logicalDelta)
        {
            Grid? columns = FirstRowColumns();
            if (columns == null || boundary < 0 || boundary >= 6) return false;
            double[] widths = GetColumnWidths(columns.ActualWidth);
            double[] minimum = _racingDesign == true
                ? new[] { 18d, 70d, 65d, 42d, 60d, 65d, 60d }
                : new[] { 22d, 70d, 65d, 60d, 42d, 75d, 60d };
            double lower = minimum[boundary] - widths[boundary];
            double upper = widths[boundary + 1] - minimum[boundary + 1];
            if (lower > upper) return false;
            double delta = Math.Clamp(logicalDelta, lower, upper);
            if (Math.Abs(delta) < 0.1) return false;
            widths[boundary] += delta;
            widths[boundary + 1] -= delta;
            double[] baseline = _racingDesign == true
                ? new[] { 24d, 0d, 100d, 60d, 104d, 94d, 88d }
                : new[] { 36d, 0d, 100d, 100d, 52d, 112d, 88d };
            baseline[1] = Math.Max(1, columns.ActualWidth - baseline.Sum());
            string[] order = _racingDesign == true ? RacingColumnOrder : LegacyColumnOrder;
            for (int index = 0; index < widths.Length; index++)
                _textAppearance.TowerColumnScales[order[index]] = Math.Clamp(widths[index] / baseline[index], 0.5, 2);
            ApplyTowerSizing();
            return true;
        }

        public bool ResizeRows(double logicalDelta)
        {
            double next = Math.Clamp(_textAppearance.TowerRowHeight + logicalDelta, 28, 64);
            if (Math.Abs(next - _textAppearance.TowerRowHeight) < 0.1) return false;
            _textAppearance.TowerRowHeight = next;
            ApplyTowerSizing();
            return true;
        }

        public DrivingHudSettings.TextAppearance CurrentTextAppearance => _textAppearance;

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
