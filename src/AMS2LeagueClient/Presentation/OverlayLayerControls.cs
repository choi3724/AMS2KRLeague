using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    internal sealed class OverlayLayerControls
    {
        private List<string> _order;
        private readonly string _component;
        private readonly IReadOnlyList<string> _visibleComponents;
        private readonly TextBlock _position = new TextBlock { Foreground = Brushes.LightGray, FontSize = 11 };
        private readonly Button _back = Button("맨 뒤로");
        private readonly Button _backward = Button("뒤로");
        private readonly Button _forward = Button("앞으로");
        private readonly Button _front = Button("맨 앞으로");

        public OverlayLayerControls(string component, IReadOnlyList<string> current, IReadOnlyList<string>? visibleComponents = null)
        {
            _component = component;
            _order = OverlayLayerOrder.Normalize(current);
            _visibleComponents = visibleComponents ?? OverlayComponentKeys.All;
            var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 10) };
            panel.Children.Add(new TextBlock { Text = "겹침 순서", FontSize = 16, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(new TextBlock { Text = "다른 오버레이와 겹칠 때 표시되는 앞뒤 순서입니다.",
                Foreground = Brushes.LightGray, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            _position.Margin = new Thickness(0, 6, 0, 6);
            panel.Children.Add(_position);
            var actions = new WrapPanel();
            actions.Children.Add(_back);
            actions.Children.Add(_backward);
            actions.Children.Add(_forward);
            actions.Children.Add(_front);
            panel.Children.Add(actions);
            _back.Click += (_, __) => Move(OverlayLayerMove.Back);
            _backward.Click += (_, __) => Move(OverlayLayerMove.Backward);
            _forward.Click += (_, __) => Move(OverlayLayerMove.Forward);
            _front.Click += (_, __) => Move(OverlayLayerMove.Front);
            View = panel;
            Refresh();
        }

        public FrameworkElement View { get; }
        public IReadOnlyList<string> Order => _order;
        public event EventHandler? Changed;

        private void Move(OverlayLayerMove direction)
        {
            var next = OverlayLayerOrder.Move(_order, _component, direction, _visibleComponents);
            if (next.IndexOf(_component) == _order.IndexOf(_component)) return;
            _order = next;
            Refresh();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Refresh()
        {
            int index = _order.IndexOf(_component);
            _position.Text = "현재 " + (index + 1) + " / " + _order.Count + " · 숫자가 클수록 앞에 표시";
            _back.IsEnabled = index > 0;
            _backward.IsEnabled = OverlayLayerOrder.Move(_order, _component, OverlayLayerMove.Backward, _visibleComponents).IndexOf(_component) != index;
            _forward.IsEnabled = OverlayLayerOrder.Move(_order, _component, OverlayLayerMove.Forward, _visibleComponents).IndexOf(_component) != index;
            _front.IsEnabled = index < _order.Count - 1;
        }

        private static Button Button(string label)
        {
            var button = new Button { Content = label, MinWidth = 83, Height = 30, Margin = new Thickness(0, 0, 5, 0) };
            System.Windows.Automation.AutomationProperties.SetName(button, "겹침 순서 " + label);
            return button;
        }
    }
}
