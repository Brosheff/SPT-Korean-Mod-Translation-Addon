using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Tutorial Backport 1.1.0: only its intro labels and its own TMP factories.
    // Never patch a global TMP setter or change input, timeline, sound or saved config.
    internal static class TutorialBackportUiHooks
    {
        private const string AssemblyName = "TutorialBackport";
        private const string Namespace = "TutorialBackport.Client.";
        private const string Profile = "com.sage.tutorialbackport";
        private const string Channel = "small_ui";
        private static readonly string[] IntroSources = { "Skip", "Subtitles: On", "Subtitles: Off" };
        // Only four upstream tape subtitle IDs with verified Korean-source typos.
        // This does not modify the upstream/global Korean dictionary or subtitle timing.
        private static readonly HashSet<string> CorrectedTapeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "691343ce4ded41ee2bea97ff",
            "6913452812748b69b284e206",
            "68b6dbfae852a369ef69cff3",
            "68ea5554bb3c96a1b0486d78"
        };
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int FontRefreshFrames = 300;

        private sealed class FontState
        {
            internal TMP_FontAsset OriginalFont;
            internal Material OriginalMaterial;
            internal TMP_FontAsset AppliedFont;
            internal Material AppliedMaterial;
            internal bool Changed;
            internal string LastText;
            internal string LastCulture;
            internal int LastFrame;
            internal TMP_FontAsset LastFont;
            internal Material LastMaterial;
        }

        private sealed class IntroState
        {
            internal WeakReference<TMP_Text> Label;
            internal string Source;
            internal string Applied;
            internal bool Resized;
            internal bool OriginalAutoSize;
            internal float OriginalSize;
            internal float OriginalSizeMin;
            internal float OriginalSizeMax;
        }

        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static readonly List<IntroState> IntroLabels = new List<IntroState>();
        private static readonly List<WeakReference<TMP_Text>> FontLabels = new List<WeakReference<TMP_Text>>();
        private static ConditionalWeakTable<TMP_Text, FontState> fontStates = new ConditionalWeakTable<TMP_Text, FontState>();
        private static Harmony owner;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static FieldInfo introToggleField;
        private static FieldInfo introSubtitleField;
        private static FieldInfo tapeSubtitleField;
        private static TMP_FontAsset[] loadedFonts;
        private static int searchedAt = -FontRefreshFrames;

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (patcher == null || data == null || !data.HasRules(Profile, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                enabled = true;
                owner = patcher;
                profiles = data;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int count = 0;
            if (assemblies != null)
                foreach (var assembly in assemblies) count += TryPatch(assembly);
            return count;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatch(args.LoadedAssembly); }
            catch (Exception ex) { Warn("tutorial-backport-late-install", ex); }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (!enabled || assembly == null || assembly.GetName().Name != AssemblyName) return 0;
            var version = assembly.GetName().Version;
            // A changed upstream binary needs a fresh source/IL audit before patching.
            if (version == null || version.Major != 1 || version.Minor != 1 || version.Build != 0) return 0;
            lock (Gate)
            {
                if (Patched.Contains(assembly)) return 0;
                var intro = assembly.GetType(Namespace + "IntroPlayer", false);
                var build = Exact(intro, "BuildUi", 0);
                var update = Exact(intro, "Update", 0);
                if (build == null || update == null) return 0;
                introToggleField = AccessTools.Field(intro, "_toggleLabel");
                introSubtitleField = AccessTools.Field(intro, "_subtitleText");
                if (introToggleField == null || introSubtitleField == null) return 0;

                try
                {
                    owner.Patch(build, postfix: Hook(nameof(AfterIntroBuild)));
                    owner.Patch(update, postfix: Hook(nameof(AfterIntroUpdate)));
                    int installed = 2;
                    var hints = assembly.GetType(Namespace + "TutorialHintView", false);
                    var notes = assembly.GetType(Namespace + "NoteWindow", false);
                    var tape = assembly.GetType(Namespace + "TapeRecorder", false);
                    var notification = assembly.GetType(Namespace + "TapeNotificationViews", false);
                    installed += Optional(hints, "Build", 1, nameof(AfterContainerBuild));
                    installed += Optional(notes, "Build", 2, nameof(AfterContainerBuild));
                    installed += Optional(notes, "ShowPage", 1, nameof(AfterContainerBuild));
                    installed += Optional(tape, "UpdateUi", 0, nameof(AfterTapeUpdate));
                    installed += Optional(tape, "Localize", 2, nameof(AfterTapeLocalize));
                    installed += Optional(notification, "AddLines", 3, nameof(AfterTapeLines));
                    tapeSubtitleField = tape == null ? null : AccessTools.Field(tape, "_subtitleText");
                    Patched.Add(assembly);
                    return installed;
                }
                catch (Exception ex)
                {
                    // No half-installed translation hooks when the required intro contract fails.
                    Warn("tutorial-backport-install", ex);
                    Disable();
                    return 0;
                }
            }
        }

        private static HarmonyMethod Hook(string method)
        {
            return new HarmonyMethod(typeof(TutorialBackportUiHooks).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static MethodInfo Exact(Type type, string name, int count)
        {
            if (type == null) return null;
            MethodInfo found = null;
            foreach (var method in type.GetMethods(All))
                if (method.Name == name && method.GetParameters().Length == count)
                {
                    if (found != null) return null;
                    found = method;
                }
            return found;
        }

        private static int Optional(Type type, string name, int count, string postfix)
        {
            var target = Exact(type, name, count);
            if (target == null) return 0;
            try { owner.Patch(target, postfix: Hook(postfix)); return 1; }
            catch (Exception ex) { Warn("tutorial-backport-optional-" + name, ex); return 0; }
        }

        private static void AfterIntroBuild(object __instance)
        {
            try
            {
                var component = __instance as Component;
                if (component == null) return;
                foreach (var label in component.GetComponentsInChildren<TMP_Text>(true))
                {
                    TrackIntro(label);
                    EnsureFont(label);
                }
            }
            catch (Exception ex) { Warn("tutorial-backport-intro-build", ex); }
        }

        private static void AfterIntroUpdate(object __instance)
        {
            try
            {
                TrackIntro(introToggleField?.GetValue(__instance) as TMP_Text);
                EnsureFont(introSubtitleField?.GetValue(__instance) as TMP_Text);
            }
            catch (Exception ex) { Warn("tutorial-backport-intro-update", ex); }
        }

        private static bool SourceLabel(string value)
        {
            foreach (var source in IntroSources) if (value == source) return true;
            return false;
        }

        private static void TrackIntro(TMP_Text label)
        {
            if (label == null) return;
            var current = label.text;
            IntroState state = null;
            for (int i = IntroLabels.Count - 1; i >= 0; --i)
            {
                if (!IntroLabels[i].Label.TryGetTarget(out var existing) || existing == null)
                    IntroLabels.RemoveAt(i);
                else if (existing == label) state = IntroLabels[i];
            }
            if (state == null && !SourceLabel(current)) return;
            if (state == null)
            {
                state = new IntroState { Label = new WeakReference<TMP_Text>(label), Source = current };
                IntroLabels.Add(state);
            }
            else if (SourceLabel(current)) state.Source = current; // Native Update rewrites the toggle each press.
            else if (current != state.Applied) return; // Another owner wrote this label.

            // The original Skip label is only 59.6 units wide at font size 32.
            // Fit the longer Korean caption without touching its button, timer, or anchors.
            AdjustSkipSize(label, state);
            string replacement = profiles.Translate(Profile, Channel, state.Source);
            if (label.text != replacement) label.text = replacement;
            state.Applied = replacement;
            EnsureFont(label);
        }

        private static void AdjustSkipSize(TMP_Text label, IntroState state)
        {
            if (state.Source != "Skip") return;
            if (LocaleMode.IsKoreanMode())
            {
                if (!state.Resized)
                {
                    state.OriginalAutoSize = label.enableAutoSizing;
                    state.OriginalSize = label.fontSize;
                    state.OriginalSizeMin = label.fontSizeMin;
                    state.OriginalSizeMax = label.fontSizeMax;
                    state.Resized = true;
                }
                label.enableAutoSizing = true;
                label.fontSizeMin = 12f;
                label.fontSizeMax = state.OriginalSize;
            }
            else RestoreSkipSize(label, state);
        }

        private static void RestoreSkipSize(TMP_Text label, IntroState state)
        {
            if (!state.Resized) return;
            label.enableAutoSizing = state.OriginalAutoSize;
            label.fontSize = state.OriginalSize;
            label.fontSizeMin = state.OriginalSizeMin;
            label.fontSizeMax = state.OriginalSizeMax;
            state.Resized = false;
        }

        private static void AfterContainerBuild(object __instance)
        {
            try
            {
                var container = __instance as Component;
                if (container == null) return;
                foreach (var label in container.GetComponentsInChildren<TMP_Text>(true)) EnsureFont(label);
            }
            catch (Exception ex) { Warn("tutorial-backport-container-font", ex); }
        }

        private static void AfterTapeLocalize(string key, ref string __result)
        {
            if (!enabled || profiles == null || !LocaleMode.IsKoreanMode() ||
                !CorrectedTapeIds.Contains(key) || string.IsNullOrEmpty(__result)) return;
            try
            {
                // Original Localize has already resolved the native Korean string.
                // Exact-source guard prevents changing English or other translations.
                __result = profiles.Translate(Profile, Channel, __result);
            }
            catch (Exception ex) { Warn("tutorial-backport-tape-locale", ex); }
        }

        private static void AfterTapeUpdate(object __instance)
        {
            try { EnsureFont(tapeSubtitleField?.GetValue(__instance) as TMP_Text); }
            catch (Exception ex) { Warn("tutorial-backport-tape-font", ex); }
        }

        private static void AfterTapeLines(List<TextMeshProUGUI> __result)
        {
            if (__result == null) return;
            try { foreach (var label in __result) EnsureFont(label); }
            catch (Exception ex) { Warn("tutorial-backport-tape-lines", ex); }
        }

        internal static void RefreshLanguage()
        {
            try
            {
                for (int i = IntroLabels.Count - 1; i >= 0; i--)
                {
                    var entry = IntroLabels[i];
                    if (!entry.Label.TryGetTarget(out var label) || label == null)
                    {
                        IntroLabels.RemoveAt(i);
                        continue;
                    }
                    // Preserve updates from the original mod and other plugins.
                    if (label.text != entry.Applied && label.text != entry.Source) continue;
                    AdjustSkipSize(label, entry);
                    string next = profiles.Translate(Profile, Channel, entry.Source);
                    if (label.text != next) label.text = next;
                    entry.Applied = next;
                    EnsureFont(label);
                }
                RefreshTrackedFonts();
            }
            catch (Exception ex) { Warn("tutorial-backport-language-refresh", ex); }
        }

        private static void RefreshTrackedFonts()
        {
            for (int i = FontLabels.Count - 1; i >= 0; i--)
            {
                if (!FontLabels[i].TryGetTarget(out var label) || label == null)
                    FontLabels.RemoveAt(i);
                else EnsureFont(label);
            }
        }

        private static bool Hangul(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (c >= '\uac00' && c <= '\ud7a3' || c >= '\u3130' && c <= '\u318f' || c >= '\u1100' && c <= '\u11ff') return true;
            return false;
        }

        private static bool Covers(TMP_FontAsset font, TMP_Text label, string value)
        {
            if (font == null || font.material == null) return false;
            bool tag = false;
            foreach (char c in value)
            {
                if (label.richText && c == '<') { tag = true; continue; }
                if (tag) { if (c == '>') tag = false; continue; }
                if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                if (!font.HasCharacter(c)) return false;
            }
            return true;
        }

        private static void EnsureFont(TMP_Text label)
        {
            if (label == null) return;
            if (!LocaleMode.IsKoreanMode() || !Hangul(label.text))
            {
                RestoreFont(label);
                return;
            }
            if (!fontStates.TryGetValue(label, out var state))
            {
                state = new FontState();
                fontStates.Add(label, state);
                FontLabels.Add(new WeakReference<TMP_Text>(label));
            }
            string culture = LocaleMode.CurrentCulture();
            if (state.LastText == label.text && state.LastCulture == culture &&
                state.LastFont == label.font && state.LastMaterial == label.fontSharedMaterial &&
                Time.frameCount - state.LastFrame < FontRefreshFrames)
                return;

            if (Covers(label.font, label, label.text) && label.fontSharedMaterial != null &&
                label.font != null && label.font.material != null &&
                label.fontSharedMaterial.mainTexture == label.font.material.mainTexture)
            {
                RememberFontCheck(label, state, culture);
                return;
            }
            if (loadedFonts == null || Time.frameCount - searchedAt >= FontRefreshFrames)
            {
                loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                searchedAt = Time.frameCount;
            }
            TMP_FontAsset chosen = null;
            foreach (var font in loadedFonts)
                if (Covers(font, label, label.text)) { chosen = font; break; }
            if (chosen == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("tutorial-backport-font-unavailable", () =>
                    "Tutorial Backport text has Korean glyphs unavailable in its font; no complete loaded TMP font found.");
                RememberFontCheck(label, state, culture);
                return;
            }
            if (!state.Changed)
            {
                state.OriginalFont = label.font;
                state.OriginalMaterial = label.fontSharedMaterial;
                state.Changed = true;
            }
            label.font = chosen;
            label.fontSharedMaterial = chosen.material;
            state.AppliedFont = chosen;
            state.AppliedMaterial = chosen.material;
            label.SetVerticesDirty();
            RememberFontCheck(label, state, culture);
        }

        private static void RememberFontCheck(TMP_Text label, FontState state, string culture)
        {
            state.LastText = label.text;
            state.LastCulture = culture;
            state.LastFont = label.font;
            state.LastMaterial = label.fontSharedMaterial;
            state.LastFrame = Time.frameCount;
        }

        private static void RestoreFont(TMP_Text label)
        {
            if (label == null || !fontStates.TryGetValue(label, out var state) || !state.Changed) return;
            if (label.font == state.AppliedFont && label.fontSharedMaterial == state.AppliedMaterial)
            {
                if (state.OriginalFont != null) label.font = state.OriginalFont;
                if (state.OriginalMaterial != null) label.fontSharedMaterial = state.OriginalMaterial;
                label.SetVerticesDirty();
            }
            state.Changed = false;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                foreach (var item in IntroLabels)
                    if (item.Label.TryGetTarget(out var label) && label != null)
                    {
                        if (label.text == item.Applied) label.text = item.Source;
                        RestoreSkipSize(label, item);
                    }
                foreach (var weak in FontLabels)
                    if (weak.TryGetTarget(out var label) && label != null) RestoreFont(label);
                IntroLabels.Clear();
                FontLabels.Clear();
                fontStates = new ConditionalWeakTable<TMP_Text, FontState>();
                loadedFonts = null;
                owner?.UnpatchSelf();
                owner = null;
                profiles = null;
                introToggleField = null;
                introSubtitleField = null;
                tapeSubtitleField = null;
                Patched.Clear();
            }
        }

        private static void Warn(string key, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce(key, () => "Tutorial Backport display hook skipped: " + ex.Message);
        }
    }
}
