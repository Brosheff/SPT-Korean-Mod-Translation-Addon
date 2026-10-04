using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    // Dynamic Maps 1.2.1 safe display-only localization.
    //
    // Safety rules:
    //  * Never mutate MapDef/MapMarkerDef/MapLabelDef source data.
    //  * Never invoke Dynamic Maps lifecycle/private refresh methods.
    //  * Never patch MapDef.DisplayName globally.
    //  * Patch only exact, source-audited 1.2.1 UI methods.
    //  * If the installed Dynamic Maps version/layout differs, fail open and leave it untouched.
    internal static class DynamicMapsUiHooks
    {
        private const string PluginGuid = "com.mpstark.dynamicmaps";
        private static readonly Version SupportedVersion = new Version(1, 2, 1);
        private const string ProfileId = "DynamicMaps";
        private const string UiChannel = "custom_ui";
        private const string MapChannel = "map_content";

        private static readonly object Gate = new object();
        private static readonly List<TrackedVisual> Visuals = new List<TrackedVisual>();
        private static readonly List<WeakReference> Selectors = new List<WeakReference>();
        private static readonly MethodInfo UiRuntimeTranslator = typeof(DynamicMapsUiHooks).GetMethod(
            nameof(TranslateUiRuntime), BindingFlags.Static | BindingFlags.NonPublic);

        private sealed class TrackedVisual
        {
            internal readonly WeakReference Target;
            internal readonly string Source;
            internal TrackedVisual(object target, string source)
            {
                Target = new WeakReference(target);
                Source = source;
            }
        }

        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static bool enabled;

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> loadedAssemblies, EditableProfiles data)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!data.HasRules(ProfileId, UiChannel) && !data.HasRules(ProfileId, MapChannel)) return 0;

            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var pluginInfo) || pluginInfo?.Instance == null)
                return 0;

            var version = pluginInfo.Metadata?.Version;
            if (version == null || version != SupportedVersion)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("DynamicMapsUiHooks:60", () => "[Dynamic Maps UI] unsupported version " +
                    (version?.ToString() ?? "<unknown>") + "; expected 1.2.1. Hooks skipped.");
                return 0;
            }

            var assembly = pluginInfo.Instance.GetType().Assembly;
            if (assembly == null || !string.Equals(assembly.GetName().Name, "DynamicMaps", StringComparison.Ordinal))
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("DynamicMapsUiHooks:68", () => "[Dynamic Maps UI] plugin assembly identity mismatch; hooks skipped.");
                return 0;
            }

            lock (Gate)
            {
                if (enabled) return 0;
                harmony = patcher;
                profiles = data;
            }

            var patched = PatchKnown121Methods(assembly);
            lock (Gate) enabled = patched > 0;
            SPT.EditableTranslations.MinimalLog.WarnOnce("DynamicMapsUiHooks:81", () => "[Dynamic Maps UI] safe 1.2.1 hooks installed=" + patched);
            return patched;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                enabled = false;
                harmony = null;
                profiles = null;
                Visuals.Clear();
                Selectors.Clear();
            }
        }

        internal static void RefreshLanguage()
        {
            // Display-only refresh. Never call Dynamic Maps' ChangeAvailableMapDefs, OnLoadMap,
            // SelectedLevel setter, or any other lifecycle method from here.
            if (!enabled || profiles == null) return;
            RefreshTrackedVisuals();
            RefreshSelectorLabels();
        }

        private static int PatchKnown121Methods(Assembly assembly)
        {
            var count = 0;

            // Map selector fixed label. Awake is exact in Dynamic Maps 1.2.1.
            var selectorType = assembly.GetType("DynamicMaps.UI.Controls.MapSelectDropdown", false);
            var selectorAwake = selectorType?.GetMethod("Awake",
                BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (selectorAwake != null && ContainsLiteral(selectorAwake, "Select a Map"))
            {
                count += TryPatch(selectorAwake,
                    postfix: Hook(nameof(RegisterSelector)),
                    transpiler: Hook(nameof(TranslateUiLiterals)));
            }

            // Per-frame coordinate labels; locale changes are picked up automatically on the next Update.
            count += PatchLiteralMethod(assembly, "DynamicMaps.UI.Controls.CursorPositionText", "Update", "Cursor: ");
            count += PatchLiteralMethod(assembly, "DynamicMaps.UI.Controls.PlayerPositionText", "Update", "Player: ");

            // Level text is generated only when SelectedLevel changes.
            var levelType = assembly.GetType("DynamicMaps.UI.Controls.LevelSelectSlider", false);
            var levelSetter = levelType?.GetProperty("SelectedLevel",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetSetMethod(true);
            if (levelSetter != null && ContainsLiteral(levelSetter, "Level "))
                count += TryPatch(levelSetter, transpiler: Hook(nameof(TranslateUiLiterals)));

            // Translate only the compiler-generated projection used by
            // ChangeAvailableMapDefs: defs.Select(def => def.DisplayName).
            // This avoids the unsafe global MapDef.DisplayName getter patch used previously.
            var projection = FindMapSelectorDisplayProjection(selectorType, assembly);
            if (projection != null)
                count += TryPatch(projection, postfix: Hook(nameof(TranslateMapDisplayName)));

            // Translate rendered marker/label objects after their normal creation is complete.
            // Source definition objects remain untouched.
            var markerType = assembly.GetType("DynamicMaps.UI.Components.MapMarker", false);
            var markerDefType = assembly.GetType("DynamicMaps.Data.MapMarkerDef", false);
            var markerCreate = FindCreateWithDefinition(markerType, markerDefType);
            if (markerCreate != null)
                count += TryPatch(markerCreate, postfix: Hook(nameof(AfterDefinitionVisualCreated)));

            var labelType = assembly.GetType("DynamicMaps.UI.Components.MapLabel", false);
            var labelDefType = assembly.GetType("DynamicMaps.Data.MapLabelDef", false);
            var labelCreate = FindCreateWithDefinition(labelType, labelDefType);
            if (labelCreate != null)
                count += TryPatch(labelCreate, postfix: Hook(nameof(AfterDefinitionVisualCreated)));

            return count;
        }

        private static int PatchLiteralMethod(Assembly assembly, string typeName, string methodName, string expectedLiteral)
        {
            var type = assembly.GetType(typeName, false);
            var method = type?.GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method == null || !ContainsLiteral(method, expectedLiteral)) return 0;
            return TryPatch(method, transpiler: Hook(nameof(TranslateUiLiterals)));
        }

        private static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(typeof(DynamicMapsUiHooks).GetMethod(name,
                BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        }

        private static int TryPatch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null)
        {
            if (original == null || harmony == null) return 0;
            try
            {
                // HarmonyX marks the legacy five-argument overload obsolete-as-error. Explicitly select
                // the current overload and never install a finalizer/IL manipulator for these hooks.
                harmony.Patch(original,
                    prefix: prefix,
                    postfix: postfix,
                    transpiler: transpiler,
                    finalizer: null,
                    ilmanipulator: null);
                return 1;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("DynamicMapsUiHooks:191", () => "[Dynamic Maps UI] patch skipped: " +
                    original.DeclaringType?.FullName + "." + original.Name + " - " + ex.Message);
                return 0;
            }
        }

        private static MethodInfo FindMapSelectorDisplayProjection(Type selectorType, Assembly assembly)
        {
            if (selectorType == null || assembly == null) return null;
            var mapDefType = assembly.GetType("DynamicMaps.Data.MapDef", false);
            if (mapDefType == null) return null;

            var candidates = new List<Type> { selectorType };
            try { candidates.AddRange(selectorType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)); }
            catch { }

            foreach (var type in candidates)
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                              BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.DeclaredOnly);
                }
                catch { continue; }

                foreach (var method in methods)
                {
                    if (method.ReturnType != typeof(string)) continue;
                    if (method.Name.IndexOf("ChangeAvailableMapDefs", StringComparison.Ordinal) < 0) continue;
                    var p = method.GetParameters();
                    if (p.Length == 1 && p[0].ParameterType == mapDefType)
                        return method;
                }
            }
            return null;
        }

        private static MethodInfo FindCreateWithDefinition(Type visualType, Type definitionType)
        {
            if (visualType == null || definitionType == null) return null;
            MethodInfo[] methods;
            try
            {
                methods = visualType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch { return null; }

            return methods.FirstOrDefault(m =>
            {
                if (m.Name != "Create" || m.IsGenericMethodDefinition || m.ReturnType != visualType) return false;
                var p = m.GetParameters();
                return p.Any(x => x.ParameterType == definitionType);
            });
        }

        private static IEnumerable<CodeInstruction> TranslateUiLiterals(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                var translate = instruction.opcode == OpCodes.Ldstr &&
                                instruction.operand is string source &&
                                profiles?.CanTranslate(ProfileId, UiChannel, source) == true;
                yield return instruction;
                if (translate)
                    yield return new CodeInstruction(OpCodes.Call, UiRuntimeTranslator);
            }
        }

        private static string TranslateUiRuntime(string source)
        {
            if (profiles == null || source == null) return source;
            var culture = LocaleMode.CurrentCulture();
            if (!LocaleMode.IsKoreanCulture(culture)) return source;
            return profiles.Translate(ProfileId, UiChannel, source, culture);
        }

        private static void RegisterSelector(object __instance)
        {
            if (__instance == null) return;
            lock (Gate)
            {
                RemoveDeadSelectors();
                if (!Selectors.Any(w => ReferenceEquals(w.Target, __instance)))
                    Selectors.Add(new WeakReference(__instance));
            }
        }

        private static void TranslateMapDisplayName(ref string __result)
        {
            __result = TranslateMapContent(__result);
        }

        private static void AfterDefinitionVisualCreated(object __result, object[] __args)
        {
            if (__result == null || __args == null) return;
            try
            {
                string source = null;
                foreach (var arg in __args)
                {
                    if (arg == null) continue;
                    var fullName = arg.GetType().FullName;
                    if (fullName != "DynamicMaps.Data.MapMarkerDef" &&
                        fullName != "DynamicMaps.Data.MapLabelDef") continue;

                    var text = arg.GetType().GetProperty("Text",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    source = text?.GetValue(arg, null) as string;
                    break;
                }

                if (source == null) return;
                TrackVisual(__result, source);
                ApplyVisualText(__result, TranslateMapContent(source));
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("DynamicMapsUiHooks:310", () => "[Dynamic Maps UI] visual translation skipped: " + ex.Message);
            }
        }

        private static void TrackVisual(object visual, string source)
        {
            if (visual == null || source == null) return;
            lock (Gate)
            {
                for (var i = Visuals.Count - 1; i >= 0; i--)
                {
                    var target = Visuals[i].Target.Target;
                    if (target == null) Visuals.RemoveAt(i);
                    else if (ReferenceEquals(target, visual))
                    {
                        Visuals[i] = new TrackedVisual(visual, source);
                        return;
                    }
                }
                Visuals.Add(new TrackedVisual(visual, source));
            }
        }

        private static void RefreshTrackedVisuals()
        {
            List<TrackedVisual> snapshot;
            lock (Gate) snapshot = Visuals.ToList();

            foreach (var entry in snapshot)
            {
                var target = entry.Target.Target;
                if (target == null) continue;
                try { ApplyVisualText(target, TranslateMapContent(entry.Source)); }
                catch { }
            }

            lock (Gate)
            {
                for (var i = Visuals.Count - 1; i >= 0; i--)
                    if (!Visuals[i].Target.IsAlive || Visuals[i].Target.Target == null)
                        Visuals.RemoveAt(i);
            }
        }

        private static void RefreshSelectorLabels()
        {
            List<object> snapshot;
            lock (Gate)
            {
                RemoveDeadSelectors();
                snapshot = Selectors.Select(w => w.Target).Where(x => x != null).ToList();
            }

            var translated = TranslateUiRuntime("Select a Map");
            foreach (var selector in snapshot)
            {
                try
                {
                    var dropdown = selector.GetType().GetField("_dropdown",
                        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(selector);
                    if (dropdown == null) continue;
                    var setLabel = dropdown.GetType().GetMethod("SetLabelText",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string) }, null);
                    setLabel?.Invoke(dropdown, new object[] { translated });
                }
                catch { }
            }
        }

        private static void RemoveDeadSelectors()
        {
            for (var i = Selectors.Count - 1; i >= 0; i--)
                if (!Selectors[i].IsAlive || Selectors[i].Target == null)
                    Selectors.RemoveAt(i);
        }

        private static void ApplyVisualText(object visual, string translated)
        {
            if (visual == null || translated == null) return;
            var type = visual.GetType();

            // Keep Dynamic Maps' public visual state consistent with the rendered label, but never touch
            // the source definition object that came from map JSON/dynamic providers.
            var text = type.GetProperty("Text",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var setter = text?.GetSetMethod(true);
            if (setter != null)
            {
                var current = text.GetValue(visual, null) as string;
                if (!string.Equals(current, translated, StringComparison.Ordinal))
                    setter.Invoke(visual, new object[] { translated });
            }

            var label = type.GetProperty("Label",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(visual, null);
            if (label == null) return;
            var labelText = label.GetType().GetProperty("text",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (labelText?.CanWrite == true)
            {
                var current = labelText.GetValue(label, null) as string;
                if (!string.Equals(current, translated, StringComparison.Ordinal))
                    labelText.SetValue(label, translated, null);
            }
        }

        private static string TranslateMapContent(string source)
        {
            if (profiles == null || source == null) return source;
            var culture = LocaleMode.CurrentCulture();
            if (!LocaleMode.IsKoreanCulture(culture)) return source;
            return profiles.Translate(ProfileId, MapChannel, source, culture);
        }

        private static bool ContainsLiteral(MethodBase method, string expected)
        {
            if (method == null || expected == null) return false;
            MethodBody body;
            try { body = method.GetMethodBody(); }
            catch { return false; }
            var il = body?.GetILAsByteArray();
            if (il == null) return false;

            // A tiny IL reader is unnecessary here. Resolve every 4-byte candidate after an ldstr opcode.
            // ldstr is 0x72 and always carries a four-byte metadata string token.
            for (var i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x72) continue;
                try
                {
                    var token = BitConverter.ToInt32(il, i + 1);
                    if (string.Equals(method.Module.ResolveString(token), expected, StringComparison.Ordinal))
                        return true;
                }
                catch { }
            }
            return false;
        }
    }
}
