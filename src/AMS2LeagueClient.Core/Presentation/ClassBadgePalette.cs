using System;
using System.Collections.Generic;
using System.Text;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class ClassBadgeStyle
    {
        public ClassBadgeStyle(string family, string background, string foreground)
        {
            Family = family;
            Background = background;
            Foreground = foreground;
        }

        public string Family { get; }
        public string Background { get; }
        public string Foreground { get; }
    }

    /// <summary>
    /// Fixed, HUD-inspired class colours. These values deliberately live in
    /// the overlay and never read or redistribute AMS2 game resources.
    /// </summary>
    public static class ClassBadgePalette
    {
        public const string FallbackBackground = "#526A7D";
        public const string FallbackForeground = "#FFFFFF";

        private static readonly ClassBadgeStyle Safety = new ClassBadgeStyle("SAFETY", "#F1F1F1", "#111820");
        private static readonly ClassBadgeStyle Gt3 = new ClassBadgeStyle("GT3", "#00B866", "#071A11");
        private static readonly ClassBadgeStyle Gt4 = new ClassBadgeStyle("GT4", "#169FD0", "#071820");
        private static readonly ClassBadgeStyle Gte = new ClassBadgeStyle("GTE", "#DFAF31", "#201603");
        private static readonly ClassBadgeStyle Prototype = new ClassBadgeStyle("P1/DPI", "#8F6BD8", "#FFFFFF");
        private static readonly ClassBadgeStyle Prototype2 = new ClassBadgeStyle("P2", "#278BC4", "#FFFFFF");
        private static readonly ClassBadgeStyle Prototype3 = new ClassBadgeStyle("P3", "#DD742F", "#211006");
        private static readonly ClassBadgeStyle Formula = new ClassBadgeStyle("FORMULA", "#D94D52", "#FFFFFF");
        private static readonly ClassBadgeStyle Touring = new ClassBadgeStyle("TOURING", "#D7A72E", "#201703");
        private static readonly ClassBadgeStyle Stock = new ClassBadgeStyle("STOCK", "#3D8FC2", "#FFFFFF");
        private static readonly ClassBadgeStyle Classic = new ClassBadgeStyle("CLASSIC", "#B56C3D", "#FFFFFF");
        private static readonly ClassBadgeStyle Kart = new ClassBadgeStyle("KART", "#E2C63A", "#211D04");
        private static readonly ClassBadgeStyle Lancer = new ClassBadgeStyle("LANCER", "#D94D52", "#FFFFFF");
        private static readonly ClassBadgeStyle Fallback = new ClassBadgeStyle("FALLBACK", FallbackBackground, FallbackForeground);

        private static readonly IReadOnlyDictionary<string, ClassBadgeStyle> ExactMappings
            = new Dictionary<string, ClassBadgeStyle>(StringComparer.Ordinal)
            {
                ["SAFETYCAR"] = Safety,
                ["GT3"] = Gt3,
                ["GT3GEN2"] = Gt3,
                ["GT4"] = Gt4,
                ["GTE"] = Gte,
                ["DPI"] = Prototype,
                ["LMDH"] = Prototype,
                ["HYPERCAR"] = Prototype,
                ["P1"] = Prototype,
                ["LMP1"] = Prototype,
                ["P2"] = Prototype2,
                ["LMP2"] = Prototype2,
                ["P3"] = Prototype3,
                ["LMP3"] = Prototype3,
                ["LANCER"] = Lancer
            };

        public static ClassBadgeStyle Resolve(string? vehicleClass)
        {
            string token = Normalize(vehicleClass);
            if (ExactMappings.TryGetValue(token, out ClassBadgeStyle? exact)) return exact;
            if (token.StartsWith("GT3", StringComparison.Ordinal)) return Gt3;
            if (token.StartsWith("GT4", StringComparison.Ordinal)) return Gt4;
            if (token.StartsWith("GTE", StringComparison.Ordinal)) return Gte;
            if (token.StartsWith("DPI", StringComparison.Ordinal)
                || token.StartsWith("LMDH", StringComparison.Ordinal)
                || token.StartsWith("HYPERCAR", StringComparison.Ordinal)
                || token.StartsWith("LMP1", StringComparison.Ordinal)) return Prototype;
            if (token.StartsWith("LMP2", StringComparison.Ordinal) || token.StartsWith("P2", StringComparison.Ordinal)) return Prototype2;
            if (token.StartsWith("LMP3", StringComparison.Ordinal) || token.StartsWith("P3", StringComparison.Ordinal)) return Prototype3;
            if (token.StartsWith("FORMULA", StringComparison.Ordinal)
                || token.StartsWith("FULTIMATE", StringComparison.Ordinal)
                || token.StartsWith("FHITECH", StringComparison.Ordinal)
                || token.StartsWith("FCLASSIC", StringComparison.Ordinal)
                || token.StartsWith("FVINTAGE", StringComparison.Ordinal)) return Formula;
            if (token.StartsWith("TCR", StringComparison.Ordinal)
                || token.Contains("TOURING", StringComparison.Ordinal)
                || token.StartsWith("COPA", StringComparison.Ordinal)) return Touring;
            if (token.Contains("STOCK", StringComparison.Ordinal)) return Stock;
            if (token.StartsWith("GROUPC", StringComparison.Ordinal)
                || token.Contains("CLASSIC", StringComparison.Ordinal)
                || token.Contains("VINTAGE", StringComparison.Ordinal)) return Classic;
            if (token.Contains("KART", StringComparison.Ordinal)) return Kart;
            return Fallback;
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var result = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character)) result.Append(char.ToUpperInvariant(character));
            }
            return result.ToString();
        }
    }
}
