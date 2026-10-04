using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Generic scaffold for the common grey BepInEx Configuration Manager boxes.
    // It patches ConfigurationManager only, not every mod and not Unity GUI globally.
    internal static class ConfigurationManagerHooks
    {
        private const string ManagerTypeName = "ConfigurationManager.ConfigurationManager";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> PatchedAssemblies = new HashSet<Assembly>();
        private static ConditionalWeakTable<object, OriginalDisplayState> originals = new ConditionalWeakTable<object, OriginalDisplayState>();
        private static WeakReference lastManager;
        private static Harmony harmony;
        private static ConfigManagerProfiles profiles;
        private static bool enabled;

        // Value/dropdown translation must know which setting owns the value being rendered.
        // ConfigurationManager's ObjectToGuiContent only receives the raw value, so keep a
        // thread-local display context while its combo/flags drawer is running.
        [ThreadStatic] private static ConfigUiSource activeValueSource;

        // Strings owned by the SPT/AKI Configuration Manager itself. These are not mod ConfigEntry
        // names, so profile JSON cannot reach them. Patch only the Configuration Manager assembly
        // and resolve the text at draw time so English / kr / kr-en can switch without rewriting cfg data.
        private static readonly Dictionary<string, string> ManagerUiKorean = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Current ConfigurationManager UI (the SPT build shown in-game).
            ["Plugin / mod settings"] = "플러그인 / 모드 설정",
            ["Tip: Click plugin names to expand. Click setting and group names to see their descriptions."] = "팁: 플러그인 이름을 클릭하면 펼칠 수 있습니다. 설정이나 그룹 이름을 클릭하면 설명을 볼 수 있습니다.",
            ["Tip: You can drag this window to move it. It will stay open while you interact with the game."] = "팁: 이 창을 드래그해 옮길 수 있습니다. 창을 옮긴 뒤에는 게임을 조작하는 동안에도 열린 상태로 유지됩니다.",
            ["Normal settings"] = "일반 설정",
            ["Keyboard shortcuts"] = "단축키",
            ["Advanced settings"] = "고급 설정",
            ["Debug info"] = "디버그 정보",
            ["Open Log"] = "로그 열기",
            ["Close"] = "닫기",
            ["Search: "] = "검색: ",
            ["Clear"] = "지우기",
            ["Expand All"] = "모두 펼치기",
            ["Collapse All"] = "모두 접기",
            ["URL"] = "링크",
            ["Plugins with no options available: "] = "설정 항목이 없는 플러그인: ",
            ["Failed to draw this field, check log for details."] = "이 설정을 표시하지 못했습니다. 자세한 내용은 로그를 확인하세요.",
            ["Reset"] = "초기화",

            // Older SPT/AKI ConfigurationManager variants. Keeping these scoped to the
            // ConfigurationManager assembly is harmless and avoids regressions on installs
            // that ship a slightly older manager build.
            ["Expand"] = "펼치기",
            ["Collapse"] = "접기",
            ["Show: "] = "표시: ",
            ["Debug mode"] = "디버그 모드",
            ["Log"] = "로그",
            ["Search settings: "] = "설정 검색: ",
        };

        private static readonly Dictionary<string, string> FieldUiKorean = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Enabled"] = "활성화",
            ["Disabled"] = "비활성화",
            ["Press any key"] = "아무 키나 입력하세요",
            ["Press any key combination"] = "키 조합을 입력하세요",
            ["Cancel"] = "취소",
            ["Clear"] = "해제",
            ["Set..."] = "지정...",
            ["Set the key by pressing any key on your keyboard."] = "키보드에서 아무 키나 눌러 키를 지정합니다.",
        };

        private static readonly HashSet<string> ManagerLiteralMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "OnGUI",
            "SettingsWindow",
            "DrawTips",
            "DrawWindowHeader",
            "BuildFilteredSettingList",
            "DrawSingleSetting",
            "DrawSinglePlugin",
            "DrawDefaultButton",
        };

        private static readonly HashSet<string> FieldLiteralMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "DrawBoolField",
            "DrawKeyCode",
            "DrawKeyboardShortcut",
            "DrawKeyboardShortcutObsolete",
        };

        private static readonly MethodInfo ManagerLiteralRuntime = typeof(ConfigurationManagerHooks).GetMethod(
            nameof(TranslateManagerLiteral), BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo FieldLiteralRuntime = typeof(ConfigurationManagerHooks).GetMethod(
            nameof(TranslateFieldLiteral), BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly HarmonyMethod ManagerLiteralTranspiler = new HarmonyMethod(typeof(ConfigurationManagerHooks).GetMethod(
            nameof(TranslateManagerLiterals), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        private static readonly HarmonyMethod FieldLiteralTranspiler = new HarmonyMethod(typeof(ConfigurationManagerHooks).GetMethod(
            nameof(TranslateFieldLiterals), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        private static readonly HarmonyMethod ValueDrawerPrefix = new HarmonyMethod(typeof(ConfigurationManagerHooks).GetMethod(
            nameof(BeforeValueDrawer), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.First };
        private static readonly HarmonyMethod ValueDrawerPostfix = new HarmonyMethod(typeof(ConfigurationManagerHooks).GetMethod(
            nameof(AfterValueDrawer), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        private static readonly HarmonyMethod ValueDrawerFinalizer = new HarmonyMethod(typeof(ConfigurationManagerHooks).GetMethod(
            nameof(FinalizeValueDrawer), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        private static readonly HarmonyMethod ValueContentPostfix = new HarmonyMethod(typeof(ConfigurationManagerHooks).GetMethod(
            nameof(AfterObjectToGuiContent), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        private static readonly HarmonyMethod FlagsValueTranspiler = new HarmonyMethod(typeof(ConfigurationManagerHooks).GetMethod(
            nameof(TranslateFlagsValues), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        private static readonly MethodInfo ActiveValueRuntime = typeof(ConfigurationManagerHooks).GetMethod(
            nameof(TranslateActiveValueText), BindingFlags.Static | BindingFlags.NonPublic);

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> loadedAssemblies, ConfigManagerProfiles data)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            if (data == null) throw new ArgumentNullException(nameof(data));

            lock (Gate)
            {
                if (enabled) return 0;
                harmony = patcher;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }

            var count = 0;
            foreach (var assembly in loadedAssemblies ?? Enumerable.Empty<Assembly>())
                count += TryPatchAssembly(assembly);
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
                PatchedAssemblies.Clear();
                originals = new ConditionalWeakTable<object, OriginalDisplayState>();
                lastManager = null;
                activeValueSource = null;
            }
        }

        internal static void RefreshLanguage()
        {
            object manager = null;
            lock (Gate)
            {
                if (!enabled || lastManager == null) return;
                manager = lastManager.Target;
            }
            if (manager == null) return;

            try
            {
                var build = manager.GetType().GetMethod("BuildSettingList",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                build?.Invoke(manager, null);
                ClearFieldDrawerCache(manager.GetType().Assembly);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("ConfigurationManagerHooks:174", () => "[Config UI] Locale refresh skipped: " + ex.Message);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                var count = TryPatchAssembly(args.LoadedAssembly);
                if (count > 0) {}
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("ConfigurationManagerHooks:187", () => "[Config UI] Late-load hook skipped: " + ex.Message);
            }
        }

        private static int TryPatchAssembly(Assembly assembly)
        {
            if (assembly == null) return 0;
            Type managerType;
            try { managerType = assembly.GetType(ManagerTypeName, false); }
            catch { return 0; }
            if (managerType == null) return 0;

            lock (Gate)
            {
                if (!enabled || harmony == null || profiles == null || !PatchedAssemblies.Add(assembly)) return 0;
            }

            try
            {
                var count = 0;
                var build = managerType.GetMethod("BuildSettingList",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (build == null) return 0;
                var postfix = new HarmonyMethod(typeof(ConfigurationManagerHooks).GetMethod(
                    nameof(AfterBuildSettingList), BindingFlags.Static | BindingFlags.NonPublic))
                {
                    priority = Priority.Last,
                };
                harmony.Patch(build, postfix: postfix);
                count++;

                // The grey Configuration Manager shell owns strings such as Reset/Clear/Enabled itself.
                // They are patched separately from per-mod profiles and translated at runtime.
                count += PatchManagerOwnedLiterals(assembly, managerType);
                return count;
            }
            catch
            {
                lock (Gate) PatchedAssemblies.Remove(assembly);
                throw;
            }
        }

        private static int PatchManagerOwnedLiterals(Assembly assembly, Type managerType)
        {
            var count = 0;
            const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            MethodInfo[] managerMethods;
            try { managerMethods = managerType.GetMethods(bindingFlags); }
            catch { managerMethods = Array.Empty<MethodInfo>(); }

            foreach (var method in managerMethods)
            {
                // Roslyn may move the local Reset-button function into a generated method whose
                // name starts with <DrawDefaultButton>. Include it without patching unrelated code.
                if (!ManagerLiteralMethods.Contains(method.Name) &&
                    method.Name.IndexOf("<DrawDefaultButton>", StringComparison.Ordinal) < 0) continue;
                if (TryPatchLiteralMethod(method, ManagerLiteralTranspiler)) count++;
            }

            Type fieldDrawer = null;
            try { fieldDrawer = assembly.GetType("ConfigurationManager.SettingFieldDrawer", false); }
            catch { }
            if (fieldDrawer != null)
            {
                MethodInfo[] fieldMethods;
                try { fieldMethods = fieldDrawer.GetMethods(bindingFlags); }
                catch { fieldMethods = Array.Empty<MethodInfo>(); }
                foreach (var method in fieldMethods)
                {
                    if (!FieldLiteralMethods.Contains(method.Name)) continue;
                    if (TryPatchLiteralMethod(method, FieldLiteralTranspiler)) count++;
                }

                // Enum and AcceptableValueList labels are display text too. Patch only the
                // ConfigurationManager renderer: the ConfigEntry value itself is never replaced.
                var combo = fieldMethods.FirstOrDefault(method => method.Name == "DrawComboboxField");
                if (TryPatchValueDrawer(combo, false)) count++;
                var flagsDrawer = fieldMethods.FirstOrDefault(method => method.Name == "DrawFlagsField");
                if (TryPatchValueDrawer(flagsDrawer, true)) count++;
                var objectToContent = fieldMethods.FirstOrDefault(method => method.Name == "ObjectToGuiContent");
                if (objectToContent != null)
                {
                    try
                    {
                        harmony.Patch(objectToContent, postfix: ValueContentPostfix);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        SPT.EditableTranslations.MinimalLog.WarnOnce("ConfigurationManagerHooks:279", () => "[Config UI] Value-content hook skipped: " + ex.Message);
                    }
                }
            }

            return count;
        }

        private static bool TryPatchValueDrawer(MethodInfo method, bool translateFlags)
        {
            if (method == null || method.IsAbstract || method.ContainsGenericParameters) return false;
            try
            {
                harmony.Patch(method,
                    prefix: ValueDrawerPrefix,
                    postfix: ValueDrawerPostfix,
                    transpiler: translateFlags ? FlagsValueTranspiler : null,
                    finalizer: ValueDrawerFinalizer,
                    ilmanipulator: null);
                return true;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("ConfigurationManagerHooks:302", () => "[Config UI] Value drawer hook skipped: " + method.Name + " - " + ex.Message);
                return false;
            }
        }

        private static void BeforeValueDrawer(object __0)
        {
            activeValueSource = null;
            if (__0 == null || profiles == null) return;
            try
            {
                OriginalDisplayState original;
                if (!originals.TryGetValue(__0, out original))
                    original = originals.GetValue(__0, CaptureOriginalDisplayState);
                activeValueSource = InspectSetting(__0, original);
            }
            catch { activeValueSource = null; }
        }

        private static void AfterValueDrawer()
        {
            activeValueSource = null;
        }

        private static Exception FinalizeValueDrawer(Exception __exception)
        {
            activeValueSource = null;
            return __exception;
        }

        private static void AfterObjectToGuiContent(object __0, GUIContent __result)
        {
            if (__result == null || activeValueSource == null || profiles == null || !LocaleMode.IsKoreanMode()) return;
            try
            {
                var sourceText = __result.text;
                var translated = profiles.ResolveValue(activeValueSource, __0, sourceText);
                if (!string.IsNullOrEmpty(translated)) __result.text = translated;
            }
            catch { }
        }

        private static IEnumerable<CodeInstruction> TranslateFlagsValues(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    instruction.operand is MethodInfo called && called.Name == "ToString" &&
                    called.ReturnType == typeof(string) && called.GetParameters().Length == 0)
                    yield return new CodeInstruction(OpCodes.Call, ActiveValueRuntime);
            }
        }

        private static string TranslateActiveValueText(string source)
        {
            if (source == null || activeValueSource == null || profiles == null || !LocaleMode.IsKoreanMode()) return source;
            try
            {
                var direct = profiles.ResolveValue(activeValueSource, source, source);
                if (!string.IsNullOrEmpty(direct)) return direct;

                // [Flags] enums are rendered by ConfigurationManager as "A, B, C". Profiles
                // store one rule per flag, so translate each member while preserving unknown
                // future flags instead of requiring every possible combination in JSON.
                if (source.IndexOf(",", StringComparison.Ordinal) >= 0)
                {
                    var parts = source.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var changed = false;
                    for (var i = 0; i < parts.Length; i++)
                    {
                        var raw = parts[i].Trim();
                        var translated = profiles.ResolveValue(activeValueSource, raw, raw);
                        if (!string.IsNullOrEmpty(translated) && !string.Equals(translated, raw, StringComparison.Ordinal))
                        {
                            parts[i] = translated;
                            changed = true;
                        }
                        else parts[i] = raw;
                    }
                    if (changed) return string.Join(", ", parts);
                }
                return source;
            }
            catch { return source; }
        }

        private static void ClearFieldDrawerCache(Assembly assembly)
        {
            try
            {
                var drawer = assembly?.GetType("ConfigurationManager.SettingFieldDrawer", false);
                var clear = drawer?.GetMethod("ClearCache", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                clear?.Invoke(null, null);
            }
            catch { }
        }

        private static bool TryPatchLiteralMethod(MethodInfo method, HarmonyMethod transpiler)
        {
            if (method == null || method.IsAbstract || method.ContainsGenericParameters) return false;
            try
            {
                harmony.Patch(method, transpiler: transpiler);
                return true;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("ConfigurationManagerHooks:411", () => "[Config UI] Manager literal hook skipped: " + method.Name + " - " + ex.Message);
                return false;
            }
        }

        private static IEnumerable<CodeInstruction> TranslateManagerLiterals(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string source &&
                    ManagerUiKorean.ContainsKey(source))
                    yield return new CodeInstruction(OpCodes.Call, ManagerLiteralRuntime);
            }
        }

        private static IEnumerable<CodeInstruction> TranslateFieldLiterals(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string source &&
                    FieldUiKorean.ContainsKey(source))
                    yield return new CodeInstruction(OpCodes.Call, FieldLiteralRuntime);
            }
        }

        private static string TranslateManagerLiteral(string source)
        {
            if (source == null || !ManagerUiKorean.TryGetValue(source, out var korean)) return source;
            return LocaleText.Select(source, korean);
        }

        private static string TranslateFieldLiteral(string source)
        {
            if (source == null || !FieldUiKorean.TryGetValue(source, out var korean)) return source;
            return LocaleText.Select(source, korean);
        }

        private static void AfterBuildSettingList(object __instance)
        {
            if (__instance == null || profiles == null) return;
            try
            {
                lock (Gate) lastManager = new WeakReference(__instance);

                var allSettingsField = __instance.GetType().GetField("_allSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (!(allSettingsField?.GetValue(__instance) is IEnumerable settings)) return;

                var korean = LocaleMode.IsKoreanMode();
                var changed = 0;
                foreach (var setting in settings)
                {
                    if (setting == null) continue;
                    var original = originals.GetValue(setting, CaptureOriginalDisplayState);
                    var source = InspectSetting(setting, original);
                    if (source == null) continue;

                    if (!korean)
                    {
                        if (SetStringMember(setting, "Category", original.category)) changed++;
                        if (SetStringMember(setting, "DispName", original.displayName)) changed++;
                        if (SetStringMember(setting, "Description", original.description)) changed++;
                        continue;
                    }

                    var translation = profiles.Resolve(source);
                    if (translation == null) continue;
                    if (!string.IsNullOrEmpty(translation.category) &&
                        SetStringMember(setting, "Category", translation.category)) changed++;
                    if (!string.IsNullOrEmpty(translation.display_name) &&
                        SetStringMember(setting, "DispName", translation.display_name)) changed++;
                    if (!string.IsNullOrEmpty(translation.description) &&
                        SetStringMember(setting, "Description", translation.description)) changed++;
                }


                // BuildSettingList already made category groups using the previous display strings.
                // Rebuild filtered groups after either applying Korean or restoring the source language.
                if (changed > 0)
                {
                    var rebuild = __instance.GetType().GetMethod("BuildFilteredSettingList",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null, Type.EmptyTypes, null);
                    rebuild?.Invoke(__instance, null);
                }
            }
            catch (Exception ex)
            {
                // Fail open. A ConfigurationManager update must never take down the Configuration Manager window or the mod.
                SPT.EditableTranslations.MinimalLog.WarnOnce("ConfigurationManagerHooks:504", () => "[Config UI] BuildSettingList pass skipped: " + ex.Message);
            }
        }

        private static OriginalDisplayState CaptureOriginalDisplayState(object setting)
        {
            return new OriginalDisplayState
            {
                category = ReadMember(setting, "Category") as string,
                displayName = ReadMember(setting, "DispName") as string,
                description = ReadMember(setting, "Description") as string,
            };
        }

        private static ConfigUiSource InspectSetting(object setting, OriginalDisplayState original)
        {
            try
            {
                var pluginInfo = ReadMember(setting, "PluginInfo");
                var guid = ReadMember(pluginInfo, "GUID")?.ToString();
                if (string.IsNullOrWhiteSpace(guid)) return null;

                var displayCategory = original?.category ?? ReadMember(setting, "Category") as string;
                var displayName = original?.displayName ?? ReadMember(setting, "DispName") as string;
                var displayDescription = original?.description ?? ReadMember(setting, "Description") as string;

                // ConfigSettingEntry exposes the backing ConfigEntryBase as Entry. Pull Definition from
                // that when available so translation identity is independent from the text we display.
                var entry = ReadMember(setting, "Entry");
                var definition = ReadMember(entry, "Definition");
                var section = ReadMember(definition, "Section") as string;
                var key = ReadMember(definition, "Key") as string;
                if (string.IsNullOrEmpty(section)) section = displayCategory ?? "";
                if (string.IsNullOrEmpty(key)) key = displayName ?? "";

                var source = new ConfigUiSource
                {
                    plugin_guid = guid,
                    section = section,
                    key = key,
                    source_category = displayCategory,
                    source_display_name = displayName,
                    source_description = displayDescription,
                };

                return source;
            }
            catch { return null; }
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var type = instance.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try { return property.GetValue(instance, null); }
                catch { }
            }
            var field = type.GetField(name, flags);
            if (field != null)
            {
                try { return field.GetValue(instance); }
                catch { }
            }
            return null;
        }

        private static bool SetStringMember(object instance, string name, string value)
        {
            if (instance == null) return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = instance.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null && property.PropertyType == typeof(string))
            {
                var setter = property.GetSetMethod(true);
                if (setter != null)
                {
                    var current = property.GetValue(instance, null) as string;
                    if (string.Equals(current, value, StringComparison.Ordinal)) return false;
                    setter.Invoke(instance, new object[] { value });
                    return true;
                }
            }
            var field = type.GetField(name, flags);
            if (field != null && field.FieldType == typeof(string))
            {
                var current = field.GetValue(instance) as string;
                if (string.Equals(current, value, StringComparison.Ordinal)) return false;
                field.SetValue(instance, value);
                return true;
            }
            return false;
        }

        private sealed class OriginalDisplayState
        {
            internal string category;
            internal string displayName;
            internal string description;
        }
    }
}
