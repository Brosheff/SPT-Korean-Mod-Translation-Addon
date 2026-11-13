using System;
using System.Text.RegularExpressions;

namespace SPT.ModKoreanAddon
{
    // Shared GUI belongs to one of three independently compiled Kennerz plugins.
    // This is only a display adapter for their audited PaPNormalGame/RaygunNormalGame/
    // ThundergunNormalGame render methods; it never changes shared choices or callbacks.
    internal static class KennerzPromptText
    {
        private const string Pap = "com.kennerz.paphideoutmodule";
        private const string Ray = "com.kennerz.raygun";
        private const string Thunder = "com.kennerz.thundergun";
        private static readonly Regex NewHeading = new Regex(
            @"^(<b>[^<>]+</b>)(  <color=#ffd24a><size=\d+>NEW</size></color>)?$",
            RegexOptions.CultureInvariant);

        internal static string Translate(EditableProfiles profiles, string text, string culture)
        {
            if (profiles == null || string.IsNullOrEmpty(text) || !LocaleMode.IsKoreanCulture(culture)) return text;
            try
            {
                // A note is composed from multiple plugins' independent 'gone' rows,
                // plus the common instructions. Translate the exact source of each line.
                if (text.IndexOf('\n') >= 0)
                {
                    string[] lines = text.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                        lines[i] = TranslateLine(profiles, lines[i], culture);
                    return string.Join("\n", lines);
                }
                return TranslateLine(profiles, text, culture);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("kennerz-prompt-translate",
                    () => "Kennerz shared prompt kept its source text: " + ex.Message);
                return text;
            }
        }

        private static string TranslateLine(EditableProfiles data, string source, string culture)
        {
            if (source.Length == 0) return source;
            // Prompt.Draw constructs a heading with a runtime font size. The name and
            // NEW badge are display-only; do not alter m["name"] or m["route"].
            Match heading = NewHeading.Match(source);
            if (heading.Success)
            {
                string name = TranslateExact(data, heading.Groups[1].Value, culture);
                string badge = heading.Groups[2].Value;
                if (badge.Length != 0)
                    badge = badge.Replace(">NEW</size>", ">" + TranslateExact(data, "NEW", culture) + "</size>");
                return name + badge;
            }
            // NoteText prepends a bullet to the mod-owned m["gone"] string.
            // Restore that bullet after matching the unmodified registered source.
            if (source.StartsWith("• ", StringComparison.Ordinal))
            {
                string translated = TranslateExact(data, source, culture);
                if (!string.Equals(translated, source, StringComparison.Ordinal)) return translated;
                return "• " + TranslateExact(data, source.Substring(2), culture);
            }
            return TranslateExact(data, source, culture);
        }

        private static string TranslateExact(EditableProfiles data, string source, string culture)
        {
            // Existing rules are exact or individually verified dynamic UI fragments;
            // identical source literals in different non-Kennerz windows never visit here.
            var result = data.Translate(Pap, "small_ui", source, culture);
            result = data.Translate(Ray, "raygun_ui", result, culture);
            return data.Translate(Thunder, "thundergun_ui", result, culture);
        }
    }
}
