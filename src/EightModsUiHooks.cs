using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    // Narrowly scoped translations for source-audited client mods.
    // Do not modify stored configuration values, game-wide labels, or user-entered death text.
    internal static class EightModsUiHooks
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static Harmony harmony;
        private static bool enabled;

        private static readonly Dictionary<string, string> PaletteLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tuning: "] = "조정: ",
            [" (unsaved)"] = " (저장되지 않음)",
            ["Save Palette"] = "팔레트 저장",
            ["Palette Saved"] = "팔레트 저장됨",
            ["MainTexColorCoef - Brightness"] = "기본 텍스처 계수 - 밝기",
            ["MinTempValue - ColorDiff"] = "최저 온도 값 - 색상 차이",
            ["RampShift - ColorShift"] = "램프 이동 - 색상 이동",
        };

        internal static void Enable(Harmony patcher, IEnumerable<Assembly> assemblies)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            lock (Gate)
            {
                if (enabled) return;
                harmony = patcher;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) TryPatchAssembly(assembly);
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

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e)
        {
            try { TryPatchAssembly(e.LoadedAssembly); }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("eight-mods-late-load", () =>
                    "[Eight Mods] late-load hook skipped: " + ex.Message);
            }
        }

        private static void TryPatchAssembly(Assembly assembly)
        {
            if (assembly == null) return;
            var name = assembly.GetName().Name ?? "";
            const BindingFlags methods = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (name == "BetterThermal" || name == "BetterThermalNightVision")
            {
                var type = assembly.GetType("BetterVision.BetterVision", false);
                var draw = type?.GetMethod("DrawPaletteTuning", methods);
                if (draw != null) Patch(draw, transpiler: nameof(TranslatePaletteLiterals));
            }
            else if (name == "HeadshotDarkness")
            {
                var type = assembly.GetType("HeadshotDarkness.Components.DeathTextManager", false);
                var method = type?.GetMethods(methods).FirstOrDefault(m => m.Name == "DoDeathText" &&
                    m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == typeof(string));
                if (method != null) Patch(method, prefix: nameof(TranslateDeathText));
            }
            else if (name == "Softwyx-LootInVicinity")
            {
                var type = assembly.GetType("Softwyx.LootInVicinity.Config.Settings", false);
                var method = type?.GetMethod("FormatPanelTitle", methods, null, new[] { typeof(int) }, null);
                if (method != null) Patch(method, postfix: nameof(TranslateNearbyPanelTitle));
            }
        }

        private static void Patch(MethodBase target, string prefix = null, string postfix = null, string transpiler = null)
        {
            lock (Gate)
            {
                if (!enabled || harmony == null || !Patched.Add(target)) return;
            }
            try
            {
                HarmonyMethod p = prefix == null ? null : new HarmonyMethod(typeof(EightModsUiHooks).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic));
                HarmonyMethod q = postfix == null ? null : new HarmonyMethod(typeof(EightModsUiHooks).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic));
                HarmonyMethod t = transpiler == null ? null : new HarmonyMethod(typeof(EightModsUiHooks).GetMethod(transpiler, BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(target, prefix: p, postfix: q, transpiler: t);
            }
            catch (Exception ex)
            {
                lock (Gate) Patched.Remove(target);
                SPT.EditableTranslations.MinimalLog.WarnOnce("eight-mods-" + target.DeclaringType.FullName + "." + target.Name,
                    () => "[Eight Mods] hook skipped: " + ex.Message);
            }
        }

        private static string TranslatePaletteString(string source)
        {
            if (!LocaleMode.IsKoreanMode() || source == null) return source;
            return PaletteLabels.TryGetValue(source, out var ko) ? ko : source;
        }

        private static IEnumerable<CodeInstruction> TranslatePaletteLiterals(IEnumerable<CodeInstruction> instructions)
        {
            var translate = typeof(EightModsUiHooks).GetMethod(nameof(TranslatePaletteString), BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string source && PaletteLabels.ContainsKey(source))
                    yield return new CodeInstruction(OpCodes.Call, translate);
            }
        }

        private static void TranslateDeathText(ref string text)
        {
            if (!LocaleMode.IsKoreanMode()) return;
            // Only source-defined defaults are translated. Any user-authored sentence is untouched.
            switch (text)
            {
                case "You are dead.": text = "사망했습니다."; break;
                case "You have been shot in the head.": text = "머리에 총상을 입었습니다."; break;
                case "You have been blown up.": text = "폭발로 사망했습니다."; break;
            }
        }

        private static void TranslateNearbyPanelTitle(ref string __result)
        {
            if (!LocaleMode.IsKoreanMode() || string.IsNullOrEmpty(__result)) return;
            if (__result == "Nearby Items") { __result = "근처 아이템"; return; }
            const string prefix = "Nearby Items (";
            if (!__result.StartsWith(prefix, StringComparison.Ordinal) || !__result.EndsWith(")", StringComparison.Ordinal)) return;
            var count = __result.Substring(prefix.Length, __result.Length - prefix.Length - 1);
            if (count.Length > 0 && count.All(char.IsDigit)) __result = "근처 아이템 (" + count + ")";
        }
    }
}
