using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public partial class EventCardView : UserControl
    {
        private string _eventId = string.Empty;
        public EventCardView() => InitializeComponent();

        public void SetViewModel(EventCardViewModel viewModel, bool animate)
        {
            DataContext = viewModel;
            if (animate && viewModel.IsVisible && viewModel.EventId != _eventId) AnimateIn();
            _eventId = viewModel.EventId;
        }

        private void AnimateIn()
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(280));
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
            if (Panel.RenderTransform is TranslateTransform transform) transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, duration) { EasingFunction = easing });
        }
    }
}
