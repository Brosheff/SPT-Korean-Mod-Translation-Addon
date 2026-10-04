using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    // Localizes hard-coded IMGUI literals in explicitly scoped client mods.
    // The original mod assemblies are never rewritten. If a future mod update
    // removes/renames a target, that target is skipped and the original UI remains usable.
    internal static class ModUiLiteralHooks
    {
        private const string ProfileId = "7Bpencil.WeaponCamoAndStickers.UI";
        private const string UiChannel = "ui_literals";
        private const string ExternalChannel = "external_ui_literals";
        private const string PlacementChannel = "equipment_placements";

        private static readonly HashSet<string> TargetAssemblies = new HashSet<string>(StringComparer.Ordinal)
        {
            "7Bpencil.WeaponCamoAndStickers",
            "7Bpencil.EquipmentStickers",
            "7Bpencil.MaterialEditor",
        };

        private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
        private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> PatchedAssemblies = new HashSet<Assembly>();
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static bool enabled;

        static ModUiLiteralHooks()
        {
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode)) continue;
                var op = (OpCode)field.GetValue(null);
                var value = unchecked((ushort)op.Value);
                if (value < 0x100) OneByteOpCodes[value] = op;
                else if ((value & 0xff00) == 0xfe00) TwoByteOpCodes[value & 0xff] = op;
            }
        }

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> loadedAssemblies, EditableProfiles data)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!data.HasRules(ProfileId, UiChannel) && !data.HasRules(ProfileId, ExternalChannel) && !data.HasRules(ProfileId, PlacementChannel)) return 0;

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
            }
        }

        internal static void RefreshLanguage()
        {
            Assembly[] assemblies;
            lock (Gate)
            {
                if (!enabled || profiles == null) return;
                assemblies = PatchedAssemblies.ToArray();
            }

            var changed = 0;
            foreach (var assembly in assemblies)
                changed += RefreshExistingUiState(assembly);
            if (changed > 0)
                {}
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                var count = TryPatchAssembly(args.LoadedAssembly);
                if (count > 0)
                    {}
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("ModUiLiteralHooks:108", () => "Weapon Camo & Stickers late-load UI hook skipped: " + ex.Message);
            }
        }

        private static int TryPatchAssembly(Assembly assembly)
        {
            if (assembly == null || !TargetAssemblies.Contains(assembly.GetName().Name)) return 0;

            lock (Gate)
            {
                if (!enabled || harmony == null || profiles == null || !PatchedAssemblies.Add(assembly)) return 0;
            }

            var count = 0;
            var failed = 0;
            var types = SafeTypes(assembly);

            // Common/short labels are patched only in known or self-identified UI types.
            // This prevents strings such as "Back" or "Texture" from changing internal data/logging.
            foreach (var type in types)
            {
                if (!IsUiType(type)) continue;
                PatchType(type, UiChannel, UiTranspilerMethod, ref count, ref failed);
            }

            // Long/unique external UI strings are safe to discover assembly-wide. This makes the
            // context-menu/error translations survive method or class renames in future releases.
            foreach (var type in types)
                PatchType(type, ExternalChannel, ExternalTranspilerMethod, ref count, ref failed);

            // Equipment sticker start-position captions are display data created in Plugin's
            // instance constructor, not GUI literals. Patch only that constructor so a late-loaded
            // future instance starts localized without translating unrelated words such as "Back".
            if (assembly.GetName().Name == "7Bpencil.EquipmentStickers")
            {
                var pluginType = assembly.GetType("SevenBoldPencil.EquipmentStickers.Plugin", false);
                if (pluginType != null)
                    PatchConstructors(pluginType, PlacementChannel, PlacementTranspilerMethod, ref count, ref failed);
            }

            // BepInEx normally runs plugin Awake methods before Start. Some UI labels therefore
            // already live in arrays/dictionaries by the time this addon installs its transpilers.
            // Translate that already-created state too; future instances remain covered by IL hooks.
            var stateCount = RefreshExistingUiState(assembly);

            var version = assembly.GetName().Version?.ToString() ?? "unknown";
            return count;
        }


        private static int RefreshExistingUiState(Assembly assembly)
        {
            try
            {
                switch (assembly.GetName().Name)
                {
                    case "7Bpencil.WeaponCamoAndStickers":
                        return RefreshMainEditorResources(assembly);
                    case "7Bpencil.MaterialEditor":
                        return RefreshStaticStringArray(assembly, "SevenBoldPencil.MaterialEditor.CamoEditor", "SettingsScreens");
                    case "7Bpencil.EquipmentStickers":
                        return RefreshEquipmentState(assembly);
                    default:
                        return 0;
                }
            }
            catch (Exception ex)
            {
                // Existing-state repair is only a compatibility fallback. Never make it fatal.
                SPT.EditableTranslations.MinimalLog.WarnOnce("ModUiLiteralHooks:178", () => "Weapon Camo & Stickers existing UI state skipped: " + ex.Message);
                return 0;
            }
        }

        private static int RefreshMainEditorResources(Assembly assembly)
        {
            var pluginType = assembly.GetType("SevenBoldPencil.WeaponCamoAndStickers.Plugin", false);
            if (pluginType == null) return 0;

            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var instance = pluginType.GetField("Instance", staticFlags)?.GetValue(null);
            if (instance == null) return 0;

            var resources = pluginType.GetField("CamoEditorResources", instanceFlags)?.GetValue(instance);
            if (resources == null) return 0;

            var resourceType = resources.GetType();
            var count = 0;
            count += RefreshStringArrayField(resources, resourceType, "DecalSettingsToolbar", instanceFlags);
            count += RefreshStringArrayField(resources, resourceType, "DecalTypesToolbar", instanceFlags);
            return count;
        }

        private static int RefreshStaticStringArray(Assembly assembly, string typeName, string fieldName)
        {
            var type = assembly.GetType(typeName, false);
            if (type == null) return 0;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            return RefreshStringArrayField(null, type, fieldName, flags);
        }

        private static int RefreshStringArrayField(object instance, Type type, string fieldName, BindingFlags flags)
        {
            string[] values;
            try { values = type.GetField(fieldName, flags)?.GetValue(instance) as string[]; }
            catch { return 0; }
            if (values == null) return 0;

            var count = 0;
            for (var i = 0; i < values.Length; i++)
            {
                var current = values[i];
                if (current == null) continue;
                var source = profiles?.RestoreExact(ProfileId, UiChannel, current) ?? current;
                var desired = LocaleMode.IsKoreanMode()
                    ? (profiles?.Translate(ProfileId, UiChannel, source) ?? source)
                    : source;
                if (string.Equals(current, desired, StringComparison.Ordinal)) continue;
                values[i] = desired;
                count++;
            }
            return count;
        }

        private static int RefreshEquipmentState(Assembly assembly)
        {
            var count = RefreshStaticStringDictionaryValues(assembly, "SevenBoldPencil.EquipmentStickers.DecalStringCache", "BonesReadableNames");

            var pluginType = assembly.GetType("SevenBoldPencil.EquipmentStickers.Plugin", false);
            if (pluginType == null) return count;
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object instance;
            object placements;
            try
            {
                instance = pluginType.GetField("Instance", staticFlags)?.GetValue(null);
                placements = instance == null ? null : pluginType.GetField("StartDecalTransforms", instanceFlags)?.GetValue(instance);
            }
            catch { return count; }

            if (!(placements is IDictionary dictionary)) return count;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Value is Array array) count += RefreshPlacementArray(array);
            }
            return count;
        }

        private static int RefreshPlacementArray(Array array)
        {
            var count = 0;
            for (var i = 0; i < array.Length; i++)
            {
                var value = array.GetValue(i);
                if (value is Array nested)
                {
                    count += RefreshPlacementArray(nested);
                    continue;
                }
                if (value == null || value.GetType().FullName != "SevenBoldPencil.EquipmentStickers.StartDecalTransform") continue;

                var property = value.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || !property.CanRead || !property.CanWrite) continue;
                string current;
                try { current = property.GetValue(value, null) as string; }
                catch { continue; }
                if (current == null) continue;

                var source = profiles?.RestoreExact(ProfileId, PlacementChannel, current) ?? current;
                var desired = LocaleMode.IsKoreanMode()
                    ? (profiles?.Translate(ProfileId, PlacementChannel, source) ?? source)
                    : source;
                if (string.Equals(current, desired, StringComparison.Ordinal)) continue;
                try
                {
                    // StartDecalTransform is a record struct. Set on the boxed copy, then write
                    // that copy back into the leaf array so the mutation is retained.
                    property.SetValue(value, desired, null);
                    array.SetValue(value, i);
                    count++;
                }
                catch { }
            }
            return count;
        }

        private static int RefreshStaticStringDictionaryValues(Assembly assembly, string typeName, string fieldName)
        {
            var type = assembly.GetType(typeName, false);
            if (type == null) return 0;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            IDictionary dictionary;
            try { dictionary = type.GetField(fieldName, flags)?.GetValue(null) as IDictionary; }
            catch { return 0; }
            if (dictionary == null) return 0;

            // Snapshot keys first so value replacement cannot invalidate an implementation's enumerator.
            var keys = new object[dictionary.Keys.Count];
            dictionary.Keys.CopyTo(keys, 0);
            var count = 0;
            foreach (var key in keys)
            {
                if (!(dictionary[key] is string current)) continue;
                var source = profiles?.RestoreExact(ProfileId, UiChannel, current) ?? current;
                var desired = LocaleMode.IsKoreanMode()
                    ? (profiles?.Translate(ProfileId, UiChannel, source) ?? source)
                    : source;
                if (string.Equals(current, desired, StringComparison.Ordinal)) continue;
                dictionary[key] = desired;
                count++;
            }
            return count;
        }

        private static Type[] SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
        }

        private static bool IsUiType(Type type)
        {
            for (var current = type; current != null; current = current.DeclaringType)
            {
                var name = current.Name;
                if (name.IndexOf("CamoEditor", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (name == "PresetsWindow" || name == "TexturesWindow" || name.EndsWith("StringCache", StringComparison.Ordinal)) return true;
            }

            // If upstream renames an editor class but it still draws IMGUI directly, keep working.
            return DeclaresUnityImmediateModeGuiCall(type);
        }

        private static readonly HarmonyMethod UiTranspilerMethod = MakeTranspiler(nameof(TranslateUiLiterals));
        private static readonly HarmonyMethod ExternalTranspilerMethod = MakeTranspiler(nameof(TranslateExternalLiterals));
        private static readonly HarmonyMethod PlacementTranspilerMethod = MakeTranspiler(nameof(TranslatePlacementLiterals));
        private static readonly MethodInfo UiRuntimeTranslator = typeof(ModUiLiteralHooks).GetMethod(nameof(TranslateUiRuntime), BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo ExternalRuntimeTranslator = typeof(ModUiLiteralHooks).GetMethod(nameof(TranslateExternalRuntime), BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo PlacementRuntimeTranslator = typeof(ModUiLiteralHooks).GetMethod(nameof(TranslatePlacementRuntime), BindingFlags.Static | BindingFlags.NonPublic);

        private static HarmonyMethod MakeTranspiler(string name)
        {
            return new HarmonyMethod(typeof(ModUiLiteralHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic))
            {
                priority = Priority.Last,
            };
        }

        private static void PatchConstructors(Type type, string channel, HarmonyMethod transpiler, ref int count, ref int failed)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            ConstructorInfo[] constructors;
            try { constructors = type.GetConstructors(flags); }
            catch { failed++; return; }
            foreach (var constructor in constructors)
                PatchMethod(constructor, channel, transpiler, ref count, ref failed);
        }

        private static void PatchType(Type type, string channel, HarmonyMethod transpiler, ref int count, ref int failed)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            MethodBase[] members;
            try
            {
                members = type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)).Distinct().ToArray();
            }
            catch
            {
                failed++;
                return;
            }

            foreach (var method in members)
                PatchMethod(method, channel, transpiler, ref count, ref failed);
        }

        private static void PatchMethod(MethodBase method, string channel, HarmonyMethod transpiler, ref int count, ref int failed)
        {
            try
            {
                if (method == null || method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) return;
                if (!ContainsTranslatableLiteral(method, channel)) return;
                harmony.Patch(method, transpiler: transpiler);
                count++;
            }
            catch
            {
                // Compatibility is fail-open: one changed/unpatchable method must not disable the mod or flood the log.
                failed++;
            }
        }

        private static bool DeclaresUnityImmediateModeGuiCall(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            IEnumerable<MethodBase> members;
            try { members = type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)); }
            catch { return false; }

            foreach (var method in members)
            {
                try
                {
                    if (ReferencesUnityImmediateModeGui(method)) return true;
                }
                catch { }
            }
            return false;
        }

        private static bool ReferencesUnityImmediateModeGui(MethodBase method)
        {
            return VisitIl(method, (op, token) =>
            {
                if (op.OperandType != OperandType.InlineMethod) return false;
                MethodBase called;
                try { called = method.Module.ResolveMethod(token, GetTypeArguments(method.DeclaringType), GetMethodArguments(method)); }
                catch { return false; }
                var owner = called?.DeclaringType?.FullName;
                return owner == "UnityEngine.GUI" || owner == "UnityEngine.GUILayout";
            });
        }

        private static bool ContainsTranslatableLiteral(MethodBase method, string channel)
        {
            return VisitIl(method, (op, token) =>
            {
                if (op.OperandType != OperandType.InlineString) return false;
                string source;
                try { source = method.Module.ResolveString(token); }
                catch { return false; }
                return profiles?.CanTranslate(ProfileId, channel, source) == true;
            });
        }

        private static bool VisitIl(MethodBase method, Func<OpCode, int, bool> tokenVisitor)
        {
            MethodBody body;
            try { body = method?.GetMethodBody(); }
            catch { return false; }
            var il = body?.GetILAsByteArray();
            if (il == null) return false;

            var offset = 0;
            while (offset < il.Length)
            {
                var first = il[offset++];
                OpCode op;
                if (first == 0xfe)
                {
                    if (offset >= il.Length) return false;
                    op = TwoByteOpCodes[il[offset++]];
                }
                else
                {
                    op = OneByteOpCodes[first];
                }

                var operandStart = offset;
                var operandSize = GetOperandSize(op.OperandType, il, operandStart);
                if (operandSize < 0 || operandStart + operandSize > il.Length) return false;

                switch (op.OperandType)
                {
                    case OperandType.InlineString:
                    case OperandType.InlineMethod:
                        if (operandSize == 4)
                        {
                            var token = BitConverter.ToInt32(il, operandStart);
                            if (tokenVisitor(op, token)) return true;
                        }
                        break;
                }
                offset += operandSize;
            }
            return false;
        }

        private static int GetOperandSize(OperandType type, byte[] il, int offset)
        {
            switch (type)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch:
                    if (offset + 4 > il.Length) return -1;
                    var count = BitConverter.ToInt32(il, offset);
                    if (count < 0 || count > (il.Length - offset - 4) / 4) return -1;
                    return 4 + count * 4;
                default: return -1;
            }
        }

        private static Type[] GetTypeArguments(Type type)
        {
            return type != null && type.IsGenericType ? type.GetGenericArguments() : null;
        }

        private static Type[] GetMethodArguments(MethodBase method)
        {
            return method != null && method.IsGenericMethod ? method.GetGenericArguments() : null;
        }

        private static IEnumerable<CodeInstruction> TranslateUiLiterals(IEnumerable<CodeInstruction> instructions)
        {
            return TranslateLiterals(instructions, UiChannel, UiRuntimeTranslator);
        }

        private static IEnumerable<CodeInstruction> TranslateExternalLiterals(IEnumerable<CodeInstruction> instructions)
        {
            return TranslateLiterals(instructions, ExternalChannel, ExternalRuntimeTranslator);
        }

        private static IEnumerable<CodeInstruction> TranslatePlacementLiterals(IEnumerable<CodeInstruction> instructions)
        {
            return TranslateLiterals(instructions, PlacementChannel, PlacementRuntimeTranslator);
        }

        private static string TranslateUiRuntime(string source)
        {
            return TranslateRuntime(source, UiChannel);
        }

        private static string TranslateExternalRuntime(string source)
        {
            return TranslateRuntime(source, ExternalChannel);
        }

        private static string TranslatePlacementRuntime(string source)
        {
            return TranslateRuntime(source, PlacementChannel);
        }

        private static string TranslateRuntime(string source, string channel)
        {
            if (!LocaleMode.IsKoreanMode() || profiles == null) return source;
            return profiles.Translate(ProfileId, channel, source);
        }

        private static IEnumerable<CodeInstruction> TranslateLiterals(
            IEnumerable<CodeInstruction> instructions, string channel, MethodInfo runtimeTranslator)
        {
            foreach (var instruction in instructions)
            {
                var shouldTranslate = false;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string source && profiles != null)
                {
                    shouldTranslate = profiles.CanTranslate(ProfileId, channel, source);
                }

                yield return instruction;
                if (shouldTranslate)
                {
                    // Keep the original literal in IL and decide at execution time. This is what
                    // allows EFT's interface language to switch between English/other languages
                    // and kr/kr-en without repatching the third-party mod.
                    yield return new CodeInstruction(OpCodes.Call, runtimeTranslator);
                }
            }
        }
    }
}
