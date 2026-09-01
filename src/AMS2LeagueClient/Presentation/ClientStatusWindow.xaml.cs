using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    public partial class ClientStatusWindow : Window
    {
        private bool _updatingLayoutChecks;
        public ClientStatusWindow(ClientStatusViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
        }

        public ClientStatusViewModel ViewModel { get; }

        public event EventHandler? LayoutEditRequested;
        public event EventHandler? LayoutResetRequested;
        public event EventHandler<LayoutComponentToggleEventArgs>? LayoutComponentToggled;

        public void SetLayoutEditState(bool editing, string message)
        {
            LayoutEditButton.Content = editing ? "저장 후 잠금" : "레이아웃 편집";
            LayoutHint.Text = message;
            LayoutVisibilityPanel.IsEnabled = editing;
            LayoutVisibilityPanel.Opacity = editing ? 1 : 0.45;
        }

        public void SetLayoutComponentStates(IReadOnlyDictionary<string, bool> states)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            _updatingLayoutChecks = true;
            try
            {
                foreach (CheckBox checkBox in new[]
                {
                    TimingTowerCheck,
                    RelativeDriversCheck,
                    LapTimingCheck,
                    SessionInfoCheck,
                    EventCardCheck,
                    RaceControlCheck,
                    WaitingCheck
                })
                {
                    string key = checkBox.Tag as string ?? string.Empty;
                    checkBox.IsChecked = !states.TryGetValue(key, out bool enabled) || enabled;
                }
            }
            finally
            {
                _updatingLayoutChecks = false;
            }
        }

        private void LayoutEditButton_Click(object sender, RoutedEventArgs eventArgs)
            => LayoutEditRequested?.Invoke(this, EventArgs.Empty);

        private void LayoutResetButton_Click(object sender, RoutedEventArgs eventArgs)
            => LayoutResetRequested?.Invoke(this, EventArgs.Empty);

        private void LayoutComponentCheck_Changed(object sender, RoutedEventArgs eventArgs)
        {
            CheckBox? checkBox = sender as CheckBox;
            string? component = checkBox?.Tag as string;
            if (_updatingLayoutChecks || checkBox == null || component == null) return;
            LayoutComponentToggled?.Invoke(this, new LayoutComponentToggleEventArgs(component, checkBox.IsChecked == true));
        }
    }

    public sealed class LayoutComponentToggleEventArgs : EventArgs
    {
        public LayoutComponentToggleEventArgs(string component, bool enabled)
        {
            Component = component;
            Enabled = enabled;
        }

        public string Component { get; }
        public bool Enabled { get; }
    }
}
