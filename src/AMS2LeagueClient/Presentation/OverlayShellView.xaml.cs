using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public partial class OverlayShellView : UserControl
    {
        private string _eventId = string.Empty;
        private string _raceControlId = string.Empty;

        public OverlayShellView()
        {
            InitializeComponent();
            EventZone.RenderTransform = new TranslateTransform();
            RaceControlZone.RenderTransform = new TranslateTransform();
        }

        public void SetLayout(double width, double height)
        {
            OverlayComponentLayout layout = OverlayComponentLayoutCalculator.Calculate(
                (int)Math.Round(width),
                (int)Math.Round(height),
                96,
                false,
                true);
            TimingZone.Width = layout.Timing.Width;
            TimingZone.Height = layout.Timing.Height;
            TimingZone.Margin = new Thickness(layout.Timing.X, layout.Timing.Y, 0, 0);
            SessionZone.Width = layout.Session.Width;
            SessionZone.Height = layout.Session.Height;
            SessionZone.Margin = new Thickness(layout.Session.X, layout.Session.Y, 0, 0);
            RaceControlZone.Width = layout.RaceControl.Width;
            RaceControlZone.Height = layout.RaceControl.Height;
            RaceControlZone.Margin = new Thickness(layout.RaceControl.X, layout.RaceControl.Y, 0, 0);
            EventZone.Width = layout.EventCard.Width;
            EventZone.Height = layout.EventCard.Height;
            EventZone.Margin = new Thickness(0, 0, 0, layout.BottomInset);
        }

        public void SetViewModel(OverlayShellViewModel viewModel, bool animate)
        {
            DataContext = viewModel;
            TimingZone.SetViewModel(viewModel.Timing);
            if (!animate) return;
            if (viewModel.EventCard.IsVisible && viewModel.EventCard.EventId != _eventId)
            {
                AnimateIn(EventZone, 14);
            }
            if (viewModel.RaceControl.IsVisible && viewModel.RaceControl.EventId != _raceControlId)
            {
                AnimateIn(RaceControlZone, -10);
            }
            _eventId = viewModel.EventCard.EventId;
            _raceControlId = viewModel.RaceControl.EventId;
        }

        private static void AnimateIn(UIElement element, double fromY)
        {
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var duration = new Duration(TimeSpan.FromMilliseconds(280));
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
            if (element.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, duration) { EasingFunction = easing });
            }
        }
    }
}
