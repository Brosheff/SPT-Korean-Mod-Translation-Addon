using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // SWAG + Donuts 5.0.1 / 4.0.4. Translate only the client-owned F9
    // presentation paths. The original Setting<T>, Options, serialized values,
    // scenario names and AI type IDs are never assigned localized text.
    internal static class SwagDonutsUiHooks
    {
        private const string Profile = "com.dvize.Donuts.UI";
        private const string Channel = "small_ui";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Installed = new HashSet<Assembly>();
        private static EditableProfiles profiles;
        private static Harmony harmony;
        private static bool enabled;
        private static readonly MethodInfo TranslateMethod = typeof(SwagDonutsUiHooks).GetMethod(
            nameof(Display), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo ContentMethod = typeof(SwagDonutsUiHooks).GetMethod(
            nameof(Content), BindingFlags.NonPublic | BindingFlags.Static);

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (patcher == null || data == null || !data.HasRules(Profile, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                harmony = patcher;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            var count = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) count += PatchAssembly(assembly);
            return count;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                harmony = null;
                profiles = null;
                Installed.Clear();
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { PatchAssembly(args.LoadedAssembly); }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("swag-donuts-late-ui", () =>
                    "Donuts UI localization skipped after assembly load: " + ex.Message);
            }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly == null || !string.Equals(assembly.GetName().Name, "Donuts", StringComparison.Ordinal)) return 0;
            lock (Gate)
            {
                if (!enabled || !Installed.Add(assembly)) return 0;
            }
            var count = 0;
            // Patch only Donuts-owned presentation methods. No global GUI or string APIs.
            var toolkit = assembly.GetType("Donuts.PluginGUI.ImGUIToolkit", false);
            if (toolkit != null)
            {
                foreach (var method in toolkit.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (!new[] { "Dropdown", "Slider", "TextField", "Toggle", "Button", "Accordion", "KeybindField" }.Contains(method.Name)) continue;
                    // The generic Dropdown<T> is compiled as a shared generic body.
                    // Patch the string instantiation used by the source (scenario, difficulty, AI type).
                    if (method.IsGenericMethodDefinition)
                    {
                        try { count += PatchDraw(method.MakeGenericMethod(typeof(string))); }
                        catch (Exception ex) { Warn(method.Name, ex); }
                    }
                    else count += PatchDraw(method);
                }
            }
            foreach (var type in SafeTypes(assembly))
            {
                if (type.FullName == null || !type.FullName.StartsWith("Donuts.PluginGUI.Pages.", StringComparison.Ordinal)) continue;
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters) continue;
                    if (method.Name == "get_Name" && method.ReturnType == typeof(string))
                    {
                        try
                        {
                            harmony.Patch(method, postfix: new HarmonyMethod(typeof(SwagDonutsUiHooks), nameof(AfterPageName)));
                            count++;
                        }
                        catch (Exception ex) { Warn(method.Name, ex); }
                    }
                    else if (method.Name == "Draw" || method.Name == "DrawFooter" || method.Name == "DrawTabButtons")
                        count += PatchDraw(method);
                }
            }
            var helper = assembly.GetType("Donuts.Utils.DonutsHelper", false);
            if (helper != null)
            {
                foreach (var name in new[] { "NotifyModSettingsStatus", "DisplayNotification", "NotifyLogError" })
                {
                    var method = helper.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == name && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(string));
                    if (method == null) continue;
                    try { harmony.Patch(method, prefix: new HarmonyMethod(typeof(SwagDonutsUiHooks), nameof(BeforeNotification))); count++; }
                    catch (Exception ex) { Warn(name, ex); }
                }
            }
            // Separate dependency-checker UI, still restricted to the Donuts assembly.
            var checker = assembly.GetType("Donuts.Tools.DependencyCheckerPlugin", false);
            if (checker != null)
            {
                var generator = checker.GetMethod("GenerateErrorMessage", BindingFlags.NonPublic | BindingFlags.Static);
                if (generator != null && generator.ReturnType == typeof(string))
                {
                    try { harmony.Patch(generator, postfix: new HarmonyMethod(typeof(SwagDonutsUiHooks), nameof(AfterDependencyError))); count++; }
                    catch (Exception ex) { Warn(generator.Name, ex); }
                }
                var drawer = checker.GetMethod("ErrorLabelDrawer", BindingFlags.NonPublic | BindingFlags.Static);
                if (drawer != null) count += PatchDraw(drawer);
            }
            return count;
        }

        private static Type[] SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
        }

        private static int PatchDraw(MethodInfo method)
        {
            try
            {
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(SwagDonutsUiHooks), nameof(DrawTranspiler)));
                return 1;
            }
            catch (Exception ex) { Warn(method.Name, ex); return 0; }
        }

        private static void Warn(string method, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("swag-donuts-ui-" + method, () =>
                "Donuts display localization skipped for " + method + ": " + ex.Message);
        }

        private static void AfterPageName(ref string __result) { __result = Display(__result); }
        private static void BeforeNotification(ref string __0)
        {
            if (!enabled || !LocaleMode.IsKoreanMode() || string.IsNullOrEmpty(__0)) return;
            try
            {
                var translated = profiles?.Translate(Profile, "notifications", __0);
                __0 = Display(translated ?? __0);
            }
            catch { /* Leave the original message intact on an unknown upstream version. */ }
        }

        private static void AfterDependencyError(ref string __result)
        {
            if (!enabled || !LocaleMode.IsKoreanMode() || string.IsNullOrEmpty(__result)) return;
            try { __result = profiles?.Translate(Profile, "dependency_errors", __result) ?? __result; }
            catch { /* Do not interfere with prerequisite errors. */ }
        }

        private static string Display(string source)
        {
            if (!enabled || !LocaleMode.IsKoreanMode() || string.IsNullOrEmpty(source)) return source;
            try
            {
                // Setting<T>.ToolTipText uses StringBuilder.AppendLine, which is
                // CRLF on Windows; profiles store canonical LF text.
                var normalized = source.Replace("\r\n", "\n");
                var value = profiles?.Translate(Profile, Channel, normalized) ?? source;
                if (string.Equals(value, normalized, StringComparison.Ordinal))
                    value = profiles?.Translate(Profile, "dependency_errors", normalized) ?? value;
                return value;
            }
            catch { return source; }
        }

        private static GUIContent Content(string source, string tooltip)
        {
            return new GUIContent(Display(source), Display(tooltip));
        }

        private static IEnumerable<CodeInstruction> DrawTranspiler(IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            // Only presentation instructions are changed. Do NOT translate all
            // ldstr instructions: some names are used as serialized identifiers.
            var sequence = instructions.ToList();
            var ctor = typeof(GUIContent).GetConstructor(new[] { typeof(string), typeof(string) });
            var methodName = __originalMethod?.Name ?? string.Empty;
            var typeName = __originalMethod?.DeclaringType?.FullName ?? string.Empty;
            for (var i = 0; i < sequence.Count; i++)
            {
                var instruction = sequence[i];
                if (ctor != null && instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, ctor))
                {
                    var copy = new CodeInstruction(OpCodes.Call, ContentMethod);
                    copy.labels.AddRange(instruction.labels);
                    copy.blocks.AddRange(instruction.blocks);
                    yield return copy;
                    continue;
                }
                yield return instruction;
                // Only literal-only GUILayout.Label calls for known loading text,
                // plus the explicitly audited key binding Clear button.
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string caption &&
                    ((typeName == "Donuts.PluginGUI.Pages.MainSettingsGeneralPage" &&
                      methodName == "Draw" &&
                      (caption == "Loading PMC scenarios..." || caption == "Loading SCAV scenarios...")) ||
                     (typeName == "Donuts.PluginGUI.ImGUIToolkit" && methodName == "KeybindField" &&
                      caption == "Clear")))
                {
                    yield return new CodeInstruction(OpCodes.Call, TranslateMethod);
                }
                // Setting<T> getters are read strictly on F9 GUI render methods;
                // the resulting labels are translated, not their backing fields.
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    instruction.operand is MethodInfo getter && getter.ReturnType == typeof(string) &&
                    (getter.Name == "get_Name" || getter.Name == "get_ToolTipText" ||
                     getter.Name == "get_Description") &&
                    getter.DeclaringType != null && getter.DeclaringType.FullName != null &&
                    (getter.DeclaringType.FullName.StartsWith("Donuts.Models.Setting", StringComparison.Ordinal) ||
                     getter.DeclaringType.FullName.StartsWith("Donuts.PluginGUI.Pages.", StringComparison.Ordinal) ||
                     (getter.Name == "get_Description" &&
                      getter.DeclaringType.FullName == "BepInEx.Configuration.ConfigDescription")))
                    yield return new CodeInstruction(OpCodes.Call, TranslateMethod);
            }
        }
    }
}
