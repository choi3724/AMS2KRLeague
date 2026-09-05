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

        /// <summary>
        /// Per-overlay on/off toggles are always available; they no longer depend
        /// on layout-edit mode.
        /// </summary>
        public bool AreComponentTogglesEnabled => LayoutVisibilityPanel.IsEnabled;

        public void SetLayoutEditState(bool editing, string message)
        {
            LayoutEditButton.Content = editing ? "저장 후 잠금" : "레이아웃 편집";
            LayoutHint.Text = message;
        }

        public void SetLayoutComponentStates(IReadOnlyDictionary<string, bool> states)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            _updatingLayoutChecks = true;
            try
            {
                foreach (CheckBox checkBox in ComponentChecks())
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

        public IReadOnlyDictionary<string, bool> GetLayoutComponentStates()
        {
            var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (CheckBox checkBox in ComponentChecks())
            {
                string key = checkBox.Tag as string ?? string.Empty;
                if (key.Length > 0) states[key] = checkBox.IsChecked == true;
            }
            return states;
        }

        private CheckBox[] ComponentChecks()
            => new[]
            {
                TimingTowerCheck,
                RelativeDriversCheck,
                LapTimingCheck,
                SessionInfoCheck,
                EventCardCheck,
                RaceControlCheck,
                WaitingCheck
            };

        private void LayoutEditButton_Click(object sender, RoutedEventArgs eventArgs)
            => LayoutEditRequested?.Invoke(this, EventArgs.Empty);

        private void LayoutResetButton_Click(object sender, RoutedEventArgs eventArgs)
            => LayoutResetRequested?.Invoke(this, EventArgs.Empty);

        private void ShowAllButton_Click(object sender, RoutedEventArgs eventArgs)
            => SetAllComponents(true);

        private void HideAllButton_Click(object sender, RoutedEventArgs eventArgs)
            => SetAllComponents(false);

        /// <summary>Turns every overlay on or off; each change is forwarded like a user toggle.</summary>
        public void SetAllComponents(bool enabled)
        {
            // Each CheckBox raises Checked/Unchecked, which forwards one toggle per component.
            foreach (CheckBox checkBox in ComponentChecks())
            {
                checkBox.IsChecked = enabled;
            }
        }

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
