using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // PitFireTeam 1.1.0 re-creates the roster button from two different EFT templates.
    // Preserve the original button hierarchy, labels, interaction, and text. Only fix
    // missing Korean glyphs on labels owned by the confirmed SquadControlMenuUi factories.
    // The actual DefaultUIButton._headerLabel must be used; the first child TMP is not
    // necessarily the header. A copied font is NOT sufficient unless it has every glyph.
    internal static class PitFireTeamButtonFontHooks
    {
        private const string AssemblyName = "pitFireTeam";
        private const string UiTypeName = "pitTeam.Components.SquadControlMenuUi";
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const int FontRefreshFrames = 300;
        private static FieldInfo addButtonField;
        private static ConditionalWeakTable<TMP_Text, LabelState> states = new ConditionalWeakTable<TMP_Text, LabelState>();
        private static readonly List<WeakReference<TMP_Text>> labels = new List<WeakReference<TMP_Text>>();
        private static TMP_FontAsset[] knownFonts;
        private static TMP_FontAsset preferredFont;
        private static int lastFontSearchFrame = -FontRefreshFrames;
        private static bool subscribed;

        private sealed class LabelState
        {
            internal TMP_FontAsset OriginalFont;
            internal Material OriginalMaterial;
            internal TMP_FontAsset AppliedFont;
            internal Material AppliedMaterial;
            internal string AppliedText;
            internal bool Changed;
        }

        internal static int Enable(Harmony harmony, Assembly[] assemblies)
        {
            if (harmony == null || assemblies == null) return 0;
            Type ui = null;
            foreach (var assembly in assemblies)
            {
                if (assembly != null && string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.Ordinal))
                {
                    ui = assembly.GetType(UiTypeName, false);
                    break;
                }
            }
            if (ui == null) return 0;
            addButtonField = AccessTools.Field(ui, "addTeammateButton");
            var create = ui.GetMethod("CreateAddTeammateButton", InstanceFlags);
            var refresh = ui.GetMethod("RefreshLocalizedText", InstanceFlags);
            if (addButtonField == null || create == null || refresh == null ||
                create.GetParameters().Length != 2 || refresh.GetParameters().Length != 0)
                return 0;

            // Install the two essential hooks first. Missing optional factories must not
            // prevent the Add Teammate label from being corrected.
            harmony.Patch(create, postfix: new HarmonyMethod(typeof(PitFireTeamButtonFontHooks), nameof(AfterCreateAddButton)));
            harmony.Patch(refresh, postfix: new HarmonyMethod(typeof(PitFireTeamButtonFontHooks), nameof(AfterRefresh)));
            int installed = 2;
            installed += PatchOptional(harmony, ui, "CreateOverlayActionButton", 3, nameof(AfterCreateButton));
            installed += PatchOptional(harmony, ui, "ResolveOverlayBackButtonTemplate", 0, nameof(AfterCreateButton));
            // CreateText and CreateWindowCloseButton copy a font from the same EFT
            // player-button template. They can exhibit the same missing-glyph failure.
            installed += PatchOptional(harmony, ui, "CreateText", 4, nameof(AfterCreateText));
            installed += PatchOptional(harmony, ui, "CreateWindowCloseButton", 2, nameof(AfterCreateWindowCloseButton));
            if (!subscribed)
            {
                Canvas.preWillRenderCanvases += BeforeCanvasRender;
                subscribed = true;
            }
            return installed;
        }

        private static int PatchOptional(Harmony harmony, Type ui, string name, int parameters, string postfix)
        {
            try
            {
                var method = ui.GetMethod(name, InstanceFlags);
                if (method == null || method.GetParameters().Length != parameters) return 0;
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(PitFireTeamButtonFontHooks), postfix));
                return 1;
            }
            catch (Exception ex)
            {
                // An optional factory is independent of the required roster button.
                Warn("pit-squad-font-optional-" + name, ex);
                return 0;
            }
        }

        internal static void Disable()
        {
            if (subscribed)
            {
                Canvas.preWillRenderCanvases -= BeforeCanvasRender;
                subscribed = false;
            }
            RestoreAll();
            states = new ConditionalWeakTable<TMP_Text, LabelState>();
            labels.Clear();
            knownFonts = null;
            preferredFont = null;
            lastFontSearchFrame = -FontRefreshFrames;
            addButtonField = null;
        }

        private static TMP_Text ButtonHeader(object button)
        {
            if (button == null) return null;
            // EFT.UI.DefaultUIButton owns the actual label in a private _headerLabel
            // field. Do not select an arbitrary child TMP or touch sibling icons.
            for (Type type = button.GetType(); type != null; type = type.BaseType)
            {
                var header = type.GetField("_headerLabel", InstanceFlags);
                if (header != null) return header.GetValue(button) as TMP_Text;
            }
            SPT.EditableTranslations.MinimalLog.WarnOnce("pit-squad-header-missing", () =>
                "PitFireTeam DefaultUIButton._headerLabel is unavailable; no font substitute was applied.");
            return null;
        }

        private static void Track(TMP_Text label)
        {
            if (label == null || states.TryGetValue(label, out _)) return;
            states.Add(label, new LabelState());
            labels.Add(new WeakReference<TMP_Text>(label));
            if (labels.Count > 128) labels.RemoveAll(weak => !weak.TryGetTarget(out var target) || target == null);
        }

        private static void TrackButton(object button)
        {
            Track(ButtonHeader(button));
        }

        private static void AfterCreateAddButton(object __instance)
        {
            try { TrackButton(addButtonField?.GetValue(__instance)); }
            catch (Exception ex) { Warn("pit-squad-font-create", ex); }
        }

        private static void AfterRefresh(object __instance)
        {
            try { TrackButton(addButtonField?.GetValue(__instance)); }
            catch (Exception ex) { Warn("pit-squad-font-refresh", ex); }
        }

        private static void AfterCreateButton(object __result)
        {
            try { TrackButton(__result); }
            catch (Exception ex) { Warn("pit-squad-font-overlay", ex); }
        }

        private static void AfterCreateText(GameObject __result)
        {
            try { if (__result != null) Track(__result.GetComponent<TMP_Text>()); }
            catch (Exception ex) { Warn("pit-squad-font-text", ex); }
        }

        private static void AfterCreateWindowCloseButton(object __result)
        {
            try
            {
                var component = __result as Component;
                if (component == null) return;
                // This factory creates one TMP under its own button. It does not
                // use DefaultUIButton and therefore has no _headerLabel.
                Track(component.GetComponentInChildren<TMP_Text>(true));
            }
            catch (Exception ex) { Warn("pit-squad-font-window", ex); }
        }

        private static void BeforeCanvasRender()
        {
            try
            {
                bool korean = LocaleMode.IsKoreanMode();
                for (int i = labels.Count - 1; i >= 0; i--)
                {
                    if (!labels[i].TryGetTarget(out var label) || label == null)
                    {
                        labels.RemoveAt(i);
                        continue;
                    }
                    if (!states.TryGetValue(label, out var state)) continue;
                    if (!korean) Restore(label, state);
                    else if (label.isActiveAndEnabled) EnsureKoreanGlyphs(label, state);
                }
            }
            catch (Exception ex) { Warn("pit-squad-font-pre-render", ex); }
        }

        private static bool HasAllGlyphs(TMP_FontAsset font, TMP_Text label, string text)
        {
            if (font == null || font.material == null || string.IsNullOrEmpty(text)) return false;
            bool insideTag = false;
            foreach (char letter in text)
            {
                if (label.richText && letter == '<') { insideTag = true; continue; }
                if (insideTag) { if (letter == '>') insideTag = false; continue; }
                if (char.IsWhiteSpace(letter) || char.IsControl(letter)) continue;
                if (!font.HasCharacter(letter)) return false;
            }
            return true;
        }

        private static bool HasHangul(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (c >= '\uac00' && c <= '\ud7a3' || c >= '\u3130' && c <= '\u318f' || c >= '\u1100' && c <= '\u11ff')
                    return true;
            return false;
        }

        private static bool CompatibleMaterial(TMP_Text label)
        {
            var current = label.fontSharedMaterial;
            var primary = label.font == null ? null : label.font.material;
            return current != null && primary != null &&
                (primary.mainTexture == null || current.mainTexture == primary.mainTexture);
        }

        private static TMP_FontAsset FindCompleteFont(TMP_Text label, string text)
        {
            if (HasAllGlyphs(label.font, label, text)) return label.font;
            if (HasAllGlyphs(preferredFont, label, text)) return preferredFont;
            if (knownFonts != null)
                foreach (var font in knownFonts)
                    if (HasAllGlyphs(font, label, text)) return preferredFont = font;
            if (knownFonts == null || Time.frameCount - lastFontSearchFrame >= FontRefreshFrames)
            {
                knownFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                lastFontSearchFrame = Time.frameCount;
                foreach (var font in knownFonts)
                    if (HasAllGlyphs(font, label, text)) return preferredFont = font;
            }
            return null;
        }

        private static void EnsureKoreanGlyphs(TMP_Text label, LabelState state)
        {
            string text = label.text;
            if (!HasHangul(text))
            {
                Restore(label, state);
                return;
            }
            // A font copied from the main-menu template can be missing only SOME
            // syllables: a valid Korean-looking label does not prove full coverage.
            bool complete = HasAllGlyphs(label.font, label, text);
            if (complete && CompatibleMaterial(label)) return;
            TMP_FontAsset font = FindCompleteFont(label, text);
            if (font == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("pit-squad-korean-glyph-missing", () =>
                    "PitFireTeam squad label has missing Korean glyphs and no loaded TMP font covers the full label.");
                return;
            }
            Material material = font.material;
            if (material == null) return;
            if (!state.Changed)
            {
                state.OriginalFont = label.font;
                state.OriginalMaterial = label.fontSharedMaterial;
                state.Changed = true;
            }
            if (label.font != font) label.font = font;
            if (label.fontSharedMaterial != material) label.fontSharedMaterial = material;
            state.AppliedFont = font;
            state.AppliedMaterial = material;
            state.AppliedText = text;
            // Runs just before canvas generation, after SetRawText/RefreshLocalizedText.
            // Do not change the label's string, button click, layout or shared material.
            label.SetVerticesDirty();
        }

        private static void Restore(TMP_Text label, LabelState state)
        {
            if (!state.Changed) return;
            if (label.font == state.AppliedFont && label.fontSharedMaterial == state.AppliedMaterial)
            {
                if (state.OriginalFont != null) label.font = state.OriginalFont;
                if (state.OriginalMaterial != null) label.fontSharedMaterial = state.OriginalMaterial;
                label.SetVerticesDirty();
            }
            state.Changed = false;
            state.OriginalFont = null;
            state.OriginalMaterial = null;
            state.AppliedFont = null;
            state.AppliedMaterial = null;
            state.AppliedText = null;
        }

        private static void RestoreAll()
        {
            foreach (var weak in labels)
                if (weak.TryGetTarget(out var label) && label != null && states.TryGetValue(label, out var state))
                    Restore(label, state);
        }

        private static void Warn(string key, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce(key, () => "PitFireTeam scoped font repair skipped: " + ex.Message);
        }
    }
}
