using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Manimal Terminal 0.2.2 — only ten source-audited methods in its exact client assembly.
    // No game-wide Unity, TMP, IMGUI, or arbitrary third-party method scans.
    internal static class ManimalTerminalUiHooks
    {
        private const string Profile = "com.manimal.terminal";
        private const string Channel = "small_ui";
        private const string AssemblyName = "ManimalTerminalClient";
        private static EditableProfiles profiles;
        private static bool patched;
        private static FieldInfo configField;
        private static FieldInfo nextButtonField;

        internal static int Enable(Harmony patcher, EditableProfiles data, Assembly assembly)
        {
            if (patched || patcher == null || data == null || assembly == null ||
                !string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase) ||
                !data.HasRules(Profile, Channel)) return 0;

            var subs = assembly.GetType("Manimal.Terminal.TerminalSubtitles", false);
            var epi = assembly.GetType("Manimal.Terminal.TerminalEpilogueScreen+EpilogueRunner", false);
            var pump = assembly.GetType("Manimal.Terminal.Patch_PumpSwitchActions", false);
            var gate = assembly.GetType("Manimal.Terminal.Patch_GateSwitchActions", false);
            var hold = assembly.GetType("Manimal.Terminal.GateHoldSession", false);
            if (subs == null || epi == null || pump == null || gate == null || hold == null) return 0;
            profiles = data;
            configField = AccessTools.Field(epi, "_cfg");
            nextButtonField = AccessTools.Field(epi, "_nextBtnClone");

            // Resolve all signatures before patching; all ten targets must be present.
            var methods = new[]
            {
                Resolve(subs, "DrawPanel", 1),
                Resolve(epi, "LoadConfig", 0),
                Resolve(epi, "MakeText", 5),
                Resolve(epi, "CloneShellButton", 5),
                Resolve(epi, "TryClonePrestigeRewardCard", 7),
                Resolve(epi, "BuildFallbackRewardCard", 4),
                Resolve(epi, "UpdateButtonVisibility", 1),
                Resolve(pump, "Postfix", 3),
                Resolve(gate, "Postfix", 3),
                Resolve(hold, "Update", 0)
            };
            if (configField == null || nextButtonField == null || methods.Any(m => m == null))
                return 0;

            var hooks = new[]
            {
                nameof(BeforeSubtitle), nameof(AfterLoadConfig), nameof(BeforeMakeText),
                nameof(BeforeCloneButton), nameof(BeforeRewardPrefab),
                nameof(BeforeFallbackReward), nameof(AfterButtonVisibility),
                nameof(AfterInteractionActions), nameof(AfterInteractionActions), nameof(BeforeHoldUpdate)
            };
            try
            {
                for (var i = 0; i < methods.Length; i++)
                {
                    var callback = AccessTools.Method(typeof(ManimalTerminalUiHooks), hooks[i]);
                    if (callback == null) return i;
                    patcher.Patch(methods[i],
                        prefix: i == 1 || (i >= 6 && i <= 8) ? null : new HarmonyMethod(callback),
                        postfix: i == 1 || (i >= 6 && i <= 8) ? new HarmonyMethod(callback) : null);
                }
                patched = true;
                return methods.Length;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("ManimalTerminalUiHooks:Patch",
                    () => "Manimal Terminal UI hooks could not be installed: " + ex.Message);
                return 0;
            }
        }

        internal static void Disable()
        {
            profiles = null;
            patched = false;
            configField = null;
            nextButtonField = null;
        }

        private static MethodInfo Resolve(Type type, string name, int args)
        {
            var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name == name && m.GetParameters().Length == args).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        private static string Localize(string text)
        {
            if (string.IsNullOrEmpty(text) || profiles == null) return text;
            return profiles.Translate(Profile, Channel, text);
        }

        private static void BeforeSubtitle(ref string __0) { __0 = Localize(__0); }
        private static void BeforeMakeText(ref string __2) { __2 = Localize(__2); }
        private static void BeforeCloneButton(ref string __1) { __1 = Localize(__1); }
        private static void BeforeRewardPrefab(ref string __2, ref string __3)
        {
            __2 = Localize(__2);
            __3 = Localize(__3);
        }
        private static void BeforeFallbackReward(ref string __2, ref string __3)
        {
            __2 = Localize(__2);
            __3 = Localize(__3);
        }

        // The source mod's two Postfix methods receive
        // ref EFT.UI.AvailableInteractionState as argument 0. This addon does not
        // reference Assembly-CSharp (EFT), so use Harmony's supported __args
        // injection instead of declaring an unresolved compile-time EFT type.
        // Only inspect actions on the mod's own Terminal scene switches.
        private static void AfterInteractionActions(object[] __args)
        {
            if (profiles == null || __args == null || __args.Length < 3 || __args[0] == null) return;
            try
            {
                var sceneSwitch = __args[2] as Component;
                if (sceneSwitch == null || !sceneSwitch.gameObject.scene.name.StartsWith(
                        "Terminal", StringComparison.OrdinalIgnoreCase)) return;
                var state = __args[0];
                var stateType = state.GetType();
                var actionsProperty = AccessTools.Property(stateType, "Actions");
                var actionsField = actionsProperty == null ? AccessTools.Field(stateType, "Actions") : null;
                var actions = (actionsProperty != null ? actionsProperty.GetValue(state) :
                    actionsField?.GetValue(state)) as System.Collections.IList;
                if (actions == null) return;
                for (var i = 0; i < actions.Count; i++)
                {
                    var item = actions[i];
                    if (item == null) continue;
                    var nameProperty = AccessTools.Property(item.GetType(), "Name");
                    var nameField = nameProperty == null ? AccessTools.Field(item.GetType(), "Name") : null;
                    var name = nameProperty != null ? nameProperty.GetValue(item) as string :
                        nameField?.GetValue(item) as string;
                    var localized = Localize(name);
                    if (string.Equals(name, localized, StringComparison.Ordinal)) continue;
                    if (nameProperty != null && nameProperty.CanWrite) nameProperty.SetValue(item, localized);
                    else nameField?.SetValue(item, localized);
                    actions[i] = item;
                }
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("ManimalTerminalUiHooks:Interaction",
                    () => "Manimal Terminal interaction text left unchanged: " + ex.Message);
            }
        }

        // GateHoldSession owns its floating progress text. Modify only its own
        // field before the first Update/ShowObjectivesPanel, not EFT's global UI.
        private static void BeforeHoldUpdate(object __instance)
        {
            if (__instance == null || profiles == null) return;
            var f = AccessTools.Field(__instance.GetType(), "PanelText");
            if (f == null) return;
            var source = f.GetValue(__instance) as string;
            var translated = Localize(source);
            if (!string.Equals(source, translated, StringComparison.Ordinal))
                f.SetValue(__instance, translated);
        }

        // EpilogueRunner loads the authored JSON into JObject. Translate only its visible
        // values after parsing, retaining the exact kind and Item/Dogtag identifiers used
        // for slide dispatch and reward-card backgrounds. Never modify the source file.
        private static void AfterLoadConfig(object __instance, bool __result)
        {
            if (!__result || __instance == null || profiles == null) return;
            var root = configField?.GetValue(__instance) as JObject;
            if (root != null) TranslateJson(root);
        }

        private static void TranslateJson(JToken node)
        {
            if (node is JObject obj)
            {
                foreach (var p in obj.Properties())
                {
                    switch (p.Name)
                    {
                        case "_comment": case "kind": case "type": case "id":
                        case "image": case "icon": case "sprite": case "path":
                        case "music": case "background": case "medal":
                        case "achievement_id": case "action":
                            continue;
                    }
                    if (p.Value.Type == JTokenType.String)
                    {
                        var current = p.Value.Value<string>();
                        var translated = Localize(current);
                        if (!string.Equals(current, translated, StringComparison.Ordinal))
                            p.Value = new JValue(translated);
                    }
                    else TranslateJson(p.Value);
                }
            }
            else if (node is JArray array)
                foreach (var child in array) TranslateJson(child);
        }

        // The upstream mod overwrites NEXT/OK after the button has been cloned.
        // Restore just that one EpilogueRunner-owned button's translated label.
        private static void AfterButtonVisibility(object __instance, string __0)
        {
            if (__instance == null || profiles == null) return;
            try
            {
                var component = nextButtonField?.GetValue(__instance) as Component;
                if (component == null) return;
                var setText = AccessTools.Method(component.GetType(), "SetHeaderText", new[] { typeof(string) });
                setText?.Invoke(component, new object[] { Localize(__0 == "thanks" ? "OK" : "NEXT") });
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("ManimalTerminalUiHooks:NextLabel",
                    () => "Manimal Terminal next button text left unchanged: " + ex.Message);
            }
        }
    }
}
