using System;
using System.Collections;
using System.IO;
using BepInEx.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // SPZombies 0.1.1 R005. Only the final GUI drawing commands of the two
    // settings pages are translated. ConfigEntry, button results, and key capture
    // are never modified. No hooks are installed on global Unity GUI setters.
    internal static class KennerZombiesSettingsHooks
    {
        private const string ModId = "com.kennerz.zmcore";
        private const string AssemblyId = "Kennerz_ZM_Core";
        private const string Channel = "small_ui";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static Harmony owner;
        private static EditableProfiles profiles;
        private static ConfigManagerProfiles perkF12;
        private const string PerkGuid = "com.kennerz.perkbeverages";
        private const string PerkSection = "1. Volume";
        private static FieldInfo groupTitleField, groupRowsField, rowEntryField, rowLabelField, rowHelpField;
        private static bool enabled;
        private static UnityEngine.Object[] fonts;
        private static int scannedAt = -1000;
        private static readonly Dictionary<GUIStyle, GUIStyle> KoreanStyles = new Dictionary<GUIStyle, GUIStyle>();
        private static FieldInfo noteLines;
        private static bool noteLayoutPatched;

        internal static int Enable(Harmony harmony, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (harmony == null || data == null || !data.HasRules(ModId, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                owner = harmony;
                profiles = data;
                // Use the existing F12 profile as the single source for Perk display names/descriptions.
                // It is read once, never on each GUI repaint.
                perkF12 = new ConfigManagerProfiles(Path.GetDirectoryName(typeof(KennerZombiesSettingsHooks).Assembly.Location));
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int count = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) count += PatchAssembly(assembly);
            return count;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                Patched.Clear();
                owner = null;
                profiles = null;
                perkF12 = null;
                groupTitleField = groupRowsField = rowEntryField = rowLabelField = rowHelpField = null;
                fonts = null;
                KoreanStyles.Clear();
                scannedAt = -1000;
                noteLines = null;
                noteLayoutPatched = false;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e)
        {
            try { PatchAssembly(e.LoadedAssembly); }
            catch (Exception ex) { Warn("late-load", ex); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly?.GetName().Name != AssemblyId) return 0;
            var plugin = assembly.GetType("KennerZombies.ZombiesPlugin", false);
            var guid = plugin?.GetField("PluginGuid", BindingFlags.Static | BindingFlags.Public);
            var ver = plugin?.GetField("PluginVersion", BindingFlags.Static | BindingFlags.Public);
            if (guid == null || ver == null || !guid.IsLiteral || !ver.IsLiteral ||
                (string)guid.GetRawConstantValue() != ModId || (string)ver.GetRawConstantValue() != "0.1.1") return 0;
            var report = assembly.GetType("KennerZombies.ZombiesReport", false);
            var controls = assembly.GetType("KennerZombies.ZombiesControls", false);
            var sounds = assembly.GetType("KennerZombies.ZombiesSoundPage", false);
            if (controls == null || sounds == null || report == null) return 0;
            var page = AccessTools.Method(controls, "DrawPage", Type.EmptyTypes);
            var panel = AccessTools.Method(controls, "DrawPanel", new[] { typeof(Vector2), typeof(float) });
            var sound = AccessTools.Method(sounds, "Draw", new[] { typeof(Rect), typeof(float) });
            var soundGroups = AccessTools.Method(sounds, "Groups", Type.EmptyTypes);
            var groupType = sounds.GetNestedType("Group", BindingFlags.NonPublic);
            var rowType = sounds.GetNestedType("Row", BindingFlags.NonPublic);
            var titleField = groupType == null ? null : AccessTools.Field(groupType, "Title");
            var rowsField = groupType == null ? null : AccessTools.Field(groupType, "Rows");
            var entryField = rowType == null ? null : AccessTools.Field(rowType, "Entry");
            var labelField = rowType == null ? null : AccessTools.Field(rowType, "Label");
            var helpField = rowType == null ? null : AccessTools.Field(rowType, "Help");
            var drawNote = AccessTools.Method(report, "DrawNote", Type.EmptyTypes);
            var noteShadow = AccessTools.Method(report, "Shadow", new[] { typeof(Rect), typeof(string), typeof(GUIStyle), typeof(Color) });
            var noteField = AccessTools.Field(report, "NoteLines");
            if (page == null || panel == null || sound == null || drawNote == null || noteShadow == null ||
                soundGroups == null || !soundGroups.IsStatic || !soundGroups.IsPrivate ||
                !typeof(IEnumerable).IsAssignableFrom(soundGroups.ReturnType) ||
                titleField?.FieldType != typeof(string) || rowsField == null ||
                entryField?.FieldType != typeof(ConfigEntry<float>) ||
                labelField?.FieldType != typeof(string) || helpField?.FieldType != typeof(string) ||
                labelField.IsInitOnly || helpField.IsInitOnly ||
                noteField == null || noteField.FieldType != typeof(string[]) || !noteField.IsStatic ||
                !noteField.IsInitOnly ||
                !page.IsStatic || !panel.IsStatic || !sound.IsStatic ||
                !page.IsPublic || !panel.IsPrivate || !sound.IsPublic ||
                !drawNote.IsPrivate || drawNote.IsStatic || !noteShadow.IsPrivate || !noteShadow.IsStatic ||
                page.ReturnType != typeof(void) || panel.ReturnType != typeof(void) || sound.ReturnType != typeof(void) ||
                drawNote.ReturnType != typeof(void) || noteShadow.ReturnType != typeof(void)) return 0;
            lock (Gate)
            {
                if (!enabled || owner == null || !Patched.Add(assembly)) return 0;
            }
            try
            {
                owner.Patch(page, transpiler: new HarmonyMethod(typeof(KennerZombiesSettingsHooks), nameof(PageDisplay)));
                owner.Patch(panel, transpiler: new HarmonyMethod(typeof(KennerZombiesSettingsHooks), nameof(PanelDisplay)));
                owner.Patch(sound, transpiler: new HarmonyMethod(typeof(KennerZombiesSettingsHooks), nameof(SoundDisplay)));
                groupTitleField = titleField;
                groupRowsField = rowsField;
                rowEntryField = entryField;
                rowLabelField = labelField;
                rowHelpField = helpField;
                owner.Patch(soundGroups, postfix: new HarmonyMethod(typeof(KennerZombiesSettingsHooks), nameof(AfterSoundGroups)));
                noteLines = noteField;
                owner.Patch(drawNote, transpiler: new HarmonyMethod(typeof(KennerZombiesSettingsHooks), nameof(NoteDisplay)));
                owner.Patch(noteShadow, prefix: new HarmonyMethod(typeof(KennerZombiesSettingsHooks), nameof(BeforeNoteShadow)));
                return 6;
            }
            catch (Exception ex)
            {
                lock (Gate) Patched.Remove(assembly);
                Warn("install", ex);
                throw;
            }
        }

        private static string T(string original)
        {
            if (!enabled || profiles == null || string.IsNullOrEmpty(original)) return original;
            try { return profiles.Translate(ModId, Channel, original) ?? original; }
            catch (Exception ex) { Warn("translate", ex); return original; }
        }

        private static bool HasHangul(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if ((c >= '\uAC00' && c <= '\uD7A3') || (c >= '\u1100' && c <= '\u11FF') ||
                    (c >= '\u3130' && c <= '\u318F')) return true;
            return false;
        }

        // UnityEngine.Font is forwarded to TextRenderingModule, which is not part of
        // the addon's standalone reference set. Resolve only the optional IMGUI font
        // API at runtime, without introducing a new required build reference.
        private static bool HasGlyphs(object font, string value)
        {
            if (font == null || !HasHangul(value)) return false;
            try
            {
                var hasCharacter = font.GetType().GetMethod("HasCharacter",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(char) }, null);
                if (hasCharacter == null || hasCharacter.ReturnType != typeof(bool)) return false;
                foreach (char c in value)
                {
                    if (!((c >= '\uAC00' && c <= '\uD7A3') || (c >= '\u1100' && c <= '\u11FF') ||
                          (c >= '\u3130' && c <= '\u318F'))) continue;
                    if (!Equals(hasCharacter.Invoke(font, new object[] { c }), true)) return false;
                }
                return true;
            }
            catch { return false; } // a loaded Unity font may have been destroyed
        }

        // Only a local copy of the settings page style is touched; the original
        // shared GUIStyle and game-wide GUI.skin stay unchanged.
        internal static GUIStyle StyleFor(GUIStyle original, string text)
        {
            if (original == null || !HasHangul(text)) return original;
            try
            {
                if (HasGlyphs(ImguiFontCompat.GetStyleFont(original), text)) return original;
                if (fonts == null || Time.frameCount - scannedAt > 300)
                {
                    // Use the actual runtime property type instead of referencing Font
                    // in the client's compile-time source or project file.
                    var fontType = typeof(GUIStyle).GetProperty("font",
                        BindingFlags.Public | BindingFlags.Instance)?.PropertyType;
                    fonts = fontType == null ? new UnityEngine.Object[0] :
                        Resources.FindObjectsOfTypeAll(fontType);
                    scannedAt = Time.frameCount;
                    KoreanStyles.Clear();
                }
                var replacement = (fonts ?? new UnityEngine.Object[0])
                    .FirstOrDefault(f => HasGlyphs(f, text));
                if (replacement == null)
                {
                    SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r005-imgui-font",
                        () => "SPZombies settings: no loaded Unity IMGUI font with Korean glyphs; missing glyphs may remain.");
                    return original;
                }
                if (!KoreanStyles.TryGetValue(original, out var copy) || copy == null ||
                    !Equals(ImguiFontCompat.GetStyleFont(copy), replacement))
                {
                    copy = new GUIStyle(original);
                    ImguiFontCompat.SetStyleFont(copy, replacement);
                    if (!Equals(ImguiFontCompat.GetStyleFont(copy), replacement)) return original;
                    KoreanStyles[original] = copy;
                }
                return copy;
            }
            catch { return original; } // font lookup must never hide the source page
        }

        private static void DrawLabel(Rect rect, string source, GUIStyle style)
        {
            string text = T(source);
            GUI.Label(rect, text, StyleFor(style, text));
        }
        private static void DrawContentLabel(Rect rect, GUIContent source, GUIStyle style)
        {
            if (source == null) { GUI.Label(rect, source, style); return; }
            string text = T(source.text);
            string tooltip = T(source.tooltip);
            var translated = new GUIContent(source) { text = text, tooltip = tooltip };
            GUI.Label(rect, translated, StyleFor(style, text));
        }
        private static bool DrawButton(Rect rect, string source, GUIStyle style)
        {
            string text = T(source);
            return GUI.Button(rect, text, StyleFor(style, text));
        }

        private static readonly MethodInfo LabelMethod = AccessTools.Method(typeof(GUI), "Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
        private static readonly MethodInfo ContentLabelMethod = AccessTools.Method(typeof(GUI), "Label", new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) });
        private static readonly MethodInfo ButtonMethod = AccessTools.Method(typeof(GUI), "Button", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
        private static readonly MethodInfo WrappedLabel = AccessTools.Method(typeof(KennerZombiesSettingsHooks), nameof(DrawLabel));
        private static readonly MethodInfo WrappedContentLabel = AccessTools.Method(typeof(KennerZombiesSettingsHooks), nameof(DrawContentLabel));
        private static readonly MethodInfo WrappedButton = AccessTools.Method(typeof(KennerZombiesSettingsHooks), nameof(DrawButton));

        // The source sound page caches rows and redraws them every frame. Read the ORIGINAL
        // ConfigEntry each time (not the possibly translated cached Row.Label/Help), so
        // language changes and page reopening recover the English labels precisely.
        // Group.Title comes only from the audited Known mapping; unrelated mod groups are untouched.
        private static void AfterSoundGroups(object __result)
        {
            if (!enabled || perkF12 == null || !(__result is IEnumerable groups) ||
                groupTitleField == null || groupRowsField == null || rowEntryField == null ||
                rowLabelField == null || rowHelpField == null) return;
            try
            {
                foreach (var group in groups)
                {
                    if (group == null || !string.Equals(groupTitleField.GetValue(group) as string,
                        "PERK MACHINES", StringComparison.Ordinal)) continue;
                    if (!(groupRowsField.GetValue(group) is IEnumerable rows)) continue;
                    foreach (var row in rows)
                    {
                        if (row == null || !(rowEntryField.GetValue(row) is ConfigEntry<float> entry) ||
                            !string.Equals(entry.Definition.Section, PerkSection, StringComparison.Ordinal)) continue;
                        string key = entry.Definition.Key;
                        string description = entry.Description?.Description ?? string.Empty;
                        var translated = perkF12.Resolve(new ConfigUiSource
                        {
                            plugin_guid = PerkGuid,
                            section = PerkSection,
                            key = key,
                            source_display_name = key,
                            source_description = description
                        });
                        string label = translated?.display_name ?? key;
                        string help = translated?.description ?? description;
                        if (!string.Equals(rowLabelField.GetValue(row) as string, label, StringComparison.Ordinal))
                            rowLabelField.SetValue(row, label);
                        if (!string.Equals(rowHelpField.GetValue(row) as string, help, StringComparison.Ordinal))
                            rowHelpField.SetValue(row, help);
                    }
                }
            }
            catch (Exception ex) { Warn("perk-sound-rows", ex); }
        }

        private static IEnumerable<CodeInstruction> PageDisplay(IEnumerable<CodeInstruction> instructions) =>
            Replace(instructions, 1, 0, 2, "settings-page");
        private static IEnumerable<CodeInstruction> PanelDisplay(IEnumerable<CodeInstruction> instructions) =>
            Replace(instructions, 6, 0, 5, "settings-keybinds");
        private static IEnumerable<CodeInstruction> SoundDisplay(IEnumerable<CodeInstruction> instructions) =>
            Replace(instructions, 4, 1, 0, "settings-sound");

        // The same three note lines drive both GUI layout height and final drawing.
        // Translate both references to NoteLines; never mutate the original static array.
        private static string[] ReadNoteLines()
        {
            var original = noteLines?.GetValue(null) as string[];
            if (original == null) return new string[0];
            var localized = new string[original.Length];
            for (int i = 0; i < original.Length; i++) localized[i] = T(original[i]);
            return localized;
        }

        private static void BeforeNoteShadow(ref string __1, ref GUIStyle __2)
        {
            if (!noteLayoutPatched) return; // preserve original height when IL shape is not understood
            __1 = T(__1);
            __2 = StyleFor(__2, __1);
        }

        private static IEnumerable<CodeInstruction> NoteDisplay(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var sourceField = noteLines;
            var getter = AccessTools.Method(typeof(KennerZombiesSettingsHooks), nameof(ReadNoteLines));
            int arrayLoads = sourceField == null ? -1 : codes.Count(x => x.opcode == OpCodes.Ldsfld && Equals(x.operand, sourceField));
            int l = LabelMethod == null ? -1 : codes.Count(x => x.Calls(LabelMethod));
            int b = ButtonMethod == null ? -1 : codes.Count(x => x.Calls(ButtonMethod));
            if (arrayLoads != 2 || l != 1 || b != 1 || getter == null || WrappedLabel == null || WrappedButton == null)
            {
                Warn("menu-note", new InvalidOperationException("unexpected note IL: " + arrayLoads + "/" + l + "/" + b));
                return codes;
            }
            noteLayoutPatched = true;
            foreach (var code in codes)
            {
                if (code.opcode == OpCodes.Ldsfld && Equals(code.operand, sourceField)) { code.opcode = OpCodes.Call; code.operand = getter; }
                else if (code.Calls(LabelMethod)) { code.opcode = OpCodes.Call; code.operand = WrappedLabel; }
                else if (code.Calls(ButtonMethod)) { code.opcode = OpCodes.Call; code.operand = WrappedButton; }
            }
            return codes;
        }

        private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions,
            int stringLabels, int contentLabels, int buttons, string location)
        {
            var codes = instructions.ToList();
            int l = LabelMethod == null ? -1 : codes.Count(x => x.Calls(LabelMethod));
            int c = ContentLabelMethod == null ? -1 : codes.Count(x => x.Calls(ContentLabelMethod));
            int b = ButtonMethod == null ? -1 : codes.Count(x => x.Calls(ButtonMethod));
            if (l != stringLabels || c != contentLabels || b != buttons ||
                WrappedLabel == null || WrappedContentLabel == null || WrappedButton == null)
            {
                Warn(location, new InvalidOperationException("unexpected GUI calls: " + l + "/" + c + "/" + b));
                return codes;
            }
            foreach (var code in codes)
            {
                if (code.Calls(LabelMethod)) { code.opcode = OpCodes.Call; code.operand = WrappedLabel; }
                else if (code.Calls(ContentLabelMethod)) { code.opcode = OpCodes.Call; code.operand = WrappedContentLabel; }
                else if (code.Calls(ButtonMethod)) { code.opcode = OpCodes.Call; code.operand = WrappedButton; }
            }
            return codes;
        }

        private static void Warn(string path, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r005-" + path,
                () => "SPZombies R005 settings translation skipped (" + path + "): " + ex.Message);
        }
    }
}
