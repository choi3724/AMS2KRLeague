using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    /// <summary>
    /// Bottom event card ("flash" banner). A new event slides in from the left
    /// with a growing accent bar and staggered body text; when the event ends the
    /// card slides back out. <see cref="SetViewModel"/> returns the exit
    /// animation length so the host window can keep the surface visible until
    /// the animation has finished.
    /// </summary>
    public partial class EventCardView : UserControl
    {
        public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(220);

        private string _eventId = string.Empty;
        private bool _presented;

        public EventCardView() => InitializeComponent();

        public TimeSpan SetViewModel(EventCardViewModel viewModel, bool animate)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (viewModel.IsVisible)
            {
                DataContext = viewModel;
                if (animate && viewModel.EventId != _eventId) AnimateIn();
                else if (!animate) ResetMotion();
                _eventId = viewModel.EventId;
                _presented = true;
                return TimeSpan.Zero;
            }

            bool wasPresented = _presented;
            _eventId = string.Empty;
            _presented = false;
            if (animate && wasPresented)
            {
                // Keep the last content on screen while it slides out.
                AnimateOut();
                return ExitDuration;
            }

            DataContext = viewModel;
            ResetMotion();
            return TimeSpan.Zero;
        }

        private void AnimateIn()
        {
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = new Duration(TimeSpan.FromMilliseconds(320));
            Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(240))) { EasingFunction = easing });
            if (Panel.RenderTransform is TranslateTransform panel)
            {
                panel.BeginAnimation(TranslateTransform.YProperty, null);
                panel.Y = 0;
                panel.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-28, 0, duration) { EasingFunction = easing });
            }
            HudMotion.GrowY(AccentBar, 300);
            HudMotion.SlideIn(Body, -16, 0, 380);
            HudMotion.SlideIn(Aside, 12, 0, 380);
        }

        private void AnimateOut()
        {
            HudMotion.SlideOut(Panel, -24, 0, (int)ExitDuration.TotalMilliseconds);
        }

        private void ResetMotion()
        {
            Panel.BeginAnimation(OpacityProperty, null);
            Panel.Opacity = 1;
            if (Panel.RenderTransform is TranslateTransform panel)
            {
                panel.BeginAnimation(TranslateTransform.XProperty, null);
                panel.BeginAnimation(TranslateTransform.YProperty, null);
                panel.X = 0;
                panel.Y = 0;
            }
        }
    }
}
