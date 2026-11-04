using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TMPro;

namespace SPT.ModKoreanAddon
{
    // K9 Companion 1.1.0: isolated, display-only DOG UI hooks.
    // This family deliberately does not share the other optional mods' Harmony owner
    // or error handler: one unrelated small-UI patch failure must not erase the DOG tab.
    internal static class K9CompanionUiHooks
    {
        private const string AssemblyName = "K9Companion";
        private const string ProfileId = "jrs.k9companion";
        private const string Channel = "small_ui";
        private static readonly object Gate = new object();
        private static readonly HashSet<MethodBase> Installed = new HashSet<MethodBase>();
        private static readonly string[] DisplayFields =
        {
            "headerLine", "jacketNote", "behaviourLine", "approachLine", "armourLine",
            "kennelLevel", "kennelNext", "statusLine"
        };
        private sealed class LiteralScope
        {
            internal readonly string TypeName, MethodName;
            internal readonly int Parameters;
            internal readonly bool IncludeNested;
            internal LiteralScope(string type, string method = "*", int parameters = -1, bool nested = false)
            {
                TypeName = type;
                MethodName = method;
                Parameters = parameters;
                IncludeNested = nested;
            }
        }
        // Only the auxiliary K9 UI presenters need literal-level translation.
        // Never rewrite DogScreen.Build/ShowSlot/ShowStats (or any DogScreen methods):
        // these construct EFT inventory views and one failure aborts the rest of the
        // DOG panel. DogScreen's own Text/Status display helpers and Refresh postfix
        // already cover its static labels and assembled dynamic output, including
        // action delegates and the "PRESET " + number label.
        // The DOG tab uses a separate, exact "DOG" translator in AddTab only.
        private static readonly LiteralScope[] LiteralScopes =
        {
            new LiteralScope("K9Companion.KennelUi"),
            new LiteralScope("K9Companion.DogLobby"),
            new LiteralScope("K9Companion.RenameDialog", "Draw", 1),
            new LiteralScope("K9Companion.RenameDialog", "OnGUI", 0),
            new LiteralScope("K9Companion.DogInteraction"),
            new LiteralScope("K9Companion.DogCam"),
            new LiteralScope("K9Companion.Companion"),
            new LiteralScope("K9Companion.DogLobbyPanel", "*", -1, true),
            new LiteralScope("K9Companion.DogDeployRow", "*", -1, true),
        };
        private static Harmony patcher;
        private static EditableProfiles profiles;
        private static bool active;
        private static bool coreReady;
        private static FieldInfo[] displayFields;
        private static FieldInfo factsField, actionsField;

