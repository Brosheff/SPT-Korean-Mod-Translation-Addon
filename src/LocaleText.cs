using System;

namespace SPT.ModKoreanAddon
{
    internal static class LocaleText
    {
        internal static string Select(string source, string korean, string bilingualOverride = null)
        {
            return Select(source, korean, bilingualOverride, LocaleMode.CurrentCulture());
        }

        internal static string Select(string source, string korean, string bilingualOverride, string culture)
        {
            // General UI, traders and messages have no automatic bilingual annotation.
            // Keep the legacy parameter for schema compatibility, but never let old profiles
            // re-enable the removed global policy. Audited locale types use LocaleDisplayRules.
            if (LocaleMode.IsKoreanCulture(culture))
                return string.IsNullOrEmpty(korean) ? source : korean;

            return source;
        }

        internal static string Compose(string korean, string english)
        {
            return string.IsNullOrEmpty(korean) ? english : korean;
        }
    }
}
