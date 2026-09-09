using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public sealed class VrSettingsWindow : Window
    {
        private readonly ComboBox _output = new ComboBox { MinWidth = 310 };
        private readonly CheckBox _follow = new CheckBox { Content = "고개를 따라 움직이기", Foreground = Brushes.White };
        private readonly Dictionary<string, Slider> _sliders = new Dictionary<string, Slider>();
        public VrHudSettings Settings { get; private set; }

        public VrSettingsWindow(VrHudSettings current, Action<VrHudSettings> apply, Action recenter)
        {
            Settings = current.Normalize();
            Title = "모니터·VR 표시 설정";
            Width = 590; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName);
            Background = new SolidColorBrush(Color.FromRgb(15, 26, 39)); Foreground = Brushes.White;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "VR 표시 · 시험 기능",
                FontSize = 20, Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "SteamVR로 실행한 AMS2를 지원합니다. 다른 VR 실행 방식은 아직 지원하지 않습니다.\n실제 헤드셋에서의 표시·성능은 검증 전입니다.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 15)
            });
            _output.ItemsSource = new[] { "모니터 표시", "VR 표시", "모니터 + VR 동시 표시" };
            _output.SelectedIndex = (int)Settings.Output;
            panel.Children.Add(_output);
            panel.Children.Add(new TextBlock { Text = "VR 패널 위치 · 미터 / 각도 · 도", Margin = new Thickness(0, 14, 0, 6) });
            AddSlider(panel, "너비", Settings.WidthMetres, 0.5, 4);
            AddSlider(panel, "거리", Settings.DistanceMetres, 0.5, 5);
            AddSlider(panel, "좌우", Settings.HorizontalMetres, -2, 2);
            AddSlider(panel, "상하", Settings.VerticalMetres, -2, 2);
            AddSlider(panel, "좌우 각도", Settings.YawDegrees, -60, 60);
            AddSlider(panel, "상하 각도", Settings.PitchDegrees, -60, 60);
            _follow.IsChecked = Settings.FollowHead; _follow.Margin = new Thickness(0, 12, 0, 8);
            panel.Children.Add(_follow);
            panel.Children.Add(new TextBlock
            {
                Text = "체크를 해제하면 적용 당시 바라보는 방향에 고정됩니다.\n개별 UI 배치는 상태창의 레이아웃 편집을 이용하세요.",
                Foreground = Brushes.LightGray
            });
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) };
            panel.Children.Add(hint);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "적용", Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(4) };
            save.Click += (_, __) =>
            {
                try
                {
                    var next = new VrHudSettings
                    {
                        Output = (OverlayOutputMode)_output.SelectedIndex,
                        WidthMetres = _sliders["너비"].Value, DistanceMetres = _sliders["거리"].Value,
                        HorizontalMetres = _sliders["좌우"].Value, VerticalMetres = _sliders["상하"].Value,
                        YawDegrees = _sliders["좌우 각도"].Value, PitchDegrees = _sliders["상하 각도"].Value,
                        FollowHead = _follow.IsChecked == true
                    }.Normalize();
                    apply(next); Settings = next; hint.Text = "저장하고 적용했습니다.";
                }
                catch { hint.Text = "저장하지 못했습니다. 설정 폴더의 권한과 여유 공간을 확인해 주세요."; }
            };
            var center = new Button { Content = "정면 재설정", Padding = new Thickness(15, 7, 15, 7), Margin = new Thickness(4) };
            center.Click += (_, __) => { recenter(); hint.Text = "현재 바라보는 방향을 기준으로 다시 배치합니다."; };
            var close = new Button { Content = "닫기", Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(4) };
            close.Click += (_, __) => Close();
            buttons.Children.Add(save); buttons.Children.Add(center); buttons.Children.Add(close);
            panel.Children.Add(buttons);
            Content = new Border { Padding = new Thickness(22), Background = Background, Child = panel };
        }

        private void AddSlider(Panel panel, string label, double value, double min, double max)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            row.Children.Add(new TextBlock { Text = label, Width = 85, VerticalAlignment = VerticalAlignment.Center });
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = max > 10 ? 1 : 0.1 };
            var number = new TextBlock { Width = 60, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            number.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = slider, StringFormat = "{0:0.0}" });
            DockPanel.SetDock(number, Dock.Right); row.Children.Add(number); row.Children.Add(slider);
            _sliders.Add(label, slider); panel.Children.Add(row);
        }
    }
}
