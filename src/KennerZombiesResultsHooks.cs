using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // SPZombies 0.1.1 R004: translate IMGUI drawing arguments only.
    // No match/history/XP state, player names, buttons' return values or saved data are modified.
    internal static class KennerZombiesResultsHooks
    {
        private const string ModId = "com.kennerz.zmcore";
        private const string AssemblyId = "Kennerz_ZM_Core";
        private const string Channel = "small_ui";
        private static readonly HashSet<Assembly> Installed = new HashSet<Assembly>();
        private static readonly object Gate = new object();
        private static Harmony owner;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static FieldInfo boardNameStyle;

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
                boardNameStyle = null;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e)
        {
            try { PatchAssembly(e.LoadedAssembly); }
            catch (Exception ex) { Warn("late", ex); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly?.GetName().Name != AssemblyId) return 0;
            var plugin = assembly.GetType("KennerZombies.ZombiesPlugin", false);
            var guid = plugin?.GetField("PluginGuid", BindingFlags.Static | BindingFlags.Public);
            var version = plugin?.GetField("PluginVersion", BindingFlags.Static | BindingFlags.Public);
            if (guid == null || version == null || !guid.IsLiteral || !version.IsLiteral ||
                (string)guid.GetRawConstantValue() != ModId ||
                (string)version.GetRawConstantValue() != "0.1.1") return 0;

            var downed = assembly.GetType("KennerZombies.ZombiesDowned", false);
            var gameOver = assembly.GetType("KennerZombies.ZombiesGameOver", false);
            var stats = assembly.GetType("KennerZombies.ZombiesStats", false);
            var report = assembly.GetType("KennerZombies.ZombiesReport", false);
            var history = assembly.GetType("KennerZombies.ZombiesStatsScreen", false);
            var xp = assembly.GetType("KennerZombies.ZombiesXp", false);
            var hud = assembly.GetType("KennerZombies.ZombiesHud", false);
            if (downed == null || gameOver == null || stats == null || report == null ||
                history == null || xp == null || hud == null) return 0;

            var drawDowned = AccessTools.Method(downed, "Draw", Type.EmptyTypes);
            var shadow = AccessTools.Method(gameOver, "Shadowed", new[] { typeof(Rect), typeof(string), typeof(GUIStyle), typeof(Color) });
            var drawBlack = AccessTools.Method(gameOver, "DrawBlack", Type.EmptyTypes);
            var drawBoard = AccessTools.Method(stats, "DrawBoard", new[] { typeof(bool), typeof(float) });
            var onReport = AccessTools.Method(report, "OnGUI", Type.EmptyTypes);
            var resultType = xp.GetNestedType("Result", BindingFlags.Public | BindingFlags.NonPublic);
            var tally = resultType == null ? null : AccessTools.Method(report, "DrawTally", new[] { resultType, typeof(Rect), typeof(float), typeof(float), typeof(float) });
            var onHistory = AccessTools.Method(history, "OnGUI", Type.EmptyTypes);
            var hudLabel = AccessTools.Method(hud, "Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle), typeof(Color), typeof(float) });
            var nameStyle = AccessTools.Field(stats, "_nameStyle");
            if (drawDowned == null || shadow == null || drawBlack == null || drawBoard == null ||
                onReport == null || tally == null || onHistory == null || hudLabel == null ||
                nameStyle == null || nameStyle.FieldType != typeof(GUIStyle) || !nameStyle.IsStatic ||
                drawDowned.ReturnType != typeof(void) || !drawDowned.IsStatic ||
                shadow.ReturnType != typeof(void) || !shadow.IsStatic ||
                drawBlack.ReturnType != typeof(void) || !drawBlack.IsStatic ||
                drawBoard.ReturnType != typeof(void) || !drawBoard.IsStatic ||
                onReport.ReturnType != typeof(void) || onReport.IsStatic ||
                tally.ReturnType != typeof(void) || tally.IsStatic ||
                onHistory.ReturnType != typeof(void) || onHistory.IsStatic ||
                hudLabel.ReturnType != typeof(void) || !hudLabel.IsStatic) return 0;

            lock (Gate)
            {
                if (!enabled || owner == null || !Installed.Add(assembly)) return 0;
            }
            try
            {
                boardNameStyle = nameStyle;
                owner.Patch(drawDowned, transpiler: new HarmonyMethod(typeof(KennerZombiesResultsHooks), nameof(DownedLabels)));
                owner.Patch(shadow, prefix: new HarmonyMethod(typeof(KennerZombiesResultsHooks), nameof(BeforeGameOverShadow)));
                owner.Patch(drawBlack, transpiler: new HarmonyMethod(typeof(KennerZombiesResultsHooks), nameof(BlackLabels)));
                owner.Patch(drawBoard, transpiler: new HarmonyMethod(typeof(KennerZombiesResultsHooks), nameof(BoardLabels)));
                owner.Patch(onReport, transpiler: new HarmonyMethod(typeof(KennerZombiesResultsHooks), nameof(ReportDisplay)));
                owner.Patch(tally, transpiler: new HarmonyMethod(typeof(KennerZombiesResultsHooks), nameof(TallyLabels)));
                owner.Patch(onHistory, transpiler: new HarmonyMethod(typeof(KennerZombiesResultsHooks), nameof(HistoryDisplay)));
                // The scoreboard Row is a compiler-generated local function; limit
                // the shared HUD Label hook to its unique _nameStyle and own-row suffix.
                owner.Patch(hudLabel, prefix: new HarmonyMethod(typeof(KennerZombiesResultsHooks), nameof(BeforeBoardName)));
                return 8;
            }
            catch (Exception ex)
            {
                lock (Gate) Installed.Remove(assembly);
                Warn("install", ex);
                // Plugin owns this independent Harmony ID and unpatches on failure.
                throw;
            }
        }

        private static string T(string original)
        {
            if (!enabled || profiles == null || string.IsNullOrEmpty(original)) return original;
            try { return profiles.Translate(ModId, Channel, original) ?? original; }
            catch (Exception ex) { Warn("translation", ex); return original; }
        }

        private static void Label(Rect rect, string original, GUIStyle style) => GUI.Label(rect, T(original), style);
        private static bool Button(Rect rect, string original, GUIStyle style) => GUI.Button(rect, T(original), style);
        private static void BeforeGameOverShadow(ref string __1) { __1 = T(__1); }

        private static void BeforeBoardName(ref string __1, GUIStyle __2)
        {
            // The original draws the row rank and nickname in this unique style.
            // Do not pass the player name through a general template/segment matcher.
            if (!enabled || __1 == null || boardNameStyle == null ||
                !ReferenceEquals(boardNameStyle.GetValue(null), __2) ||
                !__1.EndsWith("  (YOU)", StringComparison.Ordinal)) return;
            __1 = __1.Substring(0, __1.Length - "(YOU)".Length) + T("(YOU)");
        }

        private static readonly MethodInfo OriginalLabel = AccessTools.Method(typeof(GUI), "Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
        private static readonly MethodInfo OriginalButton = AccessTools.Method(typeof(GUI), "Button", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
        private static readonly MethodInfo TranslatedLabel = AccessTools.Method(typeof(KennerZombiesResultsHooks), nameof(Label));
        private static readonly MethodInfo TranslatedButton = AccessTools.Method(typeof(KennerZombiesResultsHooks), nameof(Button));

        private static IEnumerable<CodeInstruction> DownedLabels(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, 1, 0, "downed-draw");
        private static IEnumerable<CodeInstruction> BlackLabels(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, 1, 0, "gameover-loading");
        private static IEnumerable<CodeInstruction> BoardLabels(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, 3, 0, "scoreboard");
        private static IEnumerable<CodeInstruction> ReportDisplay(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, 6, 1, "report");
        private static IEnumerable<CodeInstruction> TallyLabels(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, 9, 0, "xp-tally");
        private static IEnumerable<CodeInstruction> HistoryDisplay(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, 8, 1, "statistics");

        // Only targeted methods' exact GUI.Label/Button overloads are replaced.
        // An upstream IL change results in unchanged original instructions, not a partial patch.
        private static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> instructions,
            int expectedLabels, int expectedButtons, string path)
        {
            var codes = instructions.ToList();
            int labels = OriginalLabel == null ? -1 : codes.Count(c => c.Calls(OriginalLabel));
            int buttons = OriginalButton == null ? -1 : codes.Count(c => c.Calls(OriginalButton));
            if (labels != expectedLabels || buttons != expectedButtons || TranslatedLabel == null || TranslatedButton == null)
            {
                Warn(path, new InvalidOperationException("GUI calls changed: labels=" + labels + ", buttons=" + buttons));
                return codes;
            }
            foreach (var c in codes)
            {
                if (c.Calls(OriginalLabel)) { c.opcode = OpCodes.Call; c.operand = TranslatedLabel; }
                else if (c.Calls(OriginalButton)) { c.opcode = OpCodes.Call; c.operand = TranslatedButton; }
            }
            return codes;
        }

        private static void Warn(string path, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r004-" + path,
                () => "SPZombies R004 display hook skipped (" + path + "): " + ex.Message);
        }
    }
}
