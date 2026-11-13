using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Hideout Last Stand R002 / 0.1.0-0.1.3: translate only the mod's final IMGUI
    // labels. Original menu button hitboxes, siege state, Fika packets,
    // timers and keybind values remain unchanged.
    internal static class HideoutLastStandUiHooks
    {
        private const string ProfileId = "jrs.hideoutsiege";
        private const string Channel = "small_ui";
        private const string PluginType = "HideoutSiege.Plugin";
        private const string TextType = "HideoutSiege.SiegeUi";
        private const string HudType = "HideoutSiege.SiegeHud";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static readonly Dictionary<GUIStyle, FontState> StyleFonts = new Dictionary<GUIStyle, FontState>();
        private sealed class FontState
        {
            internal object Original, Applied;
        }

        private static Harmony patcher;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static object koreanFont;
        private static bool koreanFontChecked;

        internal static int Enable(Harmony owner, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (data == null || !data.HasRules(ProfileId, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                patcher = owner;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int count = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>())
                count += TryPatch(assembly);
            return count;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                foreach (var pair in StyleFonts)
                {
                    try
                    {
                        if (pair.Key != null && Equals(ImguiFontCompat.GetStyleFont(pair.Key), pair.Value.Applied))
                            ImguiFontCompat.SetStyleFont(pair.Key, pair.Value.Original);
                    }
                    catch (Exception) { /* The source UI may already have been destroyed. */ }
                }
                StyleFonts.Clear();
                Patched.Clear();
                patcher = null;
                profiles = null;
                koreanFont = null;
                koreanFontChecked = false;
            }
        }

        // All translated labels are rendered from the original English each frame.
        // No state rewrite or cached translated message needs a language-change pass.
        internal static void RefreshLanguage() { }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatch(args.LoadedAssembly); }
            catch (Exception e)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("hls-r002-late", () =>
                    "Hideout Last Stand UI hook unavailable: " + e.Message);
            }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (assembly == null) return 0;
            var plugin = assembly.GetType(PluginType, false);
            if (plugin == null) return 0;
            var id = plugin.GetField("Id", BindingFlags.Public | BindingFlags.Static);
            var version = plugin.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            if (id == null || version == null || !id.IsLiteral || !version.IsLiteral ||
                !string.Equals(id.GetRawConstantValue() as string, ProfileId, StringComparison.Ordinal) ||
                !HideoutLastStandVersionGuard.IsSupported(version.GetRawConstantValue() as string))
                return 0;

            // A previously unsupported but correctly identified upstream version must
            // not accidentally patch arbitrary new UI methods. Keep the exact
            // signature checks below for every reviewed release.
            var textType = assembly.GetType(TextType, false);
            var hudType = assembly.GetType(HudType, false);
            if (textType == null || hudType == null) return 0;
            var text = AccessTools.Method(textType, "Text", new[] { typeof(Rect), typeof(string), typeof(GUIStyle), typeof(float), typeof(Color) });
            var readout = AccessTools.Method(hudType, "Readout", new[] { typeof(float), typeof(float), typeof(string), typeof(string), typeof(string), typeof(Color) });
            if (text == null || readout == null || !text.IsStatic || !readout.IsStatic) return 0;

            lock (Gate)
            {
                if (!enabled || patcher == null || !Patched.Add(assembly)) return 0;
            }
            try
            {
                patcher.Patch(text, prefix: new HarmonyMethod(typeof(HideoutLastStandUiHooks), nameof(BeforeText)));
                patcher.Patch(readout, prefix: new HarmonyMethod(typeof(HideoutLastStandUiHooks), nameof(BeforeReadout)));
            }
            catch
            {
                lock (Gate) Patched.Remove(assembly);
                throw;
            }

            // The original banner's red underline measures a GUIContent object.
            // Translate only that single measured string (not SiegeState.Banner),
            // keeping its width synchronized with the translated label.
            var draw = AccessTools.Method(hudType, "Draw", Type.EmptyTypes);
            if (draw != null)
            {
                try
                {
                    patcher.Patch(draw, transpiler: new HarmonyMethod(typeof(HideoutLastStandUiHooks), nameof(MeasureBannerTranspiler)));
                }
                catch (Exception e)
                {
                    SPT.EditableTranslations.MinimalLog.WarnOnce("hls-r002-banner-width", () =>
                        "Hideout Last Stand banner width enhancement unavailable; original layout retained: " + e.Message);
                }
            }
            return 2;
        }

        private static void BeforeText(ref string __1, GUIStyle __2)
        {
            if (!enabled || profiles == null || __1 == null) return;
            try
            {
                __1 = ToDisplay(__1);
                SetKoreanFontIfNeeded(__2, __1);
            }
            catch (Exception)
            {
                // A translation failure must not stop the source game's GUI method.
            }
        }

        // The same literal BREACH is used for an active breach banner and a
        // countdown until the front-door charge. They have distinct meanings.
        private static void BeforeReadout(ref string __2)
        {
            if (enabled && LocaleMode.IsKoreanMode() &&
                string.Equals(__2, "BREACH", StringComparison.Ordinal))
                __2 = "정문 폭파까지";
        }

        private static string ToDisplay(string value)
        {
            if (!enabled || profiles == null || value == null || !LocaleMode.IsKoreanMode()) return value;
            return profiles.Translate(ProfileId, Channel, value);
        }

        private static IEnumerable<CodeInstruction> MeasureBannerTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var translate = AccessTools.Method(typeof(HideoutLastStandUiHooks), nameof(ToDisplay));
            int match = -1;
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Newobj || !(codes[i].operand is ConstructorInfo ctor) ||
                    ctor.DeclaringType != typeof(GUIContent) ||
                    !ctor.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string) })) continue;
                if (codes[i - 1].opcode != OpCodes.Ldsfld || !(codes[i - 1].operand is FieldInfo field) ||
                    field.DeclaringType?.FullName != "HideoutSiege.SiegeState" || field.Name != "Banner") continue;
                if (match >= 0) throw new InvalidOperationException("Multiple HUD banner measurements; no safe scoped replacement");
                match = i;
            }
            if (match < 0) throw new InvalidOperationException("Exact HUD banner measurement not found");
            codes.Insert(match, new CodeInstruction(OpCodes.Call, translate));
            return codes;
        }

        private static void SetKoreanFontIfNeeded(GUIStyle style, string text)
        {
            if (style == null) return;
            var current = ImguiFontCompat.GetStyleFont(style);
            if (!LocaleMode.IsKoreanMode())
            {
                if (StyleFonts.TryGetValue(style, out var original) && Equals(current, original.Applied))
                    ImguiFontCompat.SetStyleFont(style, original.Original);
                return;
            }
            if (string.IsNullOrEmpty(text) || !text.Any(ch => ch >= '\uAC00' && ch <= '\uD7A3')) return;
            if (!StyleFonts.TryGetValue(style, out var state))
            {
                state = new FontState { Original = current };
                StyleFonts[style] = state;
            }
            if (ImguiFontCompat.HasKoreanGlyphs(current)) return;
            var fallback = GetKoreanFont();
            if (fallback != null)
            {
                ImguiFontCompat.SetStyleFont(style, fallback);
                state.Applied = fallback;
            }
        }

        private static object GetKoreanFont()
        {
            if (koreanFontChecked) return koreanFont;
            koreanFontChecked = true;
            foreach (var family in new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "NanumGothic" })
            {
                var candidate = ImguiFontCompat.CreateOSFont(family, 16, false);
                if (candidate != null) return koreanFont = candidate;
            }
            return null;
        }
    }
    // These exact source releases share the reviewed SiegeUi.Text / HUD,
    // interaction and defeat-screen method contracts. Do not accept future
    // versions by numeric range without reading their implementation.
    internal static class HideoutLastStandVersionGuard
    {
        internal static bool IsSupported(string version)
        {
            return string.Equals(version, "0.1.0", StringComparison.Ordinal) ||
                   string.Equals(version, "0.1.1", StringComparison.Ordinal) ||
                   string.Equals(version, "0.1.2", StringComparison.Ordinal) ||
                   string.Equals(version, "0.1.3", StringComparison.Ordinal);
        }
    }
}
