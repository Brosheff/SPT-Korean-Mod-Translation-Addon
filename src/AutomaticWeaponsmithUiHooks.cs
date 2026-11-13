using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    // AutomaticWeaponsmith 1.0.2: translate only the text returned for its own
    // build editor buttons, progress messages, build preview and preview notes.
    // The original Lang.T executes first, including its own missing-key fallback.
    // F12 labels are handled independently by ConfigManagerProfiles.
    internal static class AutomaticWeaponsmithUiHooks
    {
        private const string AssemblyName = "AutomaticWeaponsmith";
        private const string LangType = "AutomaticWeaponsmith.Lang";
        private const string ProfileId = "com.automaticweaponsmith";
        private const string Channel = "small_ui";

        private static readonly object Gate = new object();
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static FieldInfo englishField;

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            if (data == null) throw new ArgumentNullException(nameof(data));
            // Do not patch an installed mod if the corresponding translation profile is absent.
            if (!data.HasRules(ProfileId, Channel)) return 0;

            lock (Gate)
            {
                if (enabled) return 0;
                harmony = patcher;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }

            var count = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>())
                count += TryPatch(assembly);
            return count;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                harmony = null;
                profiles = null;
                englishField = null;
                Patched.Clear();
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatch(args.LoadedAssembly); }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("automaticweaponsmith-assembly-load", () =>
                    "AutomaticWeaponsmith optional translation skipped: " + ex.Message);
            }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (assembly == null || assembly.GetName().Name != AssemblyName) return 0;
            lock (Gate)
            {
                if (!enabled || Patched.Contains(assembly)) return 0;

                var type = assembly.GetType(LangType, false);
                if (type == null) return 0;
                var lookup = type.GetMethod("T", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(ValueTuple<string, object>[]) }, null);
                var field = type.GetField("_english", BindingFlags.NonPublic | BindingFlags.Static);
                if (lookup == null || lookup.ReturnType != typeof(string) || field == null ||
                    !typeof(IDictionary<string, string>).IsAssignableFrom(field.FieldType))
                    return 0; // Unknown upstream version: preserve its original UI.

                var postfix = typeof(AutomaticWeaponsmithUiHooks).GetMethod(nameof(AfterLookup),
                    BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(lookup, postfix: new HarmonyMethod(postfix));
                englishField = field;
                Patched.Add(assembly);
                return 1;
            }
        }

        // Harmony's positional arguments match Lang.T(string, ValueTuple<string, object>[]).
        // Replacing the final return text leaves language files, config keys, previews,
        // weapon state, UI creation and original click handlers completely untouched.
        private static void AfterLookup(string __0, ValueTuple<string, object>[] __1, ref string __result)
        {
            if (!enabled || !LocaleMode.IsKoreanMode() || string.IsNullOrEmpty(__0) ||
                !IsBuildUiKey(__0)) return;

            try
            {
                var english = englishField?.GetValue(null) as IDictionary<string, string>;
                if (english == null || !english.TryGetValue(__0, out var original)) return;
                var translated = profiles?.Translate(ProfileId, Channel, original);
                if (string.IsNullOrEmpty(translated) || string.Equals(original, translated, StringComparison.Ordinal))
                    return; // Missing or changed translation: preserve Lang.T's actual result.

                // Lang.T uses literal named placeholders and Value.ToString() for each supplied
                // argument. Preserve that exact interpolation behavior and the current values.
                if (__1 != null)
                {
                    foreach (var item in __1)
                        translated = translated.Replace("{" + item.Item1 + "}", item.Item2?.ToString() ?? "");
                }
                __result = translated;
            }
            catch (Exception ex)
            {
                // Localisation must never prevent the caller's original UI or calculation.
                SPT.EditableTranslations.MinimalLog.WarnOnce("automaticweaponsmith-text", () =>
                    "AutomaticWeaponsmith display translation skipped: " + ex.Message);
            }
        }

        private static bool IsBuildUiKey(string key)
        {
            return key.StartsWith("button.", StringComparison.Ordinal) ||
                   key.StartsWith("mode.", StringComparison.Ordinal) ||
                   key.StartsWith("preview.", StringComparison.Ordinal) ||
                   key.StartsWith("note.", StringComparison.Ordinal);
        }
    }
}
