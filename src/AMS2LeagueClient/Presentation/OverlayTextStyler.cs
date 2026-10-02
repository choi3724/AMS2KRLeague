using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    internal static class OverlayTextStyler
    {
        private sealed class Original
        {
            internal Original(TextBlock text) { Family = text.FontFamily; Size = text.FontSize; }
            internal FontFamily Family { get; }
            internal double Size { get; }
        }

        private static readonly ConditionalWeakTable<TextBlock, Original> Originals = new ConditionalWeakTable<TextBlock, Original>();
        private sealed class RootState
        {
            internal DrivingHudSettings.TextAppearance Appearance = new DrivingHudSettings.TextAppearance();
            internal bool Hooked;
        }
        private static readonly ConditionalWeakTable<FrameworkElement, RootState> Roots = new ConditionalWeakTable<FrameworkElement, RootState>();

        internal static void Apply(FrameworkElement root, DrivingHudSettings.TextAppearance appearance)
        {
            RootState state = Roots.GetValue(root, _ => new RootState());
            state.Appearance = appearance;
            if (!state.Hooked)
            {
                state.Hooked = true;
                root.Loaded += (sender, args) => ApplyLoaded(root, state.Appearance);
            }
            ApplyLoaded(root, appearance);
        }

        private static void ApplyLoaded(FrameworkElement root, DrivingHudSettings.TextAppearance appearance)
        {
            FontFamily? chosen = appearance.Font.Length == 0 ? null : DrivingNumberView.ResolveFont(appearance.Font);
            Visit(root, chosen, appearance.Scale);
        }

        private static void Visit(DependencyObject node, FontFamily? chosen, double scale)
        {
            if (node is TextBlock text)
            {
                if (chosen == null && scale == 1 && !Originals.TryGetValue(text, out _)) return;
                Original original = Originals.GetValue(text, item => new Original(item));
                text.FontFamily = chosen ?? original.Family;
                text.FontSize = original.Size * scale;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Visit(VisualTreeHelper.GetChild(node, i), chosen, scale);
        }
    }
}
