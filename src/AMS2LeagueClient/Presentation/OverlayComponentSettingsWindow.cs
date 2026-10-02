using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    /// <summary>Settings for the single HUD surface selected in layout edit mode.</summary>
    public sealed class OverlayComponentSettingsWindow : Window
    {
        private const string OriginalFontLabel = "기본 글꼴";
        private bool _ready;
        private readonly OverlayLayerControls _layers;
        public DrivingHudSettings Settings { get; }
        public IReadOnlyList<string> LayerOrder => _layers.Order;
        public event EventHandler? SettingsChanged;
        public event EventHandler? LayerOrderChanged;

        public OverlayComponentSettingsWindow(string component, DrivingHudSettings current, IReadOnlyList<string>? layerOrder = null,
            IReadOnlyList<string>? visibleComponents = null)
        {
            if (Array.IndexOf(OverlayComponentKeys.TextConfigurable, component) < 0)
                throw new ArgumentOutOfRangeException(nameof(component));
            Settings = current.Normalize();
            string label = Label(component);
            Title = label + " 설정";
            FontFamily = DrivingNumberView.ResolveFont(DrivingHudSettings.DefaultFontName);
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/AMS2LeagueClient;component/Assets/AppIcon.ico"));
            Width = 440;
            MaxHeight = SystemParameters.WorkArea.Height;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(15, 26, 39));
            Foreground = Brushes.White;
            Topmost = true;

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(new TextBlock { Text = "설정을 바꾸면 편집 중인 오버레이에 바로 표시됩니다.",
                Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) });

            if (component == OverlayComponentKeys.TimingTower || component == OverlayComponentKeys.PedalTelemetry)
            {
                var design = new ComboBox { MinWidth = 210, ItemsSource = new[] { "기본", "개량" },
                    SelectedIndex = (component == OverlayComponentKeys.TimingTower ? Settings.TowerDesign : Settings.TelemetryDesign) == "racing" ? 1 : 0 };
                design.SelectionChanged += (_, __) =>
                {
                    if (component == OverlayComponentKeys.TimingTower) Settings.TowerDesign = design.SelectedIndex == 1 ? "racing" : "legacy";
                    else Settings.TelemetryDesign = design.SelectedIndex == 1 ? "racing" : "legacy";
                    Changed();
                };
                panel.Children.Add(Row("디자인", design));
            }

            if (component == OverlayComponentKeys.PedalTelemetry || component == OverlayComponentKeys.PedalGauge)
            {
                panel.Children.Add(Heading("입력 막대 색상"));
                AddColor(panel, "브레이크", Settings.BrakeColor, value => Settings.BrakeColor = value);
                AddColor(panel, "악셀", Settings.ThrottleColor, value => Settings.ThrottleColor = value);
                AddColor(panel, "클러치", Settings.ClutchColor, value => Settings.ClutchColor = value);
                AddColor(panel, "핸드브레이크", Settings.HandBrakeColor, value => Settings.HandBrakeColor = value);
            }

            if (component == OverlayComponentKeys.PedalTelemetry)
            {
                var steering = Slider(180, 1440, 90, Settings.SteeringRangeDegrees, 200);
                System.Windows.Automation.AutomationProperties.SetName(steering, "핸들 전체 회전각");
                var value = new TextBlock { Text = steering.Value.ToString("0") + "°", VerticalAlignment = VerticalAlignment.Center };
                steering.ValueChanged += (_, __) => { Settings.SteeringRangeDegrees = steering.Value; value.Text = steering.Value.ToString("0") + "°"; Changed(); };
                var contents = new StackPanel { Orientation = Orientation.Horizontal };
                contents.Children.Add(steering); contents.Children.Add(value);
                panel.Children.Add(Row("핸들 전체 회전각", contents));
                panel.Children.Add(new TextBlock { Text = "게임의 좌우 전체 회전 범위에 맞춰 주세요.", Foreground = Brushes.LightGray, FontSize = 11 });
            }

            var appearance = Settings.TextFor(component);
            var fonts = new[] { OriginalFontLabel }.Concat(Fonts.SystemFontFamilies.Select(font => font.Source)
                .Concat(new[] { DrivingHudSettings.DefaultFontName }).Distinct().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)).ToArray();
            var font = new ComboBox { MinWidth = 210, MaxDropDownHeight = 300, ItemsSource = fonts,
                SelectedItem = appearance.Font.Length == 0 || !fonts.Contains(appearance.Font) ? OriginalFontLabel : appearance.Font };
            var size = Slider(50, 200, 5, appearance.Scale * 100, 200);
            var percent = new TextBlock { Text = size.Value.ToString("0") + "%", VerticalAlignment = VerticalAlignment.Center };
            var scaleRow = new StackPanel { Orientation = Orientation.Horizontal };
            scaleRow.Children.Add(size); scaleRow.Children.Add(percent);
            var sample = new TextBlock { Text = component == OverlayComponentKeys.Gear ? "3" : component == OverlayComponentKeys.Speed ? "123 km/h" : "가 123",
                FontSize = 28 * appearance.Scale, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(sample, label + " 글꼴 미리보기");
            var sampleBorder = new Border { Height = 58, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(3, 13, 23)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(79, 105, 126)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Child = sample, Margin = new Thickness(0, 4, 0, 10) };
            void UpdateText()
            {
                string selected = font.SelectedItem as string ?? OriginalFontLabel;
                appearance.Font = selected == OriginalFontLabel ? "" : selected;
                appearance.Scale = size.Value / 100;
                Settings.OverlayText[component] = appearance;
                if (component == OverlayComponentKeys.Speed) Settings.SpeedFont = appearance.Font.Length == 0 ? DrivingHudSettings.DefaultFontName : appearance.Font;
                if (component == OverlayComponentKeys.Gear) Settings.GearFont = appearance.Font.Length == 0 ? DrivingHudSettings.DefaultFontName : appearance.Font;
                if (component == OverlayComponentKeys.RaceControl) Settings.RaceControlFontScale = appearance.Scale;
                percent.Text = size.Value.ToString("0") + "%";
                sample.FontFamily = DrivingNumberView.ResolveFont(appearance.Font.Length == 0 ? DrivingHudSettings.DefaultFontName : appearance.Font);
                sample.FontSize = 28 * appearance.Scale;
                Changed();
            }
            font.SelectionChanged += (_, __) => UpdateText();
            size.ValueChanged += (_, __) => UpdateText();
            UpdateText();
            panel.Children.Add(Heading("글꼴과 내부 글자 크기"));
            panel.Children.Add(Row("글꼴", font));
            panel.Children.Add(Row("글자 크기", scaleRow));
            panel.Children.Add(sampleBorder);
            System.Windows.Automation.AutomationProperties.SetName(font, label + " 글꼴");
            System.Windows.Automation.AutomationProperties.SetName(size, label + " 글자 크기");

            if (component == OverlayComponentKeys.Speed)
                AddColor(panel, "속도계 그림자", Settings.SpeedShadowColor, value => Settings.SpeedShadowColor = value);
            if (component == OverlayComponentKeys.Gear)
                AddColor(panel, "기어 그림자", Settings.GearShadowColor, value => Settings.GearShadowColor = value);

            _layers = new OverlayLayerControls(component, layerOrder ?? OverlayComponentKeys.All, visibleComponents);
            _layers.Changed += (_, __) => LayerOrderChanged?.Invoke(this, EventArgs.Empty);
            panel.Children.Add(_layers.View);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "저장", Width = 90, Height = 32, IsDefault = true, Margin = new Thickness(5) };
            save.Click += (_, __) => DialogResult = true;
            actions.Children.Add(save);
            actions.Children.Add(new Button { Content = "취소", Width = 90, Height = 32, IsCancel = true, Margin = new Thickness(5) });
            panel.Children.Add(actions);
            Content = new Border { Padding = new Thickness(20), Background = Background,
                Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            Loaded += (_, __) => _ready = true;
        }

        private void Changed()
        {
            if (_ready) SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AddColor(Panel panel, string label, string hex, Action<string> apply)
        {
            var button = new Button { MinWidth = 210, Height = 30 };
            SetColor(button, hex);
            button.Click += (_, __) =>
            {
                Color old = (Color)ColorConverter.ConvertFromString((string)button.Tag);
                using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true,
                    Color = System.Drawing.Color.FromArgb(old.R, old.G, old.B) };
                var owner = new System.Windows.Forms.NativeWindow();
                owner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                try
                {
                    if (picker.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return;
                    string selected = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
                    SetColor(button, selected);
                    apply(selected);
                    Changed();
                }
                finally { owner.ReleaseHandle(); }
            };
            panel.Children.Add(Row(label, button));
        }

        private static void SetColor(Button button, string hex)
        {
            Color color = (Color)ColorConverter.ConvertFromString(hex);
            button.Tag = hex;
            button.Content = hex + " · 색상 변경";
            button.Background = new SolidColorBrush(color);
            button.Foreground = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 150 ? Brushes.Black : Brushes.White;
        }

        private static Slider Slider(double min, double max, double tick, double value, double width)
            => new Slider { Minimum = min, Maximum = max, TickFrequency = tick, SmallChange = tick,
                LargeChange = tick * 2, IsSnapToTickEnabled = true, Value = value, Width = width };

        private static TextBlock Heading(string text)
            => new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) };

        private static Grid Row(string label, FrameworkElement control)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            System.Windows.Automation.AutomationProperties.SetName(control, label);
            return row;
        }

        private static string Label(string component) => component switch
        {
            OverlayComponentKeys.TimingTower => "순위 타워",
            OverlayComponentKeys.RelativeDrivers => "전후방 거리",
            OverlayComponentKeys.LapTiming => "현재·섹터 타임",
            OverlayComponentKeys.SessionInfo => "세션 정보",
            OverlayComponentKeys.EventCard => "이벤트 카드",
            OverlayComponentKeys.RaceControl => "레이스 컨트롤",
            OverlayComponentKeys.Waiting => "멀티 대기 화면",
            OverlayComponentKeys.PedalTelemetry => "텔레메트리",
            OverlayComponentKeys.PedalGauge => "페달 게이지",
            OverlayComponentKeys.Speed => "속도계",
            OverlayComponentKeys.Gear => "기어",
            OverlayComponentKeys.DrivingDashboard => "레이싱 계기판",
            _ => component
        };
    }
}
