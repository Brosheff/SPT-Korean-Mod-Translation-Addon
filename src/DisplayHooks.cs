using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    internal static class DisplayHooks
    {
        private static EditableProfiles profiles;

        // Notification text is translated only when the call stack belongs to one of these
        // BepInEx plugins. The GUID is more stable than an assembly/file name across releases.
        private static readonly Dictionary<string, string> ProfileByPluginGuid =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev.oogabooga.spt-vagabond"] = "Vagabond",
                ["com.Amanda.Graphics"] = "AmandsGraphics",
                ["Mattexe.BossNotifier"] = "BossNotifier",
                ["com.choochoo.tradermodding"] = "ChooChoo.TraderModding",
                ["com.janky.hollywoodfx"] = "HollywoodFX",
                ["ekky.raidreview"] = "RaidReview",
                ["com.ozen.continuousloadammo"] = "ContinuousLoadAmmo",
                ["com.maschine.UnloadAllMagazines"] = "UnloadAllMagazines",
                ["com.tyfon.weaponcustomizer"] = "WeaponCustomizer",
                ["com.acidphantasm.stattrack"] = "StatTrack",
                ["com.acidphantasm.botplacementsystem"] = "BotPlacementSystem",
                ["com.ozen.magcheckinterrupt"] = "MagCheckInterrupt",
                ["com.jbobyh.itempreviewqol"] = "ItemPreviewQoL",
                ["com.lacyway.csf"] = "QuickSellFlea",
                ["katrin0522.FastSellInFlea"] = "FastSellInFlea",
                ["com.Tangh.CookingGrenades"] = "CookingGrenades",
                ["com.mpstark.dynamicmaps"] = "DynamicMaps",
                ["flir.iof"] = "InventoryOrganizingFeatures",
                ["com.danw.questingbots"] = "QuestingBots",
                ["com.tyfon.uifixes"] = "UIFixes",
                ["com.manimal.icebreaker"] = "Icebreaker",
                ["com.tyfon.hideoutinprogress"] = "HideoutInProgress"
            };

        private static readonly Dictionary<Assembly, string> ProfileByAssembly =
            new Dictionary<Assembly, string>();
        private static readonly object PluginMapLock = new object();

        internal static int Enable(Harmony harmony, Assembly game, Assembly[] assemblies, EditableProfiles data)
        {
            profiles = data;
            RebuildPluginAssemblyMap();
            var count = 0;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type[] types;
            try { types = game.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            // EFT notifications. We patch the common display methods once, then scope each call
            // by the originating BepInEx plugin GUID before consulting that mod's JSON profile.
            foreach (var method in types.SelectMany(t => t.GetMethods(flags)).Distinct().Where(m =>
                m.IsStatic &&
                (m.Name == "DisplayWarningNotification" || m.Name == "DisplayMessageNotification" || m.Name == "DisplayErrorNotification") &&
                m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(string)))
            {
                harmony.Patch(method, prefix: Hook(nameof(BeforeNotification)));
                count++;
            }

            // Some mods use the full-screen EFT error panel instead of NotificationManager.
            // Restrict this to PreloaderUI and only translate title/body for a known caller plugin.
            foreach (var method in types.Where(t => t.Name == "PreloaderUI")
                         .SelectMany(t => t.GetMethods(flags)).Distinct().Where(m =>
                             m.Name == "ShowErrorScreen" &&
                             m.GetParameters().Length >= 2 &&
                             m.GetParameters()[0].ParameterType == typeof(string) &&
                             m.GetParameters()[1].ParameterType == typeof(string)))
            {
                harmony.Patch(method, prefix: Hook(nameof(BeforeErrorScreen)));
                count++;
            }

            // Vagabond owns a custom modal service. Keep its existing dedicated dialog path.
            var vagabond = assemblies.Select(a => a.GetType("Vagabond.Client.Services.UIMessageService", false)).FirstOrDefault(t => t != null);
            var dialog = vagabond?.GetMethod("ConfigureDialog", BindingFlags.NonPublic | BindingFlags.Instance);
            if (dialog != null && dialog.GetParameters().Take(4).All(p => p.ParameterType == typeof(string)))
            {
                harmony.Patch(dialog, prefix: Hook(nameof(BeforeDialog)));
                count++;
            }

            return count;
        }

        private static HarmonyMethod Hook(string name) =>
            new HarmonyMethod(typeof(DisplayHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

        private static void BeforeNotification(ref string __0)
        {
            if (string.IsNullOrEmpty(__0)) return;
            var culture = LocaleMode.Refresh();
            if (!LocaleMode.IsKoreanCulture(culture)) return;

            try
            {
                var input = __0;
                var profile = FindCallingPluginProfile();
                if (string.Equals(profile, "BossNotifier", StringComparison.Ordinal) &&
                    (profiles.HasRules(profile, "notifications") || profiles.HasRules(profile, "notification_terms")))
                    input = TranslateBossNotifierNotification(input, culture);
                else if (profile != null && profiles.HasRules(profile, "notifications"))
                    input = profiles.Translate(profile, "notifications", input, culture);

                // Backend-originated errors have no client-mod stack frame. These channels are
                // deliberately explicit and remain scoped by exact source strings in their JSON.
                input = profiles.Translate("Vagabond", "server_errors", input, culture);
                input = profiles.Translate("Croupier", "server_errors", input, culture);
                __0 = input;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("DisplayHooks:122", () => "Notification kept unchanged: " + ex.Message);
            }
        }

        private static string TranslateBossNotifierNotification(string input, string culture)
        {
            // BossNotifier keeps boss/location names as dynamic placeholders. For bilingual mode,
            // localize those terms only in the Korean half, then append the untouched English
            // source once. This avoids rewriting names/locations inside the English half.
            if (string.Equals(culture, LocaleMode.Bilingual, StringComparison.OrdinalIgnoreCase))
            {
                var korean = profiles.Translate("BossNotifier", "notifications", input, LocaleMode.Korean);
                korean = profiles.Translate("BossNotifier", "notification_terms", korean, LocaleMode.Korean);
                return LocaleText.Compose(korean, input);
            }

            var translated = profiles.Translate("BossNotifier", "notifications", input, culture);
            return profiles.Translate("BossNotifier", "notification_terms", translated, culture);
        }

        private static void BeforeErrorScreen(ref string __0, ref string __1)
        {
            var culture = LocaleMode.Refresh();
            if (!LocaleMode.IsKoreanCulture(culture)) return;

            try
            {
                var profile = FindCallingPluginProfile();
                if (profile == null || !profiles.HasRules(profile, "error_screens")) return;
                __0 = profiles.Translate(profile, "error_screens", __0, culture);
                __1 = profiles.Translate(profile, "error_screens", __1, culture);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("DisplayHooks:156", () => "Error screen kept unchanged: " + ex.Message);
            }
        }

        private static void BeforeDialog(ref string __0, ref string __1, ref string __2, ref string __3)
        {
            var culture = LocaleMode.Refresh();
            if (!LocaleMode.IsKoreanCulture(culture)) return;
            try
            {
                var title = profiles.Translate("Vagabond", "dialogs", __0, culture);
                var body = profiles.Translate("Vagabond", "dialogs", __1, culture);
                var primary = profiles.Translate("Vagabond", "dialogs", __2, culture);
                var secondary = profiles.Translate("Vagabond", "dialogs", __3, culture);
                __0 = title;
                __1 = body;
                __2 = primary;
                __3 = secondary;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("DisplayHooks:177", () => "Dialog kept unchanged: " + ex.Message);
            }
        }

        private static string FindCallingPluginProfile()
        {
            var frames = new StackTrace().GetFrames();
            if (frames == null) return null;

            foreach (var frame in frames)
            {
                var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
                if (assembly == null) continue;

                string profile;
                lock (PluginMapLock)
                {
                    if (ProfileByAssembly.TryGetValue(assembly, out profile))
                        return profile;
                }

                // A plugin loaded after Start() may not be in the first map. Resolve it lazily.
                profile = ResolveProfileForAssembly(assembly);
                if (profile != null)
                {
                    lock (PluginMapLock) ProfileByAssembly[assembly] = profile;
                    return profile;
                }
            }

            return null;
        }

        private static void RebuildPluginAssemblyMap()
        {
            lock (PluginMapLock)
            {
                ProfileByAssembly.Clear();
                foreach (var pair in Chainloader.PluginInfos)
                {
                    if (!ProfileByPluginGuid.TryGetValue(pair.Key, out var profile)) continue;
                    var instance = pair.Value?.Instance;
                    if (instance == null) continue;
                    ProfileByAssembly[instance.GetType().Assembly] = profile;
                }
            }
        }

        private static string ResolveProfileForAssembly(Assembly assembly)
        {
            foreach (var pair in Chainloader.PluginInfos)
            {
                var instance = pair.Value?.Instance;
                if (instance == null || instance.GetType().Assembly != assembly) continue;
                return ProfileByPluginGuid.TryGetValue(pair.Key, out var profile) ? profile : null;
            }
            return null;
        }
    }
}
