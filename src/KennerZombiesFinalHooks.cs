using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // SPZombies 0.1.1 R006: the remaining source-audited, *display-only* loading,
    // tip and pause paths, plus names of items built by this mode's gun table.
    // No map identifiers, saved game data, gun table entries or player names are changed.
    internal static class KennerZombiesFinalHooks
    {
        private const string ModId = "com.kennerz.zmcore";
        private const string AssemblyId = "Kennerz_ZM_Core";
        private const string Channel = "small_ui";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Installed = new HashSet<Assembly>();
        private static readonly HashSet<string> GunSources = new HashSet<string>(StringComparer.Ordinal)
        {
            "Ray Gun", "Thundergun", "M32A1 (War Machine)",
            "Mosin", "Desert Eagle L6", "Glock 18C", "Five-seveN MK2",
            "SR-2M Veresk", "PP-19-01 Vityaz"
        };
        private static Harmony owner;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static MethodInfo ofGun;
        private static FieldInfo gunName;
        private static PropertyInfo activeMatch;

        internal static int Enable(Harmony harmony, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (harmony == null || data == null || !data.HasRules(ModId, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                owner = harmony;
                profiles = data;
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
                Installed.Clear();
                owner = null;
                profiles = null;
                ofGun = null;
                gunName = null;
                activeMatch = null;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { PatchAssembly(args.LoadedAssembly); }
            catch (Exception ex) { Warn("late", ex); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly?.GetName().Name != AssemblyId) return 0;
            var plugin = assembly.GetType("KennerZombies.ZombiesPlugin", false);
            var guid = plugin?.GetField("PluginGuid", BindingFlags.Static | BindingFlags.Public);
            var ver = plugin?.GetField("PluginVersion", BindingFlags.Static | BindingFlags.Public);
            if (guid == null || ver == null || !guid.IsLiteral || !ver.IsLiteral ||
                (string)guid.GetRawConstantValue() != ModId ||
                (string)ver.GetRawConstantValue() != "0.1.1") return 0;
            var tipsType = assembly.GetType("KennerZombies.ZombiesTips", false);
            var matchType = assembly.GetType("KennerZombies.ZombiesMatch", false);
            var pauseType = assembly.GetType("KennerZombies.ZombiesPause", false);
            var gunType = assembly.GetType("KennerZombies.ZombiesGunTable", false);
            var tips = tipsType == null ? null : AccessTools.Method(tipsType, "Draw", new[] { typeof(float), typeof(float), typeof(float) });
            var fade = matchType == null ? null : AccessTools.Method(matchType, "DrawFadeIn", Type.EmptyTypes);
            var pauseLabel = pauseType == null ? null : AccessTools.Method(pauseType, "Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle), typeof(Color) });
            var active = matchType == null ? null : AccessTools.Property(matchType, "Active");
            var sourceItem = AccessTools.TypeByName("EFT.InventoryLogic.Item");
            var of = gunType == null || sourceItem == null ? null : AccessTools.Method(gunType, "Of", new[] { sourceItem });
            var name = of == null ? null : AccessTools.Field(of.ReturnType, "Name");
            if (tips == null || fade == null || pauseLabel == null || active == null || of == null || name == null ||
                !tips.IsStatic || fade.IsStatic || !pauseLabel.IsStatic || !of.IsStatic ||
                tips.ReturnType != typeof(void) || fade.ReturnType != typeof(void) ||
                pauseLabel.ReturnType != typeof(void) || active.PropertyType != typeof(bool) ||
                !active.GetGetMethod(true).IsStatic || name.FieldType != typeof(string) ||
                !fade.IsPrivate || !pauseLabel.IsPrivate) return 0;

            lock (Gate)
            {
                if (!enabled || owner == null || !Installed.Add(assembly)) return 0;
            }
            try
            {
                ofGun = of;
                gunName = name;
                activeMatch = active;
                owner.Patch(tips, transpiler: new HarmonyMethod(typeof(KennerZombiesFinalHooks), nameof(TranslateTipsDisplay)));
                owner.Patch(fade, transpiler: new HarmonyMethod(typeof(KennerZombiesFinalHooks), nameof(TranslateLoadingDisplay)));
                owner.Patch(pauseLabel, prefix: new HarmonyMethod(typeof(KennerZombiesFinalHooks), nameof(BeforePauseLabel)));
                int count = 3;
                // The source's ZombiesGunTable.PatchNames() puts its high-priority
                // postfix on these *three* EFT name getters. Apply our low-priority
                // postfix only if its exact table name survived all earlier postfx.
                // Game builds can place these EFT helper types in either EFT or
                // EFT.InventoryLogic; resolve the exact method signature afterward.
                var localization = AccessTools.TypeByName("EFT.InventoryLogic.LocalizationExtensions")
                                   ?? AccessTools.TypeByName("EFT.LocalizationExtensions");
                var factory = AccessTools.TypeByName("EFT.InventoryLogic.ItemFactory")
                              ?? AccessTools.TypeByName("EFT.ItemFactory");
                var candidates = new List<MethodInfo>();
                if (localization != null)
                {
                    foreach (var methodName in new[] { "LocalizedName", "LocalizedShortName" })
                    {
                        var method = AccessTools.Method(localization, methodName, new[] { sourceItem });
                        if (method != null && method.ReturnType == typeof(string)) candidates.Add(method);
                    }
                }
                if (factory != null)
                {
                    var brief = factory.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                        .Where(m => m.Name == "BriefItemName" && m.ReturnType == typeof(string) &&
                                    m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == sourceItem)
                        .ToArray();
                    if (brief.Length == 1) candidates.Add(brief[0]);
                }
                var postfix = new HarmonyMethod(typeof(KennerZombiesFinalHooks), nameof(AfterTableGunName))
                { priority = Priority.Last };
                foreach (var method in candidates.Distinct())
                {
                    owner.Patch(method, postfix: postfix);
                    count++;
                }
                if (candidates.Count != 3)
                    SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r006-name-getters",
                        () => "SPZombies: only " + candidates.Count + "/3 source-audited gun name getters matched; unmatched getters preserve originals.");
                return count;
            }
            catch (Exception ex)
            {
                lock (Gate) Installed.Remove(assembly);
                Warn("install", ex);
                // The plugin owns this Harmony ID and unpatches it on an installation error.
                throw;
            }
        }

        private static string T(string source)
        {
            if (!enabled || profiles == null || string.IsNullOrEmpty(source)) return source;
            try { return profiles.Translate(ModId, Channel, source) ?? source; }
            catch (Exception ex) { Warn("translate", ex); return source; }
        }

        // The tip source contains {use}; translate BEFORE the original replaces
        // it with the player's bound key. Both its CalcHeight and its drawn label
        // then receive the identical localized sentence.
        private static string TranslateTipAndReplace(string source, string token, string key) =>
            (T(source) ?? source)?.Replace(token, key);

        private static GUIStyle LocalStyle(GUIStyle style, string value) =>
            KennerZombiesSettingsHooks.StyleFor(style, value);

        private static float TipHeight(GUIStyle style, GUIContent content, float width)
        {
            if (style == null) return 0f;
            return LocalStyle(style, content?.text).CalcHeight(content, width);
        }

        private static void Label(Rect rect, string source, GUIStyle style)
        {
            string translated = T(source);
            GUI.Label(rect, translated, LocalStyle(style, translated));
        }

        private static void BeforePauseLabel(ref string __1, ref GUIStyle __2)
        {
            __1 = T(__1);
            __2 = LocalStyle(__2, __1);
        }

        // Strictly match only the table gun, active match and actual unmodified
        // English name. Never translate an already renamed/infused gun, another
        // mod's weapon, an item key or the BoxGuns.Gun.Name source field.
        private static void AfterTableGunName(object __0, ref string __result)
        {
            if (!enabled || __0 == null || !GunSources.Contains(__result)) return;
            try
            {
                if (activeMatch == null || !(bool)activeMatch.GetValue(null)) return;
                var gun = ofGun?.Invoke(null, new[] { __0 });
                if (gun == null || !string.Equals(gunName?.GetValue(gun) as string, __result, StringComparison.Ordinal)) return;
                __result = T(__result);
            }
            catch (Exception ex) { Warn("gun", ex); }
        }

        private static readonly MethodInfo ReplaceString = AccessTools.Method(typeof(string), "Replace", new[] { typeof(string), typeof(string) });
        private static readonly MethodInfo SourceLabel = AccessTools.Method(typeof(GUI), "Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
        private static readonly MethodInfo CalcHeight = AccessTools.Method(typeof(GUIStyle), "CalcHeight", new[] { typeof(GUIContent), typeof(float) });
        private static readonly MethodInfo LocalizedReplace = AccessTools.Method(typeof(KennerZombiesFinalHooks), nameof(TranslateTipAndReplace));
        private static readonly MethodInfo LocalizedLabel = AccessTools.Method(typeof(KennerZombiesFinalHooks), nameof(Label));
        private static readonly MethodInfo LocalizedHeight = AccessTools.Method(typeof(KennerZombiesFinalHooks), nameof(TipHeight));

        private static IEnumerable<CodeInstruction> TranslateTipsDisplay(IEnumerable<CodeInstruction> instructions)
        {
            var source = instructions.ToList();
            int replaces = source.Count(x => x.Calls(ReplaceString));
            int labels = source.Count(x => x.Calls(SourceLabel));
            int heights = source.Count(x => x.Calls(CalcHeight));
            if (replaces != 1 || labels != 3 || heights != 1)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r006-tips-shape",
                    () => "SPZombies tips IL changed: expected one key substitution, 3 labels and one height calculation; leaving original method.");
                return source;
            }
            foreach (var c in source)
            {
                if (c.Calls(ReplaceString)) { c.opcode = OpCodes.Call; c.operand = LocalizedReplace; }
                else if (c.Calls(SourceLabel)) c.operand = LocalizedLabel;
                else if (c.Calls(CalcHeight)) { c.opcode = OpCodes.Call; c.operand = LocalizedHeight; }
            }
            return source;
        }

        private static IEnumerable<CodeInstruction> TranslateLoadingDisplay(IEnumerable<CodeInstruction> instructions)
        {
            var source = instructions.ToList();
            int labels = source.Count(x => x.Calls(SourceLabel));
            if (labels != 2)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r006-loading-shape",
                    () => "SPZombies loading screen IL changed: expected two labels; leaving original method.");
                return source;
            }
            foreach (var c in source)
                if (c.Calls(SourceLabel)) c.operand = LocalizedLabel;
            return source;
        }

        private static void Warn(string phase, Exception ex) =>
            SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r006-" + phase,
                () => "SPZombies R006 display translation skipped: " + ex.Message);
    }
}