        internal static int Enable(Harmony harmony, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (harmony == null || data == null) return 0;
            if (!data.HasRules(ProfileId, Channel))
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:profile",
                    () => "K9Companion DOG translation disabled: jrs.k9companion small_ui profile missing or rejected. Check [Editable JSON] log messages.");
                return 0;
            }
            lock (Gate)
            {
                if (!active)
                {
                    patcher = harmony;
                    profiles = data;
                    active = true;
                    AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                }
            }
            var assembly = (assemblies ?? Enumerable.Empty<Assembly>()).FirstOrDefault(a => a != null &&
                string.Equals(a.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:late",
                    () => "K9Companion DOG translation awaiting K9Companion assembly load.");
                return 0;
            }
            return Patch(assembly);
        }

        // AssemblyLoad can fire before the upstream K9 plugin finishes initializing.
        // Re-check idempotently on the normal locale poll; never touch other assemblies.
        internal static void RetryPending()
        {
            lock (Gate) { if (!active || coreReady) return; }
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a != null &&
                    string.Equals(a.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase));
                if (assembly != null) Patch(assembly);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:retry",
                    () => "K9Companion DOG hook retry failed: " + ex.Message);
            }
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (active) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                active = false;
                coreReady = false;
                patcher = null;
                profiles = null;
                Installed.Clear();
                displayFields = null;
                factsField = actionsField = null;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                var assembly = args.LoadedAssembly;
                if (assembly != null && string.Equals(assembly.GetName().Name, AssemblyName,
                    StringComparison.OrdinalIgnoreCase)) Patch(assembly);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:late-error",
                    () => "K9Companion late-loaded UI hook failed: " + ex.Message);
            }
        }

        private static int Patch(Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var screen = assembly.GetType("K9Companion.DogScreen", false);
            if (screen == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:screen-type",
                    () => "K9Companion DOG translation unavailable: K9Companion.DogScreen type missing.");
                return 0;
            }
            var text = screen.GetMethods(flags).SingleOrDefault(m => m.DeclaringType == screen &&
                m.Name == "Text" && m.GetParameters().Length == 7 &&
                m.GetParameters()[0].ParameterType == typeof(string) &&
                m.ReturnType == typeof(TextMeshProUGUI));
            var refresh = screen.GetMethod("Refresh", flags, null, Type.EmptyTypes, null);
            var status = screen.GetMethod("Status", flags, null, new[] { typeof(string) }, null);

            displayFields = DisplayFields.Select(name => screen.GetField(name, flags)).Where(f => f != null).ToArray();
            factsField = screen.GetField("facts", flags);
            actionsField = screen.GetField("actions", flags);
            if (displayFields.Length != DisplayFields.Length || factsField == null || actionsField == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:fields",
                    () => "K9Companion DOG display fields changed: static labels can still translate; recheck dynamic fields.");
            }

            // Patch each source-audited display method independently. A changed signature must
            // not turn off the other UI methods, nor any unrelated translation profile.
            var total = 0;
            total += Install(text, nameof(BeforeText), true);
            total += Install(refresh, nameof(AfterRefresh), false);
            total += Install(status, nameof(BeforeText), true);
            // The upstream K9 Build creates the model before equipment/health views.
            // An exception in either of those optional EFT view adapters otherwise
            // aborts the remaining DOG screen (combat, care and kennel controls).
            // Guard ONLY those two source-verified, construction-only methods;
            // never swallow errors from the game's inventory transactions.
            total += InstallSectionRecovery(screen, "ShowSlot", 1);
            total += InstallSectionRecovery(screen, "ShowStats", 1);
            var tab = assembly.GetType("K9Companion.DogTab", false);
            var addTab = tab?.GetMethod("AddTab", flags);
            total += InstallTab(addTab);
            total += PatchLiteralScopes(assembly);
            // The number of installed *optional* literal hooks must never be mistaken
            // for success at the four essential DOG UI entry points.
            lock (Gate)
                coreReady = text != null && refresh != null && status != null && addTab != null &&
                    Installed.Contains(text) && Installed.Contains(refresh) &&
                    Installed.Contains(status) && Installed.Contains(addTab);
            if (!coreReady)
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:core-pending",
                    () => "K9Companion DOG essential hooks are incomplete; retrying on locale poll.");
            if (text == null || refresh == null || status == null || addTab == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:signature",
                    () => "K9Companion DOG translation partial: Text/Refresh/Status/AddTab method mismatch.");
            }
            return total;
        }

        // K9 1.1.0's DogScreen.Open catches Build exceptions but leaves a blank,
        // partially drawn window. Its Build invokes ShowSlot, then ShowStats,
        // then the controls. If one EFT adapter fails, keep building the *other*
        // controls and report the original cause. Normal executions are untouched.
        // This is recovery of the UI creation chain, not a repair of a broken
        // inventory-slot implementation. The actual K9 exception remains logged.
        private static int InstallSectionRecovery(Type screen, string methodName, int parameterCount)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            var method = screen.GetMethods(flags).SingleOrDefault(m =>
                m.DeclaringType == screen && m.Name == methodName &&
                m.GetParameters().Length == parameterCount && m.ReturnType == typeof(void));
            if (method == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:section-missing:" + methodName,
                    () => "DOG UI recovery could not find " + methodName + " on K9Companion 1.1.0.");
                return 0;
            }
            lock (Gate) { if (!active || patcher == null || !Installed.Add(method)) return 0; }
            try
            {
                var cb = typeof(K9CompanionUiHooks).GetMethod(nameof(AfterSectionFailure), BindingFlags.NonPublic | BindingFlags.Static);
                if (cb == null) throw new MissingMethodException(nameof(K9CompanionUiHooks), nameof(AfterSectionFailure));
                patcher.Patch(method, finalizer: new HarmonyMethod(cb) { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) Installed.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:section-patch:" + methodName,
                    () => "DOG UI section recovery patch failed for " + methodName + ": " + ex.Message);
                return 0;
            }
        }

        // A Harmony finalizer returning null suppresses only the exception raised
        // by ShowSlot/ShowStats. Build can then continue its next method. The
        // exception is logged once with its type, message and first stack frames.
        // Do not recover arbitrary DOG methods or change inventory transactions.
        private static Exception AfterSectionFailure(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null) return null;
            // This addon only changes the Korean UI. Preserve K9's original
            // exception semantics in English or other non-Korean modes.
            if (!active || !LocaleMode.IsKoreanMode()) return __exception;
            string name = __originalMethod?.Name ?? "Unknown";
            SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:section-failed:" + name,
                () => "K9 DOG " + name + " failed; remaining UI sections will still build. " + __exception);
            return null;
        }

        private static int PatchLiteralScopes(Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var count = 0;
            foreach (var scope in LiteralScopes)
            {
                var root = assembly.GetType(scope.TypeName, false);
                if (root == null) continue;
                var types = new List<Type> { root };
                if (scope.IncludeNested)
                {
                    try { types.AddRange(root.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)); }
                    catch { }
                }
                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try { methods = type.GetMethods(flags); }
                    catch { continue; }
                    foreach (var method in methods)
                    {
                        if (method == null || method.IsAbstract || method.ContainsGenericParameters) continue;
                        if (scope.MethodName != "*" && !string.Equals(method.Name, scope.MethodName, StringComparison.Ordinal)) continue;
                        if (scope.Parameters >= 0 && method.GetParameters().Length != scope.Parameters) continue;
                        if (!SmallUiLiteralHooks.ContainsTranslatableLiteral(method, ProfileId, profiles)) continue;
                        count += InstallLiteral(method);
                    }
                }
            }
            return count;
        }

        private static int InstallLiteral(MethodBase method)
        {
            lock (Gate) { if (!active || patcher == null || !Installed.Add(method)) return 0; }
            try
            {
                var cb = typeof(K9CompanionUiHooks).GetMethod(nameof(TranslateLiterals), BindingFlags.NonPublic | BindingFlags.Static);
                if (cb == null) throw new MissingMethodException(nameof(K9CompanionUiHooks), nameof(TranslateLiterals));
                patcher.Patch(method, transpiler: new HarmonyMethod(cb) { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) Installed.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:literal:" + method.DeclaringType?.FullName + "." + method.Name,
                    () => "K9Companion UI literal hook failed for " + method.Name + ": " + ex.Message);
                return 0;
            }
        }

        private static IEnumerable<CodeInstruction> TranslateLiterals(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string source &&
                    profiles?.CanTranslate(ProfileId, Channel, source) == true)
                    yield return new CodeInstruction(OpCodes.Call,
                        typeof(K9CompanionUiHooks).GetMethod(nameof(Translate), BindingFlags.NonPublic | BindingFlags.Static));
            }
        }

        private static int Install(MethodBase method, string callback, bool prefix)
        {
            if (method == null) return 0;
            lock (Gate) { if (!active || patcher == null || !Installed.Add(method)) return 0; }
            try
            {
                var cb = typeof(K9CompanionUiHooks).GetMethod(callback, BindingFlags.NonPublic | BindingFlags.Static);
                if (cb == null) throw new MissingMethodException(nameof(K9CompanionUiHooks), callback);
                var patch = new HarmonyMethod(cb) { priority = Priority.Last };
                if (prefix) patcher.Patch(method, prefix: patch);
                else patcher.Patch(method, postfix: patch);
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) Installed.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:patch:" + method.Name,
                    () => "K9Companion UI hook failed for " + method.Name + ": " + ex.Message);
                return 0;
            }
        }

        private static int InstallTab(MethodBase method)
        {
            if (method == null) return 0;
            lock (Gate) { if (!active || patcher == null || !Installed.Add(method)) return 0; }
            try
            {
                var cb = typeof(K9CompanionUiHooks).GetMethod(nameof(TranslateTabLiteral), BindingFlags.NonPublic | BindingFlags.Static);
                if (cb == null) throw new MissingMethodException(nameof(K9CompanionUiHooks), nameof(TranslateTabLiteral));
                patcher.Patch(method, transpiler: new HarmonyMethod(cb) { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) Installed.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:tab",
                    () => "K9Companion DOG tab label patch failed: " + ex.Message);
                return 0;
            }
        }

        // The upstream AddTab method assigns "DOG" to the copied tab's TMP_Text labels.
        // Change only this literal at that specific source method, not any EFT tab selector.
        private static IEnumerable<CodeInstruction> TranslateTabLiteral(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && string.Equals(instruction.operand as string, "DOG", StringComparison.Ordinal))
                    yield return new CodeInstruction(OpCodes.Call,
                        typeof(K9CompanionUiHooks).GetMethod(nameof(Translate), BindingFlags.NonPublic | BindingFlags.Static));
            }
        }

        // Fail open. K9's DogScreen.Open catches any error during Build and leaves
        // a partially constructed (apparently blank) panel on screen. Translation
        // must never be the exception that interrupts construction or user actions.
        private static string Translate(string source)
        {
            if (string.IsNullOrEmpty(source) || profiles == null) return source;
            try
            {
                if (!LocaleMode.IsKoreanMode()) return source;
                return profiles.Translate(ProfileId, Channel, source) ?? source;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:translation-exception",
                    () => "K9Companion display translation failed; keeping original text: " + ex.Message);
                return source;
            }
        }

        // Text is the K9 DOG screen's own TMP creation helper, not a global TMPro setter.
        private static void BeforeText(ref string __0)
        {
            if (__0 != null) __0 = Translate(__0);
        }

        private static void TranslateLabel(TMP_Text label)
        {
            if (label == null || string.IsNullOrEmpty(label.text)) return;
            var original = label.text;
            var translated = Translate(original);
            if (!string.Equals(original, translated, StringComparison.Ordinal)) label.text = translated;
        }

        private static void AfterRefresh(object __instance)
        {
            if (__instance == null || profiles == null || !LocaleMode.IsKoreanMode()) return;
            try
            {
                foreach (var field in displayFields ?? Array.Empty<FieldInfo>())
                    TranslateLabel(field.GetValue(__instance) as TMP_Text);
                var facts = factsField?.GetValue(__instance) as IEnumerable;
                if (facts != null) foreach (var item in facts) TranslateLabel(item as TMP_Text);
                var actions = actionsField?.GetValue(__instance) as IEnumerable;
                if (actions != null)
                {
                    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
                    foreach (var action in actions)
                    {
                        if (action == null) continue;
                        var t = action.GetType();
                        TranslateLabel(t.GetField("Title", flags)?.GetValue(action) as TMP_Text);
                        TranslateLabel(t.GetField("Detail", flags)?.GetValue(action) as TMP_Text);
                    }
                }
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("K9UI:refresh",
                    () => "K9Companion DOG final text refresh failed: " + ex.Message);
            }
        }
    }
}
