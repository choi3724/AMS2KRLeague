using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public partial class ClientStatusWindow
    {
        private readonly Dictionary<Image, (string Component, string Design)> _galleryImages = new Dictionary<Image, (string, string)>();
        private readonly List<RadioButton> _designChoices = new List<RadioButton>();
        private bool _updatingDesignChecks;
        private bool _settingOpacities;
        private readonly Dictionary<string, Slider> _opacitySliders = new Dictionary<string, Slider>();
        public event Action<string, double>? ComponentOpacityChanged;
        public void SetComponentOpacities(IReadOnlyDictionary<string, double> values)
        {
            _settingOpacities = true;
            try { foreach (var item in _opacitySliders) item.Value.Value = values.TryGetValue(item.Key, out double opacity) ? (1 - opacity) * 100 : 0; }
            finally { _settingOpacities = false; }
        }
        private string _previewAppearance = string.Empty;
        private DrivingHudSettings _gallerySettings = new DrivingHudSettings();
        public event EventHandler<OverlayDesignSelectionEventArgs>? OverlayDesignSelected;

        private void BuildGallery()
        {
            var descriptions = new Dictionary<string, string> {
                [OverlayComponentKeys.TimingTower] = "순위 · 격차 · 랩타임 · 페널티",
                [OverlayComponentKeys.PedalTelemetry] = "페달 입력 변화 · 핸들 회전",
                [OverlayComponentKeys.AvanteCluster] = "RPM · 기어 · 속도 · 주행 상태",
                [OverlayComponentKeys.AvanteClusterExpanded] = "일반형 + 온도 · 터보 · 토크 · 연료",
                [OverlayComponentKeys.PedalGauge] = "악셀 · 브레이크 · 클러치 · 핸드브레이크",
                [OverlayComponentKeys.DrivingDashboard] = "기어 · 속도 · RPM을 한 화면에",
                [OverlayComponentKeys.RelativeDrivers] = "앞차와 뒷차의 간격",
                [OverlayComponentKeys.LapTiming] = "현재 랩과 섹터 기록",
                [OverlayComponentKeys.SessionInfo] = "남은 시간 · 현재 순위 · 랩",
                [OverlayComponentKeys.EventCard] = "접전 · 추월 · 주요 주행 이벤트",
                [OverlayComponentKeys.RaceControl] = "깃발 · 경고 · 경기 진행 안내",
                [OverlayComponentKeys.Waiting] = "멀티플레이어 세션 대기 정보",
                [OverlayComponentKeys.Speed] = "속도 숫자를 독립적으로 표시",
                [OverlayComponentKeys.Gear] = "기어 숫자를 독립적으로 표시"
            };
            var checks = LayoutVisibilityPanel.Children.OfType<CheckBox>().ToArray();
            LayoutVisibilityPanel.Children.Clear();
            var pairs = new Grid { Name = "OverlayPairGrid" };
            pairs.ColumnDefinitions.Add(new ColumnDefinition());
            pairs.ColumnDefinitions.Add(new ColumnDefinition());
            int pairIndex = 0;
            foreach (CheckBox check in checks)
            {
                string key = (string)check.Tag;
                bool variants = key == OverlayComponentKeys.TimingTower || key == OverlayComponentKeys.PedalTelemetry;
                var row = new Grid { Margin = new Thickness(14, 12, 6, 12) };
                if (variants)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                }
                else
                {
                    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    row.RowDefinitions.Add(new RowDefinition());
                }
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center,
                    Margin = variants ? new Thickness(0, 0, 14, 0) : new Thickness(0, 0, 8, 8) };
                check.FontSize = 15; check.FontWeight = FontWeights.SemiBold;
                check.ToolTip = check.Content + " 표시 켜기 / 끄기";
                label.Children.Add(check);
                label.Children.Add(new TextBlock { Text = descriptions[key], TextWrapping = TextWrapping.Wrap,
                    FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(145, 165, 181)), Margin = new Thickness(18, 8, 0, 0) });
                var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 8, 0, 0) };
                var opacityText = new TextBlock { Text = "투명도 0%", FontSize = 11, Width = 64, VerticalAlignment = VerticalAlignment.Center };
                var opacitySlider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Width = 64,
                    Tag = key, ToolTip = "0%: 선명하게 표시 / 100%: 완전히 투명" };
                AutomationProperties.SetName(opacitySlider, check.Content + " 투명도");
                opacitySlider.ValueChanged += (_, e) =>
                {
                    opacityText.Text = "투명도 " + e.NewValue.ToString("0") + "%";
                    if (!_settingOpacities) ComponentOpacityChanged?.Invoke(key, 1 - e.NewValue / 100);
                };
                _opacitySliders.Add(key, opacitySlider); opacityRow.Children.Add(opacityText); opacityRow.Children.Add(opacitySlider);
                label.Children.Add(opacityRow);
                row.Children.Add(label);
                var examples = new Grid();
                if (variants) Grid.SetColumn(examples, 1); else Grid.SetRow(examples, 1);
                row.Children.Add(examples);
                foreach (string design in variants ? new[] { "legacy", "racing" } : new[] { "default" })
                {
                    int column = examples.ColumnDefinitions.Count;
                    examples.ColumnDefinitions.Add(new ColumnDefinition());
                    var image = new Image { Height = key == OverlayComponentKeys.TimingTower ? 155 : 94,
                        Stretch = Stretch.Uniform, Margin = new Thickness(4, 8, 4, 0) };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    _galleryImages.Add(image, (key, design));
                    AutomationProperties.SetName(image, check.Content + (design == "legacy" ? " 기본" : design == "racing" ? " 개량" : "") + " 미리보기");
                    FrameworkElement preview;
                    if (variants)
                    {
                        var content = new StackPanel();
                        content.Children.Add(new TextBlock { Text = design == "legacy" ? "기본" : "개량",
                            FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 55, 0) });
                        content.Children.Add(image);
                        var choice = new RadioButton { Content = content, Style = (Style)FindResource("GalleryChoice"),
                            GroupName = key, Tag = (key, design) };
                        AutomationProperties.SetName(choice, check.Content + (design == "legacy" ? " 기본 디자인" : " 개량 디자인"));
                        choice.Checked += DesignChoice_Checked; _designChoices.Add(choice); preview = choice;
                    }
                    else preview = new Border { Background = new SolidColorBrush(Color.FromRgb(11, 17, 24)),
                        CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8),
                        Child = image };
                    Grid.SetColumn(preview, column); examples.Children.Add(preview);
                }
                var card = new Border { Background = new SolidColorBrush(Color.FromRgb(20, 29, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(37, 51, 62)), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 10, 10), Child = row };
                if (variants) LayoutVisibilityPanel.Children.Add(card);
                else
                {
                    if (pairIndex % 2 == 0) pairs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetColumn(card, pairIndex % 2); Grid.SetRow(card, pairIndex / 2);
                    pairs.Children.Add(card); pairIndex++;
                }
            }
            LayoutVisibilityPanel.Children.Add(pairs);
            IsVisibleChanged += (_, __) => RefreshGalleryImages();
            StateChanged += (_, __) => RefreshGalleryImages();
            Closed += (_, __) => { foreach (var image in _galleryImages.Keys) image.Source = null; };
            SetDesignLabels(new DrivingHudSettings());
        }

        public void SetDesignLabels(DrivingHudSettings settings)
        {
            settings = settings.Normalize();
            _updatingDesignChecks = true;
            try
            {
                foreach (RadioButton choice in _designChoices)
                {
                    var selection = ((string Component, string Design))choice.Tag;
                    string selected = selection.Component == OverlayComponentKeys.TimingTower ? settings.TowerDesign : settings.TelemetryDesign;
                    choice.IsChecked = selection.Design == selected;
                }
            }
            finally { _updatingDesignChecks = false; }
            string appearance = string.Join("|", settings.BrakeColor, settings.ThrottleColor, settings.ClutchColor, settings.HandBrakeColor,
                settings.SpeedFont, settings.GearFont, settings.SpeedShadowColor, settings.GearShadowColor, settings.SteeringRangeDegrees);
            if (_previewAppearance == appearance) return;
            _gallerySettings = settings;
            foreach (var image in _galleryImages.Keys) image.Source = null;
            _previewAppearance = appearance;
            RefreshGalleryImages();
        }

        private void RefreshGalleryImages()
        {
            bool active = IsVisible && WindowState != WindowState.Minimized;
            foreach (var item in _galleryImages)
                if (!active) item.Key.Source = null;
                else if (item.Key.Source == null) item.Key.Source = OverlayGalleryPreview.Create(item.Value.Component, item.Value.Design, _gallerySettings);
        }

        private void DesignChoice_Checked(object sender, RoutedEventArgs args)
        {
            if (_updatingDesignChecks) return;
            var selection = ((string Component, string Design))((RadioButton)sender).Tag;
            OverlayDesignSelected?.Invoke(this, new OverlayDesignSelectionEventArgs(selection.Component, selection.Design));
        }
    }

    public sealed class OverlayDesignSelectionEventArgs : EventArgs
    {
        public OverlayDesignSelectionEventArgs(string component, string design) { Component = component; Design = design; }
        public string Component { get; }
        public string Design { get; }
    }
}
