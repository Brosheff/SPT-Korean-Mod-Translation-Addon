using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPT.ModKoreanAddon
{
    // The original mod updates the countdown every second; translating its initial
    // locale string is not enough. Patch only its own render method, after it writes.
    internal static class QuartermasterRuntimeHooks
    {
        private const string ClientAssembly = "TheQuartermaster.Client";
        private static readonly object Gate = new object();
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static QuartermasterContractLocales catalog;
        private static Harmony harmony;
        private static bool enabled;

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> loaded, QuartermasterContractLocales contracts)
        {
            if (patcher == null || contracts == null) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                enabled = true;
                harmony = patcher;
                catalog = contracts;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            var count = 0;
            foreach (var asm in loaded ?? Enumerable.Empty<Assembly>())
                count += TryPatch(asm);
            return count;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                harmony = null;
                catalog = null;
                Patched.Clear();
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { TryPatch(args.LoadedAssembly); }
            catch (Exception ex) { Warn("Quartermaster late-load", ex); }
        }

        private static int TryPatch(Assembly asm)
        {
            if (asm == null) return 0;
            var name = asm.GetName().Name;
            if (name != ClientAssembly && name != "Assembly-CSharp") return 0;
            var count = 0;
            try
            {
                if (name == ClientAssembly)
                {
                    var countdown = asm.GetType("TheQuartermaster.Client.Patches.LiveCountdownBehaviour", false);
                    var method = countdown?.GetMethod("UpdateText", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    count += Patch(method, nameof(AfterCountdown));

                    var panel = asm.GetType("TheQuartermaster.Client.UI.CommunityPanel", false);
                    foreach (var methodName in new[] { "PopulateSubmissionList", "CreateSubmissionRow", "ShowSubmissionDetails" })
                    {
                        var render = panel?.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        count += Patch(render, nameof(AfterCommunityRender));
                    }
                }
                else
                {
                    var notes = asm.GetType("EFT.UI.NotesTaskDescription", false);
                    if (notes != null)
                    {
                        foreach (var method in notes.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            var args = method.GetParameters();
                            if (method.Name == "Show" && args.Length == 2 && args[0].ParameterType.FullName == "EFT.Quests.Quest")
                                count += Patch(method, nameof(AfterQuestDescription));
                        }
                    }
                }
            }
            catch (Exception ex) { Warn("Quartermaster patch setup", ex); }
            return count;
        }

        private static int Patch(MethodBase target, string callback)
        {
            if (target == null) return 0;
            lock (Gate)
            {
                if (!enabled || harmony == null || !Patched.Add(target)) return 0;
            }
            try
            {
                var method = typeof(QuartermasterRuntimeHooks).GetMethod(callback, BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(target, postfix: new HarmonyMethod(method) { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) Patched.Remove(target);
                Warn("Quartermaster " + target.DeclaringType?.FullName + "." + target.Name, ex);
                return 0;
            }
        }

        private static void AfterCountdown(object __instance)
        {
            if (!LocaleMode.IsKoreanCulture(LocaleMode.CurrentCulture()) || __instance == null) return;
            try
            {
                var field = AccessTools.Field(__instance.GetType(), "_tmpText");
                var text = field?.GetValue(__instance) as TMP_Text;
                if (text != null)
                {
                    // Upstream recreates the *entire* description from its saved English
                    // base on every tick, so reapply both description and countdown.
                    var value = catalog?.TranslateKnownDescription(text.text, LocaleMode.CurrentCulture()) ?? text.text;
                    text.text = QuartermasterContractLocales.TranslateCountdown(value);
                }
            }
            catch (Exception ex) { Warn("Quartermaster countdown", ex); }
        }

        private static void AfterQuestDescription(object __instance)
        {
            if (!LocaleMode.IsKoreanCulture(LocaleMode.CurrentCulture()) || __instance == null || catalog == null) return;
            try
            {
                var field = AccessTools.Field(__instance.GetType(), "_description");
                var text = field?.GetValue(__instance) as TMP_Text;
                if (text == null) return;
                var source = text.text;
                var translated = catalog.TranslateKnownDescription(source, LocaleMode.CurrentCulture());
                if (source != translated) text.text = translated;
                // Leave the QM_EXPIRY token and initial English label intact here;
                // the original timer scans them and the countdown hook translates the rendered result.
            }
            catch (Exception ex) { Warn("Quartermaster quest description", ex); }
        }

        private static void AfterCommunityRender(object __instance)
        {
            if (!LocaleMode.IsKoreanCulture(LocaleMode.CurrentCulture()) || __instance == null || catalog == null) return;
            try
            {
                // CommunityPanel is DontDestroyOnLoad: only scan its own identified UI
                // containers, never its entire parent tree or arbitrary player text fields.
                var type = __instance.GetType();
                foreach (var name in new[] { "_submissionListContainer", "_detailsRows" })
                {
                    var root = AccessTools.Field(type, name)?.GetValue(__instance) as Transform;
                    if (root == null) continue;
                    foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true)) Translate(tmp);
                    foreach (var label in root.GetComponentsInChildren<Text>(true)) Translate(label);
                }
                foreach (var name in new[] { "_statusText", "_rightSideText" })
                {
                    var label = AccessTools.Field(type, name)?.GetValue(__instance) as Text;
                    if (label != null) Translate(label);
                }
            }
            catch (Exception ex) { Warn("Quartermaster community panel", ex); }
        }
        private static void Translate(TMP_Text tmp)
        {
            if (tmp == null || catalog == null) return;
            var original = tmp.text;
            var result = catalog.TranslateCommunityText(original);
            if (original != result) tmp.text = result;
        }
        private static void Translate(Text label)
        {
            if (label == null || catalog == null) return;
            var original = label.text;
            var result = catalog.TranslateCommunityText(original);
            if (original != result) label.text = result;
        }
        private static void Warn(string label, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("QuartermasterRuntimeHooks:" + label,
                () => label + " hook skipped: " + ex.Message);
        }
    }
}
