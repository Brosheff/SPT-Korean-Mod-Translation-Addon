using System;
using System.Reflection;

namespace SPT.ModKoreanAddon
{
    // Uses the exact locale state owned by Korean Patch Fix 2.1.1.
    // The addon intentionally has no separate language setting.
    internal static class LocaleMode
    {
        internal const string Korean = "kr";
        internal const string Bilingual = "kr-en";

        private static MethodInfo currentCulture;
        private static string cachedCulture;
        private static Func<string> readCulture;

        internal static void Initialize(Assembly koreanPatchAssembly)
        {
            if (koreanPatchAssembly == null) throw new ArgumentNullException(nameof(koreanPatchAssembly));
            var runtime = koreanPatchAssembly.GetType("KoreanPatchFix.ClientLocaleRuntime", false)
                ?? throw new TypeLoadException("KoreanPatchFix.ClientLocaleRuntime was not found.");
            currentCulture = runtime.GetMethod("CurrentCulture",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(runtime.FullName, "CurrentCulture");
            if (currentCulture.ReturnType != typeof(string))
                throw new MissingMethodException(runtime.FullName, "CurrentCulture returned an unexpected type.");
            // Keep every existing query point. Only remove our outer reflection invocation.
            try { readCulture = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), currentCulture); }
            catch { readCulture = () => currentCulture.Invoke(null, null) as string; }
            Refresh();
        }

        // Keep original freshness boundaries: every 30 Update frames, before Casino render,
        // and at notification/dialog entry. Parent-owned language remains authoritative.
        internal static string Refresh()
        {
            try { cachedCulture = readCulture?.Invoke(); }
            catch (Exception ex)
            {
                cachedCulture = null;
                SPT.EditableTranslations.MinimalLog.WarnOnce("locale-query",()=>"Parent language state unavailable; source text retained: "+ex.Message);
            }
            return cachedCulture;
        }

        internal static string CurrentCulture()
        {
            return cachedCulture;
        }

        internal static bool IsKoreanCulture(string culture)
        {
            return string.Equals(culture, Korean, StringComparison.OrdinalIgnoreCase)
                || string.Equals(culture, Bilingual, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsKoreanMode()
        {
            return IsKoreanCulture(cachedCulture);
        }
    }
}
