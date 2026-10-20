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
    /// <summary>
    /// ArenaMode 0.1.1 independent UI: translate only strings at audited visual output sites.
    /// Game-mode identifiers, option values, object names, and callbacks remain untouched.
    /// This is deliberately separate from the small-UI literal-transpiler registry.
    /// </summary>
    internal static class ArenaModeUiHooks
    {
        private const string ProfileId = "com.maschine.ArenaMode";
        private const string Channel = "small_ui";
        private const string AssemblyName = "maschine-ArenaMode.Client";

        // Method names were audited against ArenaMode.Client in ArenaMode-0.1.1.zip.
        // Do not add model, catalog, settings, or gameplay-state classes to this list.
        private sealed class UiScope
        {
            internal readonly string TypeName;
            internal readonly HashSet<string> Methods;

            internal UiScope(string typeName, params string[] methods)
            {
                TypeName = typeName;
                Methods = new HashSet<string>(methods, StringComparer.Ordinal);
            }
        }

        private static readonly UiScope[] Scopes =
        {
            // Character selection: these output methods create the visible Arena caption
            // and the selected-side description. Do not patch game-mode state or IDs.
            new UiScope("ArenaMode.Session.ArenaSideSelectionLayout", "Arrange"),
            new UiScope("ArenaMode.Session.ArenaSideSelectionPatch", "SetDescription"),
            new UiScope("ArenaMode.Session.ArenaModeSelectionView",
                "Synchronize", "Create", "CreateRow", "CreatePreviewRow", "Refresh",
                "CreateFact", "CreateRule", "CreateText"),
            new UiScope("ArenaMode.Session.ArenaMutatorsView",
                "Create", "Layout", "Synchronize", "Text"),
            new UiScope("ArenaMode.Session.ArenaAreaSelectionView",
                "Create", "CreateRow", "Refresh", "CreateText", "SetText"),
            new UiScope("ArenaMode.Session.ArenaEquipmentChoiceView",
                "Show", "Refresh", "CreateChoice"),
            new UiScope("ArenaMode.Session.ArenaDeathmatchRulesView",
                "Show", "Refresh", "CreateChoice", "CreateText"),
            new UiScope("ArenaMode.Session.ArenaZombieRulesView",
                "Show", "Synchronize", "Refresh", "Text"),
            new UiScope("ArenaMode.Session.ArenaTdmMatchRulesView",
                "Show", "Refresh", "CreateChoice", "CreateText"),
            new UiScope("ArenaMode.Hud.ScorePanel",
                "ShowAssist", "Create", "Tick", "LayoutComparison", "Comparison"),
            new UiScope("ArenaMode.Hud.ArenaPowerupPanel", "Tick"),
            // 0.1.1 audited final text setters; only the renderer methods are
            // patched. Model/state/catalog methods and cleanup/restore stay native.
            new UiScope("ArenaMode.Session.ArenaCompactMapInfo", "Refresh"),
            new UiScope("ArenaMode.Session.ArenaPortedMapPresentation", "Refresh"),
            new UiScope("ArenaMode.Session.Maps.ArenaAreaMapPreview", "Refresh", "Text"),
            new UiScope("ArenaMode.Hud.ArenaAssemblyBanner", "Tick"),
            new UiScope("ArenaMode.Hud.KillfeedView", "Push"),
            new UiScope("ArenaMode.Hud.CalloutElement", "Show"),
            new UiScope("ArenaMode.Hud.InfoPanels.UI.StatusStripView", "SetEntries"),
            new UiScope("ArenaMode.Pickups.ArenaPickupView", "Tick"),
            // Dedicated IMGUI rendering; never intercept global GUI calls.
            new UiScope("ArenaMode.Hud.ArenaPanel", "OnGUI", "DrawWindow", "DrawScoreboard",
                "DrawRoundBlock", "DrawStat", "DrawSlider"),
            new UiScope("ArenaMode.Hud.TacticalBuyPanel", "OnGUI", "DrawWindow"),
            new UiScope("ArenaMode.Areas.ArenaAreaCapture", "OnGUI"),
            new UiScope("ArenaMode.Pickups.ArenaPickupCapture", "OnGUI"),
            new UiScope("ArenaMode.Spectator.ArenaSpectatorOverlay", "OnGUI")
        };

        private sealed class TrackedText
        {
            internal readonly WeakReference Label;
            internal string Source;
            internal string LastOutput;

            internal TrackedText(TMP_Text label, string source, string lastOutput)
            {
                Label = new WeakReference(label);
                Source = source;
                LastOutput = lastOutput;
            }
        }

        private static readonly object Gate = new object();
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static readonly List<TrackedText> Tracked = new List<TrackedText>();
        private static readonly MethodInfo TextSetter = typeof(ArenaModeUiHooks).GetMethod(
            nameof(SetVisibleText), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo SideChoiceTranslator = typeof(ArenaModeUiHooks).GetMethod(
            nameof(TranslateSideChoiceText), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo TooltipSetter = typeof(ArenaModeUiHooks).GetMethod(
            nameof(SetTooltipText), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo LayoutLabel = typeof(ArenaModeUiHooks).GetMethod(
            nameof(DrawLayoutLabel), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo LayoutButton = typeof(ArenaModeUiHooks).GetMethod(
            nameof(DrawLayoutButton), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo LayoutToggle = typeof(ArenaModeUiHooks).GetMethod(
            nameof(DrawLayoutToggle), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo RectWindow = typeof(ArenaModeUiHooks).GetMethod(
            nameof(DrawWindowCaption), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo RectLabel = typeof(ArenaModeUiHooks).GetMethod(
            nameof(DrawRectLabel), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo RectLabelDefault = typeof(ArenaModeUiHooks).GetMethod(
            nameof(DrawRectLabelDefault), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo RectBox = typeof(ArenaModeUiHooks).GetMethod(
            nameof(DrawRectBox), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo RectBoxDefault = typeof(ArenaModeUiHooks).GetMethod(
            nameof(DrawRectBoxDefault), BindingFlags.NonPublic | BindingFlags.Static);
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static MethodInfo originalTooltipSetter;

        internal static int Enable(Harmony patcher, EditableProfiles data, Assembly assembly)
        {
            if (patcher == null || data == null) return 0;
            lock (Gate)
            {
                harmony = patcher;
                profiles = data;
            }
            return Patch(assembly);
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                harmony = null;
                profiles = null;
                originalTooltipSetter = null;
                Patched.Clear();
                Tracked.Clear();
            }
        }

        internal static int Patch(Assembly assembly)
        {
            if (assembly == null || !string.Equals(assembly.GetName().Name, AssemblyName,
                    StringComparison.OrdinalIgnoreCase)) return 0;
            Harmony patcher;
            lock (Gate)
            {
                if (harmony == null || profiles?.HasRules(ProfileId, Channel) != true) return 0;
                patcher = harmony;
            }

            var count = 0;
            var transpiler = new HarmonyMethod(typeof(ArenaModeUiHooks).GetMethod(
                nameof(TranslateVisualSetters), BindingFlags.NonPublic | BindingFlags.Static))
                { priority = Priority.Last };
            foreach (var scope in Scopes)
            {
                var type = assembly.GetType(scope.TypeName, false);
                if (type == null) continue;
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch { continue; }
                foreach (var method in methods)
                {
                    if (method.IsAbstract || method.ContainsGenericParameters ||
                        !scope.Methods.Contains(method.Name) || method.GetMethodBody() == null) continue;
                    if (!TryRegister(method)) continue;
                    try
                    {
                        patcher.Patch(method, transpiler: transpiler);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        Forget(method);
                        SPT.EditableTranslations.MinimalLog.WarnOnce(
                            "ArenaMode UI: " + scope.TypeName + "." + method.Name,
                            () => "ArenaMode text output hook skipped: " + ex.Message);
                    }
                }
            }

            // The native character-choice toggle uses UIAnimatedToggleSpawner.vmethod_0,
            // NOT TMP_Text.set_text. This exact original method supplies only the
            // visible "Arena" label; preserve its boolean/Transform and game logic.
            var sideView = assembly.GetType("ArenaMode.Session.ArenaSideSelectionView", false);
            var sideShow = sideView?.GetMethods(BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .SingleOrDefault(m => m.Name == "Show" && m.ReturnType == typeof(void) &&
                    m.GetParameters().Length == 3 &&
                    m.GetParameters()[0].ParameterType == typeof(Transform) &&
                    m.GetParameters()[2].ParameterType == typeof(bool));
            if (sideShow != null && TryRegister(sideShow))
            {
                try
                {
                    patcher.Patch(sideShow,
                        transpiler: new HarmonyMethod(typeof(ArenaModeUiHooks).GetMethod(
                            nameof(TranslateSideChoiceLiteral), BindingFlags.NonPublic | BindingFlags.Static))
                            { priority = Priority.Last });
                    count++;
                }
                catch (Exception ex)
                {
                    Forget(sideShow);
                    SPT.EditableTranslations.MinimalLog.WarnOnce("ArenaMode UI: ArenaSideSelectionView.Show",
                        () => "ArenaMode character-choice label hook skipped: " + ex.Message);
                }
            }

            // The scoreboard uses its own IMGUI renderer rather than TMP text setters.
            var board = assembly.GetType("ArenaMode.Hud.ArenaScoreboardPresentation", false);
            var label = board?.GetMethods(BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .SingleOrDefault(m => m.Name == "Label" && m.ReturnType == typeof(void) &&
                    m.GetParameters().Length == 4 &&
                    m.GetParameters()[0].ParameterType == typeof(Rect) &&
                    m.GetParameters()[1].ParameterType == typeof(string) &&
                    m.GetParameters()[2].ParameterType == typeof(GUIStyle) &&
                    m.GetParameters()[3].ParameterType == typeof(Color));
            if (label != null)
                count += PatchSpecial(patcher, label, nameof(BeforeScoreboardLabel), true);

            // This result is a visible label, not the cause classifier input.
            var killfeed = assembly.GetType("ArenaMode.Hud.KillfeedText", false);
            var cause = killfeed?.GetMethod("DeathCause", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            if (cause != null && cause.ReturnType == typeof(string))
                count += PatchSpecial(patcher, cause, nameof(AfterDeathCause), false);
            // Arena's own notification helper is the single boundary for
            // dynamically assembled match-result and progression toasts.
            var toasts = assembly.GetType("ArenaMode.Modes.ArenaToasts", false);
            var showToast = toasts?.GetMethod("Show", BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (showToast != null && showToast.ReturnType == typeof(void))
                count += PatchSpecial(patcher, showToast, nameof(BeforeArenaToast), true);

            // ARENA ZONE is owned by the mod's restore ledger. A postfix alone
            // makes that ledger lose its exact-English ownership test; a guarded
            // prefix restores its sentinel before non-Arena restore, while the
            // postfix translates only the final on-screen label.
            var captions = assembly.GetType("ArenaMode.Session.ArenaSessionModeCaptionPatch", false);
            var apply = captions?.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public, null, new[] { typeof(TextMeshProUGUI), typeof(bool) }, null);
            if (apply != null && TryRegister(apply))
            {
                try
                {
                    patcher.Patch(apply,
                        prefix: new HarmonyMethod(typeof(ArenaModeUiHooks).GetMethod(
                            nameof(BeforeSessionCaption), BindingFlags.Static | BindingFlags.NonPublic)),
                        postfix: new HarmonyMethod(typeof(ArenaModeUiHooks).GetMethod(
                            nameof(AfterSessionCaption), BindingFlags.Static | BindingFlags.NonPublic)));
                    count++;
                }
                catch (Exception ex)
                {
                    Forget(apply);
                    SPT.EditableTranslations.MinimalLog.WarnOnce("ArenaMode UI: session caption",
                        () => "ArenaMode session caption hook skipped: " + ex.Message);
                }
            }
            return count;
        }

        private static bool TryRegister(MethodBase method)
        {
            lock (Gate)
                return harmony != null && Patched.Add(method);
        }

        private static void Forget(MethodBase method)
        {
            lock (Gate) Patched.Remove(method);
        }

        private static int PatchSpecial(Harmony patcher, MethodInfo target,
            string handler, bool isPrefix)
        {
            if (!TryRegister(target)) return 0;
            try
            {
                var callback = new HarmonyMethod(typeof(ArenaModeUiHooks).GetMethod(
                    handler, BindingFlags.NonPublic | BindingFlags.Static));
                if (isPrefix) patcher.Patch(target, prefix: callback);
                else patcher.Patch(target, postfix: callback);
                return 1;
            }
            catch (Exception ex)
            {
                Forget(target);
                SPT.EditableTranslations.MinimalLog.WarnOnce("ArenaMode UI: " + handler,
                    () => "ArenaMode visual hook skipped: " + ex.Message);
                return 0;
            }
        }

        // Only the original ArenaSideSelectionView.Show literal feeding the visible
        // toggle label is wrapped. Never replace the original "Arena" literal in a
        // catalog/selection object: the value can also be a gameplay identifier.
        private static IEnumerable<CodeInstruction> TranslateSideChoiceLiteral(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (SideChoiceTranslator != null && instruction.opcode == OpCodes.Ldstr &&
                    string.Equals(instruction.operand as string, "Arena", StringComparison.Ordinal))
                    yield return new CodeInstruction(OpCodes.Call, SideChoiceTranslator);
            }
        }

        private static string TranslateSideChoiceText(string source)
        {
            return TranslateVisible(source, profiles);
        }

        private static bool IsTmpTextSetter(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                return false;
            var setter = instruction.operand as MethodInfo;
            if (setter == null || setter.Name != "set_text" ||
                setter.ReturnType != typeof(void) || setter.DeclaringType == null ||
                !typeof(TMP_Text).IsAssignableFrom(setter.DeclaringType)) return false;
            var args = setter.GetParameters();
            return args.Length == 1 && args[0].ParameterType == typeof(string);
        }

        private static bool IsTooltipSetter(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                return false;
            var method = instruction.operand as MethodInfo;
            if (method == null || method.Name != "SetMessageText" ||
                method.ReturnType != typeof(void) ||
                method.DeclaringType?.FullName != "EFT.UI.HoverTooltipArea") return false;
            var args = method.GetParameters();
            return args.Length == 2 && args[0].ParameterType == typeof(string) &&
                args[1].ParameterType == typeof(bool);
        }

        // Only exact display method overloads are redirected. The original GUI
        // method still executes with the same styles/options and return values.
        private static MethodInfo GuiDisplayWrapper(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                return null;
            var method = instruction.operand as MethodInfo;
            if (method == null) return null;
            var args = method.GetParameters();
            if (method.DeclaringType == typeof(GUILayout) && args.Length == 3 &&
                args[0].ParameterType == typeof(string) &&
                args[1].ParameterType == typeof(GUIStyle) &&
                args[2].ParameterType == typeof(GUILayoutOption[]))
            {
                if (method.Name == "Label" && method.ReturnType == typeof(void)) return LayoutLabel;
                if (method.Name == "Button" && method.ReturnType == typeof(bool)) return LayoutButton;
            }
            if (method.DeclaringType == typeof(GUILayout) && method.Name == "Toggle" &&
                method.ReturnType == typeof(bool) && args.Length == 4 &&
                args[0].ParameterType == typeof(bool) &&
                args[1].ParameterType == typeof(string) &&
                args[2].ParameterType == typeof(GUIStyle) &&
                args[3].ParameterType == typeof(GUILayoutOption[]))
                return LayoutToggle;
            // Window captions belong to the render call, not to an arena mode ID.
            if (method.DeclaringType == typeof(GUI) && method.Name == "Window" &&
                method.ReturnType == typeof(Rect) && args.Length == 5 &&
                args[0].ParameterType == typeof(int) && args[1].ParameterType == typeof(Rect) &&
                args[2].ParameterType == typeof(GUI.WindowFunction) &&
                args[3].ParameterType == typeof(string) && args[4].ParameterType == typeof(GUIStyle))
                return RectWindow;
            if (method.DeclaringType == typeof(GUI) && args.Length >= 2 && args.Length <= 3 &&
                args[0].ParameterType == typeof(Rect) && args[1].ParameterType == typeof(string) &&
                method.ReturnType == typeof(void) &&
                (args.Length == 2 || args[2].ParameterType == typeof(GUIStyle)))
            {
                if (method.Name == "Label") return args.Length == 3 ? RectLabel : RectLabelDefault;
                if (method.Name == "Box") return args.Length == 3 ? RectBox : RectBoxDefault;
            }
            return null;
        }

        private static void DrawLayoutLabel(string label, GUIStyle style, GUILayoutOption[] options)
        {
            GUILayout.Label(TranslateVisible(label, profiles), style, options);
        }
        private static bool DrawLayoutButton(string label, GUIStyle style, GUILayoutOption[] options)
        {
            return GUILayout.Button(TranslateVisible(label, profiles), style, options);
        }
        private static bool DrawLayoutToggle(bool value, string label, GUIStyle style,
            GUILayoutOption[] options)
        {
            return GUILayout.Toggle(value, TranslateVisible(label, profiles), style, options);
        }
        private static Rect DrawWindowCaption(int id, Rect rect, GUI.WindowFunction draw,
            string label, GUIStyle style)
        {
            return GUI.Window(id, rect, draw, TranslateVisible(label, profiles), style);
        }
        private static void DrawRectLabel(Rect rect, string label, GUIStyle style)
        {
            GUI.Label(rect, TranslateVisible(label, profiles), style);
        }
        private static void DrawRectLabelDefault(Rect rect, string label)
        {
            GUI.Label(rect, TranslateVisible(label, profiles));
        }
        private static void DrawRectBox(Rect rect, string label, GUIStyle style)
        {
            GUI.Box(rect, TranslateVisible(label, profiles), style);
        }
        private static void DrawRectBoxDefault(Rect rect, string label)
        {
            GUI.Box(rect, TranslateVisible(label, profiles));
        }

        private static IEnumerable<CodeInstruction> TranslateVisualSetters(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (TextSetter != null && IsTmpTextSetter(instruction))
                {
                    // Only the final renderer's (TMP_Text, string) call is redirected.
                    // Labels, exception blocks and the evaluation stack are unchanged.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = TextSetter;
                }
                else if (TooltipSetter != null && IsTooltipSetter(instruction))
                {
                    // The original method is still invoked, with its original rawText flag.
                    lock (Gate) originalTooltipSetter = (MethodInfo)instruction.operand;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = TooltipSetter;
                }
                else
                {
                    var wrapper = GuiDisplayWrapper(instruction);
                    if (wrapper != null)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = wrapper;
                    }
                }
                yield return instruction;
            }
        }

        private static void SetVisibleText(TMP_Text target, string source)
        {
            if (target == null) return;
            var translated = TranslateVisible(source, profiles);
            target.text = translated;
            Track(target, source, translated);
        }

        private static void SetTooltipText(object target, string source, bool rawText)
        {
            if (target == null) return;
            MethodInfo original;
            lock (Gate) original = originalTooltipSetter;
            // Harmony may still own the transpiled call during addon shutdown.
            // Recover the original display method even when the translator is disabled.
            if (original == null)
                original = target.GetType().GetMethod("SetMessageText",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string), typeof(bool) }, null);
            if (original == null)
                throw new MissingMethodException("ArenaMode tooltip renderer is unavailable");
            var translated = TranslateVisible(source, profiles);
            // Do not swallow exceptions: this call must retain the mod's native behaviour.
            try { original.Invoke(target, new object[] { translated, rawText }); }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static void Track(TMP_Text label, string source, string output)
        {
            lock (Gate)
            {
                for (var i = Tracked.Count - 1; i >= 0; i--)
                {
                    var entry = Tracked[i];
                    var tracked = entry.Label.Target as TMP_Text;
                    if (tracked == null) { Tracked.RemoveAt(i); continue; }
                    if (!ReferenceEquals(tracked, label)) continue;
                    if (string.Equals(source, output, StringComparison.Ordinal))
                        Tracked.RemoveAt(i);
                    else
                    {
                        entry.Source = source;
                        entry.LastOutput = output;
                    }
                    return;
                }
                if (!string.IsNullOrEmpty(source) &&
                    !string.Equals(source, output, StringComparison.Ordinal))
                    Tracked.Add(new TrackedText(label, source, output));
            }
        }

        internal static void RefreshLanguage()
        {
            EditableProfiles data;
            TrackedText[] snapshot;
            lock (Gate)
            {
                if (profiles == null || Tracked.Count == 0) return;
                data = profiles;
                snapshot = Tracked.ToArray();
            }
            var stale = new List<TrackedText>();
            foreach (var entry in snapshot)
            {
                var target = entry.Label.Target as TMP_Text;
                if (target == null) { stale.Add(entry); continue; }
                try
                {
                    // The mod may have re-used this label for a different mode/round.
                    // Never overwrite newly rendered content with an older cached source.
                    var current = target.text;
                    if (!string.Equals(current, entry.LastOutput, StringComparison.Ordinal) &&
                        !string.Equals(current, entry.Source, StringComparison.Ordinal))
                    {
                        stale.Add(entry);
                        continue;
                    }
                    var translated = TranslateVisible(entry.Source, data);
                    target.text = translated;
                    entry.LastOutput = translated;
                    // Keep the original for a future English -> Korean switch.
                    // The weak reference and source-vs-current guard prune dead
                    // or repurposed labels without leaking game objects.
                }
                catch { stale.Add(entry); }
            }
            if (stale.Count == 0) return;
            lock (Gate)
                foreach (var entry in stale) Tracked.Remove(entry);
        }

        private static void BeforeSessionCaption(TextMeshProUGUI __0, bool __1)
        {
            if (__1 || __0 == null || profiles == null) return;
            // This pre-normalization is allowed only for a label WE changed.
            // Never rewrite a native caption that happens to share this text.
            bool owned = false;
            lock (Gate)
                foreach (var entry in Tracked)
                    if (ReferenceEquals(entry.Label.Target, __0) &&
                        string.Equals(entry.Source, "ARENA ZONE", StringComparison.Ordinal))
                    { owned = true; break; }
            if (!owned) return;
            var korean = profiles.Translate(ProfileId, Channel, "ARENA ZONE", "kr");
            if (!string.Equals(korean, "ARENA ZONE", StringComparison.Ordinal) &&
                string.Equals(__0.text, korean, StringComparison.Ordinal))
                __0.text = "ARENA ZONE";
        }

        private static void AfterSessionCaption(TextMeshProUGUI __0, bool __1)
        {
            if (!__1 || __0 == null || profiles == null || !LocaleMode.IsKoreanMode()) return;
            var current = __0.text;
            if (!string.Equals(current, "ARENA ZONE", StringComparison.Ordinal)) return;
            var translated = profiles.Translate(ProfileId, Channel, current);
            if (string.Equals(current, translated, StringComparison.Ordinal)) return;
            __0.text = translated;
            Track(__0, current, translated);
        }

        private static void BeforeScoreboardLabel(ref string __1, GUIStyle __2)
        {
            var data = profiles;
            if (__1 == null || data == null) return;
            // ArenaScoreboardPresentation sets _name.fontSize to 14; no other
            // fixed column style uses this size (13/15/16/11/17/12).
            // Do not access GUIStyle.fontStyle: its FontStyle enum lives in
            // UnityEngine.TextRenderingModule, which is not a project reference.
            // Keep player-chosen names out of the generic translator.
            if (__2 != null && __2.fontSize == 14)
            {
                if (LocaleMode.IsKoreanMode() &&
                    __1.EndsWith("  (YOU)", StringComparison.Ordinal))
                    __1 = __1.Substring(0, __1.Length - "  (YOU)".Length) +
                        data.Translate(ProfileId, Channel, "  (YOU)");
                return;
            }
            __1 = TranslateVisible(__1, data);
        }

        private static void BeforeArenaToast(ref string __0)
        {
            __0 = TranslateVisible(__0, profiles);
        }

        private static void AfterDeathCause(ref string __result)
        {
            var data = profiles;
            if (data != null && LocaleMode.IsKoreanMode() && !string.IsNullOrEmpty(__result))
                __result = data.Translate(ProfileId, Channel, __result);
        }

        private static string TranslateVisible(string source, EditableProfiles data)
        {
            if (string.IsNullOrEmpty(source) || data == null || !LocaleMode.IsKoreanMode()) return source;
            var exact = data.Translate(ProfileId, Channel, source);
            if (!string.Equals(exact, source, StringComparison.Ordinal)) return exact;

            // The patterns below are restricted to the output shapes found in the 0.1.1
            // mode menu, scoreboard and power-up HUD. Do not tokenize player names or IDs.
            string[] scoreTokens = { "<size=13>KILLS</size>", "<size=13>DEATHS</size>",
                "<size=13>ASSISTS</size>", "<size=13>PTS</size>", "ASSIST +" };
            if (source.Contains("<size=13>KILLS</size>"))
            {
                foreach (var token in scoreTokens) source = ReplaceToken(source, token, data);
                return source;
            }
            if (source.Contains("<color=#FF70BD>DOUBLE DAMAGE  ") ||
                source.Contains("<color=#70DFFF>SHIELD  ") ||
                source.Contains("<color=#80FF60>JUMP BOOTS  ") ||
                source.Contains("<color=#FF8C40>INFINITE AMMO  "))
            {
                foreach (var token in new[] { "<color=#FF70BD>DOUBLE DAMAGE  ",
                    "<color=#70DFFF>SHIELD  ", "<color=#80FF60>JUMP BOOTS  ",
                    "<color=#FF8C40>INFINITE AMMO  " }) source = ReplaceToken(source, token, data);
                return source;
            }
            if (source.StartsWith("ASSIST +", StringComparison.Ordinal))
                return ReplaceToken(source, "ASSIST +", data);
            if (source.StartsWith("Streak ", StringComparison.Ordinal))
                return ReplaceToken(ReplaceToken(source, "Streak ", data), " (Best ", data);
            if (source.EndsWith("  (YOU)", StringComparison.Ordinal))
                return source.Substring(0, source.Length - "  (YOU)".Length) +
                    data.Translate(ProfileId, Channel, "  (YOU)");
            if (source.StartsWith("Tier ", StringComparison.Ordinal))
                return ReplaceToken(ReplaceToken(source, "Tier ", data), "x complete", data);
            if (source.StartsWith("Round ", StringComparison.Ordinal))
            {
                source = ReplaceToken(source, "Round ", data);
                foreach (var phrase in new[] { " — waiting for your kit", " — fight! (",
                    " — buy phase. ", " — rebuilding both teams", " — assembling ",
                    " won  (+", " lost  (+", " — next in ", "   next round in ",
                    " | Free-for-all", " | All vs player", " kills", " — equipping player" })
                    source = ReplaceToken(source, phrase, data);
                return source;
            }
            if (source.Length >= 8 && char.IsDigit(source[0]) && char.IsDigit(source[1]) &&
                source[2] == ':' && source.Contains("   Round "))
                return ReplaceToken(source, "   Round ", data);
            if (source.StartsWith("WEAPON LEFT — ", StringComparison.Ordinal) ||
                source.Contains("<size=14>WEAPON LEFT — "))
            {
                source = ReplaceToken(source, "WEAPON LEFT — ", data);
                source = ReplaceToken(source, "; approach again", data);
                return ReplaceToken(source, "transfer unavailable", data);
            }
            foreach (var prefix in new[] { "ROUNDS: ", "KILLS / ROUND: ", "TIME / ROUND: ",
                "OPPONENTS: ", "TEAM KILLS: ", "TIME LIMIT: ", "Rival ",
                "Team pts ", "Opposition ", "TACTICAL / ", "ZOMBIES / " })
            {
                if (!source.StartsWith(prefix, StringComparison.Ordinal)) continue;
                source = ReplaceToken(source, prefix, data);
                if (prefix == "OPPONENTS: ")
                    foreach (var value in new[] { "Free-for-all", "All vs player" })
                        source = ReplaceToken(source, value, data);
                if (prefix == "ROUNDS: " || prefix == "KILLS / ROUND: " || prefix == "TIME / ROUND: ")
                    foreach (var value in new[] { "Endless", "Off" })
                        source = ReplaceToken(source, value, data);
                return source;
            }
            foreach (var suffix in new[] { " - you included", " - opposing team starts",
                " - fixed teams", " - configured teams" })
            {
                if (!source.EndsWith(suffix, StringComparison.Ordinal)) continue;
                return source.Substring(0, source.Length - suffix.Length) +
                    data.Translate(ProfileId, Channel, suffix);
            }
            // Arena HUD/notice text is composed by each gameplay mode. Do NOT
            // patch mode state or mode IDs: these conditions match only the
            // observed final on-screen sentence shapes.
            if (source.StartsWith("PREPARING DEATHMATCH — ", StringComparison.Ordinal))
                return ReplaceToken(source, "PREPARING DEATHMATCH — ", data);
            if (source.StartsWith("MATCH STARTS IN ", StringComparison.Ordinal))
                return ReplaceToken(source, "MATCH STARTS IN ", data);
            if (source.StartsWith("START IN ", StringComparison.Ordinal))
                return ReplaceToken(source, "START IN ", data);
            // 2v2/5v5 HUD status lines retain faction names and colored scores.
            if (source.Length >= 5 && char.IsDigit(source[0]) && source.Contains("v") &&
                (source.Contains("   Round ") || source.Contains("<color=#7FDBFF>")))
            {
                foreach (var phrase in new[] { "   Round ", "   Allies ", "   Enemies ",
                    "   YOU ARE DOWN", "   assembling opponents ",
                    "   assembling teammates ", "   next round in ",
                    "   raid ending in ", "   rebuilding teams (",
                    " | ASSEMBLING TEAMS", " | START IN ", " | First to ",
                    " | No kill limit", " | spawn delayed", " | No time limit" })
                    source = ReplaceToken(source, phrase, data);
                return source;
            }
            if (source.StartsWith("TEAM DM stopped — ", StringComparison.Ordinal))
                return ReplaceToken(source, "TEAM DM stopped — ", data);
            if (source.StartsWith("PREPARING ARENA AREA — ", StringComparison.Ordinal))
                return ReplaceToken(source, "PREPARING ARENA AREA — ", data);
            if (source.StartsWith("ROUND ", StringComparison.Ordinal) && source.Contains("  |  "))
            {
                source = ReplaceToken(source, "ROUND ", data);
                foreach (var phrase in new[] { " opponents arrived — waiting for combat readiness",
                    " teammates", "  |  Rebuilding teams" })
                    source = ReplaceToken(source, phrase, data);
                return source;
            }
            if (source.StartsWith("VICTORY — ", StringComparison.Ordinal) ||
                source.StartsWith("DEFEAT — ", StringComparison.Ordinal) ||
                source.StartsWith("DRAW — ", StringComparison.Ordinal) ||
                source.StartsWith("NO CONTEST — ", StringComparison.Ordinal) ||
                source.StartsWith("MATCH COMPLETE — ", StringComparison.Ordinal))
            {
                foreach (var prefix in new[] { "VICTORY", "DEFEAT", "DRAW", "NO CONTEST", "MATCH COMPLETE" })
                    if (source.StartsWith(prefix + " — ", StringComparison.Ordinal))
                        source = ReplaceToken(source, prefix, data);
                foreach (var phrase in new[] { " wins — ", " rounds won / ", " lost — ",
                    " | Finishing player replacement…", " | Returning to menu in " })
                    source = ReplaceToken(source, phrase, data);
                return source;
            }
            if (source.StartsWith("Returning to menu in ", StringComparison.Ordinal))
                return ReplaceToken(source, "Returning to menu in ", data);
            if (source.StartsWith("Wave ", StringComparison.Ordinal) &&
                source.EndsWith(" zombie kills", StringComparison.Ordinal))
                return ReplaceToken(ReplaceToken(source, "Wave ", data), " zombie kills", data);
            if (source.StartsWith("WAVE ", StringComparison.Ordinal))
                return ReplaceToken(source, "WAVE ", data);
            if (source.StartsWith("Gun Game — ", StringComparison.Ordinal))
                return ReplaceToken(source, "Gun Game — ", data);
            if (source.StartsWith("Continuous Deathmatch", StringComparison.Ordinal))
                return ReplaceToken(ReplaceToken(source, "Continuous Deathmatch", data),
                    " | Free-for-all", data);
            // Audited output templates: translate only fixed text fragments,
            // preserving numeric values, markup and player/kit names unchanged.
            if (source.StartsWith("Kills  ", StringComparison.Ordinal) &&
                source.Contains("     Match points  "))
                return ReplaceToken(ReplaceToken(source, "Kills  ", data), "     Match points  ", data);
            if (source.StartsWith("Rounds  ", StringComparison.Ordinal) &&
                source.Contains("     Match points  "))
            {
                source = ReplaceToken(source, "Rounds  ", data);
                source = ReplaceToken(source, "     Match points  ", data);
                return ReplaceToken(source, "     Kills  ", data);
            }
            if (source.StartsWith("Earlier opponent bodies: ", StringComparison.Ordinal) &&
                source.EndsWith(" kills (included in match total)", StringComparison.Ordinal))
            {
                source = ReplaceToken(source, "Earlier opponent bodies: ", data);
                source = ReplaceToken(source, " points, ", data);
                return ReplaceToken(source, " kills (included in match total)", data);
            }
            if (source.EndsWith(" additional current participant(s) hidden", StringComparison.Ordinal))
                return ReplaceToken(source, " additional current participant(s) hidden", data);
            if (source.EndsWith(" lower or completed bot run(s) not shown", StringComparison.Ordinal))
                return ReplaceToken(source, " lower or completed bot run(s) not shown", data);
            if (source.StartsWith("Buying: ", StringComparison.Ordinal))
                return ReplaceToken(source, "Buying: ", data);
            if (source.StartsWith("starting in ", StringComparison.Ordinal))
                return ReplaceToken(source, "starting in ", data);
            if (source.EndsWith("  (short)", StringComparison.Ordinal))
                return ReplaceToken(source, "  (short)", data);
            if (source.EndsWith("\nLOADING", StringComparison.Ordinal) ||
                source.EndsWith("\nCOLLECTING", StringComparison.Ordinal))
            {
                source = ReplaceToken(source, "\nLOADING", data);
                return ReplaceToken(source, "\nCOLLECTING", data);
            }
            if ((source.StartsWith("SPECTATING ALLY: ", StringComparison.Ordinal) ||
                 source.StartsWith("SPECTATING OPPONENT: ", StringComparison.Ordinal)) &&
                source.EndsWith("    LMB/RMB switch", StringComparison.Ordinal))
            {
                source = ReplaceToken(source, "SPECTATING ALLY: ", data);
                source = ReplaceToken(source, "SPECTATING OPPONENT: ", data);
                source = ReplaceToken(source, "\nHP unavailable", data);
                source = ReplaceToken(source, "\nHP ", data);
                return ReplaceToken(source, "    LMB/RMB switch", data);
            }
            if (source.EndsWith(". Draft only; no live pickup spawned.", StringComparison.Ordinal))
                return ReplaceToken(source, ". Draft only; no live pickup spawned.", data);
            if (source.StartsWith("CUSTOMS — ", StringComparison.Ordinal))
                return ReplaceToken(source, "CUSTOMS — ", data);
            if (source.StartsWith("AREA OUTLINE DRAFT — ", StringComparison.Ordinal))
            {
                // Do not translate coordinate data, map ID, hotkeys or status.
                source = ReplaceToken(source, "AREA OUTLINE DRAFT — ", data);
                source = ReplaceToken(source, " points\n", data);
                source = ReplaceToken(source, ": aim point  |  ", data);
                source = ReplaceToken(source, ": feet\n", data);
                source = ReplaceToken(source, ": undo  |  ", data);
                source = ReplaceToken(source, ": export\n", data);
                source = ReplaceToken(source, "Floor/ceiling: ", data);
                source = ReplaceToken(source, "; spawn feet: ", data);
                source = ReplaceToken(source, "\nPreview only; visible through walls. Spawn display: Waypoints.", data);
                return ReplaceToken(source, "\nNormal PMC raid: ordinary gear/profile consequences still apply.", data);
            }
            if (source.StartsWith("PICKUP DRAFT — ", StringComparison.Ordinal))
            {
                source = ReplaceToken(source, "PICKUP DRAFT — ", data);
                source = ReplaceToken(source, ": aim   ", data);
                source = ReplaceToken(source, ": feet\n", data);
                source = ReplaceToken(source, ": undo   ", data);
                source = ReplaceToken(source, ": export\n", data);
                return source;
            }
            return source;
        }

        private static string ReplaceToken(string source, string token, EditableProfiles data)
        {
            if (!source.Contains(token)) return source;
            var translated = data.Translate(ProfileId, Channel, token);
            return string.Equals(translated, token, StringComparison.Ordinal)
                ? source : source.Replace(token, translated);
        }
    }
}
