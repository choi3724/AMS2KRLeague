using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Presentation
{
    public static class HudTypography
    {
        public const string PrimaryFamily = "Noto Sans KR";
        public const string KoreanFallbackFamily = "Malgun Gothic";
        public const string SansFallbackFamily = "Segoe UI";
        public const string FamilyChain = PrimaryFamily + ", " + KoreanFallbackFamily + ", " + SansFallbackFamily;
        public const string KoreanGlyphSample = "남은 시간 종합 클래스 현재 랩 앞차 뒤차 순위 간격 거리";
        public const string NumericGlyphSample = "1708:.-+ 24:18 1:40.973 +0.842 57m";

        public static string SelectFamily(ISet<string> installedFamilies)
        {
            if (installedFamilies == null) throw new ArgumentNullException(nameof(installedFamilies));
            if (installedFamilies.Contains(PrimaryFamily)) return PrimaryFamily;
            if (installedFamilies.Contains(KoreanFallbackFamily)) return KoreanFallbackFamily;
            return SansFallbackFamily;
        }
    }
}
