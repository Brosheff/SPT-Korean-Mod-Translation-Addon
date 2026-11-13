using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    // Hideout Last Stand R003 / 0.1.0-0.1.3. Only its own interaction-list results and
    // the final string passed to EFT's notification API are localized. Never
    // rewrite the Upgrade DTO, state/id, ItemRequirement, or action delegates.
    internal static class HideoutLastStandInteractionHooks
    {
        private const string ProfileId = "jrs.hideoutsiege";
        private const string Channel = "small_ui";
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static bool enabled;

        internal static int Enable(Harmony owner, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (data == null || !data.HasRules(ProfileId, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                harmony = owner;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int count = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) count += TryPatch(assembly);
            return count;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                Patched.Clear();
                harmony = null;
                profiles = null;
            }
        }

        // The four Results are newly constructed by the source on each query;
        // translating them in Postfix also refreshes their language on reopening.
        internal static void RefreshLanguage() { }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatch(args.LoadedAssembly); }
            catch (Exception e) { Warn("late", e); }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (assembly == null) return 0;
            var plugin = assembly.GetType("HideoutSiege.Plugin", false);
            if (plugin == null) return 0;
            var id = plugin.GetField("Id", BindingFlags.Public | BindingFlags.Static);
            var version = plugin.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            if (id == null || version == null || !id.IsLiteral || !version.IsLiteral ||
                !string.Equals(id.GetRawConstantValue() as string, ProfileId, StringComparison.Ordinal) ||
                !HideoutLastStandVersionGuard.IsSupported(version.GetRawConstantValue() as string)) return 0;

            var mannequin = assembly.GetType("HideoutSiege.SiegeMannequins", false);
            var part = assembly.GetType("HideoutSiege.SiegeMannequinPart", false);
            var station = assembly.GetType("HideoutSiege.SiegeStations", false);
            var stash = assembly.GetType("HideoutSiege.SiegeStash", false);
            var door = assembly.GetType("HideoutSiege.SiegeDoorUpgrades", false);
            var upgrade = assembly.GetType("HideoutSiege.SiegeDoorUpgrades+Upgrade", false);
            var swap = assembly.GetType("HideoutSiege.MagazineSwapPatch", false);
            var takeLoadout = assembly.GetType("HideoutSiege.TakeLoadoutPatch", false);
            var generatorInteraction = assembly.GetType("HideoutSiege.StationInteractionPatch", false);
            if (mannequin == null || part == null || station == null || stash == null || door == null || upgrade == null) return 0;

            var target = new List<MethodInfo>();
            MethodInfo actions = mannequin.GetMethods(All).SingleOrDefault(m => m.Name == "ActionsFor" &&
                m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == part);
            var stationActions = station.GetMethods(All).SingleOrDefault(m => m.Name == "ActionsFor" && m.GetParameters().Length == 2);
            var stashActions = stash.GetMethods(All).SingleOrDefault(m => m.Name == "ActionsFor" && m.GetParameters().Length == 1);
            var doorActions = door.GetMethod("For", All, null, Type.EmptyTypes, null);
            var expectedState = actions?.ReturnType;
            foreach (var method in new[] { actions, stationActions, stashActions, doorActions })
                if (method == null || method.IsAbstract || expectedState == null ||
                    expectedState.Name != "AvailableInteractionState" || method.ReturnType != expectedState)
                { Warn("signature", new MissingMethodException("R003 interaction result signature mismatch")); return 0; }
                else target.Add(method);
            // Only this source's own magazine-swap postfix; not the global EFT interaction method.
            MethodInfo swapAction = swap?.GetMethods(All).SingleOrDefault(m => m.Name == "Postfix" && m.ReturnType == typeof(void));
            // Two independent source Harmony patches also create screen text without
            // going through ActionsFor. Bind only to these source-owned methods.
            MethodInfo takeAction = takeLoadout?.GetMethods(All).SingleOrDefault(m =>
                m.Name == "Postfix" && m.ReturnType == typeof(void) && m.GetParameters().Length == 3 &&
                m.GetParameters()[2].ParameterType.IsByRef &&
                m.GetParameters()[2].ParameterType.GetElementType()?.Name == "AvailableInteractionState");
            MethodInfo generatorAction = generatorInteraction?.GetMethods(All).SingleOrDefault(m =>
                m.Name == "Prefix" && m.ReturnType == typeof(bool) && m.GetParameters().Length == 3 &&
                m.GetParameters()[2].ParameterType.IsByRef &&
                m.GetParameters()[2].ParameterType.GetElementType()?.Name == "AvailableInteractionState");
            var build = door.GetMethod("Build", All, null, new[] { upgrade }, null);
            lock (Gate)
            {
                if (!enabled || harmony == null || !Patched.Add(assembly)) return 0;
            }
            var installed = new List<MethodInfo>();
            try
            {
                foreach (var method in target)
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(HideoutLastStandInteractionHooks), nameof(AfterActions)));
                    installed.Add(method);
                }
            }
            catch (Exception e)
            {
                foreach (var method in installed)
                    try { harmony.Unpatch(method, HarmonyPatchType.Postfix, harmony.Id); } catch { }
                lock (Gate) Patched.Remove(assembly);
                Warn("actions", e);
                return 0;
            }
            if (swapAction != null)
            {
                try
                {
                    harmony.Patch(swapAction, postfix: new HarmonyMethod(typeof(HideoutLastStandInteractionHooks), nameof(AfterMagazineSwap)));
                    installed.Add(swapAction);
                }
                catch (Exception e) { Warn("magazine-swap", e); }
            }
            if (takeAction != null)
            {
                try
                {
                    harmony.Patch(takeAction, postfix: new HarmonyMethod(typeof(HideoutLastStandInteractionHooks), nameof(AfterTakeLoadout)));
                    installed.Add(takeAction);
                }
                catch (Exception e) { Warn("take-loadout", e); }
            }
            if (generatorAction != null)
            {
                try
                {
                    harmony.Patch(generatorAction, postfix: new HarmonyMethod(typeof(HideoutLastStandInteractionHooks), nameof(AfterGeneratorInteraction)));
                    installed.Add(generatorAction);
                }
                catch (Exception e) { Warn("generator-interaction", e); }
            }
            // This method creates all four self-authored warnings/success messages.
            // If its exact notification calls cannot be identified, preserve original.
            if (build != null)
            {
                try { harmony.Patch(build, transpiler: new HarmonyMethod(typeof(HideoutLastStandInteractionHooks), nameof(DoorNotificationTranspiler))); }
                catch (Exception e) { Warn("door-notifications", e); }
            }
            return installed.Count;
        }

        private static void Warn(string key, Exception e)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("hls-r003-" + key,
                () => "Hideout Last Stand R003 optional localization unavailable: " + key + ": " + e.Message);
        }

        private static object Read(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var field = type.GetField(name, All);
            if (field != null) return field.GetValue(target);
            return type.GetProperty(name, All)?.GetValue(target, null);
        }

        private static void Write(object target, string name, string value)
        {
            if (target == null) return;
            var type = target.GetType();
            var field = type.GetField(name, All);
            if (field != null) { if (!field.IsInitOnly) field.SetValue(target, value); return; }
            var prop = type.GetProperty(name, All);
            if (prop != null && prop.CanWrite) prop.SetValue(target, value, null);
        }

        private static void AfterActions(object __result)
        {
            if (!enabled || profiles == null || !LocaleMode.IsKoreanMode() || __result == null) return;
            try { TranslateInteraction(__result); }
            catch (Exception e) { Warn("result", e); }
        }

        private static void AfterMagazineSwap(object[] __args)
        {
            if (!enabled || profiles == null || !LocaleMode.IsKoreanMode() || __args == null) return;
            try
            {
                // The original source patch's 3rd argument is ref AvailableInteractionState.
                // Do not touch vanilla pickup actions or any unrelated names.
                if (__args.Length >= 3) TranslateInteraction(__args[2], onlyMagazineSwap: true);
            }
            catch (Exception e) { Warn("swap-result", e); }
        }

        // The source adds another mannequin action in TakeLoadoutPatch.Postfix.
        // Leave vanilla LootItem actions and the original executable delegates alone.
        private static void AfterTakeLoadout(object[] __args)
        {
            if (!enabled || profiles == null || !LocaleMode.IsKoreanMode() || __args == null) return;
            try
            {
                if (__args.Length < 3 || !(Read(__args[2], "Actions") is IEnumerable actions)) return;
                foreach (var action in actions)
                {
                    var original = Read(action, "Name") as string;
                    if (original == null || !original.StartsWith("Take Mannequin ", StringComparison.Ordinal) ||
                        !original.EndsWith("'s loadout", StringComparison.Ordinal)) continue;
                    var translated = TranslateDisplay(original);
                    if (!string.Equals(original, translated, StringComparison.Ordinal)) Write(action, "Name", translated);
                }
            }
            catch (Exception e) { Warn("take-loadout-result", e); }
        }

        // StationInteractionPatch.Prefix directly creates the generator action
        // and its cut-power error. No general EFT GetAvailableActions patch.
        private static void AfterGeneratorInteraction(object[] __args)
        {
            if (!enabled || profiles == null || !LocaleMode.IsKoreanMode() || __args == null) return;
            try
            {
                if (__args.Length < 3 || __args[2] == null) return;
                var state = __args[2];
                var error = Read(state, "Error") as string;
                if (string.Equals(error, "They've cut the power", StringComparison.Ordinal))
                    Write(state, "Error", TranslateDisplay(error));
                if (!(Read(state, "Actions") is IEnumerable actions)) return;
                foreach (var action in actions)
                {
                    var name = Read(action, "Name") as string;
                    if (name != "Turn on the generator" && name != "Turn off the generator") continue;
                    var translated = TranslateDisplay(name);
                    if (!string.Equals(name, translated, StringComparison.Ordinal)) Write(action, "Name", translated);
                }
            }
            catch (Exception e) { Warn("generator-result", e); }
        }

        private static void TranslateInteraction(object state, bool onlyMagazineSwap = false)
        {
            if (state == null) return;
            if (!onlyMagazineSwap)
            {
                var error = Read(state, "Error") as string;
                if (error != null) Write(state, "Error", TranslateDisplay(error));
            }
            if (!(Read(state, "Actions") is IEnumerable actions)) return;
            foreach (var action in actions)
            {
                var name = Read(action, "Name") as string;
                if (string.IsNullOrEmpty(name)) continue;
                if (onlyMagazineSwap && (!name.StartsWith("Swap for your ", StringComparison.Ordinal) ||
                                         !name.EndsWith(" magazine", StringComparison.Ordinal))) continue;
                var translated = TranslateDisplay(name);
                if (!string.Equals(name, translated, StringComparison.Ordinal)) Write(action, "Name", translated);
                // Action/Disabled/TargetName and the game's selected-action index are never modified.
            }
        }

        // Visible text only; preserve numeric values. One of the two source
        // methods represents durations as "1 h 00 min", "2 min", or "3 s".
        private static string TranslateDisplay(string english)
        {
            if (!enabled || profiles == null || english == null || !LocaleMode.IsKoreanMode()) return english;
            string displayed = profiles.Translate(ProfileId, Channel, english);
            if (string.Equals(displayed, english, StringComparison.Ordinal)) return english;
            if (english.StartsWith("Build time: ", StringComparison.Ordinal) ||
                english.IndexOf(": being built", StringComparison.Ordinal) >= 0 ||
                english.StartsWith("Building the ", StringComparison.Ordinal))
            {
                displayed = Regex.Replace(displayed, @"(?<h>\d+) h (?<m>\d{2}) min", "${h}시간 ${m}분", RegexOptions.CultureInvariant);
                displayed = Regex.Replace(displayed, @"(?<n>\d+) min", "${n}분", RegexOptions.CultureInvariant);
                displayed = Regex.Replace(displayed, @"(?<n>\d+) s(?!\w)", "${n}초", RegexOptions.CultureInvariant);
            }
            return displayed;
        }

        private static IEnumerable<CodeInstruction> DoorNotificationTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var notify = AccessTools.Method(typeof(HideoutLastStandInteractionHooks), nameof(TranslateDisplay));
            int insertions = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i].operand is MethodInfo method) ||
                    (list[i].opcode != OpCodes.Call && list[i].opcode != OpCodes.Callvirt) ||
                    method.DeclaringType?.FullName != "EFT.UI.NotificationManager" ||
                    (method.Name != "DisplayWarningNotification" && method.Name != "DisplayMessageNotification")) continue;
                var pars = method.GetParameters();
                if (!method.IsStatic || pars.Length != 1 || pars[0].ParameterType != typeof(string))
                    throw new InvalidOperationException("Changed notification signature");
                list.Insert(i++, new CodeInstruction(OpCodes.Call, notify));
                insertions++;
            }
            if (insertions != 4) throw new InvalidOperationException("Expected four own notification calls, found " + insertions);
            return list;
        }
    }
}
