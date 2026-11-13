using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // RayGunSPT 1.2.0: patch only the audited Raygun assembly's final display paths.
    // No original action, menu selection, server route, ID, balance, or saved config is changed.
    internal static class RaygunUiHooks
    {
        private const string ProfileId = "com.kennerz.raygun";
        private const string SourceAssembly = "Raygun";
        private static readonly Regex X2Count = new Regex(@"\b(\d+) of them left\.", RegexOptions.CultureInvariant);
        private const string X2Beginning = "A Group 935 cold fusion cell, charged up by Pack-a-Punch while it sits in Porter's X2 Ray Gun:";
        private static EditableProfiles profiles;
        private static bool installed;
        private static Harmony harmony;
        private static Assembly patchedAssembly;

        internal static int Enable(Harmony owner, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (owner == null || data == null || !data.HasRules(ProfileId, "raygun_ui")) return 0;
            harmony = owner;
            profiles = data;
            if (!installed)
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
                installed = true;
            }
            int total = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>())
                total += PatchAssembly(assembly);
            return total;
        }

        internal static void Disable()
        {
            if (installed) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
            installed = false;
            patchedAssembly = null;
            harmony = null;
            profiles = null;
        }

        private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            try { PatchAssembly(args.LoadedAssembly); }
            catch (Exception ex) { Warn("assembly-load", ex); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly == null || harmony == null ||
                !string.Equals(assembly.GetName().Name, SourceAssembly, StringComparison.Ordinal) ||
                patchedAssembly == assembly) return 0;
            // Hard source check: refuse to install a partially matching UI on an unknown build.
            var pluginType = assembly.GetType("Raygun.RaygunPlugin", false);
            var ownerType = assembly.GetType("Raygun.RaygunNormalGame", false);
            if (pluginType == null || ownerType == null ||
                !string.Equals((string)pluginType.GetField("PluginGuid", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue(), ProfileId, StringComparison.Ordinal))
                return 0;
            const BindingFlags allStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags allInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            int count = 0;
            count += Patch(ownerType.GetMethod("DrawConfirm", allStatic), nameof(GuiTranspiler), true);
            count += Patch(assembly.GetType("Raygun.RaygunNormalGame+Prompt", false)?.GetMethod("Draw", allStatic), nameof(GuiTranspiler), true);
            var look = assembly.GetType("Raygun.RaygunNormalGame+Look", false);
            count += Patch(look?.GetMethod("ScrollNote", allStatic), nameof(BeforeScrollNote));
            count += Patch(look?.GetMethod("NoteHeight", allStatic), nameof(BeforeNoteHeight));
            count += Patch(assembly.GetType("Raygun.RaygunInteraction", false)?.GetMethod("Rebuild", allInstance), nameof(BeforeRebuild));
            count += Patch(assembly.GetType("Raygun.RaygunX2", false)?.GetMethod("DescriptionPostfix", allStatic), nameof(AfterX2Description), false, true);
            count += Patch(assembly.GetType("Raygun.RaygunAmmoCheck", false)?.GetMethod("TextPostfix", allStatic), nameof(AfterAmmoText), false, true);
            count += Patch(assembly.GetType("Raygun.RaygunAmmoCheck", false)?.GetMethod("FireModePrefix", allStatic), nameof(AmmoTranspiler), true);
            count += Patch(assembly.GetType("Raygun.RaygunAmmoCheck", false)?.GetMethod("CheckPrefix", allStatic), nameof(AmmoTranspiler), true);
            // The fire-mode inventory attribute is supplied by a compiler-generated
            // RaygunAmmoCheck lambda. Patch only closures belonging to that exact source.
            try
            {
                foreach (var nested in assembly.GetTypes().Where(t =>
                    t.FullName != null && t.FullName.StartsWith("Raygun.RaygunAmmoCheck+", StringComparison.Ordinal)))
                {
                    foreach (var method in nested.GetMethods(allStatic | BindingFlags.Instance)
                        .Where(m => m.Name.Contains("WeaponCreatedPostfix") && m.ReturnType == typeof(string) && m.GetParameters().Length == 0))
                        count += Patch(method, nameof(AmmoTranspiler), true);
                }
            }
            catch (ReflectionTypeLoadException error) { Warn("ammo-closure", error); }
            patchedAssembly = assembly;
            return count;
        }

        private static int Patch(MethodInfo original, string hookName, bool transpiler = false, bool postfix = false)
        {
            if (original == null) return 0;
            try
            {
                var patch = new HarmonyMethod(typeof(RaygunUiHooks).GetMethod(hookName, BindingFlags.Static | BindingFlags.NonPublic));
                if (transpiler) harmony.Patch(original, transpiler: patch);
                else if (postfix) harmony.Patch(original, postfix: patch);
                else harmony.Patch(original, prefix: patch);
                return 1;
            }
            catch (Exception ex)
            {
                Warn("patch-" + hookName, ex);
                return 0;
            }
        }

        private static string Translate(string channel, string source)
        {
            if (string.IsNullOrEmpty(source) || profiles == null) return source;
            var culture = LocaleMode.Refresh();
            if (!LocaleMode.IsKoreanCulture(culture)) return source;
            try
            {
                if (string.Equals(channel, "raygun_ui", StringComparison.Ordinal))
                    return KennerzPromptText.Translate(profiles, source, culture);
                return profiles.Translate(ProfileId, channel, source, culture);
            }
            catch (Exception ex) { Warn("translate-" + channel, ex); return source; }
        }

        // The original Draw methods keep all layout, event handling, and conditionals.
        // Swap only final GUI text consumers, not control-flow branch instructions.
        private static IEnumerable<CodeInstruction> GuiTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var label = typeof(GUI).GetMethod("Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
            var button = typeof(GUI).GetMethod("Button", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
            var labelReplacement = typeof(RaygunUiHooks).GetMethod(nameof(DrawLabel), BindingFlags.Static | BindingFlags.NonPublic);
            var buttonReplacement = typeof(RaygunUiHooks).GetMethod(nameof(DrawButton), BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo called)
                {
                    if (called == label) instruction.operand = labelReplacement;
                    else if (called == button) instruction.operand = buttonReplacement;
                }
                yield return instruction;
            }
        }

        private static void DrawLabel(Rect rect, string text, GUIStyle style) => GUI.Label(rect, Translate("raygun_ui", text), style);
        private static bool DrawButton(Rect rect, string text, GUIStyle style) => GUI.Button(rect, Translate("raygun_ui", text), style);
        private static void BeforeScrollNote(ref string __1) => __1 = Translate("raygun_ui", __1);
        private static void BeforeNoteHeight(ref string __0) => __0 = Translate("raygun_ui", __0);

        // The original Rebuild receives a fresh list on each interaction tick. Copying the
        // display labels leaves the original callbacks and disabled states untouched and also
        // allows Rebuild's signature check to refresh on a language change.
        private static void BeforeRebuild(ref List<ValueTuple<string, Action>> __0)
        {
            if (__0 == null || __0.Count == 0 || !LocaleMode.IsKoreanCulture(LocaleMode.Refresh())) return;
            __0 = __0.Select(option => new ValueTuple<string, Action>(Translate("interactions", option.Item1), option.Item2)).ToList();
        }

        // Original RaygunX2 runs *after* ordinary locale lookup and overwrites the item
        // description. Its source string is valid only inside an infused Ray Gun's chamber.
        private static void AfterX2Description(ref string __1)
        {
            if (__1 == null || !__1.StartsWith(X2Beginning, StringComparison.Ordinal)) return;
            var matches = X2Count.Match(__1);
            if (!matches.Success) return;
            var culture = LocaleMode.Refresh();
            if (!LocaleMode.IsKoreanCulture(culture) || profiles == null) return;
            var translated = profiles.Translate(ProfileId, "x2_description", __1, culture);
            if (string.Equals(translated, __1, StringComparison.Ordinal)) return;
            // The source item name is dynamic for a Pack-a-Punched cell (n/20),
            // not the ordinary loose-cell source key (n/10).
            var itemName = "Cold Fusion Cell (" + matches.Groups[1].Value + "/20)";
            __1 = LocaleDisplayRules.Select(__1, translated, null,
                LocaleDisplayRules.ItemDescription, "Ray Gun", culture, itemName);
        }

        private static void AfterAmmoText(ref string __0) => __0 = Translate("ammo_ui", __0);

        // These two original RaygunAmmoCheck methods supply fixed display-only literals.
        // No constructor attributes, gameplay fire mode or ammo value is altered.
        private static IEnumerable<CodeInstruction> AmmoTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var translate = typeof(RaygunUiHooks).GetMethod(nameof(TranslateAmmoLiteral), BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string text &&
                    (text == "Automatic" || text == "Cold Fusion Cells"))
                    yield return new CodeInstruction(OpCodes.Call, translate);
            }
        }
        private static string TranslateAmmoLiteral(string text) => Translate("ammo_ui", text);

        private static void Warn(string stage, Exception error) =>
            SPT.EditableTranslations.MinimalLog.WarnOnce("raygun-ui-" + stage,
                () => "Ray Gun display hook kept source unchanged: " + error.Message);
    }
}
