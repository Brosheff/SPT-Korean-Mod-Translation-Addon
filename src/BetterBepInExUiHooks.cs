using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // BetterBepInEx 1.0.4's own IMGUI presentation. No patches to Unity GUI,
    // ConfigurationManager, third-party mod drawers or game input APIs.
    // These hooks never modify the settings model, config keys, selection values,
    // search queries, screenshot destinations or the source mod.
    internal static class BetterBepInExUiHooks
    {
        private const string Profile = "com.mybutthasarash.betterbepinex";
        private const string Channel = "small_ui";
        private const string SourceVersion = "1.0.4";
        private const string PluginType = "BetterBepInEx.BetterBepInExPlugin";
        private const string WindowType = "BetterBepInEx.MenuWindow";
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static readonly string[] NativeLabels = {
            "CloseLabel", "SearchHint", "ModFilterHint", "TitleLabel", "UndoLabel", "RedoLabel",
            "ClearLabel", "AdvancedLabel", "HiddenTag", "HiddenNote", "FavoriteLabel",
            "AddFavoriteLabel", "WebsiteLabel", "ModSearchHint", "ScreenshotLabel",
            "CaptureLabel", "NotBoundLabel", "UnbindLabel", "KeyTip",
            "SwatchOpenLabel", "SwatchCloseLabel"
        };
        private sealed class OriginalContent
        {
            internal string Text;
            internal string Tooltip;
        }
        private static readonly Dictionary<string, OriginalContent> NativeOriginals =
            new Dictionary<string, OriginalContent>(StringComparer.Ordinal);

        // These are source-audited direct UI literals in four exact draw methods.
        // Do not move this to a generic string-literal replacement engine.
        private static readonly Dictionary<string, HashSet<string>> DrawLiterals =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                { "DrawRow", new HashSet<string>(StringComparer.Ordinal) {
                    "Reset", "Back to ", "This setting failed to draw. The BepInEx log has the error."
                } },
                { "DrawTags", new HashSet<string>(StringComparer.Ordinal) {
                    "Same key as ", " more)", "Open the Keybinds page"
                } },
                { "AdvancedChipLabel", new HashSet<string>(StringComparer.Ordinal) {
                    "Settings this list leaves out. Click to turn on Show advanced."
                } },
                { "AddSnapRow", new HashSet<string>(StringComparer.Ordinal) { "Default: " } }
            };

        private static Harmony patcher;
        private static EditableProfiles data;
        private static Type window;
        private static Type plugin;
        private static bool enabled;
        private static string labelCulture;
        private static WeakReference statusWindow;
        private static string statusSource;
        private static string statusLastRendered;
        // BetterBepInEx's optional OS/IMGUI font selections may contain no Korean
        // glyphs. This fallback is only for its own menu skin and screenshot renderer.
        private static object koreanFont;
        private static bool koreanFontSearched;

        internal static int Enable(Harmony owner, IEnumerable<Assembly> assemblies, EditableProfiles translations)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (translations == null) throw new ArgumentNullException(nameof(translations));
            if (!translations.HasRules(Profile, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                patcher = owner;
                data = translations;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int count = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) count += TryPatch(assembly);
            return count;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                RestoreNativeLabels();
                enabled = false;
                patcher = null;
                data = null;
                plugin = null;
                window = null;
                labelCulture = null;
                NativeOriginals.Clear();
                statusWindow = null;
                statusSource = null;
                statusLastRendered = null;
                // The original menu skin may still reference the dynamically made
                // font when the addon unloads. Do not destroy a live GUI.skin asset.
                koreanFont = null;
                koreanFontSearched = false;
                Patched.Clear();
            }
        }

        internal static void RefreshLanguage()
        {
            // Called on Unity's main thread after the R001 model refresh. The next menu
            // draw updates static GUIContent once, not on every IMGUI event.
            labelCulture = null;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatch(args.LoadedAssembly); }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("betterbepinex-ui-late", () =>
                    "[BetterBepInEx UI] Late hook unavailable: " + ex.Message);
            }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (assembly == null) return 0;
            Type menu;
            Type pluginType;
            try
            {
                menu = assembly.GetType(WindowType, false);
                if (menu == null) return 0;
                pluginType = assembly.GetType(PluginType, false);
            }
            catch { return 0; }

            var guid = pluginType?.GetField("PluginGuid", AllStatic);
            var version = pluginType?.GetField("PluginVersion", AllStatic);
            if (guid == null || version == null || !guid.IsLiteral || !version.IsLiteral ||
                !string.Equals(guid.GetRawConstantValue() as string, Profile, StringComparison.Ordinal) ||
                !string.Equals(version.GetRawConstantValue() as string, SourceVersion, StringComparison.Ordinal))
                return 0;

            lock (Gate)
            {
                if (!enabled || patcher == null || !Patched.Add(assembly)) return 0;
            }
            try
            {
                // Match the exact 1.0.4 signatures, not every method in the assembly.
                Hook(menu, "OnGUI", 0, prefix: nameof(BeforeGui));
                Hook(menu, "Cached", 1, prefix: nameof(BeforeCached));
                Hook(menu, "CountedLabel", 4, prefix: nameof(BeforeCounted));
                Hook(menu, "SetStatus", 2, prefix: nameof(BeforeStatus));
                Hook(menu, "EmptyNote", 1, prefix: nameof(BeforeEmptyNote));
                Hook(menu, "SearchSummary", 0, postfix: nameof(AfterDisplayString));
                Hook(menu, "UndoTip", 0, postfix: nameof(AfterDisplayString));
                Hook(menu, "RedoTip", 0, postfix: nameof(AfterDisplayString));
                Hook(menu, "DefaultText", 1, postfix: nameof(AfterOptionValueText));
                Hook(menu, "ShortcutText", 1, postfix: nameof(AfterDisplayString));
                Hook(menu, "SnapshotValueText", 1, postfix: nameof(AfterOptionValueText));
                Hook(menu, "AddSnapText", 5, prefix: nameof(BeforeSnapshotText));
                Hook(menu, "AddGroup", 11, prefix: nameof(BeforeGroup));
                Hook(menu, "DrawRow", 6, transpiler: nameof(TranslateDrawLiterals));
                Hook(menu, "DrawTags", 1, transpiler: nameof(TranslateDrawLiterals));
                Hook(menu, "AdvancedChipLabel", 0, transpiler: nameof(TranslateDrawLiterals));
                Hook(menu, "AddSnapRow", 5, transpiler: nameof(TranslateDrawLiterals));
                Hook(pluginType, "MenuFontAsset", 0, transpiler: nameof(TranslateMenuFontResult));
                plugin = pluginType;
                window = menu;
                labelCulture = null;
                return 18;
            }
            catch
            {
                lock (Gate) Patched.Remove(assembly);
                throw;
            }
        }

        private static void Hook(Type type, string name, int paramCount,
            string prefix = null, string postfix = null, string transpiler = null)
        {
            var target = type.GetMethods(AllInstance | AllStatic)
                .SingleOrDefault(m => m.Name == name && m.GetParameters().Length == paramCount);
            if (target == null) throw new MissingMethodException(type.FullName, name + "/" + paramCount);
            patcher.Patch(target,
                prefix: PatchMethod(prefix), postfix: PatchMethod(postfix),
                transpiler: PatchMethod(transpiler));
        }

        private static HarmonyMethod PatchMethod(string name)
        {
            if (name == null) return null;
            return new HarmonyMethod(typeof(BetterBepInExUiHooks).GetMethod(name, AllStatic));
        }

        private static void BeforeGui(object __instance)
        {
            if (!enabled || window == null || data == null) return;
            var culture = LocaleMode.CurrentCulture();
            if (string.Equals(labelCulture, culture, StringComparison.Ordinal)) return;
            try
            {
                ApplyNativeLabels(culture);
                // CountedLabel is cached by slot/count rather than prefix; without this
                // clear it would keep text from the previous language. These are only
                // presentation caches. Keep page selection, input and model untouched.
                ClearDictionary(__instance, "_countLabels");
                ClearDictionary(__instance, "_conflictLabels");
                SetField(__instance, "_searchDirty", true);
                // Per-mod filtered lists cache their row collection under _listStamp;
                // a search query can be unchanged even though Korean aliases changed.
                var stamp = __instance.GetType().GetField("_listStamp", AllInstance);
                if (stamp != null && stamp.FieldType == typeof(int))
                    stamp.SetValue(__instance, unchecked((int)stamp.GetValue(__instance) + 1));
                // Preserve eight-second feedback and its error flag/timestamp. The
                // previous implementation erased the status on each language switch.
                if (ReferenceEquals(statusWindow?.Target, __instance) && statusSource != null &&
                    string.Equals(__instance.GetType().GetField("_status", AllInstance)?.GetValue(__instance) as string,
                        statusLastRendered, StringComparison.Ordinal))
                {
                    statusLastRendered = Translate(statusSource, culture);
                    SetField(__instance, "_status", statusLastRendered);
                }
                labelCulture = culture;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("betterbepinex-ui-labels", () =>
                    "[BetterBepInEx UI] Static label refresh skipped: " + ex.Message);
            }
        }

        private static void ApplyNativeLabels(string culture)
        {
            foreach (var name in NativeLabels)
            {
                var field = window.GetField(name, AllStatic);
                if (field == null || field.FieldType != typeof(GUIContent)) continue;
                var content = field.GetValue(null) as GUIContent;
                if (content == null) continue;
                if (!NativeOriginals.TryGetValue(name, out var original))
                {
                    original = new OriginalContent { Text = content.text, Tooltip = content.tooltip };
                    NativeOriginals.Add(name, original);
                }
                content.text = Translate(original.Text, culture);
                // Undo/redo tooltips are updated at draw time by the source mod.
                if (name != "UndoLabel" && name != "RedoLabel")
                    content.tooltip = Translate(original.Tooltip, culture);
            }
        }

        private static void RestoreNativeLabels()
        {
            if (window == null) return;
            foreach (var saved in NativeOriginals)
            {
                var field = window.GetField(saved.Key, AllStatic);
                var content = field?.GetValue(null) as GUIContent;
                if (content == null) continue;
                content.text = saved.Value.Text;
                if (saved.Key != "UndoLabel" && saved.Key != "RedoLabel")
                    content.tooltip = saved.Value.Tooltip;
            }
        }

        private static void ClearDictionary(object instance, string name)
        {
            (instance?.GetType().GetField(name, AllInstance)?.GetValue(instance) as IDictionary)?.Clear();
        }

        private static void SetField(object instance, string name, object value)
        {
            instance?.GetType().GetField(name, AllInstance)?.SetValue(instance, value);
        }

        private static void BeforeCached(ref string text) { text = Translate(text); }
        private static void BeforeCounted(ref string prefix, ref string suffix)
        {
            prefix = Translate(prefix);
            suffix = Translate(suffix);
        }
        private static void BeforeStatus(object __instance, ref string text)
        {
            statusWindow = new WeakReference(__instance);
            statusSource = text;
            text = Translate(text);
            statusLastRendered = text;
        }
        private static void BeforeEmptyNote(ref string text) { text = Translate(text); }
        private static void AfterDisplayString(ref string __result) { __result = Translate(__result); }
        private static void AfterOptionValueText(object row, ref string __result)
        {
            __result = BetterBepInExF12Hooks.TranslateRenderedValue(row, __result);
            __result = Translate(__result);
        }

        private static void BeforeSnapshotText(ref GUIContent content)
        {
            content = CopyDisplayText(content);
        }

        private static void BeforeGroup(object key, ref GUIContent header)
        {
            // The source has two own display-only headers: its aggregated keybind
            // conflict group, and the special hidden section. Ordinary section
            // headings belong to third-party mods and are handled by the R001 model.
            var marker = window?.GetField("ConflictHeadingKey", AllStatic)?.GetValue(null);
            bool ownConflict = ReferenceEquals(key, marker);
            bool ownHidden = key != null && key.GetType().FullName == "BetterBepInEx.Section" &&
                Equals(key.GetType().GetField("IsHiddenGroup", AllInstance)?.GetValue(key), true) &&
                string.Equals(header?.text, "HIDDEN BY THE MOD'S AUTHOR", StringComparison.Ordinal);
            if (ownConflict || ownHidden) header = CopyDisplayText(header);
        }

        private static GUIContent CopyDisplayText(GUIContent content)
        {
            if (content == null) return null;
            var translatedText = Translate(content.text);
            var translatedTip = Translate(content.tooltip);
            if (translatedText == content.text && translatedTip == content.tooltip) return content;
            return new GUIContent(content) { text = translatedText, tooltip = translatedTip };
        }

        // This exact method returns UnityEngine.Font, whose assembly is present in
        // the game but not in the standalone compilation references. At each ret,
        // leave the original Font on the stack, pass it through an object-only
        // translation helper, and cast the result back to the original return type.
        private static IEnumerable<CodeInstruction> TranslateMenuFontResult(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var method = __originalMethod as MethodInfo;
            if (method?.ReturnType.FullName != "UnityEngine.Font")
                throw new InvalidOperationException("BetterBepInEx MenuFontAsset return type changed");
            var helper = AccessTools.Method(typeof(BetterBepInExUiHooks), nameof(ReplaceMenuFont));
            var result = new List<CodeInstruction>();
            int returns = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    returns++;
                    var call = new CodeInstruction(OpCodes.Call, helper);
                    // Branches that target a return must pass through the new
                    // instructions too, so move the original labels to the call.
                    call.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    result.Add(call);
                    result.Add(new CodeInstruction(OpCodes.Castclass, method.ReturnType));
                }
                result.Add(instruction);
            }
            if (returns == 0) throw new InvalidOperationException("MenuFontAsset has no return");
            return result;
        }

        private static object ReplaceMenuFont(object original)
        {
            if (!enabled || !LocaleMode.IsKoreanMode()) return original;
            try
            {
                if (ImguiFontCompat.HasKoreanGlyphs(original)) return original;
                return GetKoreanFont() ?? original;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("betterbepinex-korean-font", () =>
                    "[BetterBepInEx UI] Korean IMGUI font unavailable; original font kept: " + ex.Message);
                return original;
            }
        }

        private static object GetKoreanFont()
        {
            if (koreanFont != null) return koreanFont;
            if (koreanFontSearched) return null;
            koreanFontSearched = true;
            // Preserve the original menu's installed-font preference; never
            // replace the game's global GUI skin or ship font files.
            var installed = ImguiFontCompat.InstalledFontNames();
            foreach (var family in new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic" })
            {
                if (!installed.Any(n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase))) continue;
                var font = ImguiFontCompat.CreateOSFont(family, 16, true);
                if (font != null) return koreanFont = font;
            }
            return null;
        }

        private static string Translate(string original)
        {
            return Translate(original, LocaleMode.CurrentCulture());
        }

        private static string Translate(string original, string culture)
        {
            if (string.IsNullOrEmpty(original) || data == null || !LocaleMode.IsKoreanCulture(culture)) return original;
            return data.Translate(Profile, Channel, original, culture);
        }

        private static IEnumerable<CodeInstruction> TranslateDrawLiterals(IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            var translate = typeof(BetterBepInExUiHooks).GetMethod(nameof(Translate),
                AllStatic, null, new[] { typeof(string) }, null);
            if (!DrawLiterals.TryGetValue(__originalMethod.Name, out var candidates))
                return instructions;
            var rewritten = new List<CodeInstruction>();
            foreach (var instruction in instructions)
            {
                rewritten.Add(instruction);
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string source && candidates.Contains(source))
                    rewritten.Add(new CodeInstruction(OpCodes.Call, translate));
            }
            return rewritten;
        }
    }
}
