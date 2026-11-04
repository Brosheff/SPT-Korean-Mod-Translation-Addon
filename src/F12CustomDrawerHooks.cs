using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // F12 entries that replace ConfigurationManager's normal field renderer with a mod-owned
    // CustomDrawer never pass through SettingFieldDrawer. Patch only those known drawers.
    // All translations are chosen at draw time; no ConfigEntry value/description is rewritten.
    internal static class F12CustomDrawerHooks
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> PatchedAssemblies = new HashSet<Assembly>();
        private static Harmony harmony;
        private static bool enabled;

        private static readonly Dictionary<string, string> LiteralKorean = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Open mask editor"] = "마스크 편집기 열기",
            ["Close mask editor"] = "마스크 편집기 닫기",
            ["Janky's Special"] = "Janky 특제 설정",
            ["Potato"] = "저사양",
            ["Defaults"] = "기본값",
            ["Tempt Fate"] = "운명 시험하기",
            ["Open web config UI"] = "웹 설정 UI 열기",
            ["Create debug ZIP"] = "디버그 ZIP 생성",
            ["Creating debug ZIP..."] = "디버그 ZIP 생성 중...",
            ["Cancel host transfer (keep local ZIP)"] = "호스트 전송 취소(로컬 ZIP 유지)",
            ["Open debug folder"] = "디버그 폴더 열기",
            ["Restore Recommended Defaults"] = "권장 기본값 복원",
            ["Recorded Pose"] = "기록된 자세",
            ["T-Pose"] = "T-포즈",
            ["Apply Chest Bleed"] = "흉부 경출혈 적용",
            ["Apply Heavy Chest Bleed"] = "흉부 중출혈 적용",
            ["Apply Heart Bleed"] = "심장 출혈 적용",
            ["Apply Face Bleed"] = "안면 경출혈 적용",
            ["Apply Stomach Bleed"] = "복부 경출혈 적용",
            ["Apply Arm Bleed"] = "팔 경출혈 적용",
            ["Apply Leg Bleed"] = "다리 경출혈 적용",
            ["Apply Bruised"] = "타박상 적용",
            ["Apply Spine Fracture"] = "척추 골절 적용",
            ["Apply Hit Pressure"] = "피격 압력 적용",
            ["<color=grey>Set by the Fika Host</color>"] = "<color=grey>Fika 호스트가 설정함</color>",
            ["Plugin has been disabled!"] = "플러그인이 비활성화되었습니다!",
        };

        // ORBIT 3.1.0: ConfigManager CustomDrawer renders live strings returned by these two
        // Status getters. The static label dictionary above cannot translate those results.
        // Do not rewrite the underlying status or patch the global GUILayout/GUI API.
        private static readonly Dictionary<string, string> OrbitStatusKorean = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Create a ZIP to attach to your bug report."] = "버그 신고에 첨부할 ZIP 파일을 생성합니다.",
            ["Creating debug ZIP..."] = "디버그 ZIP 생성 중...",
            ["Preparing local logs, diagnostics and RR replay"] = "로컬 로그·진단·Raid Review 리플레이 준비 중",
            ["Contacting raid host/headless"] = "레이드 호스트·헤드리스 클라이언트 연결 중",
            ["Host: preparing logs and RR replay"] = "호스트에서 로그와 Raid Review 리플레이 준비 중",
            ["Host to server"] = "호스트에서 서버로 전송 중",
            ["Downloading cached host files"] = "캐시된 호스트 파일 다운로드 중",
            ["Server to client"] = "서버에서 클라이언트로 전송 중",
            ["Verifying host files"] = "호스트 파일 검증 중",
            ["Adding host files to ZIP"] = "ZIP에 호스트 파일 추가 중",
            ["Enable capture, bind the shortcut, then face a door until its interaction prompt appears."] =
                "문 포착을 활성화하고 단축키를 지정한 다음, 상호작용 안내가 나타날 때까지 문을 바라보세요.",
            ["Enter a raid with your player to capture a door."] = "문을 포착하려면 자신의 캐릭터로 레이드에 진입하세요.",
            ["Face a nearby door until its interaction prompt appears, then press the capture key."] =
                "가까운 문을 바라보아 상호작용 안내가 나타나면 문 포착 단축키를 누르세요.",
            ["Door ID copied. This powered door requires its access sequence and cannot use Marked Rush."] =
                "문 ID를 복사했습니다. 이 전력식 문은 별도 접근 절차가 필요하여 Marked Rush에 사용할 수 없습니다.",
            ["Door ID copied. This room's access is not supported by Marked Rush."] =
                "문 ID를 복사했습니다. 이 방의 접근 방식은 Marked Rush에서 지원하지 않습니다.",
            ["Door ID copied. Saving room to the active preset..."] =
                "문 ID를 복사했습니다. 현재 프리셋에 방을 저장하는 중...",
        };
        // Keep dynamic paths, exception messages and server-supplied detail intact.
        private static readonly KeyValuePair<string, string>[] OrbitStatusPrefixes =
        {
            new KeyValuePair<string, string>("Saved: ", "저장됨: "),
            new KeyValuePair<string, string>("Could not create debug ZIP: ", "디버그 ZIP을 생성하지 못했습니다: "),
            new KeyValuePair<string, string>("Local ZIP saved; host files unavailable: ",
                "로컬 ZIP을 저장했지만 호스트 파일을 가져올 수 없습니다: "),
            new KeyValuePair<string, string>("Door ID copied. Could not confirm the save: ",
                "문 ID를 복사했지만 저장을 확인할 수 없습니다: "),
            new KeyValuePair<string, string>("Door ID copied. Save not confirmed: ",
                "문 ID를 복사했지만 저장을 확인하지 못했습니다: "),
            new KeyValuePair<string, string>("Door capture failed: ", "문 포착 실패: "),
            new KeyValuePair<string, string>("Room added in ", "방을 추가했습니다. 프리셋: "),
            new KeyValuePair<string, string>("Already configured in ", "이미 설정된 방입니다. 프리셋: "),
        };

        private static readonly Dictionary<string, string> UiFixesValueKorean = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Enabled"] = "활성화",
            ["Disabled"] = "비활성화",
            ["Never"] = "사용 안 함",
            ["On Close"] = "닫을 때",
            ["Always"] = "항상",
            ["With Multitool"] = "멀티툴 보유 시",
            ["First Available Space"] = "첫 빈 공간",
            ["Same Row or Below (Wrapping)"] = "같은 행 또는 아래쪽 (줄바꿈)",
            ["Keep Original Spacing (Best Effort)"] = "기존 간격 유지 (가능한 범위)",
            ["None"] = "없음",
            ["Minimum"] = "최저가",
            ["Average"] = "평균가",
            ["Maximum"] = "최고가",
            ["Normal"] = "기본",
            ["Visible Upgrades"] = "표시되는 업그레이드",
            ["All Upgrades"] = "모든 업그레이드",
        };

        private static readonly MethodInfo RuntimeLiteral = typeof(F12CustomDrawerHooks).GetMethod(
            nameof(TranslateLiteralRuntime), BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly HarmonyMethod LiteralTranspiler = new HarmonyMethod(typeof(F12CustomDrawerHooks).GetMethod(
            nameof(TranslateLiterals), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        private static readonly HarmonyMethod UiFixesDrawerPostfix = new HarmonyMethod(typeof(F12CustomDrawerHooks).GetMethod(
            nameof(AfterMakeDisabledDrawer), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };
        private static readonly HarmonyMethod OrbitStatusPostfix = new HarmonyMethod(typeof(F12CustomDrawerHooks).GetMethod(
            nameof(AfterOrbitStatus), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> loadedAssemblies)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            lock (Gate)
            {
                if (enabled) return 0;
                enabled = true;
                harmony = patcher;
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
                PatchedAssemblies.Clear();
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
                SPT.EditableTranslations.MinimalLog.WarnOnce("F12CustomDrawerHooks:113", () => "[F12 CustomDrawer] Late-load hook skipped: " + ex.Message);
            }
        }

        private static int TryPatchAssembly(Assembly assembly)
        {
            if (assembly == null) return 0;
            var name = assembly.GetName().Name ?? "";
            if (!IsTargetAssembly(name)) return 0;

            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedAssemblies.Add(assembly)) return 0;
            }

            var count = 0;
            try
            {
                switch (name)
                {
                    case "HollywoodFX":
                        count += PatchNamed(assembly, "HollywoodFX.Plugin", "LoadTemplateDrawer");
                        count += PatchNamed(assembly, "HollywoodFX.Plugin", "SelfDestructDrawer");
                        break;
                    case "ORBIT":
                        count += PatchNamed(assembly, "Orbit.Plugin", "DrawWebConfigButton");
                        count += PatchNamed(assembly, "Orbit.Plugin", "DrawDebugExportButton");
                        // The two getters are used only by the F12 custom drawers in ORBIT 3.1.0.
                        // A source string in an external status/progress object does not pass the
                        // drawer's literal transpiler, so translate the returned UI value instead.
                        count += PatchOrbitStatusGetter(assembly, "Orbit.Helpers.DebugExport");
                        count += PatchOrbitStatusGetter(assembly, "Orbit.Helpers.DoorCaptureTool");
                        break;
                    case "TraumaCore":
                        count += PatchNamed(assembly, "TraumaCore.Plugin", "DrawDefaultPresetButton");
                        count += PatchNamed(assembly, "TraumaCore.Plugin", "DrawEffectTestButton");
                        count += PatchNamed(assembly, "TraumaCore.CalibrationMannequin", "DrawCalibrationPoseButtons");
                        break;
                    case "Coti.Client":
                        count += PatchMethodsContainingLiteral(assembly, "Open mask editor", "Close mask editor");
                        break;
                    case "ozen.MagCheckInterrupt":
                        count += PatchMethodsContainingLiteral(assembly, "<color=grey>Set by the Fika Host</color>");
                        break;
                    case "Tyfon.UIFixes":
                        count += PatchUiFixesDisabledDrawer(assembly);
                        break;
                    default:
                        // Version-check CustomDrawers only exist on an incompatible game build,
                        // but keep their fixed disabled label localized if they ever appear.
                        count += PatchMethodsContainingLiteral(assembly, "Plugin has been disabled!");
                        break;
                }
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("F12CustomDrawerHooks:163", () => "[F12 CustomDrawer] " + name + " scan skipped: " + ex.Message);
            }

            if (count > 0) SPT.EditableTranslations.MinimalLog.WarnOnce("F12CustomDrawerHooks:166", () => "[F12 CustomDrawer] " + name + " patched=" + count);
            return count;
        }

        private static bool IsTargetAssembly(string name)
        {
            return name == "HollywoodFX" || name == "ORBIT" || name == "TraumaCore" ||
                   name == "Coti.Client" || name == "ozen.MagCheckInterrupt" || name == "Tyfon.UIFixes" ||
                   name.IndexOf("DynamicMaps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("PreviewSizer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("BrightLasers", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int PatchNamed(Assembly assembly, string typeName, string methodName)
        {
            var type = assembly.GetType(typeName, false);
            if (type == null) return 0;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var methods = type.GetMethods(flags).Where(x => x.Name == methodName).ToArray();
            var count = 0;
            foreach (var method in methods)
            {
                if (PatchLiteralMethod(method)) count++;
                else SPT.EditableTranslations.MinimalLog.WarnOnce(
                    "F12CustomDrawerHooks:DrawerPatch:" + typeName + "." + methodName,
                    () => "[F12 CustomDrawer] Exact drawer patch failed: " + typeName + "." + methodName);
            }
            if (methods.Length == 0)
                SPT.EditableTranslations.MinimalLog.WarnOnce(
                    "F12CustomDrawerHooks:DrawerMissing:" + typeName + "." + methodName,
                    () => "[F12 CustomDrawer] Exact drawer method missing: " + typeName + "." + methodName);
            return count;
        }

        private static int PatchOrbitStatusGetter(Assembly assembly, string typeName)
        {
            var type = assembly.GetType(typeName, false);
            var method = type?.GetProperty("Status", BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic)?.GetGetMethod(true);
            if (method == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("F12CustomDrawerHooks:OrbitGetter:" + typeName,
                    () => "[F12 CustomDrawer] ORBIT 3.1.0 status getter missing: " + typeName);
                return 0;
            }
            try
            {
                harmony.Patch(method, postfix: OrbitStatusPostfix);
                return 1;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("F12CustomDrawerHooks:OrbitPatch:" + typeName,
                    () => "[F12 CustomDrawer] ORBIT status patch failed: " + typeName + " - " + ex.Message);
                return 0;
            }
        }

        // Only changes the return value displayed by ORBIT's F12 CustomDrawer; never
        // modifies fields, error logs, saved config or the web UI. English is untouched.
        private static void AfterOrbitStatus(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || !LocaleMode.IsKoreanMode()) return;
            if (OrbitStatusKorean.TryGetValue(__result, out var korean))
            {
                __result = korean;
                return;
            }
            // Progress labels append live byte counts/percentages to a known stage.
            // Translate the stage alone; keep all numbers and units unchanged.
            foreach (var stage in OrbitStatusKorean)
            {
                var marker = stage.Key + ": ";
                if (!__result.StartsWith(marker, StringComparison.Ordinal)) continue;
                __result = stage.Value + ": " + __result.Substring(marker.Length);
                return;
            }
            foreach (var pair in OrbitStatusPrefixes)
            {
                if (!__result.StartsWith(pair.Key, StringComparison.Ordinal)) continue;
                __result = pair.Value + __result.Substring(pair.Key.Length);
                return;
            }
        }

        private static int PatchMethodsContainingLiteral(Assembly assembly, params string[] literals)
        {
            var count = 0;
            foreach (var type in SafeTypes(assembly))
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                MethodInfo[] methods;
                try { methods = type.GetMethods(flags); }
                catch { continue; }
                foreach (var method in methods)
                {
                    if (!ContainsAnyLiteral(method, literals)) continue;
                    if (PatchLiteralMethod(method)) count++;
                }
            }
            return count;
        }

        private static bool PatchLiteralMethod(MethodInfo method)
        {
            if (method == null || method.IsAbstract || method.ContainsGenericParameters) return false;
            try
            {
                harmony.Patch(method, transpiler: LiteralTranspiler);
                return true;
            }
            catch { return false; }
        }

        private static int PatchUiFixesDisabledDrawer(Assembly assembly)
        {
            var type = assembly.GetType("UIFixes.SettingExtensions", false);
            if (type == null) return 0;
            var method = type.GetMethod("MakeDisabledDrawer", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            if (method == null) return 0;
            try
            {
                harmony.Patch(method, postfix: UiFixesDrawerPostfix);
                return 1;
            }
            catch { return 0; }
        }

        private static void AfterMakeDisabledDrawer(string explanation, ref Action<ConfigEntryBase> __result)
        {
            var original = __result;
            __result = config =>
            {
                if (!LocaleMode.IsKoreanMode())
                {
                    original?.Invoke(config);
                    return;
                }

                string sourceValue;
                var boxed = config?.BoxedValue;
                if (boxed is bool b)
                {
                    sourceValue = b ? "Enabled" : "Disabled";
                }
                else if (boxed is Enum e)
                {
                    sourceValue = GetEnumDisplayValue(e);
                }
                else
                {
                    sourceValue = boxed?.ToString() ?? "";
                }

                var value = TranslateUiFixesValue(sourceValue);
                var reason = TranslateUiFixesReason(explanation);
                var suffix = string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")";
                GUILayout.Label("<color=grey>" + value + suffix + "</color>", GUILayout.ExpandWidth(true));
            };
        }

        private static string GetEnumDisplayValue(Enum value)
        {
            if (value == null) return "";
            try
            {
                var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
                var description = member?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                    .Cast<DescriptionAttribute>().FirstOrDefault();
                return description?.Description ?? value.ToString();
            }
            catch { return value.ToString(); }
        }

        private static string TranslateUiFixesValue(string source)
        {
            if (source == null || !UiFixesValueKorean.TryGetValue(source, out var korean)) return source;
            return LocaleText.Select(source, korean);
        }

        private static string TranslateUiFixesReason(string source)
        {
            if (string.IsNullOrEmpty(source)) return "";
            string korean;
            if (source == "Readonly") korean = "읽기 전용";
            else if (source == "Set by Fika host") korean = "Fika 호스트가 설정함";
            else if (source.StartsWith("by ", StringComparison.Ordinal))
                korean = source.Substring(3) + " 설정에 의해 고정됨";
            else if (source.StartsWith("Requires ", StringComparison.Ordinal))
                korean = source.Substring(9) + " 설정 필요";
            else return source;
            return LocaleText.Select(source, korean);
        }

        private static string TranslateLiteralRuntime(string source)
        {
            if (source == null || !LiteralKorean.TryGetValue(source, out var korean)) return source;
            return LocaleText.Select(source, korean);
        }

        private static IEnumerable<CodeInstruction> TranslateLiterals(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string source && LiteralKorean.ContainsKey(source))
                    yield return new CodeInstruction(OpCodes.Call, RuntimeLiteral);
            }
        }

        private static Type[] SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(x => x != null).ToArray(); }
        }

        private static bool ContainsAnyLiteral(MethodInfo method, string[] literals)
        {
            if (method == null || literals == null || literals.Length == 0) return false;
            MethodBody body;
            try { body = method.GetMethodBody(); }
            catch { return false; }
            var il = body?.GetILAsByteArray();
            if (il == null) return false;

            // This scanner only needs ldstr. Decode enough IL to advance safely through operands.
            var one = OpCodeCache.OneByte;
            var two = OpCodeCache.TwoByte;
            var offset = 0;
            while (offset < il.Length)
            {
                OpCode op;
                var first = il[offset++];
                if (first == 0xfe)
                {
                    if (offset >= il.Length) return false;
                    op = two[il[offset++]];
                }
                else op = one[first];

                var start = offset;
                var size = OperandSize(op.OperandType, il, start);
                if (size < 0 || start + size > il.Length) return false;
                if (op.OperandType == OperandType.InlineString && size == 4)
                {
                    try
                    {
                        var s = method.Module.ResolveString(BitConverter.ToInt32(il, start));
                        if (literals.Contains(s, StringComparer.Ordinal)) return true;
                    }
                    catch { }
                }
                offset += size;
            }
            return false;
        }

        private static int OperandSize(OperandType type, byte[] il, int offset)
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

        private static class OpCodeCache
        {
            internal static readonly OpCode[] OneByte = new OpCode[0x100];
            internal static readonly OpCode[] TwoByte = new OpCode[0x100];

            static OpCodeCache()
            {
                foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.FieldType != typeof(OpCode)) continue;
                    var op = (OpCode)field.GetValue(null);
                    var value = unchecked((ushort)op.Value);
                    if (value < 0x100) OneByte[value] = op;
                    else if ((value & 0xff00) == 0xfe00) TwoByte[value & 0xff] = op;
                }
            }
        }
    }
}
