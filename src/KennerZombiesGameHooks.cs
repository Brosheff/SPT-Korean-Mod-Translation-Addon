using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // SPZombies 0.1.1 R003 — only final visible in-raid strings. Never translate
    // interaction IDs, state, keys, prices, player names, or warning log payloads.
    internal static class KennerZombiesGameHooks
    {
        private const string ModId = "com.kennerz.zmcore";
        private const string AssemblyId = "Kennerz_ZM_Core";
        private const string Channel = "small_ui";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static Harmony owner;
        private static EditableProfiles data;
        private static bool enabled;
        private static FieldInfo pointName;
        private static MemberInfo timerText;
        private static PropertyInfo gameActive;

        internal static int Enable(Harmony harmony, IEnumerable<Assembly> assemblies, EditableProfiles profiles)
        {
            if (harmony == null || profiles == null || !profiles.HasRules(ModId, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                owner = harmony;
                data = profiles;
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
                data = null;
                pointName = null;
                timerText = null;
                gameActive = null;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { PatchAssembly(args.LoadedAssembly); }
            catch (Exception ex) { Warn("late", ex); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly == null || assembly.GetName().Name != AssemblyId) return 0;
            var plugin = assembly.GetType("KennerZombies.ZombiesPlugin", false);
            var guid = plugin?.GetField("PluginGuid", BindingFlags.Public | BindingFlags.Static);
            var ver = plugin?.GetField("PluginVersion", BindingFlags.Public | BindingFlags.Static);
            if (guid == null || ver == null || !guid.IsLiteral || !ver.IsLiteral ||
                (string)guid.GetRawConstantValue() != ModId ||
                (string)ver.GetRawConstantValue() != "0.1.1") return 0;

            var hud = assembly.GetType("KennerZombies.ZombiesHud", false);
            var rounds = assembly.GetType("KennerZombies.ZombiesRounds", false);
            var ups = assembly.GetType("KennerZombies.ZombiesPowerUps", false);
            var exfil = assembly.GetType("KennerZombies.ZombiesExfil", false);
            var exfilUi = assembly.GetType("KennerZombies.ZombiesExfilUi", false);
            if (hud == null || rounds == null || ups == null || exfil == null || exfilUi == null) return 0;

            var prompt = AccessTools.Method(hud, "Prompt", new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(float), typeof(float), typeof(float) });
            var banner = AccessTools.Method(hud, "Banner", new[] { typeof(float), typeof(string), typeof(Color), typeof(float), typeof(float), typeof(float) });
            var bar = AccessTools.Method(hud, "Bar", new[] { typeof(Rect), typeof(float), typeof(Color), typeof(string), typeof(float) });
            var shadowed = AccessTools.Method(exfil, "Shadowed", new[] { typeof(Rect), typeof(string), typeof(Color) });
            var drawLeft = AccessTools.Method(rounds, "DrawLeft", new[] { typeof(float), typeof(float), typeof(float), typeof(Color) });
            var drawUps = AccessTools.Method(ups, "Draw", Type.EmptyTypes);
            var notify = AccessTools.Method(plugin, "Notify", new[] { typeof(string), typeof(bool) });
            var relabel = AccessTools.Method(exfilUi, "Relabel");
            var panel = relabel?.GetParameters();
            if (prompt == null || banner == null || bar == null || shadowed == null || drawLeft == null ||
                drawUps == null || notify == null || relabel == null || panel.Length != 1 ||
                panel[0].ParameterType.FullName != "EFT.UI.BattleTimer.ExitTimerPanel" ||
                prompt.ReturnType != typeof(float) || banner.ReturnType != typeof(void) ||
                bar.ReturnType != typeof(void) || shadowed.ReturnType != typeof(void) ||
                drawLeft.ReturnType != typeof(void) || drawUps.ReturnType != typeof(void) ||
                notify.ReturnType != typeof(void) || relabel.ReturnType != typeof(void) ||
                !prompt.IsStatic || !banner.IsStatic || !bar.IsStatic || !shadowed.IsStatic ||
                !drawLeft.IsStatic || !drawUps.IsStatic || !notify.IsStatic || !relabel.IsStatic)
                return 0;
            var name = AccessTools.Field(panel[0].ParameterType, "_pointName");
            var timer = (MemberInfo)AccessTools.Property(panel[0].ParameterType, "TimerText")
                ?? AccessTools.Field(panel[0].ParameterType, "TimerText");
            var active = AccessTools.Property(assembly.GetType("KennerZombies.ZombiesMatch", false), "Active");
            var timerType = timer is PropertyInfo timerProperty ? timerProperty.PropertyType : (timer as FieldInfo)?.FieldType;
            if (name == null || timer == null || active == null || active.PropertyType != typeof(bool) ||
                !active.GetGetMethod(true).IsStatic || !typeof(TMP_Text).IsAssignableFrom(name.FieldType) ||
                timerType == null || !typeof(TMP_Text).IsAssignableFrom(timerType)) return 0;
            lock (Gate)
            {
                if (!enabled || owner == null || !Patched.Add(assembly)) return 0;
            }
            try
            {
                pointName = name;
                timerText = timer;
                gameActive = active;
                owner.Patch(prompt, prefix: new HarmonyMethod(typeof(KennerZombiesGameHooks), nameof(BeforePrompt)));
                owner.Patch(banner, prefix: new HarmonyMethod(typeof(KennerZombiesGameHooks), nameof(BeforeBanner)));
                owner.Patch(bar, prefix: new HarmonyMethod(typeof(KennerZombiesGameHooks), nameof(BeforeBar)));
                owner.Patch(shadowed, prefix: new HarmonyMethod(typeof(KennerZombiesGameHooks), nameof(BeforeShadowed)));
                owner.Patch(drawLeft, transpiler: new HarmonyMethod(typeof(KennerZombiesGameHooks), nameof(TranslateCounterLabels)));
                owner.Patch(drawUps, transpiler: new HarmonyMethod(typeof(KennerZombiesGameHooks), nameof(TranslatePowerUpLabels)));
                owner.Patch(relabel, postfix: new HarmonyMethod(typeof(KennerZombiesGameHooks), nameof(AfterExfilRelabel)));
                owner.Patch(notify, transpiler: new HarmonyMethod(typeof(KennerZombiesGameHooks), nameof(TranslateNotifyDisplayOnly)));
                return 8;
            }
            catch (Exception ex)
            {
                lock (Gate) Patched.Remove(assembly);
                Warn("install", ex);
                // Plugin owns this separate Harmony ID and will unpatch it on failure.
                throw;
            }
        }

        private static string T(string source)
        {
            if (!enabled || string.IsNullOrEmpty(source)) return source;
            try { return data?.Translate(ModId, Channel, source) ?? source; }
            catch (Exception ex) { Warn("translate", ex); return source; }
        }

        private static void BeforePrompt(ref string __0, ref string __2)
        {
            __0 = T(__0); // Press/Hold, not the key in __1
            __2 = T(__2); // displayed action, not the interaction ID
        }
        private static void BeforeBanner(ref string __1) { __1 = T(__1); }
        private static void BeforeBar(ref string __3) { __3 = T(__3); }
        private static void BeforeShadowed(ref string __1) { __1 = T(__1); }

        // The original updates its GUIContent only when the zombie count changes.
        // Rebuild only the draw-time content; never alter LeftText/_leftShown.
        private static void LabelContent(Rect rect, GUIContent content, GUIStyle style)
        {
            string original = content?.text;
            string translated = T(original);
            if (content != null && !string.Equals(original, translated, StringComparison.Ordinal))
            {
                var clone = new GUIContent(content);
                clone.text = translated;
                GUI.Label(rect, clone, style);
            }
            else GUI.Label(rect, content, style);
        }
        private static void LabelString(Rect rect, string content, GUIStyle style) => GUI.Label(rect, T(content), style);

        private static IEnumerable<CodeInstruction> TranslateCounterLabels(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceLabels(instructions,
                AccessTools.Method(typeof(GUI), "Label", new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) }),
                AccessTools.Method(typeof(KennerZombiesGameHooks), nameof(LabelContent)), 2, "left-count");
        }
        private static IEnumerable<CodeInstruction> TranslatePowerUpLabels(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceLabels(instructions,
                AccessTools.Method(typeof(GUI), "Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) }),
                AccessTools.Method(typeof(KennerZombiesGameHooks), nameof(LabelString)), 3, "powerups");
        }
        private static IEnumerable<CodeInstruction> ReplaceLabels(IEnumerable<CodeInstruction> instructions,
            MethodInfo original, MethodInfo replacement, int expected, string place)
        {
            var codes = instructions.ToList();
            var found = original == null || replacement == null ? 0 : codes.Count(c => c.Calls(original));
            if (found != expected)
            {
                Warn(place, new InvalidOperationException("expected " + expected + " GUI labels, found " + found));
                return codes;
            }
            foreach (var code in codes.Where(c => c.Calls(original)))
            {
                code.opcode = OpCodes.Call;
                code.operand = replacement;
            }
            return codes;
        }

        private static void AfterExfilRelabel(object __0)
        {
            // Original's Relabel has already verified active zombies + specific basement
            // point. Check the sentinel ourselves to avoid touching any other exit panel.
            try
            {
                if (!enabled || __0 == null || !(gameActive?.GetValue(null, null) is bool active) || !active) return;
                var label = pointName?.GetValue(__0) as TMP_Text;
                if (label == null || (label.text != "Basement Exfil" && label.text != "지하실 탈출구")) return;
                label.text = T("Basement Exfil");
                var timer = timerText is PropertyInfo property
                    ? property.GetValue(__0, null) as TMP_Text
                    : (timerText as FieldInfo)?.GetValue(__0) as TMP_Text;
                if (timer != null) timer.text = T(timer.text);
            }
            catch (Exception ex) { Warn("exfil-panel", ex); }
        }

        // The source writes warnings both to a log and to an EFT notification.
        // Inject T only after ldarg.0 used at the two DISPLAY call sites.
        // An ordinary Prefix would translate the log too, which is prohibited.
        private static IEnumerable<CodeInstruction> TranslateNotifyDisplayOnly(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var injectAt = new List<int>();
            foreach (var pair in codes.Select((code, index) => (code, index)))
            {
                if (!(pair.code.operand is MethodInfo called) ||
                    called.DeclaringType?.FullName != "EFT.UI.Notifications.NotificationManager" &&
                    called.DeclaringType?.Name != "NotificationManager") continue;
                if (called.Name != "DisplayWarningNotification" && called.Name != "DisplayMessageNotification") continue;
                var displayArgs = called.GetParameters();
                int expectedArgs = called.Name == "DisplayWarningNotification" ? 2 : 4;
                if (!called.IsStatic || displayArgs.Length != expectedArgs ||
                    displayArgs[0].ParameterType != typeof(string)) continue;
                for (int j = pair.index - 1; j >= Math.Max(0, pair.index - 6); j--)
                {
                    if (codes[j].opcode == OpCodes.Ldarg_0)
                    {
                        injectAt.Add(j + 1);
                        break;
                    }
                    if (codes[j].opcode == OpCodes.Call || codes[j].opcode == OpCodes.Callvirt) break;
                }
            }
            if (injectAt.Count != 2 || injectAt.Distinct().Count() != 2)
            {
                Warn("notification", new InvalidOperationException("notification IL display sites changed"));
                return codes;
            }
            var translate = AccessTools.Method(typeof(KennerZombiesGameHooks), nameof(T));
            foreach (int at in injectAt.OrderByDescending(i => i))
                codes.Insert(at, new CodeInstruction(OpCodes.Call, translate));
            return codes;
        }

        private static void Warn(string place, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r003-" + place,
                () => "SPZombies R003 display hook skipped (" + place + "): " + ex.Message);
        }
    }
}
