using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using SPT.EditableTranslations;
using UnityEngine;
using TMPro;

namespace SPT.ModKoreanAddon
{
    // K9 Companion 1.3.2: translate only the result of its OWN language lookup.
    // The source mod owns the UI, formatting, cached screen state and player choices.
    // Never edit the mod's lang folder, override ConfigEntry values, or re-enable 1.1.0 IL patches.
    internal static class K9CompanionLangHooks
    {
        private const string AssemblyName = "K9Companion";
        private const string ProfileId = "jrs.k9companion";
        private const string ExpectedVersion = "1.3.2";
        private const string NewRulePrefix = "k9-132-r002-";
        private const string RaidRulePrefix = "k9-132-r003-";
        private const string FinalRulePrefix = "k9-132-r005-";
        private const int DogOrderFirst = 7300;
        private const int DogOrderCount = 16;
        // Source-audited 1.3.2 kennel composites: the upstream Lang.T receives
        // interpolated names, remaining time and lock messages in one string.
        // These preexisting expressions are limited to their original K9 rules;
        // never enable all 1.1.0 regexes on the new language system.
        // Verbatim 1.1.0 rules whose English input is explicitly present and
        // meaning-checked in the 1.3.2 R002 DOG/kennel source catalogue.
        private static readonly HashSet<string> VerifiedLegacyExactIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "ANY ENEMY", "ARMOUR LOCKER", "BED",
            "Both bowls are full.", "Comes with you.", "DEFENDS YOU",
            "FEED DOG", "FILL BOWLS", "Fleece-lined bed",
            "Full speed", "GIVE WATER", "Goes for every enemy it detects, and whoever shoots at you.",
            "HEAL DOG", "Jacket presets", "Kennel",
            "Kennel care is for out of raids.", "Makeshift Kennel", "NEEDS DROP",
            "NOT BUILT", "No K9 veterinary kit in your stash, lockers or gear (Dr. Yang sells them).", "No armour",
            "No dog food or meat in your stash, lockers or gear.", "No dog food or meat to fill the bowl with.", "No dog yet",
            "No drinking water in your stash, lockers or gear.", "No jacket this raid.", "No water to fill the bowl with.",
            "None on a Scav raid", "ON ORDER", "OPEN JACKET",
            "One thing at a time.", "Only follows you: it can't attack or be hurt, and enemies ignore it.", "PREMIUM STORAGE",
            "Plain bed", "RECOVERY", "RENAME",
            "REPAIR", "Runs straight at them.", "SMELLS LOOT WITHIN",
            "Stays home this raid.", "TRAIN DOG", "TRAINING",
            "Tactical K9 Facility", "The dog isn't wearing an armoured jacket.", "The dog's gear isn't set up yet: restart the SPT server (the K9 Companion server mod sets it up).",
            "The jacket is in full repair.", "Thermal bedding", "UPGRADE BED",
            "VET STATION", "WHEN TO ATTACK", "Warns you about enemies, and attacks when you order Over There.",
            "Working Dog Station", "You don't have a dog yet. Dr. Yang's questline (Trust Is Earned) brings you one.", "click to wear  ·  shift-click to save the jacket on the dog",
            "fully upgraded", "in the raid with you", "out of raids only",
            "you have no dog yet", "k9-screen-101", "k9-screen-103",
            "k9-screen-105", "k9-screen-106", "k9-screen-107",
            "k9-screen-108", "k9-screen-109", "k9-screen-110",
            "k9-screen-111", "k9-screen-112", "k9-screen-114",
            "k9-screen-115", "k9-screen-116", "k9-screen-117",
            "k9-screen-156", "k9-screen-kits-50", "k9-screen-built"
        };
        // Additional exact English inputs verified in 1.3.2 raid, commands and camera.
        // These 1.1.0 rows were preserved in R002 but not activated there.
        private static readonly HashSet<string> VerifiedRaidLegacyExactIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "No dog.",
            "Survival needs are off.",
            "Your dog isn't with you.",
            "Cancel",
            "Choose a name (up to 16 characters).",
            "DOG",
            "Feed dog",
            "Follow me.",
            "Food and water are available at the hideout bed.",
            "Give water",
            "Give water to dog",
            "K9 Kennel upgrades",
            "Lie down.",
            "Move closer to pet your dog.",
            "Move closer to rename your dog.",
            "Move closer to the dishes.",
            "Open the kennel from its paw icon on the hideout overview.",
            "Please enter a name.",
            "Save",
            "Stay.",
            "NO SIGNAL",
            "Stays home: "
        };
        // These are the command captions in K9Companion.DogMenu.Labels, not game-wide gestures.
        private static readonly HashSet<string> DogMenuLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "Over there (attack)", "Come here", "Sit", "Lie down", "Open jacket", "Feed",
            "Give water", "Go to bed", "Stay", "Follow", "Search (removed)",
            "Fetch (removed)", "When to attack", "Approach", "Bring to me"
        };
        private static readonly HashSet<string> VerifiedLegacyComposites = new HashSet<string>(StringComparer.Ordinal)
        {
            "k9-screen-142", "k9-screen-143", "k9-screen-144", "k9-screen-145",
            "k9-screen-146", "k9-screen-147", "k9-screen-148", "k9-screen-149",
            "k9-screen-150", "k9-screen-151", "k9-screen-152", "k9-screen-153"
        };
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly object Gate = new object();
        private static Harmony harmony;
        private static TextRules clientText;
        private static FieldInfo languageEntry;
        private static bool active;
        private static bool patched;
        private static bool dogGroupPatched;
        private static bool bindNamePatched;
        private static FieldInfo dogGroupsField;
        private static FieldInfo dogHudValueField;
        private static FieldInfo dogShownTabField;
        private static FieldInfo dogCamLabelField;
        private static MethodInfo nativeLangMethod;
        private static string lastDogTabCaption;
        private static string lastDogCamCaption;
        private static string lastDogHudDown;
        private static string lastDogHudKilled;

        internal static int Enable(Harmony owner, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (owner == null || data == null) return 0;
            var doc = data.Documents.SingleOrDefault(d => d.mod_id == ProfileId);
            if (doc == null || !doc.channels.TryGetValue("small_ui", out var rows)) return 0;
            // Isolate 1.3.2 from unchecked 1.1.0 regex/segment translations.
            // Reuse original exact entries, source-audited kennel composites, and
            // new 1.3.2 rules. Only the K9-owned Lang.T result is transformed.
            var selected = rows.Where(r => r.enabled && !string.IsNullOrEmpty(r.translation) &&
                (r.id != null && (r.id.StartsWith(NewRulePrefix, StringComparison.Ordinal) ||
                                  r.id.StartsWith(RaidRulePrefix, StringComparison.Ordinal) ||
                                  r.id.StartsWith(FinalRulePrefix, StringComparison.Ordinal)) ||
                 (VerifiedLegacyExactIds.Contains(r.id) || VerifiedRaidLegacyExactIds.Contains(r.id)) &&
                 string.Equals(r.match_mode, "exact", StringComparison.Ordinal) ||
                 VerifiedLegacyComposites.Contains(r.id) ||
                 string.Equals(r.match_mode, "segment", StringComparison.Ordinal) &&
                 r.source == "ready to deploy")).ToArray();
            if (!selected.Any(r => r.id != null && r.id.StartsWith(NewRulePrefix, StringComparison.Ordinal))) return 0;
            lock (Gate)
            {
                if (active) return 0;
                clientText = new TextRules(selected);
                harmony = owner;
                active = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int total = 0;
            foreach (var a in assemblies ?? Enumerable.Empty<Assembly>()) total += TryPatch(a);
            return total;
        }

        internal static void RetryPending()
        {
            if (!active || patched) return;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (TryPatch(a) > 0) break;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (active) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                active = false;
                patched = false;
                harmony = null;
                clientText = null;
                languageEntry = null;
                dogGroupsField = null;
                dogHudValueField = null;
                dogShownTabField = null;
                dogCamLabelField = null;
                nativeLangMethod = null;
                lastDogTabCaption = null;
                lastDogCamCaption = null;
                lastDogHudDown = null;
                lastDogHudKilled = null;
                dogGroupPatched = false;
                bindNamePatched = false;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e)
        {
            try { TryPatch(e.LoadedAssembly); }
            catch (Exception ex) { Warn("late", ex); }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (assembly == null || !string.Equals(assembly.GetName().Name, AssemblyName,
                    StringComparison.OrdinalIgnoreCase)) return 0;
            var plugin = assembly.GetType("K9Companion.Plugin", false);
            var id = plugin?.GetField("Id", StaticFlags);
            var version = plugin?.GetField("Version", StaticFlags);
            if (id == null || !id.IsLiteral || id.FieldType != typeof(string) ||
                version == null || !version.IsLiteral || version.FieldType != typeof(string) ||
                !string.Equals(id.GetRawConstantValue() as string, ProfileId, StringComparison.Ordinal) ||
                !string.Equals(version.GetRawConstantValue() as string, ExpectedVersion, StringComparison.Ordinal)) return 0;

            var lang = assembly.GetType("K9Companion.Lang", false);
            var method = lang?.GetMethod("T", StaticFlags, null, new[] { typeof(string) }, null);
            var entry = plugin.GetField("Language", StaticFlags);
            if (method == null || !method.IsStatic || method.ReturnType != typeof(string) ||
                entry == null || !entry.IsStatic ||
                !typeof(ConfigEntry<string>).IsAssignableFrom(entry.FieldType))
            {
                Warn("signature", new MissingMethodException("K9 Companion 1.3.2 Lang.T/Plugin.Language signature changed"));
                return 0;
            }
            lock (Gate)
            {
                if (!active || patched || harmony == null) return 0;
                try
                {
                    harmony.Patch(method,
                        postfix: new HarmonyMethod(typeof(K9CompanionLangHooks), nameof(AfterTranslate)));
                    languageEntry = entry;
                    patched = true;
                    dogShownTabField = assembly.GetType("K9Companion.DogTab", false)?.GetField("shownTab", StaticFlags);
                    nativeLangMethod = method;
                    // The in-raid notices, DOG screen, lobby and camera already use Lang.T.
                    // The Y-menu DOG category and its F-key bind names bypass Lang.T.
                    // Patch only the verified K9-owned display creation/command IDs.
                    return 1 + TryPatchDogMenu(assembly) + TryPatchDogHud(assembly) + TryPatchDogRename(assembly) + TryPatchDogCam(assembly);
                }
                catch (Exception ex) { Warn("patch", ex); return 0; }
            }
        }

        private static int TryPatchDogMenu(Assembly assembly)
        {
            int count = 0;
            try
            {
                var menu = assembly.GetType("K9Companion.DogMenu", false);
                var addGroup = menu?.GetMethod("AddGroup", StaticFlags);
                var groups = menu?.GetField("Groups", StaticFlags);
                if (addGroup != null && addGroup.IsStatic && addGroup.ReturnType == typeof(void) &&
                    addGroup.GetParameters().Length == 1 && groups != null && groups.IsStatic)
                {
                    harmony.Patch(addGroup, postfix: new HarmonyMethod(typeof(K9CompanionLangHooks), nameof(AfterAddDogGroup)));
                    dogGroupsField = groups;
                    dogGroupPatched = true;
                    count++;
                }
                // GetCommandName normally returns K9's own "Dog: <label>" string for
                // 7300..7315. Restrict both the bound ID and exact original prefix.
                var binds = AccessTools.TypeByName("EFT.UI.Gestures.GestureCommands");
                var bind = binds?.GetMethod("GetCommandName", StaticFlags | BindingFlags.Instance,
                    null, new[] { typeof(int) }, null);
                if (bind != null && bind.ReturnType == typeof(string))
                {
                    harmony.Patch(bind, postfix: new HarmonyMethod(typeof(K9CompanionLangHooks), nameof(AfterDogBindName)));
                    bindNamePatched = true;
                    count++;
                }
            }
            catch (Exception ex) { Warn("raid-menu", ex); }
            return count;
        }

        // K9's health overlay assigns DOWN/KILLED without calling Lang.T.
        // Postfix changes only that overlay's final value text; health state and
        // its displayed timer, percentages, colours and internal cache are untouched.
        private static int TryPatchDogHud(Assembly assembly)
        {
            try
            {
                var hud = assembly.GetType("K9Companion.DogHud", false);
                var tick = hud?.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                var field = hud?.GetField("valueLabel", BindingFlags.Instance | BindingFlags.NonPublic);
                if (tick == null || tick.ReturnType != typeof(void) || tick.GetParameters().Length != 0 || field == null) return 0;
                harmony.Patch(tick, postfix: new HarmonyMethod(typeof(K9CompanionLangHooks), nameof(AfterDogHudUpdate)));
                dogHudValueField = field;
                return 1;
            }
            catch (Exception ex) { Warn("raid-hud", ex); return 0; }
        }

        private static void AfterDogHudUpdate(object __instance)
        {
            if (!active || dogHudValueField == null || clientText == null) return;
            try
            {
                var label = dogHudValueField.GetValue(__instance) as TMP_Text;
                if (label == null || string.IsNullOrEmpty(label.text)) return;
                var raw = label.text;
                var culture = LocaleMode.CurrentCulture();
                bool korean = UseKoreanDisplay();
                const string down = "DOWN";
                const string killed = "KILLED";
                string wantedDown = korean ? clientText.Translate(down, culture) : down;
                string wantedKilled = korean ? clientText.Translate(killed, culture) : killed;
                // K9's original health state is not touched. Detect only its
                // already-known status label prefixes (including kr-en mode),
                // preserving any original remaining-time suffix verbatim.
                string[] downSources = { down, lastDogHudDown, "쓰러짐" };
                foreach (var prefix in downSources)
                {
                    if (string.IsNullOrEmpty(prefix)) continue;
                    if (raw == prefix || raw.StartsWith(prefix + "  ", StringComparison.Ordinal))
                    {
                        label.text = wantedDown + raw.Substring(prefix.Length);
                        lastDogHudDown = wantedDown;
                        return;
                    }
                }
                string[] killedSources = { killed, lastDogHudKilled, "사망" };
                foreach (var prefix in killedSources)
                {
                    if (string.IsNullOrEmpty(prefix)) continue;
                    if (raw == prefix)
                    {
                        label.text = wantedKilled;
                        lastDogHudKilled = wantedKilled;
                        return;
                    }
                }
            }
            catch (Exception ex) { Warn("raid-hud-value", ex); }
        }

        private static readonly HashSet<string> RenameVisibleLiterals = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name your dog", "Choose a name (up to 16 characters).", "Save", "Cancel",
            "Please enter a name.", "Could not save the name. Please try again."
        };

        // RenameDialog uses GUI.ModalWindow/Label/Button directly; its captions
        // bypass K9.Lang.T. The transpiler wraps only these exact display
        // literals, retaining input control names, Save/Cancel branches and data.
        private static int TryPatchDogRename(Assembly assembly)
        {
            try
            {
                var dialog = assembly.GetType("K9Companion.RenameDialog", false);
                if (dialog == null) return 0;
                var onGui = dialog.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.NonPublic);
                var draw = dialog.GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic);
                if (onGui == null || draw == null || onGui.ReturnType != typeof(void) ||
                    draw.ReturnType != typeof(void) || onGui.GetParameters().Length != 0 ||
                    draw.GetParameters().Length != 1 || draw.GetParameters()[0].ParameterType != typeof(int)) return 0;
                var patch = new HarmonyMethod(typeof(K9CompanionLangHooks), nameof(TranslateRenameLiterals));
                harmony.Patch(onGui, transpiler: patch);
                harmony.Patch(draw, transpiler: patch);
                return 2;
            }
            catch (Exception ex) { Warn("raid-rename", ex); return 0; }
        }

        private static IEnumerable<CodeInstruction> TranslateRenameLiterals(IEnumerable<CodeInstruction> instructions)
        {
            var translator = AccessTools.Method(typeof(K9CompanionLangHooks), nameof(TranslateRenameLiteral));
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string literal &&
                    RenameVisibleLiterals.Contains(literal))
                    yield return new CodeInstruction(OpCodes.Call, translator);
            }
        }

        private static string TranslateRenameLiteral(string english)
        {
            try { return UseKoreanDisplay() ? clientText.Translate(english, LocaleMode.CurrentCulture()) : english; }
            catch (Exception ex) { Warn("raid-rename-text", ex); return english; }
        }

        // DisplayHooks calls this only after identifying K9's own assembly on
        // the notification call stack. Never override a user's non-Korean K9 choice.
        internal static bool AllowsKoreanNotice() => patched && languageEntry != null && UseKoreanDisplay();

        // The DOG inventory tab persists across open/close; upstream writes its
        // caption only on initial creation. Restore/update just that caption,
        // not its identifying GameObject.name or tab event handlers.
        private static void RefreshDogTabLabel()
        {
            if (dogShownTabField == null) return;
            var tab = dogShownTabField.GetValue(null) as Component;
            if (tab == null) return;
            var native = nativeLangMethod?.Invoke(null, new object[] { "DOG" }) as string ?? "DOG";
            var desired = UseKoreanDisplay() ? clientText.Translate("DOG", LocaleMode.CurrentCulture()) : native;
            foreach (var label in tab.GetComponentsInChildren<TMP_Text>(true))
                if (label != null && (label.text == "DOG" || label.text == "반려견" ||
                    label.text == native || label.text == lastDogTabCaption))
                    label.text = desired;
            lastDogTabCaption = desired;
        }

        // The original camera feed composes the recording indicator without
        // Lang.T. Update its label after upstream has finished the frame; retain
        // the blinking dot, camera name, dog name, distance and render schedule.
        private static int TryPatchDogCam(Assembly assembly)
        {
            try
            {
                var type = assembly.GetType("K9Companion.DogCam", false);
                var update = type?.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                var label = type?.GetField("label", BindingFlags.Instance | BindingFlags.NonPublic);
                if (update == null || update.ReturnType != typeof(void) || update.GetParameters().Length != 0 || label == null)
                    return 0;
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(K9CompanionLangHooks), nameof(AfterDogCamUpdate)));
                dogCamLabelField = label;
                return 1;
            }
            catch (Exception ex) { Warn("raid-cam", ex); return 0; }
        }

        private static void AfterDogCamUpdate(object __instance)
        {
            if (!active || dogCamLabelField == null || clientText == null) return;
            try
            {
                var label = dogCamLabelField.GetValue(__instance) as TMP_Text;
                if (label == null || string.IsNullOrEmpty(label.text)) return;
                const string english = "REC  K9 CAM  ";
                // The original text also holds a blinking indicator, dog name,
                // and distance; replace ONLY the exact camera caption segment.
                string desired = UseKoreanDisplay()
                    ? clientText.Translate("REC  K9 CAM", LocaleMode.CurrentCulture()) + "  "
                    : english;
                string existing = label.text;
                if (existing.Contains(english))
                    label.text = existing.Replace(english, desired);
                else if (!string.IsNullOrEmpty(lastDogCamCaption) && existing.Contains(lastDogCamCaption))
                    label.text = existing.Replace(lastDogCamCaption, desired);
                lastDogCamCaption = desired;
            }
            catch (Exception ex) { Warn("raid-cam-label", ex); }
        }

        private static bool UseKoreanDisplay()
        {
            if (!active || clientText == null || !LocaleMode.IsKoreanMode()) return false;
            var choice = (languageEntry?.GetValue(null) as ConfigEntry<string>)?.Value?.Trim();
            return string.IsNullOrEmpty(choice) ||
                   string.Equals(choice, "auto", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(choice, "kr", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(choice, "kr-en", StringComparison.OrdinalIgnoreCase);
        }

        // K9Companion.DogMenu.AddGroup(GesturesMenu) creates the category with
        // CreatePhraseGroup("DOG", ...). This category text bypasses Lang.T.
        // Its groups map belongs to K9, so no other menu/category is modified.
        private static void AfterAddDogGroup()
        {
            if (!active || !dogGroupPatched) return;
            try { RefreshDogGroupLabels(); }
            catch (Exception ex) { Warn("raid-group", ex); }
        }

        // Run during the existing 30-frame language poll: an already open menu
        // must be restored when leaving Korean, without rebuilding its buttons.
        internal static void RefreshDogGroupLabels()
        {
            if (!active) return;
            try { RefreshDogTabLabel(); }
            catch (Exception ex) { Warn("raid-tab-refresh", ex); }
            if (!dogGroupPatched || dogGroupsField == null) return;
            try
            {
                var groups = dogGroupsField.GetValue(null) as IDictionary;
                if (groups == null) return;
                bool korean = UseKoreanDisplay();
                string wanted = korean ? clientText.Translate("DOG", LocaleMode.CurrentCulture()) : "DOG";
                foreach (DictionaryEntry entry in groups)
                {
                    var root = entry.Value as RectTransform;
                    if (root == null) continue;
                    foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                        if (label != null && (label.text == "DOG" || label.text == "반려견"))
                            label.text = wanted;
                    // The EFT gesture prefab may use the older Unity UI label
                    // rather than TextMeshPro. Cover both without changing commands.
                    foreach (var label in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                        if (label != null && (label.text == "DOG" || label.text == "반려견"))
                            label.text = wanted;
                }
            }
            catch (Exception ex) { Warn("raid-group-refresh", ex); }
        }

        private static void AfterDogBindName(int __0, ref string __result)
        {
            if (!bindNamePatched || __0 < DogOrderFirst || __0 >= DogOrderFirst + DogOrderCount ||
                string.IsNullOrEmpty(__result) || !__result.StartsWith("Dog: ", StringComparison.Ordinal)) return;
            try
            {
                if (!UseKoreanDisplay()) return;
                var label = __result.Substring("Dog: ".Length);
                if (!DogMenuLabels.Contains(label)) return;
                var culture = LocaleMode.CurrentCulture();
                __result = clientText.Translate("DOG", culture) + ": " + clientText.Translate(label, culture);
            }
            catch (Exception ex) { Warn("raid-binds", ex); }
        }

        // Postfix deliberately never changes input text, key lookup, underlying native file,
        // or K9's own cache. Both Korean Patch Fix modes display Korean UI without
        // auto-appending English; explicit non-Korean K9 language selections win.
        private static void AfterTranslate(string __0, ref string __result)
        {
            if (!active || clientText == null || string.IsNullOrEmpty(__0) ||
                !LocaleMode.IsKoreanMode() ||
                !string.Equals(__0, __result, StringComparison.Ordinal)) return;
            try
            {
                if (!UseKoreanDisplay()) return;
                var culture = LocaleMode.CurrentCulture();
                // The lobby, dog command Y-menu and approach-mode captions build
                // nested strings before passing them to Lang.T. The upstream
                // language loader resolves their pieces recursively; our addon
                // must preserve that limited behaviour without changing K9's
                // input, menu command IDs, or underlying config values.
                string translated = clientText.Translate(__0, culture);
                foreach (var prefix in new[] { "Stays home: ", "Attack: ", "Approach: " })
                {
                    if (!__0.StartsWith(prefix, StringComparison.Ordinal) || __0.Length <= prefix.Length) continue;
                    string originalPart = __0.Substring(prefix.Length);
                    bool appendedPeriod = prefix == "Stays home: " && originalPart.EndsWith(".", StringComparison.Ordinal);
                    string plain = appendedPeriod ? originalPart.Substring(0, originalPart.Length - 1) : originalPart;
                    string inner = clientText.Translate(plain, culture);
                    if (!string.Equals(inner, plain, StringComparison.Ordinal))
                    {
                        string replacement = inner + (appendedPeriod ? "." : "");
                        translated = translated.Replace(originalPart, replacement);
                    }
                    break;
                }
                if (!string.IsNullOrEmpty(translated)) __result = translated;
            }
            catch (Exception ex) { Warn("display", ex); }
        }

        private static void Warn(string key, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("K9Lang132:" + key,
                () => "K9 Companion 1.3.2 language translation skipped: " + ex.Message);
        }
    }
}
