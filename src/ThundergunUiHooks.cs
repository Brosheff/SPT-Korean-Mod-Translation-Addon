using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // ThunderGunSPT 1.2.0: modify only the final display strings of the audited
    // Thundergun assembly. Game actions, UI control IDs, inventory IDs and saves
    // are not translated, and this family fails independently of Raygun.
    internal static class ThundergunUiHooks
    {
        private const string ProfileId = "com.kennerz.thundergun";
        private const string SourceAssembly = "Thundergun";
        private static EditableProfiles profiles;
        private static Harmony harmony;
        private static Assembly patchedAssembly;
        private static bool listening;
        private static FieldInfo thundergunChargeState;
        private static readonly ConditionalWeakTable<object, object> WrappedStats =
            new ConditionalWeakTable<object, object>();

        internal static int Enable(Harmony owner, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (owner == null || data == null || !data.HasRules(ProfileId, "thundergun_ui")) return 0;
            harmony = owner;
            profiles = data;
            if (!listening)
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
                listening = true;
            }
            int count = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) count += PatchAssembly(assembly);
            return count;
        }

        internal static void Disable()
        {
            if (listening) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
            listening = false;
            patchedAssembly = null;
            harmony = null;
            profiles = null;
        }

        private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            try { PatchAssembly(args.LoadedAssembly); }
            catch (Exception error) { Warn("assembly-load", error); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly == null || harmony == null || patchedAssembly == assembly ||
                !string.Equals(assembly.GetName().Name, SourceAssembly, StringComparison.Ordinal)) return 0;
            var plugin = assembly.GetType("Thundergun.ThundergunPlugin", false);
            var window = assembly.GetType("Thundergun.ThundergunNormalGame", false);
            if (plugin == null || window == null ||
                !string.Equals((string)plugin.GetField("PluginGuid", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue(),
                    ProfileId, StringComparison.Ordinal)) return 0;
            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags instances = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            int count = 0;
            count += Patch(window.GetMethod("DrawConfirm", statics), nameof(GuiTranspiler), transpiler: true);
            count += Patch(assembly.GetType("Thundergun.ThundergunNormalGame+Prompt", false)?.GetMethod("Draw", statics),
                nameof(GuiTranspiler), transpiler: true);
            var look = assembly.GetType("Thundergun.ThundergunNormalGame+Look", false);
            count += Patch(look?.GetMethod("NoteHeight", statics), nameof(BeforeNoteHeight));
            count += Patch(look?.GetMethod("ScrollNote", statics), nameof(BeforeScrollNote));
            count += Patch(assembly.GetType("Thundergun.ThundergunInteraction", false)?.GetMethod("Rebuild", instances),
                nameof(BeforeRebuild));
            var ammo = assembly.GetType("Thundergun.ThundergunAmmoCheck", false);
            thundergunChargeState = ammo?.GetField("_charges", statics);
            count += Patch(ammo?.GetMethod("TextPostfix", statics), nameof(AfterAmmoText), postfix: true);
            count += Patch(ammo?.GetMethod("ShowPrefix", statics), nameof(AfterAmmoDetails), postfix: true);
            // The mod adds two custom item attribute rows. After the row is created,
            // wrap only its display delegates; the attribute Name remains an English
            // identity key for the original duplicate-row check.
            count += Patch(assembly.GetType("Thundergun.ThundergunHandling", false)?.GetMethod("AddRow", statics),
                nameof(AfterStatRow), postfix: true);
            patchedAssembly = assembly;
            return count;
        }

        private static int Patch(MethodInfo original, string hookName, bool transpiler = false, bool postfix = false)
        {
            if (original == null) return 0;
            try
            {
                var method = typeof(ThundergunUiHooks).GetMethod(hookName, BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null) return 0;
                var hook = new HarmonyMethod(method);
                if (transpiler) harmony.Patch(original, transpiler: hook);
                else if (postfix) harmony.Patch(original, postfix: hook);
                else harmony.Patch(original, prefix: hook);
                return 1;
            }
            catch (Exception error) { Warn("patch-" + hookName, error); return 0; }
        }

        private static string Translate(string channel, string source)
        {
            if (string.IsNullOrEmpty(source) || profiles == null) return source;
            var culture = LocaleMode.Refresh();
            if (!LocaleMode.IsKoreanCulture(culture)) return source;
            try
            {
                if (string.Equals(channel, "thundergun_ui", StringComparison.Ordinal))
                    return KennerzPromptText.Translate(profiles, source, culture);
                return profiles.Translate(ProfileId, channel, source, culture);
            }
            catch (Exception error) { Warn("translate-" + channel, error); return source; }
        }

        // Replace only Unity IMGUI's final text consumers, not labels serving as
        // route IDs, interaction signatures or values in the shared owner list.
        private static IEnumerable<CodeInstruction> GuiTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var label = typeof(GUI).GetMethod("Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
            var button = typeof(GUI).GetMethod("Button", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
            var replacementLabel = typeof(ThundergunUiHooks).GetMethod(nameof(DrawLabel), BindingFlags.Static | BindingFlags.NonPublic);
            var replacementButton = typeof(ThundergunUiHooks).GetMethod(nameof(DrawButton), BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo called)
                {
                    if (called == label) instruction.operand = replacementLabel;
                    else if (called == button) instruction.operand = replacementButton;
                }
                yield return instruction;
            }
        }

        private static void DrawLabel(Rect rect, string text, GUIStyle style) => GUI.Label(rect, Translate("thundergun_ui", text), style);
        private static bool DrawButton(Rect rect, string text, GUIStyle style) => GUI.Button(rect, Translate("thundergun_ui", text), style);
        private static void BeforeNoteHeight(ref string __0) => __0 = Translate("thundergun_ui", __0);
        private static void BeforeScrollNote(ref string __1) => __1 = Translate("thundergun_ui", __1);

        private static void BeforeRebuild(ref List<ValueTuple<string, Action>> __0)
        {
            if (__0 == null || __0.Count == 0 || !LocaleMode.IsKoreanCulture(LocaleMode.Refresh())) return;
            __0 = __0.Select(option => new ValueTuple<string, Action>(Translate("interactions", option.Item1), option.Item2)).ToList();
        }

        private static bool CheckingThundergun()
        {
            try { return thundergunChargeState != null && (int)thundergunChargeState.GetValue(null) >= 0; }
            catch { return false; }
        }

        private static void AfterAmmoText(ref string __0)
        {
            if (CheckingThundergun()) __0 = Translate("ammo_ui", __0);
        }
        private static void AfterAmmoDetails(ref string details)
        {
            if (CheckingThundergun()) details = Translate("ammo_ui", details);
        }

        private static void AfterStatRow(object __0, string __1)
        {
            if (__0 == null || (__1 != "MOVEMENT SPEED" && __1 != "TURN SPEED") || profiles == null) return;
            try
            {
                var attrProp = AccessTools.Property(__0.GetType(), "Attributes");
                var attrField = AccessTools.Field(__0.GetType(), "Attributes");
                var attrs = (attrProp != null ? attrProp.GetValue(__0, null) : attrField?.GetValue(__0)) as IEnumerable;
                if (attrs == null) return;
                foreach (object attr in attrs)
                {
                    if (attr == null) continue;
                    var nameProperty = AccessTools.Property(attr.GetType(), "Name");
                    var nameField = AccessTools.Field(attr.GetType(), "Name");
                    var storedName = nameProperty != null ? nameProperty.GetValue(attr, null) : nameField?.GetValue(attr);
                    if (!string.Equals(storedName as string, __1, StringComparison.Ordinal)) continue;
                    if (WrappedStats.TryGetValue(attr, out _)) break;
                    WrapDisplayDelegate(attr, "DisplayNameFunc", "stats_ui");
                    WrapDisplayDelegate(attr, "StringValue", "stats_ui");
                    WrappedStats.Add(attr, new object());
                    break;
                }
            }
            catch (Exception error) { Warn("stat-row", error); }
        }

        private static void WrapDisplayDelegate(object attribute, string memberName, string channel)
        {
            var type = attribute.GetType();
            var property = AccessTools.Property(type, memberName);
            var field = AccessTools.Field(type, memberName);
            var original = (property != null ? property.GetValue(attribute, null) : field?.GetValue(attribute)) as Func<string>;
            if (original == null) return;
            Func<string> localized = () => Translate(channel, original());
            if (property != null && property.CanWrite) property.SetValue(attribute, localized, null);
            else field?.SetValue(attribute, localized);
        }

        private static void Warn(string stage, Exception error) =>
            SPT.EditableTranslations.MinimalLog.WarnOnce("thundergun-ui-" + stage,
                () => "Thundergun display hook kept source unchanged: " + error.Message);
    }
}
