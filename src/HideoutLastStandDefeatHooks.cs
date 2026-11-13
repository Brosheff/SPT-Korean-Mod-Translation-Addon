using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Hideout Last Stand R004 / 0.1.0-0.1.3: the copied end-of-raid statistics
    // screen is built from the source mod's English labels, without going
    // through SiegeUi.Text (the R002 IMGUI path). Read only the completed
    // copy's TMP labels, never the raid result DTO or button callbacks.
    internal static class HideoutLastStandDefeatHooks
    {
        private const string ProfileId = "jrs.hideoutsiege";
        private const string Channel = "small_ui";
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private sealed class LabelState
        {
            internal TMP_Text Label;
            internal string Source;
            internal string Applied;
            internal TMP_FontAsset OriginalFont;
            internal TMP_FontAsset AppliedFont;
        }
        private static readonly List<LabelState> Labels = new List<LabelState>();
        private static GameObject currentCopy;
        private static FieldInfo copyField;
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static bool enabled;

        internal static int Enable(Harmony owner, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (data == null || !data.HasRules(ProfileId, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                harmony = owner;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int total = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) total += TryPatch(assembly);
            return total;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                foreach (var state in Labels)
                {
                    try
                    {
                        if (state.Label == null) continue;
                        if (state.Label.text == state.Applied) state.Label.text = state.Source;
                        if (state.AppliedFont != null && state.Label.font == state.AppliedFont)
                            state.Label.font = state.OriginalFont;
                    }
                    catch (Exception) { /* Defeat screen may have already closed. */ }
                }
                Labels.Clear();
                Patched.Clear();
                currentCopy = null;
                copyField = null;
                profiles = null;
                harmony = null;
            }
        }

        internal static void RefreshLanguage()
        {
            if (!enabled || profiles == null || currentCopy == null) return;
            foreach (var state in Labels)
                try { Apply(state); }
                catch (Exception) { /* Never interfere with the original result screen. */ }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatch(args.LoadedAssembly); }
            catch (Exception ex) { Warn("late", ex); }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (assembly == null) return 0;
            var plugin = assembly.GetType("HideoutSiege.Plugin", false);
            if (plugin == null) return 0;
            var id = plugin.GetField("Id", BindingFlags.Public | BindingFlags.Static);
            var version = plugin.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            if (id == null || version == null || !id.IsLiteral || !version.IsLiteral ||
                !string.Equals(id.GetRawConstantValue() as string, ProfileId, StringComparison.Ordinal) ||
                !HideoutLastStandVersionGuard.IsSupported(version.GetRawConstantValue() as string)) return 0;

            var defeat = assembly.GetType("HideoutSiege.SiegeDefeatScreen", false);
            var method = defeat?.GetMethod("BuildCopy", All, null, Type.EmptyTypes, null);
            var field = defeat?.GetField("copy", All);
            if (method == null || !method.IsStatic || method.ReturnType != typeof(bool) ||
                field == null || !field.IsStatic || field.FieldType != typeof(GameObject))
            {
                Warn("signature", new MissingMethodException("Hideout Last Stand reviewed BuildCopy/copy signature changed"));
                return 0;
            }
            lock (Gate)
            {
                if (!enabled || harmony == null || !Patched.Add(assembly)) return 0;
            }
            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(HideoutLastStandDefeatHooks), nameof(AfterBuildCopy)));
                copyField = field;
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) Patched.Remove(assembly);
                Warn("patch", ex);
                return 0;
            }
        }

        private static void AfterBuildCopy(bool __result)
        {
            if (!enabled || !__result || profiles == null || copyField == null) return;
            try
            {
                var copy = copyField.GetValue(null) as GameObject;
                if (copy == null) return;
                // Each copy is freshly built from the source mod's English data.
                // No mutation of Sections/Wrecked/defeat reports or button controls.
                Labels.Clear();
                currentCopy = copy;
                foreach (var label in copy.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label == null || string.IsNullOrEmpty(label.text) ||
                        !profiles.CanTranslate(ProfileId, Channel, label.text)) continue;
                    var state = new LabelState
                    {
                        Label = label,
                        Source = label.text,
                        Applied = label.text,
                        OriginalFont = label.font
                    };
                    Labels.Add(state);
                    Apply(state);
                }
            }
            catch (Exception ex) { Warn("copy", ex); }
        }

        private static void Apply(LabelState state)
        {
            var label = state.Label;
            if (label == null || (label.text != state.Applied && label.text != state.Source)) return;
            var next = profiles.Translate(ProfileId, Channel, state.Source);
            if (next == null) next = state.Source;
            if (!string.Equals(label.text, next, StringComparison.Ordinal)) label.text = next;
            state.Applied = next;
            if (!LocaleMode.IsKoreanMode())
            {
                if (state.AppliedFont != null && label.font == state.AppliedFont) label.font = state.OriginalFont;
                state.AppliedFont = null;
                return;
            }
            if (!next.Any(c => c >= '\uAC00' && c <= '\uD7A3') || Supports(label.font, next)) return;
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font == null || !Supports(font, next)) continue;
                if (label.font != font) { label.font = font; state.AppliedFont = font; }
                return;
            }
            SPT.EditableTranslations.MinimalLog.WarnOnce("hls-r004-font", () =>
                "Hideout Last Stand defeat statistics: Korean TMP font not found; text may be missing glyphs.");
        }

        private static bool Supports(TMP_FontAsset font, string text)
        {
            if (font == null) return false;
            foreach (var c in text)
                if (c >= '\uAC00' && c <= '\uD7A3' && !font.HasCharacter(c)) return false;
            return true;
        }

        private static void Warn(string key, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("hls-r004-" + key,
                () => "Hideout Last Stand defeat screen localization skipped: " + ex.Message);
        }
    }
}
