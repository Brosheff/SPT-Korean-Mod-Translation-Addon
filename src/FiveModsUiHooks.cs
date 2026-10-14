using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    // Source-audited 0.0.2 / 1.2.0 mod UI entry points only.
    // There are no global Unity UI, string, MessageBox or EFT tooltip replacements.
    internal static class FiveModsUiHooks
    {
        private const string StatsAssembly = "Wara-ModdingStatsHelper";
        private const string GrenadeAssembly = "IncendiaryGrenade";
        private const string GrenadeGuid = "com.sage.incendiarygrenade";
        private const string SourceError =
            "Incendiary Grenade requires BigBrain, which is not installed.\n\n" +
            "Install BigBrain, then start the game again.\n\n" +
            "The game will now close.";
        private const string KoreanError =
            "Incendiary Grenade를 실행하려면 BigBrain이 필요하지만 설치되지 않았습니다.\n\n" +
            "BigBrain을 설치한 뒤 게임을 다시 시작하십시오.\n\n" +
            "게임을 종료합니다.";

        private static readonly object Gate = new object();
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static Harmony harmony;
        private static bool enabled;

        internal static void Enable(Harmony patcher, IEnumerable<Assembly> assemblies)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            lock (Gate)
            {
                if (enabled) return;
                enabled = true;
                harmony = patcher;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>())
                TryPatchAssembly(assembly);
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                harmony = null;
                Patched.Clear();
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatchAssembly(args.LoadedAssembly); }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("five-mods-late-load", () =>
                    "Five mods scoped UI hook skipped: " + ex.Message);
            }
        }

        private static void TryPatchAssembly(Assembly assembly)
        {
            if (assembly == null) return;
            var name = assembly.GetName().Name;
            if (string.Equals(name, StatsAssembly, StringComparison.OrdinalIgnoreCase))
                TryPatchStatsTooltip();
            // The client's actual assembly may use the .csproj name rather than its namespace.
            if (string.Equals(name, GrenadeAssembly, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "IncendiaryGrenade.Client", StringComparison.OrdinalIgnoreCase))
                TryPatchGrenadeError(assembly);
        }

        private static void PatchOnce(MethodBase target, HarmonyMethod prefix)
        {
            if (target == null) return;
            lock (Gate)
            {
                if (!enabled || harmony == null || !Patched.Add(target)) return;
            }
            try { harmony.Patch(target, prefix: prefix); }
            catch (Exception ex)
            {
                lock (Gate) Patched.Remove(target);
                SPT.EditableTranslations.MinimalLog.WarnOnce("five-mods-" + target.Name, () =>
                    "Five mods scoped hook skipped: " + target.DeclaringType.FullName + "." + target.Name + " - " + ex.Message);
            }
        }

        private static void TryPatchStatsTooltip()
        {
            // Original ShowMeTheStats.ShowTooltipPatch.Prefix constructs the tooltip before
            // SimpleTooltip.Show is called. A last-priority prefix translates only visible
            // rich-text tokens after the mod prefix has run, without rewriting its
            // COMPARISON/EQUIPPED/STASH internal condition checks.
            var type = AccessTools.TypeByName("EFT.UI.SimpleTooltip");
            if (type == null) return;
            var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "Show" &&
                    m.GetParameters().Any(p => p.ParameterType == typeof(string) && p.Name == "text"));
            if (method == null) return;
            PatchOnce(method, new HarmonyMethod(typeof(FiveModsUiHooks).GetMethod(
                nameof(TranslateStatsTooltip), BindingFlags.Static | BindingFlags.NonPublic))
                { priority = Priority.Last });
        }

        private static void TranslateStatsTooltip(ref string text)
        {
            if (!LocaleMode.IsKoreanMode() || string.IsNullOrEmpty(text) ||
                text.IndexOf("<mspace=0.55em>", StringComparison.Ordinal) < 0) return;
            // Correct the mod's narrow monospaced glyph spacing for Korean
            // without changing its internal STATS/COMPARISON checks.
            text = ModdingStatsTooltipText.Translate(text);
        }

        private static void TryPatchGrenadeError(Assembly assembly)
        {
            var type = assembly.GetType("IncendiaryGrenade.IncendiaryPlugin", false);
            var method = type?.GetMethod("ShowError", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            if (method == null) return;
            PatchOnce(method, new HarmonyMethod(typeof(FiveModsUiHooks).GetMethod(
                nameof(TranslateGrenadeError), BindingFlags.Static | BindingFlags.NonPublic))
                { priority = Priority.Last });
        }

        private static void TranslateGrenadeError(ref string message)
        {
            if (LocaleMode.IsKoreanMode() &&
                string.Equals(message, SourceError, StringComparison.Ordinal))
                message = KoreanError;
        }
    }
}
