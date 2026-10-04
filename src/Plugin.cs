using System;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    [BepInPlugin("spt.korean.modtranslation.addon", "SPT Mod Korean Addon", "1.0.0")]
    [BepInDependency("com.GoLani.koreanpatchfix", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.mpstark.dynamicmaps", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private Harmony harmony;
        private Harmony displayHarmony;
        private Harmony modUiHarmony;
        private Harmony configManagerHarmony;
        private Harmony smallUiHarmony;
        private Harmony dynamicMapsHarmony;
        private Harmony casinoHarmony;
        private bool initialized;
        private EditableProfiles data;
        private string lastCulture;
        private string lastServerCulture;
        private int localePollFrames;
        private int serverCultureRetryPolls;
        private bool serverCultureWarningLogged;

        private void Awake()
        {
            try
            {
                var original = BepInEx.Bootstrap.Chainloader.PluginInfos["com.GoLani.koreanpatchfix"];
                if (original.Metadata.Version != new Version(2, 1, 1))
                    throw new InvalidOperationException("Original Korean Patch Fix 2.1.1 is required. Restore it before using this addon.");
                var game = AppDomain.CurrentDomain.BaseDirectory;
                var server = new[] { "SPT_Runtime", "SPT", "" }.Select(n => Path.Combine(game, n, "SPT.Server.exe")).FirstOrDefault(File.Exists);
                var version = server == null ? null : System.Diagnostics.FileVersionInfo.GetVersionInfo(server);
                if (version == null || version.FileMajorPart != 4 || version.FileMinorPart != 1 || version.FileBuildPart != 6)
                    throw new InvalidOperationException("This addon release requires SPT 4.1.6.");
                SPT.EditableTranslations.MinimalLog.Reset();
                SPT.EditableTranslations.MinimalLog.WarningSink = message => Logger.LogWarning(message);
                var originalAssembly = original.Instance.GetType().Assembly;
                LocaleMode.Initialize(originalAssembly);
                lastCulture = LocaleMode.CurrentCulture();
                var originalRoot = Path.Combine(Path.GetDirectoryName(originalAssembly.Location), "SPT-Korean");
                var addonRoot = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                harmony = new Harmony("spt.korean.modtranslation.addon");
                data = new EditableProfiles(addonRoot);
                AddonBridge.Enable(harmony, originalAssembly, originalRoot, addonRoot, data);
                initialized = true;
            }
            catch (Exception ex)
            {
                harmony?.UnpatchSelf();
                Logger.LogError("Addon disabled; original Korean plugin remains unchanged. " + ex.Message);
            }
        }

        private void Start()
        {
            // Awake can fail closed on an incompatible/missing base plugin. Unity may still invoke
            // Start afterwards, so do not install partial hook families in that state.
            if (!initialized) return;

            var addonRoot = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            bool partial = false;

            // Keep independent hook families isolated. A third-party UI change must not disable
            // unrelated notification/dialog translations, and vice versa.
            try
            {
                displayHarmony = new Harmony("spt.korean.modtranslation.addon.display");
                var game = assemblies.Single(a => a.GetName().Name == "Assembly-CSharp");
                var count = DisplayHooks.Enable(displayHarmony, game, assemblies, data);
            }
            catch (Exception ex)
            {
                partial = true;
                displayHarmony?.UnpatchSelf();
                Logger.LogError("Display hooks disabled: " + ex.Message);
            }

            try
            {
                modUiHarmony = new Harmony("spt.korean.modtranslation.addon.mod-ui");
                var count = ModUiLiteralHooks.Enable(modUiHarmony, assemblies, data);
            }
            catch (Exception ex)
            {
                partial = true;
                ModUiLiteralHooks.Disable();
                modUiHarmony?.UnpatchSelf();
                Logger.LogError("Third-party mod UI hooks disabled: " + ex.Message);
            }

            try
            {
                smallUiHarmony = new Harmony("spt.korean.modtranslation.addon.small-ui");
                var count = SmallUiLiteralHooks.Enable(smallUiHarmony, assemblies, data);
                var pitLocaleCount = PitFireTeamLocaleHooks.Enable(smallUiHarmony, assemblies, AppDomain.CurrentDomain.BaseDirectory, addonRoot);
            }
            catch (Exception ex)
            {
                partial = true;
                SmallUiLiteralHooks.Disable();
                smallUiHarmony?.UnpatchSelf();
                Logger.LogError("Small mod UI hooks disabled: " + ex.Message);
            }

            try
            {
                casinoHarmony = new Harmony("spt.korean.modtranslation.addon.casino-write-time");
                var casinoCount = CasinoUiHooks.Enable(casinoHarmony, assemblies, data);
            }
            catch (Exception ex)
            {
                partial = true;
                CasinoUiHooks.Disable();
                casinoHarmony?.UnpatchSelf();
                Logger.LogError("Casino write-time hooks disabled: " + ex.Message);
            }

            try
            {
                dynamicMapsHarmony = new Harmony("spt.korean.modtranslation.addon.dynamicmaps-ui");
                var count = DynamicMapsUiHooks.Enable(dynamicMapsHarmony, assemblies, data);
            }
            catch (Exception ex)
            {
                partial = true;
                DynamicMapsUiHooks.Disable();
                dynamicMapsHarmony?.UnpatchSelf();
                Logger.LogError("Dynamic Maps custom UI hooks disabled: " + ex.Message);
            }

            // Configuration Manager is optional. We do not reference its assembly at compile time;
            // this display-only hook discovers it at runtime and leaves ConfigEntry/.cfg data untouched.
            try
            {
                configManagerHarmony = new Harmony("spt.korean.modtranslation.addon.config-manager");
                var configUi = new ConfigManagerProfiles(addonRoot);
                var count = ConfigurationManagerHooks.Enable(configManagerHarmony, assemblies, configUi);
                var customCount = F12CustomDrawerHooks.Enable(configManagerHarmony, assemblies);
            }
            catch (Exception ex)
            {
                partial = true;
                F12CustomDrawerHooks.Disable();
                ConfigurationManagerHooks.Disable();
                configManagerHarmony?.UnpatchSelf();
                Logger.LogError("Configuration Manager scaffold disabled: " + ex.Message);
            }

            // Direct NPC messages are server-authored literals, so the server cannot infer
            // Korean Patch Fix's custom kr/kr-en mode on its own. Send only the existing
            // CurrentCulture value; failure is isolated and leaves all client UI hooks intact.
            TrySyncServerCulture(lastCulture);
            Logger.LogInfo("SPT Mod Korean Addon 1.0.0 loaded" + (partial || SPT.EditableTranslations.MinimalLog.HasWarnings ? " (with warnings)." : "."));
        }

        private void Update()
        {
            if (!initialized) return;

            // CurrentCulture is reflection-backed. Poll roughly twice per second rather than every frame.
            if (++localePollFrames < 30) return;
            localePollFrames = 0;

            var culture = LocaleMode.Refresh();

            var changed = !string.Equals(culture, lastCulture, StringComparison.OrdinalIgnoreCase);

            if (!changed)
            {
                // The local SPT route may not be ready at the first Unity Start callback. Retry a
                // failed initial sync at a low frequency without blocking or affecting other hooks.
                if (!string.Equals(culture, lastServerCulture, StringComparison.OrdinalIgnoreCase) &&
                    ++serverCultureRetryPolls >= 20)
                {
                    serverCultureRetryPolls = 0;
                    TrySyncServerCulture(culture);
                }
                return;
            }

            lastCulture = culture;

            // Push exactly once on a real culture change. If it fails, the low-frequency retry
            // path above keeps future direct messages in source text until a later sync succeeds.
            TrySyncServerCulture(culture);

            try { ModUiLiteralHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("Plugin:197", () => "Mod UI language refresh skipped: " + ex.Message); }
            try { SmallUiLiteralHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("Plugin:199", () => "Small UI language refresh skipped: " + ex.Message); }
            try { DynamicMapsUiHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("Plugin:201", () => "Dynamic Maps UI language refresh skipped: " + ex.Message); }
            try { ConfigurationManagerHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("Plugin:203", () => "Configuration Manager language refresh skipped: " + ex.Message); }

        }

        private void TrySyncServerCulture(string culture)
        {
            try
            {
                if (!ServerCultureSync.Push(culture))
                    throw new InvalidOperationException("culture route did not acknowledge the request");
                lastServerCulture = culture;
                serverCultureRetryPolls = 0;
                serverCultureWarningLogged = false;
            }
            catch (Exception ex)
            {
                // Fail closed for translation: an unsynced server keeps direct messages as source.
                // Log only once until a later retry succeeds so an absent server addon cannot spam.
                if (!serverCultureWarningLogged)
                {
                    SPT.EditableTranslations.MinimalLog.WarnOnce("Plugin:225", () => "Server direct-message culture sync unavailable; source text will be preserved: " + ex.Message);
                    serverCultureWarningLogged = true;
                }
            }
        }

        private void OnDestroy()
        {
            ModUiLiteralHooks.Disable();
            SmallUiLiteralHooks.Disable();
            CasinoUiHooks.Disable();
            casinoHarmony?.UnpatchSelf();
            DynamicMapsUiHooks.Disable();
            F12CustomDrawerHooks.Disable();
            ConfigurationManagerHooks.Disable();
            harmony?.UnpatchSelf();
            displayHarmony?.UnpatchSelf();
            modUiHarmony?.UnpatchSelf();
            smallUiHarmony?.UnpatchSelf();
            dynamicMapsHarmony?.UnpatchSelf();
            configManagerHarmony?.UnpatchSelf();
        }
    }
}
