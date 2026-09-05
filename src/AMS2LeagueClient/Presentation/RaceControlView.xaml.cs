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

        private string _eventId = string.Empty;
        private string _stateLabel = string.Empty;
        private bool _presented;

        public RaceControlView() => InitializeComponent();

        public TimeSpan SetViewModel(RaceControlViewModel viewModel, bool animate)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (viewModel.IsVisible)
            {
                DataContext = viewModel;
                if (animate && viewModel.EventId != _eventId)
                {
                    AnimateIn();
                }
                else if (animate && !string.Equals(viewModel.StateLabel, _stateLabel, StringComparison.Ordinal))
                {
                    HudMotion.Pop(StateLabelText, 1.25, 280);
                }
                else if (!animate)
                {
                    ResetMotion();
                }
                _eventId = viewModel.EventId;
                _stateLabel = viewModel.StateLabel;
                _presented = true;
                return TimeSpan.Zero;
            }

            bool wasPresented = _presented;
            _eventId = string.Empty;
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
