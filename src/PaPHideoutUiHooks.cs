using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    // Pack-a-Punch 2.2.0: F12 label, shared IMGUI renderers and hideout menu.
    // Translate presentation text only; ownership, selections and callbacks stay original.
    internal static class PaPHideoutUiHooks
    {
        private const string Profile = "com.kennerz.paphideoutmodule";
        private const string AssemblyName = "PaPHideoutModule";
        private const string OwnerType = "PaPHideoutModule.PaPHideoutModulePlugin";
        private const string Version = "2.2.0";
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();

        internal static int Enable(Harmony owner, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (owner == null || data == null || !data.HasRules(Profile, "small_ui")) return 0;
            harmony = owner;
            profiles = data;
            if (!enabled)
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
                enabled = true;
            }
            var total = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) total += Patch(assembly);
            return total;
        }

        internal static void Disable()
        {
            if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
            enabled = false;
            Patched.Clear();
            harmony = null;
            profiles = null;
        }

        private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            try { Patch(args.LoadedAssembly); }
            catch (Exception error) { SPT.EditableTranslations.MinimalLog.WarnOnce("pap-hook-late", () => "Pack-a-Punch optional F12 UI skipped: " + error.Message); }
        }

        private static int Patch(Assembly assembly)
        {
            if (!enabled || harmony == null || assembly == null ||
                assembly.GetName().Name != AssemblyName || Patched.Contains(assembly)) return 0;
            var type = assembly.GetType(OwnerType, false);
            if (type == null) return 0;
            const BindingFlags fields = BindingFlags.Public | BindingFlags.Static;
            var guid = type.GetField("PluginGuid", fields);
            var version = type.GetField("PluginVersion", fields);
            if (guid == null || !guid.IsLiteral || guid.GetRawConstantValue() as string != Profile ||
                version == null || !version.IsLiteral || version.GetRawConstantValue() as string != Version)
                return 0;
            var method = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .SingleOrDefault(m => m.Name == "DrawButton" && m.ReturnType == typeof(void) &&
                    m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string) &&
                    m.GetParameters()[1].ParameterType == typeof(Action));
            int count = 0;
            if (method != null)
                count += TryPatch(method, nameof(BeforeDrawButton), false);
            // Exact source owner/renderer. Either the PaP plugin draws the shared
            // prompt or Raygun/Thundergun does, as decided by the original Owner().
            var normal = assembly.GetType("PaPHideoutModule.PaPNormalGame", false);
            const BindingFlags statics = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            var drawConfirm = normal?.GetMethod("DrawConfirm", statics);
            if (drawConfirm != null && drawConfirm.ReturnType == typeof(void) && drawConfirm.GetParameters().Length == 0)
                count += TryPatch(drawConfirm, nameof(GuiTranspiler), true);
            var prompt = assembly.GetType("PaPHideoutModule.PaPNormalGame+Prompt", false);
            var draw = prompt?.GetMethod("Draw", statics);
            if (draw != null && draw.ReturnType == typeof(void) && draw.GetParameters().Length == 0)
                count += TryPatch(draw, nameof(GuiTranspiler), true);
            var look = assembly.GetType("PaPHideoutModule.PaPNormalGame+Look", false);
            var height = look?.GetMethod("NoteHeight", statics);
            if (height != null && height.GetParameters().Length == 3 && height.GetParameters()[0].ParameterType == typeof(string))
                count += TryPatch(height, nameof(BeforeNoteHeight), false);
            var scroll = look?.GetMethod("ScrollNote", statics);
            if (scroll != null && scroll.GetParameters().Length == 3 && scroll.GetParameters()[1].ParameterType == typeof(string))
                count += TryPatch(scroll, nameof(BeforeScrollNote), false);
            // PaPInteractionMenu.Rebuild accepts presentation tuples with separate
            // Action callbacks. The display names may change, but the Action, item
            // identity, disabled status and actual selection are never rewritten.
            var menu = assembly.GetType("PaPHideoutModule.PaPInteractionMenu", false);
            var rebuild = menu?.GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.NonPublic);
            if (rebuild != null && rebuild.ReturnType == typeof(void) && rebuild.GetParameters().Length == 1 &&
                rebuild.GetParameters()[0].ParameterType == typeof(List<ValueTuple<string, Action>>))
                count += TryPatch(rebuild, nameof(BeforeRebuild), false);
            if (count > 0) Patched.Add(assembly);
            return count;
        }

        private static int TryPatch(MethodInfo method, string callback, bool transpiler)
        {
            if (method == null || harmony == null) return 0;
            try
            {
                var hook = new HarmonyMethod(typeof(PaPHideoutUiHooks).GetMethod(callback,
                    BindingFlags.Static | BindingFlags.NonPublic));
                if (transpiler) harmony.Patch(method, transpiler: hook);
                else harmony.Patch(method, prefix: hook);
                return 1;
            }
            catch (Exception error)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("pap-patch-" + callback,
                    () => "Pack-a-Punch UI patch skipped: " + error.Message);
                return 0;
            }
        }

        // Replace only GUI.Label/Button calls inside the two audited renderers.
        // Rewriting literals or translating AppDomain shared data would break
        // comparisons, ON/OFF results and callbacks in other Kennerz plugins.
        private static IEnumerable<CodeInstruction> GuiTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var label = typeof(GUI).GetMethod("Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
            var button = typeof(GUI).GetMethod("Button", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
            var labelReplacement = typeof(PaPHideoutUiHooks).GetMethod(nameof(DrawLabel), BindingFlags.Static | BindingFlags.NonPublic);
            var buttonReplacement = typeof(PaPHideoutUiHooks).GetMethod(nameof(DrawGuiButton), BindingFlags.Static | BindingFlags.NonPublic);
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

        private static string TranslatePrompt(string source) =>
            KennerzPromptText.Translate(profiles, source, LocaleMode.Refresh());

        private static void DrawLabel(Rect rect, string source, GUIStyle style) =>
            GUI.Label(rect, TranslatePrompt(source), style);

        private static bool DrawGuiButton(Rect rect, string source, GUIStyle style) =>
            GUI.Button(rect, TranslatePrompt(source), style);

        private static void BeforeNoteHeight(ref string __0) => __0 = TranslatePrompt(__0);
        private static void BeforeScrollNote(ref string __1) => __1 = TranslatePrompt(__1);

        private static void BeforeRebuild(ref List<ValueTuple<string, Action>> __0)
        {
            if (__0 == null || __0.Count == 0 || profiles == null ||
                !LocaleMode.IsKoreanCulture(LocaleMode.Refresh())) return;
            try
            {
                var translated = __0.Select(option => new ValueTuple<string, Action>(
                    profiles.Translate(Profile, "interactions", option.Item1, LocaleMode.Refresh()), option.Item2)).ToList();
                __0 = translated;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("pap-menu-display", () =>
                    "Pack-a-Punch interaction labels kept source unchanged: " + ex.Message);
            }
        }

        private static void BeforeDrawButton(ref string __0)
        {
            if (__0 != "Restart quest" || profiles == null) return;
            try
            {
                var culture = LocaleMode.Refresh();
                if (LocaleMode.IsKoreanCulture(culture))
                    __0 = profiles.Translate(Profile, "small_ui", __0, culture);
            }
            catch (Exception error)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("pap-f12-label", () => "Pack-a-Punch F12 label translation skipped: " + error.Message);
            }
        }
    }
}
