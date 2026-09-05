using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    /// <summary>
    /// Race-control banner. A new message drops in from above with a flag-colour
    /// sweep; a flag-state change alone pops the state label; when the banner is
    /// dismissed it slides back up. <see cref="SetViewModel"/> returns the exit
    /// animation length so the host window keeps the surface until it finishes.
    /// </summary>
    public partial class RaceControlView : UserControl
    {
        public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(200);

        private string _stateLabel = string.Empty;
        private string _messageKey = string.Empty;
        private bool _presented;

        public RaceControlView()
        {
            InitializeComponent();
            DataContextChanged += (sender, args) => FitTextToCard();
            SizeChanged += (sender, args) => FitTextToCard();
        }

        private void FitTextToCard()
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            bool expanded = (DataContext as RaceControlViewModel)?.IsExpanded == true;
            bool wide = ActualWidth >= ActualHeight * 3.5;
            bool inline = wide || (!expanded && ActualWidth >= 250 && ActualWidth >= ActualHeight * 2);
            Panel.Padding = wide ? new Thickness(10, 3, 10, 3) : new Thickness(14, 8, 14, 8);
            ContentFit.Margin = new Thickness(0, wide ? 2 : 4, 0, wide ? 2 : 4);
            Body.ColumnDefinitions[0].Width = new GridLength(expanded && wide ? 0.3 : 0.55, GridUnitType.Star);
            Body.ColumnDefinitions[1].Width = new GridLength(expanded && wide ? 0.7 : 0.45, GridUnitType.Star);
            Grid.SetColumnSpan(Heading, inline ? 1 : 2);
            Grid.SetRow(ExpandedContent, wide ? 0 : 1);
            Grid.SetColumn(ExpandedContent, wide ? 1 : 0);
            Grid.SetColumnSpan(ExpandedContent, wide ? 1 : 2);
            Grid.SetRowSpan(ExpandedContent, wide ? 2 : 1);
            Grid.SetRow(StateLabelText, inline ? (expanded ? 1 : 0) : 2);
            Grid.SetColumn(StateLabelText, inline && !expanded ? 1 : 0);
            Grid.SetColumnSpan(StateLabelText, inline ? 1 : 2);
            HistoryText.Visibility = ActualHeight >= 120 && !wide ? Visibility.Visible : Visibility.Collapsed;
            ExpandedContent.Margin = wide ? new Thickness(10, 0, 0, 0) : new Thickness(0, 4, 0, 0);
            double designWidth = expanded ? OverlayUiMetrics.RaceControlExpandedWidth : OverlayUiMetrics.RaceControlCompactWidth;
            double designHeight = expanded ? OverlayUiMetrics.RaceControlExpandedHeight : OverlayUiMetrics.RaceControlCompactHeight;
            double scale = Math.Clamp(Math.Sqrt(ActualWidth * ActualHeight / (designWidth * designHeight)), 0.5, 3);
            Body.Width = Math.Max(1, ActualWidth - Panel.Padding.Left - Panel.Padding.Right
                - Panel.BorderThickness.Left - Panel.BorderThickness.Right);
            TitleText.FontSize = DriverText.FontSize = OverlayUiMetrics.FontEmphasis * scale;
            MessageText.FontSize = 17 * scale;
            HistoryText.FontSize = CountText.FontSize = OverlayUiMetrics.FontTiny * scale;
            StateLabelText.FontSize = OverlayUiMetrics.FontBody * scale;
            StateLabelText.MaxWidth = Body.Width * (inline ? (expanded ? 0.3 : 0.45) : 1);
        }

        public TimeSpan SetViewModel(RaceControlViewModel viewModel, bool animate)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (viewModel.IsVisible)
            {
                string messageKey = viewModel.Title + "\u001f" + viewModel.DriverLine
                    + "\u001f" + viewModel.Message + "\u001f" + viewModel.Accent;
                DataContext = viewModel;
                // Snapshot/history versions are not new messages. Expiry from an
                // expanded alert to its continuing flag must not replay entrance.
                if (animate && (!_presented
                    || (viewModel.IsExpanded && messageKey != _messageKey)))
                {
                    AnimateIn();
                }
                else if (animate && !string.Equals(viewModel.StateLabel, _stateLabel, StringComparison.Ordinal))
                {
                    HudMotion.Pop(StateLabelText, 1.08, 280);
                }
                else if (!animate)
                {
                    ResetMotion();
                }
                if (viewModel.IsExpanded || !_presented) _messageKey = messageKey;
                _stateLabel = viewModel.StateLabel;
                _presented = true;
                return TimeSpan.Zero;
            }

            bool wasPresented = _presented;
            _messageKey = string.Empty;
            _stateLabel = string.Empty;
            _presented = false;
            if (animate && wasPresented)
            {
                HudMotion.SlideOut(Panel, 0, -12, (int)ExitDuration.TotalMilliseconds);
                return ExitDuration;
            }

            DataContext = viewModel;
            ResetMotion();
            return TimeSpan.Zero;
        }

        private void AnimateIn()
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(300));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
            if (Panel.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, duration) { EasingFunction = easing });
            }
            HudMotion.Sweep(FlagSweep, 0.42, 360, 700);
            HudMotion.SlideIn(Body, -10, 0, 360);
        }

        private void ResetMotion()
        {
            Panel.BeginAnimation(OpacityProperty, null);
            Panel.Opacity = 1;
            if (Panel.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.X = 0;
                transform.Y = 0;
            }
        }
    }
}
