using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Dr K's SPZombies 0.1.1 / R001: menu and map presentation only.
    // Never modify mode selection, map IDs, button names, callbacks or network state.
    internal static class KennerZombiesMenuHooks
    {
        private const string ProfileId = "com.kennerz.zmcore";
        private const string Channel = "small_ui";
        private const string AssemblyName = "Kennerz_ZM_Core";
        private const string PluginType = "KennerZombies.ZombiesPlugin";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private sealed class LabelState
        {
            internal string Source, Applied, SemanticSource;
            internal TMP_FontAsset OriginalFont, AppliedFont;
        }
        private static ConditionalWeakTable<TMP_Text, LabelState> states = new ConditionalWeakTable<TMP_Text, LabelState>();
        private static readonly List<WeakReference> labels = new List<WeakReference>();
        private static Harmony patcher;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static TMP_FontAsset[] fonts;
        private static int lastFontScan = -1000;
        private static FieldInfo menuRoot, normalRoot, modalRoot;
        private static PropertyInfo zombiesMode;
        private static MethodInfo getZombiesMap;

        internal static int Enable(Harmony harmony, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (harmony == null || data == null || !data.HasRules(ProfileId, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                patcher = harmony;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int count = 0;
            foreach (var a in assemblies ?? Enumerable.Empty<Assembly>()) count += PatchAssembly(a);
            return count;
        }

        internal static void Disable()
        {
            if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            enabled = false;
            foreach (var reference in labels)
            {
                var label = reference.Target as TMP_Text;
                if (label == null || !states.TryGetValue(label, out var state)) continue;
                if (string.Equals(label.text, state.Applied, StringComparison.Ordinal)) label.text = state.Source;
                RestoreFont(label, state);
            }
            labels.Clear();
            states = new ConditionalWeakTable<TMP_Text, LabelState>();
            Patched.Clear();
            profiles = null; patcher = null; fonts = null; lastFontScan = -1000;
            menuRoot = normalRoot = modalRoot = null; zombiesMode = null; getZombiesMap = null;
        }

        // Existing addon language-change dispatcher calls this; no per-frame scene-wide scan.
        internal static void RefreshLanguage()
        {
            if (!enabled) return;
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                var label = labels[i].Target as TMP_Text;
                if (label == null) { labels.RemoveAt(i); continue; }
                if (!states.TryGetValue(label, out var state)) continue;
                // A new owner may have rewritten this label. Do not overwrite new content.
                if (label.text == state.Source || label.text == state.Applied) Apply(label, state);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { PatchAssembly(args.LoadedAssembly); }
            catch (Exception ex) { Warn("late", ex); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly == null || assembly.GetName().Name != AssemblyName) return 0;
            var plugin = assembly.GetType(PluginType, false);
            var guid = plugin?.GetField("PluginGuid", BindingFlags.Static | BindingFlags.Public);
            var version = plugin?.GetField("PluginVersion", BindingFlags.Static | BindingFlags.Public);
            if (guid == null || version == null || !guid.IsLiteral || !version.IsLiteral ||
                (string)guid.GetRawConstantValue() != ProfileId ||
                (string)version.GetRawConstantValue() != "0.1.1") return 0;
            var frontend = assembly.GetType("KennerZombies.ZombiesFrontEnd", false);
            var mapCard = frontend?.GetNestedType("MapCard", BindingFlags.NonPublic);
            var maps = assembly.GetType("KennerZombies.ZombiesMapSelect", false);
            var mainMenu = assembly.GetType("KennerZombies.ZombiesMainMenu", false);
            var mapsRegistry = assembly.GetType("KennerZombies.ZombiesMaps", false);
            if (frontend == null || mapCard == null || maps == null || mainMenu == null) return 0;
            var buildMenu = AccessTools.Method(frontend, "BuildMenu", Type.EmptyTypes);
            var buildNormal = AccessTools.Method(frontend, "BuildNormal", Type.EmptyTypes);
            var mapWindow = AccessTools.Method(frontend, "OpenMapWindow", Type.EmptyTypes);
            var cardSet = AccessTools.Method(mapCard, "Set", new[] { typeof(string) });
            var caption = maps.GetMethod("SetCaption", BindingFlags.Static | BindingFlags.NonPublic);
            var info = AccessTools.Method(maps, "InfoPostfix");
            var registryGet = mapsRegistry == null ? null : AccessTools.Method(mapsRegistry, "Get", new[] { typeof(string) });
            var applyMenu = AccessTools.Method(mainMenu, "ApplyMenu");
            // Fail closed if a source upgrade changes any R001 entry point.
            if (buildMenu == null || buildNormal == null || mapWindow == null || cardSet == null ||
                caption == null || info == null || applyMenu == null || registryGet == null ||
                !buildMenu.IsPrivate || !buildNormal.IsPrivate || !mapWindow.IsPrivate || !cardSet.IsPublic ||
                buildMenu.ReturnType != typeof(void) || buildNormal.ReturnType != typeof(void) ||
                mapWindow.ReturnType != typeof(void) || cardSet.ReturnType != typeof(void) ||
                !caption.IsStatic || !info.IsStatic || !applyMenu.IsStatic ||
                caption.ReturnType != typeof(void) || info.ReturnType != typeof(void) ||
                applyMenu.ReturnType != typeof(void) ||
                !caption.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(TMP_Text) }) ||
                info.GetParameters().Length != 3 ||
                applyMenu.GetParameters().Length != 1 ||
                applyMenu.GetParameters()[0].ParameterType.FullName != "EFT.UI.MenuScreen") return 0;
            var menu = AccessTools.Field(frontend, "_menu");
            var normal = AccessTools.Field(frontend, "_normal");
            var modal = AccessTools.Field(frontend, "_modal");
            var mode = maps.GetProperty("Zombies", BindingFlags.Static | BindingFlags.Public);
            if (menu == null || normal == null || modal == null || mode == null || mode.PropertyType != typeof(bool)) return 0;
            lock (Gate)
            {
                if (!enabled || patcher == null || !Patched.Add(assembly)) return 0;
            }
            try
            {
                menuRoot = menu; normalRoot = normal; modalRoot = modal; zombiesMode = mode; getZombiesMap = registryGet;
                patcher.Patch(buildMenu, postfix: new HarmonyMethod(typeof(KennerZombiesMenuHooks), nameof(AfterMenu)));
                patcher.Patch(buildNormal, postfix: new HarmonyMethod(typeof(KennerZombiesMenuHooks), nameof(AfterNormal)));
                patcher.Patch(mapWindow, postfix: new HarmonyMethod(typeof(KennerZombiesMenuHooks), nameof(AfterMapWindow)));
                patcher.Patch(cardSet, postfix: new HarmonyMethod(typeof(KennerZombiesMenuHooks), nameof(AfterCard)));
                patcher.Patch(caption,
                    prefix: new HarmonyMethod(typeof(KennerZombiesMenuHooks), nameof(BeforeCaption)),
                    postfix: new HarmonyMethod(typeof(KennerZombiesMenuHooks), nameof(AfterCaption)));
                patcher.Patch(info, postfix: new HarmonyMethod(typeof(KennerZombiesMenuHooks), nameof(AfterInfo)));
                patcher.Patch(applyMenu, postfix: new HarmonyMethod(typeof(KennerZombiesMenuHooks), nameof(AfterPlay)));
                return 7;
            }
            catch (Exception ex)
            {
                Patched.Remove(assembly);
                Warn("install", ex);
                // The separate Harmony owner is unpatched by the caller if this family fails.
                throw;
            }
        }

        private static void AfterMenu(object __instance) { Try(() => CaptureTree(menuRoot?.GetValue(__instance) as Component), "main-menu"); }
        private static void AfterNormal(object __instance) { Try(() => CaptureTree(normalRoot?.GetValue(__instance) as Component), "normal-button"); }
        private static void AfterMapWindow(object __instance) { Try(() => CaptureTree(modalRoot?.GetValue(__instance) as Component), "map-window"); }
        private static void AfterCard(object __instance)
        {
            Try(() =>
            {
                foreach (var name in new[] { "_time", "_kind" })
                    Capture(AccessTools.Field(__instance.GetType(), name)?.GetValue(__instance) as TMP_Text);
            }, "map-card");
        }
        // Upstream SetCaption saves previous text if it is not exactly "ZOMBIES MODE".
        // Restore our own translated caption first, preserving its normal-mode cache.
        private static void BeforeCaption(TMP_Text __0)
        {
            Try(() =>
            {
                if (__0 != null && states.TryGetValue(__0, out var state) &&
                    state.Source == "ZOMBIES MODE" && __0.text == state.Applied)
                    __0.text = state.Source;
            }, "caption-before");
        }
        private static void AfterCaption(TMP_Text __0)
        {
            Try(() =>
            {
                if (ZombiesMode()) Capture(__0);
                // The same EFT caption is reused when returning to a normal raid.
                else if (__0 != null && states.TryGetValue(__0, out var state)) RestoreFont(__0, state);
            }, "caption-after");
        }
        private static void AfterInfo(object __0, object __1)
        {
            Try(() =>
            {
                // The source's own ZombiesMaps.Get(location.Id) is the map discriminator.
                // Never translate ordinary EFT location panels or other mod maps.
                if (__1 == null || getZombiesMap == null) return;
                var locationType = __1.GetType();
                var id = (locationType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(__1, null)
                          ?? locationType.GetField("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(__1)) as string;
                if (string.IsNullOrEmpty(id) || getZombiesMap.Invoke(null, new object[] { id }) == null) return;
                CaptureTree(__0 as Component);
            }, "map-info");
        }
        private static void AfterPlay(object __0)
        {
            Try(() =>
            {
                var menu = __0 as Component;
                if (menu == null) return;
                // Only the original EFT play button, never other menu widgets.
                var b = AccessTools.Field(menu.GetType(), "_playButton")?.GetValue(menu) as Component;
                if (b == null) return;
                foreach (var label in b.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (ZombiesMode()) Capture(label);
                    else if (states.TryGetValue(label, out var state)) RestoreFont(label, state);
                }
            }, "play-button");
        }
        private static bool ZombiesMode() { return zombiesMode != null && (bool)zombiesMode.GetValue(null, null); }

        internal static void CaptureTree(Component root)
        {
            if (root == null) return;
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) Capture(label);
        }
        internal static void Capture(TMP_Text label, bool allowPlayerSlotName = false, string semanticSource = null)
        {
            if (!enabled || profiles == null || label == null) return;
            // Never modify personal names or player-option headings. The only exception
            // is the explicitly identified empty slot and current-player suffix.
            if (label.name == "Heading" || label.name == "Name" && !allowPlayerSlotName) return;
            string text = label.text;
            if (label.name == "Name" && allowPlayerSlotName &&
                text != "Open slot" && !text.Contains("(YOU)") &&
                !states.TryGetValue(label, out _)) return;
            if (states.TryGetValue(label, out var known))
            {
                if (text != known.Source && text != known.Applied)
                {
                    // The upstream owner wrote a new status/counter into a reused label.
                    // Translate that *new* source instead of reapplying the old state.
                    RestoreFont(label, known);
                    known.Source = text;
                }
                // A single uppercase token may be a status *or* an action.
                // Source remains the actual upstream label for correct language reset.
                known.SemanticSource = semanticSource;
                Apply(label, known);
                return;
            }
            if (string.IsNullOrEmpty(text) || !profiles.CanTranslate(ProfileId, Channel, text)) return;
            var state = new LabelState { Source = text, Applied = text, SemanticSource = semanticSource };
            states.Add(label, state);
            // Social rows are rebuilt on server updates; keep weak references bounded.
            if (labels.Count >= 512)
                labels.RemoveAll(reference => reference.Target as TMP_Text == null);
            labels.Add(new WeakReference(label));
            Apply(label, state);
        }
        private static void Apply(TMP_Text label, LabelState state)
        {
            // Preserve the original uppercase UI label outside Korean mode. A semantic
            // override is used only for a source token that means different things
            // in separate widgets (the Ready toggle vs the member's Ready status).
            string lookup = LocaleMode.IsKoreanMode() && !string.IsNullOrEmpty(state.SemanticSource)
                ? state.SemanticSource : state.Source;
            string result = profiles.Translate(ProfileId, Channel, lookup);
            state.Applied = result ?? state.Source;
            if (label.text != state.Applied) label.text = state.Applied;
            if (LocaleMode.IsKoreanMode() && HasHangul(state.Applied)) ApplyFont(label, state);
            else RestoreFont(label, state);
        }
        private static bool HasHangul(string s) { return s != null && s.Any(c => c >= '\uac00' && c <= '\ud7a3'); }
        private static bool Supports(TMP_FontAsset font, string value)
        {
            if (font == null) return false;
            foreach (char ch in value)
                if (ch >= '\uac00' && ch <= '\ud7a3' && !font.HasCharacter(ch)) return false;
            return true;
        }
        private static void ApplyFont(TMP_Text label, LabelState state)
        {
            if (Supports(label.font, state.Applied)) return;
            if (fonts == null || Time.frameCount - lastFontScan > 300)
            {
                fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                lastFontScan = Time.frameCount;
            }
            foreach (var font in fonts ?? new TMP_FontAsset[0])
            {
                if (font == null || !Supports(font, state.Applied)) continue;
                if (state.OriginalFont == null) state.OriginalFont = label.font;
                label.font = font; state.AppliedFont = font;
                return;
            }
            SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r001-font",
                () => "SPZombies menu: no loaded TMP font supports the Korean label; text may render as missing glyphs.");
        }
        private static void RestoreFont(TMP_Text label, LabelState state)
        {
            if (state.AppliedFont != null && label.font == state.AppliedFont && state.OriginalFont != null)
                label.font = state.OriginalFont;
            state.AppliedFont = null; state.OriginalFont = null;
        }
        private static void Try(Action action, string key)
        {
            if (!enabled) return;
            try { action(); }
            catch (Exception ex) { Warn(key, ex); }
        }
        private static void Warn(string key, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r001-" + key,
                () => "SPZombies R001 menu translation skipped (" + key + "): " + ex.Message);
        }
    }
}
