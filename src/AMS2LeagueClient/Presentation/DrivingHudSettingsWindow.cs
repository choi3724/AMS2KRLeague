using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public sealed class DrivingHudSettingsWindow : Window
    {
        private readonly Button[] _colors = new Button[6];
        private readonly Slider _steeringRange = new Slider { Minimum = 180, Maximum = 1440, TickFrequency = 90, IsSnapToTickEnabled = true, Width = 200 };
        private readonly ComboBox _speedFont = new ComboBox { MinWidth = 240, MaxDropDownHeight = 300 };
        private readonly ComboBox _gearFont = new ComboBox { MinWidth = 240, MaxDropDownHeight = 300 };
        public DrivingHudSettings Settings { get; private set; }

        public DrivingHudSettingsWindow(DrivingHudSettings current, string vehicleName = "", double? engineMaximum = null, string profileVehicleName = "")
        {
            Settings = current.Normalize();
            Title = "색상·글꼴·핸들 설정";
            FontFamily = DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName);
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/AMS2LeagueClient;component/Assets/AppIcon.ico"));
            Width = 520; MaxHeight = SystemParameters.WorkArea.Height; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(15, 26, 39)); Foreground = Brushes.White;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "텔레메트리 그래프와 입력 막대 색상", FontSize = 17, Margin = new Thickness(0, 0, 0, 14) });
            string[] labels = { "브레이크", "악셀", "클러치", "핸드브레이크" };
            string[] colors = { Settings.BrakeColor, Settings.ThrottleColor, Settings.ClutchColor, Settings.HandBrakeColor };
            for (int i = 0; i < 4; i++)
            {
                var button = new Button { MinWidth = 240, Height = 30 };
                SetColor(button, colors[i]); _colors[i] = button;
                button.Click += (sender, args) => PickColor((Button)sender);
                panel.Children.Add(Row(labels[i], button));
            }
            _steeringRange.Value = Settings.SteeringRangeDegrees;
            var rangeValue = new TextBlock { Text = Settings.SteeringRangeDegrees.ToString("0") + "°", VerticalAlignment = VerticalAlignment.Center };
            _steeringRange.ValueChanged += (_, __) => rangeValue.Text = _steeringRange.Value.ToString("0") + "°";
            var rangePanel = new StackPanel { Orientation = Orientation.Horizontal };
            rangePanel.Children.Add(_steeringRange); rangePanel.Children.Add(rangeValue);
            panel.Children.Add(Row("핸들 전체 회전각", rangePanel));
            panel.Children.Add(new TextBlock { Text = "게임에서 사용하는 좌우 전체 회전 범위에 맞춰 주세요.",
                FontSize = 11, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "숫자 글꼴", FontSize = 17, Margin = new Thickness(0, 18, 0, 10) });
            string[] fonts = Fonts.SystemFontFamilies.Select(font => font.Source).Concat(new[] { DrivingHudSettings.DefaultFontName }).Distinct().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            _speedFont.ItemsSource = fonts; _gearFont.ItemsSource = fonts;
            _speedFont.SelectedItem = fonts.Contains(Settings.SpeedFont) ? Settings.SpeedFont : DrivingHudSettings.DefaultFontName;
            _gearFont.SelectedItem = fonts.Contains(Settings.GearFont) ? Settings.GearFont : DrivingHudSettings.DefaultFontName;
            panel.Children.Add(Row("속도계", _speedFont)); panel.Children.Add(Row("기어", _gearFont));
            panel.Children.Add(new TextBlock { Text = "글자 그림자 색상", FontSize = 17, Margin = new Thickness(0, 18, 0, 10) });
            for (int i = 4; i < 6; i++)
            {
                var button = new Button { MinWidth = 240, Height = 30 };
                SetColor(button, i == 4 ? Settings.SpeedShadowColor : Settings.GearShadowColor);
                _colors[i] = button;
                button.Click += (sender, args) => PickColor((Button)sender);
                panel.Children.Add(Row(i == 4 ? "속도계 그림자" : "기어 그림자", button));
            }
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
                Settings = new DrivingHudSettings { AvanteVehicles = Settings.AvanteVehicles, TowerDesign = Settings.TowerDesign, TelemetryDesign = Settings.TelemetryDesign, SteeringRangeDegrees = _steeringRange.Value, BrakeColor = (string)_colors[0].Tag, ThrottleColor = (string)_colors[1].Tag,
                    ClutchColor = (string)_colors[2].Tag, HandBrakeColor = (string)_colors[3].Tag,
                    SpeedShadowColor = (string)_colors[4].Tag, GearShadowColor = (string)_colors[5].Tag,
                    SpeedFont = _speedFont.SelectedItem as string ?? DrivingHudSettings.DefaultFontName, GearFont = _gearFont.SelectedItem as string ?? DrivingHudSettings.DefaultFontName };
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

        private void PickColor(Button button)
        {
            Color old = (Color)ColorConverter.ConvertFromString((string)button.Tag);
            using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true,
                Color = System.Drawing.Color.FromArgb(old.R, old.G, old.B) };
            // The native modal owner keeps the picker above this settings window.
            var owner = new System.Windows.Forms.NativeWindow();
            owner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            try
            {
                if (picker.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
                    SetColor(button, "#" + picker.Color.R.ToString("X2") + picker.Color.G.ToString("X2") + picker.Color.B.ToString("X2"));
            }
            finally { owner.ReleaseHandle(); }
        }

        private static void SetColor(Button button, string hex)
        {
            Color color = (Color)ColorConverter.ConvertFromString(hex);
            button.Tag = hex; button.Content = hex + " · 색상 변경";
            button.Background = new SolidColorBrush(color);
            button.Foreground = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 150 ? Brushes.Black : Brushes.White;
        }
    }
}
