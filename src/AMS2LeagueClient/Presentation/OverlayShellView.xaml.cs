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
            OverlayLayout layout = OverlayLayoutCalculator.Calculate(width, height);
            TimingZone.Margin = new Thickness(layout.TowerLeftInset, layout.TowerTopInset, 0, 0);
            SessionZone.Margin = new Thickness(
                layout.TowerLeftInset + LeftTowerLayoutMetrics.Width + LeftTowerLayoutMetrics.SessionGap,
                layout.TowerTopInset,
                0,
                0);
            RaceControlZone.Margin = new Thickness(0, layout.TopInset, 0, 0);
            EventZone.Margin = new Thickness(0, 0, 0, layout.BottomInset);
        }

        public void SetViewModel(OverlayShellViewModel viewModel, bool animate)
        {
            DataContext = viewModel;
            TimingZone.SetViewModel(viewModel.Timing);
            if (!animate) return;
            if (viewModel.EventCard.IsVisible && viewModel.EventCard.EventId != _eventId)
            {
                AnimateIn(EventZone, 18);
            }
            if (viewModel.RaceControl.IsVisible && viewModel.RaceControl.EventId != _raceControlId)
            {
                AnimateIn(RaceControlZone, -12);
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
