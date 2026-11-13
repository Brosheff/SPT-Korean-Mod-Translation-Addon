using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TMPro;

namespace SPT.ModKoreanAddon
{
    // BTR/Lightkeeper Skipper 1.1.0: display-only strings in the Character -> Tasks UI.
    // Its quest buttons use stable GameObject names for visibility/control, while the
    // confirmation dialogs are compiled into nested click-action lambdas. Never alter
    // the internal names, the action listeners, quest progress or trader rewards.
    internal static class BtrLightkeeperSkipperUiHooks
    {
        private const string AssemblyName = "Dark-BTRLightkeeperSkipper";
        private const string PluginType = "Dark.BTRLightkeeperSkipper.Plugin";
        private const string ServiceType = "Dark.BTRLightkeeperSkipper.QuestSkipService";
        private const string SkipOwner = "Dark.BTRLightkeeperSkipper.Patches.QuestObjectiveViewPatch";
        private const string FinishOwner = "Dark.BTRLightkeeperSkipper.Patches.NotesTaskDescriptionPatch";
        private const string Profile = "com.dark.btrlightkeeperskipper";
        private const string Channel = "small_ui";
        private const string Version = "1.1.0";
        private const string SkipObject = "BTRLightkeeperSkipper_SkipButton";
        private const string FinishObject = "BTRLightkeeperSkipper_FinishButton";
        private const string SkipConfirm = "Are you sure you want to autocomplete this quest objective?";
        private const string FinishConfirm = "Are you sure you want to finish this quest and claim rewards via trader message?";
        private const string Caption = "Confirmation";
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private sealed class ButtonLabel
        {
            internal WeakReference<TMP_Text> Text;
            internal string Source;
            internal string Applied;
        }

        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static readonly List<ButtonLabel> Buttons = new List<ButtonLabel>();
        private static Harmony owner;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null))
            .GroupBy(op => unchecked((ushort)op.Value))
            .ToDictionary(group => group.Key, group => group.First());

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            if (data == null || !data.HasRules(Profile, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                owner = patcher;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }

            var count = 0;
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
                foreach (var entry in Buttons)
                {
                    if (!entry.Text.TryGetTarget(out var text) || text == null) continue;
                    try { if (text.text == entry.Applied) text.text = entry.Source; }
                    catch { /* The original button may already have been destroyed. */ }
                }
                Buttons.Clear();
                Patched.Clear();
                owner = null;
                profiles = null;
            }
        }

        internal static void RefreshLanguage()
        {
            lock (Gate)
            {
                if (!enabled) return;
                for (var i = Buttons.Count - 1; i >= 0; i--)
                {
                    var entry = Buttons[i];
                    if (!entry.Text.TryGetTarget(out var label) || label == null)
                    {
                        Buttons.RemoveAt(i);
                        continue;
                    }
                    if (label.text != entry.Applied && label.text != entry.Source) continue;
                    try
                    {
                        var translated = Translate(entry.Source);
                        label.text = translated;
                        entry.Applied = translated;
                    }
                    catch (Exception ex) { Warn("refresh", ex); }
                }
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatch(args.LoadedAssembly); }
            catch (Exception ex) { Warn("late-assembly", ex); }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (assembly == null || !string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.Ordinal))
                return 0;
            lock (Gate)
            {
                if (!enabled || Patched.Contains(assembly)) return 0;
            }

            var plugin = assembly.GetType(PluginType, false);
            var guid = plugin?.GetField("PluginGuid", All);
            var version = plugin?.GetField("PluginVersion", All);
            if (guid == null || !guid.IsLiteral || version == null || !version.IsLiteral ||
                !string.Equals(guid.GetRawConstantValue() as string, Profile, StringComparison.Ordinal) ||
                !string.Equals(version.GetRawConstantValue() as string, Version, StringComparison.Ordinal)) return 0;

            var service = assembly.GetType(ServiceType, false);
            var create = service?.GetMethods(All).Where(m => m.Name == "CreateButton" && m.IsStatic).ToArray();
            if (create == null || create.Length != 1 || !HasButtonSignature(create[0])) return 0;

            var skip = LocateConfirmation(assembly.GetType(SkipOwner, false), "<PatchPostfix>", SkipConfirm);
            var finish = LocateConfirmation(assembly.GetType(FinishOwner, false), "<TryInjectFinishButton>", FinishConfirm);

            lock (Gate)
            {
                if (!enabled || owner == null || Patched.Contains(assembly)) return 0;
                // Patch independent labels even if compiler-generated dialog lambdas
                // cannot be resolved; keep any unverified dialogs in the source language.
                owner.Patch(create[0],
                    prefix: new HarmonyMethod(typeof(BtrLightkeeperSkipperUiHooks), nameof(BeforeCreateButton)),
                    postfix: new HarmonyMethod(typeof(BtrLightkeeperSkipperUiHooks), nameof(AfterCreateButton)));
                var count = 1;
                if (skip != null)
                {
                    owner.Patch(skip, transpiler: new HarmonyMethod(typeof(BtrLightkeeperSkipperUiHooks), nameof(DialogTranspiler)));
                    count++;
                }
                else
                    SPT.EditableTranslations.MinimalLog.WarnOnce("btr-skipper-skip-dialog-target", () =>
                        "BTR Skipper 1.1.0 Skip confirmation lambda missing/ambiguous; source message retained.");
                if (finish != null)
                {
                    owner.Patch(finish, transpiler: new HarmonyMethod(typeof(BtrLightkeeperSkipperUiHooks), nameof(DialogTranspiler)));
                    count++;
                }
                else
                    SPT.EditableTranslations.MinimalLog.WarnOnce("btr-skipper-finish-dialog-target", () =>
                        "BTR Skipper 1.1.0 Finish confirmation lambda missing/ambiguous; source message retained.");
                Patched.Add(assembly);
                return count;
            }
        }

        private static bool HasButtonSignature(MethodInfo method)
        {
            var args = method.GetParameters();
            return args.Length == 5 && args[2].ParameterType == typeof(string) &&
                args[3].ParameterType == typeof(string) && args[4].ParameterType == typeof(float) &&
                method.ReturnType.Name == "DefaultUIButton";
        }

        // Discover only a compiler-generated click lambda owned by the audited patch
        // class; don't patch ItemUiContext.ShowMessageWindow or every nested method.
        private static MethodInfo LocateConfirmation(Type ownerType, string generatedPrefix, string text)
        {
            if (ownerType == null) return null;
            var matches = new List<MethodInfo>();
            foreach (var nested in AllNestedTypes(ownerType))
                foreach (var method in nested.GetMethods(All | BindingFlags.DeclaredOnly))
                    if (method.Name.StartsWith(generatedPrefix, StringComparison.Ordinal) &&
                        ContainsUiLiterals(method, text, Caption))
                        matches.Add(method);
            return matches.Count == 1 ? matches[0] : null;
        }

        private static IEnumerable<Type> AllNestedTypes(Type type)
        {
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                yield return nested;
                foreach (var child in AllNestedTypes(nested)) yield return child;
            }
        }

        // Decode IL operand sizes before inspecting actual ldstr tokens. Searching raw
        // byte sequences would misidentify metadata token bytes as strings.
        private static bool ContainsUiLiterals(MethodBase method, string first, string second)
        {
            try
            {
                var bytes = method.GetMethodBody()?.GetILAsByteArray();
                if (bytes == null) return false;
                var foundFirst = false;
                var foundSecond = false;
                for (var pos = 0; pos < bytes.Length;)
                {
                    ushort opcode = bytes[pos++];
                    if (opcode == 0xfe) opcode = (ushort)(0xfe00 | bytes[pos++]);
                    if (!Opcodes.TryGetValue(opcode, out var op)) return false;
                    var size = 0;
                    switch (op.OperandType)
                    {
                        case OperandType.InlineNone: break;
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar: size = 1; break;
                        case OperandType.InlineVar: size = 2; break;
                        case OperandType.InlineBrTarget:
                        case OperandType.InlineField:
                        case OperandType.InlineI:
                        case OperandType.InlineMethod:
                        case OperandType.InlineSig:
                        case OperandType.InlineString:
                        case OperandType.InlineTok:
                        case OperandType.InlineType:
                        case OperandType.ShortInlineR: size = 4; break;
                        case OperandType.InlineI8:
                        case OperandType.InlineR: size = 8; break;
                        case OperandType.InlineSwitch:
                            if (pos + 4 > bytes.Length) return false;
                            size = checked(4 + 4 * BitConverter.ToInt32(bytes, pos));
                            break;
                        default: return false;
                    }
                    if (pos + size > bytes.Length) return false;
                    if (op == OpCodes.Ldstr)
                    {
                        var source = method.Module.ResolveString(BitConverter.ToInt32(bytes, pos));
                        if (source == first) foundFirst = true;
                        if (source == second) foundSecond = true;
                    }
                    pos += size;
                }
                return foundFirst && foundSecond;
            }
            catch { return false; }
        }

        private static void BeforeCreateButton(string __2, ref string __3)
        {
            if ((__2 == SkipObject && __3 == "SKIP") || (__2 == FinishObject && __3 == "FINISH"))
                __3 = Translate(__3);
        }

        private static void AfterCreateButton(object __result, string __2)
        {
            var source = __2 == SkipObject ? "SKIP" : __2 == FinishObject ? "FINISH" : null;
            if (source == null || __result == null) return;
            try
            {
                TMP_Text label = null;
                for (var t = __result.GetType(); t != null && label == null; t = t.BaseType)
                    label = t.GetField("_headerLabel", All)?.GetValue(__result) as TMP_Text;
                if (label == null) return;
                lock (Gate)
                {
                    if (!enabled) return;
                    Buttons.Add(new ButtonLabel { Text = new WeakReference<TMP_Text>(label), Source = source, Applied = Translate(source) });
                    if (Buttons.Count > 128) Buttons.RemoveAll(entry => !entry.Text.TryGetTarget(out var text) || text == null);
                }
            }
            catch (Exception ex) { Warn("button-header", ex); }
        }

        private static IEnumerable<CodeInstruction> DialogTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var translate = AccessTools.Method(typeof(BtrLightkeeperSkipperUiHooks), nameof(TranslateDialogLiteral));
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string text &&
                    (text == SkipConfirm || text == FinishConfirm || text == Caption))
                    yield return new CodeInstruction(OpCodes.Call, translate);
            }
        }

        private static string TranslateDialogLiteral(string original) => Translate(original);

        private static string Translate(string source)
        {
            if (!enabled || !LocaleMode.IsKoreanMode() || profiles == null) return source;
            try { return profiles.Translate(Profile, Channel, source); }
            catch (Exception ex) { Warn("literal", ex); return source; }
        }

        private static void Warn(string part, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("btr-lightkeeper-skipper-" + part,
                () => "BTR/Lightkeeper Skipper optional display hook: " + ex.Message);
        }
    }
}
