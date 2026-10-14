using System;
using System.Text.RegularExpressions;

namespace SPT.ModKoreanAddon
{
    // Wara Modding Stats Helper uses <mspace=0.55em> to align 20-character
    // English attribute labels. Korean glyphs are typically wider than 0.55em;
    // retaining that tag forces them together and makes the tooltip illegible.
    // Keep this transformation scoped to the already-hooked stats tooltip.
    internal static class ModdingStatsTooltipText
    {
        private const string NumericColumn = "<pos=11em>"; // 20 × 0.55em in the original mod
        private const string LongLabelGap = "<space=1em>";

        private static readonly Regex KoreanStatLabel = new Regex(
            @"<mspace=0\.55em>(?<label>[^<>]*[\u1100-\u11FF\u3131-\u318E\uAC00-\uD7A3][^<>]*)</mspace>(?=<color=)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex KoreanHeading = new Regex(
            @"<mspace=(?:0\.55|0\.4875|0\.4475)em>(?<text><color=[^>]+>[^<]*[\u1100-\u11FF\u3131-\u318E\uAC00-\uD7A3][^<]*</color>)</mspace>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static string Translate(string text)
        {
            if (string.IsNullOrEmpty(text) ||
                text.IndexOf("<mspace=0.55em>", StringComparison.Ordinal) < 0)
                return text;

            // Do not alter the source mod's control-flow checks for these tokens.
            // Translate only the final tooltip string supplied to SimpleTooltip.Show.
            text = text.Replace(">SAME STATS</color>", ">능력치 동일</color>")
                       .Replace(">COMPARISON</color>", ">비교</color>")
                       .Replace(">STATS</color>", ">능력치</color>");

            // Headings require natural Korean glyph advance, not narrow monospace.
            text = KoreanHeading.Replace(text, match => match.Groups["text"].Value);

            // The source pads attribute names to 20 characters with spaces, then
            // forces every glyph into 0.55em. Remove that padding and restore the
            // same numeric column. Long translated labels use a natural gap instead
            // of placing the numeric value on top of the label.
            return KoreanStatLabel.Replace(text, match =>
            {
                var label = match.Groups["label"].Value.TrimEnd();
                double estimatedEm = 0;
                foreach (var c in label)
                {
                    if ((c >= '\u1100' && c <= '\u11FF') ||
                        (c >= '\u3131' && c <= '\u318E') ||
                        (c >= '\uAC00' && c <= '\uD7A3'))
                        estimatedEm += 1;
                    else if (c == ' ')
                        estimatedEm += 0.35;
                    else
                        estimatedEm += 0.55;
                }
                return label + (estimatedEm < 10.5 ? NumericColumn : LongLabelGap);
            });
        }
    }
}
