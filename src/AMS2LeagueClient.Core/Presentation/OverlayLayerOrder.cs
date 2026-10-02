using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Presentation
{
    public enum OverlayLayerMove { Back, Backward, Forward, Front }

    /// <summary>HUD components in back-to-front order, independent of screen position.</summary>
    public static class OverlayLayerOrder
    {
        public static List<string> Normalize(IEnumerable<string>? saved)
        {
            var order = new List<string>(OverlayComponentKeys.All.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (saved != null)
                foreach (string? value in saved)
                {
                    if (value == null) continue;
                    foreach (string key in OverlayComponentKeys.All)
                        if (string.Equals(value, key, StringComparison.OrdinalIgnoreCase) && seen.Add(key))
                        { order.Add(key); break; }
                }
            foreach (string key in OverlayComponentKeys.All)
                if (seen.Add(key)) order.Add(key);
            return order;
        }

        public static List<string> Move(IEnumerable<string>? saved, string component, OverlayLayerMove move,
            IEnumerable<string>? visibleComponents = null)
        {
            var order = Normalize(saved);
            int index = order.IndexOf(component);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(component));
            var visible = new HashSet<string>(visibleComponents ?? OverlayComponentKeys.All, StringComparer.OrdinalIgnoreCase);
            int previous = index, next = index;
            for (int i = index - 1; i >= 0; i--)
                if (visible.Contains(order[i])) { previous = i; break; }
            for (int i = index + 1; i < order.Count; i++)
                if (visible.Contains(order[i])) { next = i; break; }
            int target = move switch
            {
                OverlayLayerMove.Back => 0,
                OverlayLayerMove.Backward => previous,
                OverlayLayerMove.Forward => next,
                OverlayLayerMove.Front => order.Count - 1,
                _ => throw new ArgumentOutOfRangeException(nameof(move))
            };
            if (index == target) return order;
            order.RemoveAt(index);
            order.Insert(target, component);
            return order;
        }
    }
}
