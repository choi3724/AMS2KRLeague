using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public sealed class DrivingHudSettingsWindow : Window
    {
        private readonly OverlayLayerControls? _layers;
        private readonly Slider _frameLimit = new Slider { Minimum = DrivingHudSettings.MinimumHudFrameLimit, Maximum = DrivingHudSettings.MaximumHudFrameLimit,
            SmallChange = 1, LargeChange = 10, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 200 };
        public DrivingHudSettings Settings { get; private set; }
        public IReadOnlyList<string> LayerOrder => _layers?.Order ?? OverlayComponentKeys.All;
        public event EventHandler? LayerOrderChanged;

        public DrivingHudSettingsWindow(DrivingHudSettings current, string vehicleName = "", double? engineMaximum = null,
            string profileVehicleName = "", string? layerComponent = null, IReadOnlyList<string>? layerOrder = null,
            IReadOnlyList<string>? visibleComponents = null)
        {
            Settings = current.Normalize();
            Title = "환경설정";
            FontFamily = DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName);
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/AMS2LeagueClient;component/Assets/AppIcon.ico"));
            Width = 520; MaxHeight = SystemParameters.WorkArea.Height; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Topmost = layerComponent != null;
            Background = new SolidColorBrush(Color.FromRgb(15, 26, 39)); Foreground = Brushes.White;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "HUD 프레임 제한", FontSize = 17, Margin = new Thickness(0, 0, 0, 10) });
            _frameLimit.Value = Settings.HudFrameLimit;
            var frameLimitValue = new TextBlock { Text = Settings.HudFrameLimit + " FPS", VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
            _frameLimit.ValueChanged += (_, __) => frameLimitValue.Text = ((int)_frameLimit.Value) + " FPS";
            var frameLimitPanel = new StackPanel { Orientation = Orientation.Horizontal };
            frameLimitPanel.Children.Add(_frameLimit); frameLimitPanel.Children.Add(frameLimitValue);
            System.Windows.Automation.AutomationProperties.SetName(_frameLimit, "최대 FPS");
            panel.Children.Add(Row("최대 FPS", frameLimitPanel));
            panel.Children.Add(new TextBlock { Text = "HUD를 1초에 최대 몇 번 새로 그릴지 정합니다. 높을수록 계기판과 순위표 움직임이 부드러워지지만 그만큼 CPU를 더 사용합니다. "
                + "값이 바뀌지 않으면 다시 그리지 않습니다. 순위 이동 같은 일부 효과는 프로그램을 다시 실행하면 적용됩니다.",
                FontSize = 11, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "위치·크기는 메인 창의 레이아웃 편집에서 조절합니다.",
                FontSize = 12, Foreground = Brushes.LightGray, Margin = new Thickness(0, 14, 0, 14) });
            panel.Children.Add(new TextBlock { Text = "N 계기판 · 차량별 RPM 보정", FontSize = 17, Margin = new Thickness(0,14,0,10) });
            panel.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(vehicleName) ? "차량 미확인 · 게임에서 차량을 선택한 뒤 설정하세요." : vehicleName,
                TextWrapping = TextWrapping.Wrap });
            Settings.AvanteVehicles.TryGetValue(vehicleName, out var calibration);
            var scale = AvanteRpmScale.Resolve(engineMaximum, calibration, string.IsNullOrEmpty(profileVehicleName) ? vehicleName : profileVehicleName);
            var automaticScale = AvanteRpmScale.Resolve(engineMaximum);
            var sourceText = new TextBlock { FontSize = 11, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,10) };
            var custom = new CheckBox { Content = "이 차량의 눈금·경고 RPM 직접 지정", IsChecked = calibration != null, Foreground = Brushes.White,
                IsEnabled = !string.IsNullOrWhiteSpace(vehicleName), Margin = new Thickness(0,8,0,8) };
            panel.Children.Add(custom);
            var maximumMode = new ComboBox { Width = 200, ItemsSource = new[] { "레드존 기준 자동", "최대 눈금 수동 지정" },
                SelectedIndex = calibration != null && !calibration.AutomaticMaximum ? 1 : 0 };
            panel.Children.Add(Row("최대 눈금 결정", maximumMode));
            var maximumBox = new TextBox { Text = scale.Maximum.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), Width = 140 };
            var yellowBox = new TextBox { Text = double.IsFinite(scale.YellowStart) ? scale.YellowStart.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "", Width = 140 };
            var redBox = new TextBox { Text = double.IsFinite(scale.RedStart) ? scale.RedStart.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "", Width = 140 };
            double manualMaximum = calibration?.Maximum ?? scale.Maximum;
            int previousMaximumMode = maximumMode.SelectedIndex;
            void UpdateAutomaticMaximum()
            {
                if (custom.IsChecked == true && maximumMode.SelectedIndex == 0
                    && double.TryParse(redBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double red)
                    && double.IsFinite(red) && red > 0 && red < 100000)
                    maximumBox.Text = AvanteRpmScale.MaximumAboveRed(red).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }
            void EnableCalibration()
            {
                bool enabled = custom.IsEnabled && custom.IsChecked == true;
                maximumMode.IsEnabled = yellowBox.IsEnabled = redBox.IsEnabled = enabled;
                maximumBox.IsEnabled = enabled && maximumMode.SelectedIndex == 1;
                UpdateAutomaticMaximum();
                sourceText.Text = (enabled ? "저장된 차량별 직접 지정값을 우선 적용합니다." : automaticScale.SourceDescription)
                    + "\n직접 지정 해제 시 공통 90%·97% 표시 정책으로 복귀합니다. SHM이 제공한 경고 경계값은 아닙니다.";
            }
            maximumMode.SelectionChanged += (_, __) =>
            {
                if (previousMaximumMode == 1 && double.TryParse(maximumBox.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double manual) && double.IsFinite(manual) && manual >= 1000 && manual <= 100000)
                    manualMaximum = manual;
                if (maximumMode.SelectedIndex == 1) maximumBox.Text = manualMaximum.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                previousMaximumMode = maximumMode.SelectedIndex;
                EnableCalibration();
            };
            redBox.TextChanged += (_, __) => UpdateAutomaticMaximum();
            custom.Checked += (_, __) => EnableCalibration();
            custom.Unchecked += (_, __) =>
            {
                maximumMode.SelectedIndex = 0;
                maximumBox.Text = automaticScale.Maximum.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                yellowBox.Text = double.IsFinite(automaticScale.YellowStart) ? automaticScale.YellowStart.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
                redBox.Text = double.IsFinite(automaticScale.RedStart) ? automaticScale.RedStart.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
                EnableCalibration();
            };
            EnableCalibration();
            panel.Children.Add(Row("최대 표시 눈금 (RPM)", maximumBox));
            panel.Children.Add(Row("노랑 시작 (RPM)", yellowBox)); panel.Children.Add(Row("빨강 시작 (RPM)", redBox));
            panel.Children.Add(sourceText);
            var validation = new TextBlock { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(validation);
            maximumBox.TextChanged += (_, __) => validation.Text = "";
            yellowBox.TextChanged += (_, __) => validation.Text = "";
            redBox.TextChanged += (_, __) => validation.Text = "";
            maximumMode.SelectionChanged += (_, __) => validation.Text = "";
            if (layerComponent != null)
            {
                _layers = new OverlayLayerControls(layerComponent, layerOrder ?? OverlayComponentKeys.All, visibleComponents);
                _layers.Changed += (_, __) => LayerOrderChanged?.Invoke(this, EventArgs.Empty);
                panel.Children.Add(_layers.View);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "저장", IsDefault = true, Width = 90, Height = 32, Margin = new Thickness(5) };
            save.Click += (sender, args) =>
            {
                if (custom.IsEnabled && custom.IsChecked == true)
                {
                    bool Parse(string text, out double value) => double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value);
                    if (!Parse(maximumBox.Text, out double maximum) || !Parse(yellowBox.Text, out double yellow) || !Parse(redBox.Text, out double red)
                        || !new AvanteRpmCalibration { Maximum = maximum, YellowStart = yellow, RedStart = red, AutomaticMaximum = maximumMode.SelectedIndex == 0 }.IsValid)
                    { validation.Text = "0 ≤ 노랑 시작 < 빨강 시작 < 최대 눈금(1,000~100,000 RPM)으로 입력하세요."; return; }
                    Settings.AvanteVehicles[vehicleName] = new AvanteRpmCalibration { Maximum = maximumMode.SelectedIndex == 0 ? manualMaximum : maximum, YellowStart = yellow, RedStart = red, AutomaticMaximum = maximumMode.SelectedIndex == 0 };
                }
                else if (custom.IsEnabled) Settings.AvanteVehicles.Remove(vehicleName);
                Settings.HudFrameLimit = (int)_frameLimit.Value;
                DialogResult = true;
            };
            buttons.Children.Add(save);
            buttons.Children.Add(new Button { Content = "취소", IsCancel = true, Width = 90, Height = 32, Margin = new Thickness(5) });
            panel.Children.Add(buttons);
            Content = new Border { Padding = new Thickness(24), Background = Background, Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        }

        private static Grid Row(string label, FrameworkElement control)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(control, 1); row.Children.Add(control);
            System.Windows.Automation.AutomationProperties.SetName(control, label);
            return row;
        }

    }
}
