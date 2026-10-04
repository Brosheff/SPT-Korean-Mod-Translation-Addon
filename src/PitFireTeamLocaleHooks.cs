using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    // PitFireTeam owns its localization data, server merge/fallback, client lookups and refresh loop.
    // Supply native locale files to that pipeline, then only preserve the distinction between the
    // Korean Patch's kr and kr-en cultures. No PitFireTeam UI string is patched directly here.
    internal static class PitFireTeamLocaleHooks
    {
        private const string AssemblyName = "pitFireTeam";
        private const string TypeName = "pitTeam.pitFireTeam";
        private const string ResolveMethodName = "ResolveGameLanguageCode";
        private const string ServerModFolder = "pitFireTeam-ServerMod";
        private const string KoreanAlias = "krx";
        private const string BilingualAlias = "kren";

        internal static int Enable(Harmony harmony, IEnumerable<Assembly> assemblies, string gameRoot, string addonRoot)
        {
            if (harmony == null || assemblies == null || string.IsNullOrWhiteSpace(gameRoot) ||
                string.IsNullOrWhiteSpace(addonRoot)) return 0;

            try
            {
                var assembly = assemblies.FirstOrDefault(a =>
                    string.Equals(a.GetName().Name, AssemblyName, StringComparison.Ordinal));
                if (assembly == null) return 0;

                var type = assembly.GetType(TypeName, false);
                if (type == null) return 0;

                var method = type.GetMethod(
                    ResolveMethodName,
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null || method.ReturnType != typeof(string)) return 0;

                // SPT 4.1.6 keeps server mods under SPT_Runtime/user/mods. PitFireTeam's
                // FriendlyLanguageService reads pitFireTeam-ServerMod/Resources/lang from there.
                // Resources/lang/<locale>.json on every language request/session lookup. Install only
                // our private aliases; deliberately do not create kr.json. Before this Start() hook is
                // active PitFireTeam therefore binds its F12 ConfigEntry metadata from English, while
                // its own next language check switches custom UI/server messages to krx/kren.
                var sourceRoot = Path.Combine(addonRoot, "native-locales", "pitfireteam");
                var targetRoot = Path.Combine(gameRoot, "SPT_Runtime", "user", "mods", ServerModFolder, "Resources", "lang");
                if (!InstallLocaleFile(sourceRoot, targetRoot, KoreanAlias) ||
                    !InstallLocaleFile(sourceRoot, targetRoot, BilingualAlias))
                {
                    SPT.EditableTranslations.MinimalLog.WarnOnce("PitFireTeamLocaleHooks:55", () => "PitFireTeam native Korean locale adapter skipped: locale data not installed.");
                    return 0;
                }

                var postfix = typeof(PitFireTeamLocaleHooks).GetMethod(
                    nameof(AfterResolveGameLanguageCode),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (postfix == null) return 0;

                harmony.Patch(method, postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("PitFireTeamLocaleHooks:70", () => "PitFireTeam native localization adapter skipped: " + ex.Message);
                return 0;
            }
        }

        private static bool InstallLocaleFile(string sourceRoot, string targetRoot, string locale)
        {
            var source = Path.Combine(sourceRoot, locale + ".json");
            if (!File.Exists(source)) return false;

            Directory.CreateDirectory(targetRoot);
            var target = Path.Combine(targetRoot, locale + ".json");
            var incoming = File.ReadAllBytes(source);
            if (File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(incoming)) return true;

            var temporary = target + ".tmp";
            File.WriteAllBytes(temporary, incoming);
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
            return true;
        }

        private static void AfterResolveGameLanguageCode(ref string __result)
        {
            var culture = LocaleMode.CurrentCulture();
            if (string.Equals(culture, LocaleMode.Bilingual, StringComparison.OrdinalIgnoreCase))
            {
                // PitFireTeam normally truncates kr-en at '-', which would make it indistinguishable
                // from kr. A private native-locale alias lets its own CheckLanguageSettingChanged()
                // observe kr <-> kr-en and run its existing RefreshLocalizedText() path.
                __result = BilingualAlias;
            }
            else if (string.Equals(culture, LocaleMode.Korean, StringComparison.OrdinalIgnoreCase))
            {
                __result = KoreanAlias;
            }
        }
    }
}
