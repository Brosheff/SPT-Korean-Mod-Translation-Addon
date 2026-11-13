using System;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;

namespace SPT.ModKoreanAddon
{
    [BepInPlugin("spt.korean.modtranslation.addon", "SPT Mod Korean Addon", "1.0.6")]
    [BepInDependency("com.GoLani.koreanpatchfix", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.mpstark.dynamicmaps", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private Harmony harmony;
        private Harmony displayHarmony;
        private Harmony modUiHarmony;
        private Harmony configManagerHarmony;
        private Harmony betterBepInExUiHarmony;
        private Harmony smallUiHarmony;
        private Harmony k9UiHarmony;
        private Harmony papHideoutUiHarmony;
        private Harmony raygunUiHarmony;
        private Harmony thundergunUiHarmony;
        private Harmony tutorialBackportUiHarmony;
        private Harmony btrSkipperUiHarmony;
        private Harmony k9LangHarmony;
        private Harmony automaticWeaponsmithHarmony;
        private Harmony swagDonutsHarmony;
        private Harmony hideoutInteriorsHarmony;
        private Harmony hideoutLastStandHarmony;
        private Harmony kennerZombiesMenuHarmony;
        private Harmony kennerZombiesLobbyHarmony;
        private Harmony kennerZombiesGameHarmony;
        private Harmony kennerZombiesResultsHarmony;
        private Harmony kennerZombiesSettingsHarmony;
        private Harmony kennerZombiesFinalHarmony;
        private Harmony hideoutLastStandInteractionHarmony;
        private Harmony hideoutLastStandDefeatHarmony;
        private Harmony pitFireTeamFontHarmony;
        private bool pitFireTeamFontPending;
        private Harmony dynamicMapsHarmony;
        private Harmony casinoHarmony;
        private Harmony quartermasterHarmony;
        private Harmony fiveModsHarmony;
        private Harmony eightModsHarmony;
        private QuartermasterContractLocales quartermasterCatalog;
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
                // Under Fire registers its one-off health-effect label in Awake, before
                // this addon's Start-only small UI hooks. Install only that exact
                // source-audited literal hook now; keep all other small UI hooks in Start.
                try
                {
                    smallUiHarmony = new Harmony("spt.korean.modtranslation.addon.small-ui");
                    SmallUiLiteralHooks.EnableUnderFireEarly(smallUiHarmony,
                        AppDomain.CurrentDomain.GetAssemblies(), data);
                }
                catch (Exception earlyError)
                {
                    SmallUiLiteralHooks.Disable();
                    smallUiHarmony?.UnpatchSelf();
                    smallUiHarmony = null;
                    Logger.LogWarning("Under Fire early display hook skipped: " + earlyError.Message);
                }
                // Install Donuts UI/error display patches as early as available;
                // DependencyChecker may show its error before other plugins reach Start.
                try
                {
                    swagDonutsHarmony = new Harmony("spt.korean.modtranslation.addon.swag-donuts-ui");
                    SwagDonutsUiHooks.Enable(swagDonutsHarmony,
                        AppDomain.CurrentDomain.GetAssemblies(), data);
                }
                catch (Exception earlyError)
                {
                    SwagDonutsUiHooks.Disable();
                    swagDonutsHarmony?.UnpatchSelf();
                    swagDonutsHarmony = null;
                    Logger.LogWarning("Donuts early display hook skipped: " + earlyError.Message);
                }
                quartermasterCatalog = new QuartermasterContractLocales(addonRoot);
                AddonBridge.Enable(harmony, originalAssembly, originalRoot, addonRoot, data);
                // Install the grenade error hook in Awake, not Start: missing BigBrain
                // may terminate the client before Start executes. Installation order of
                // third-party plugins still determines whether this runs early enough.
                try
                {
                    fiveModsHarmony = new Harmony("spt.korean.modtranslation.addon.five-mods-ui");
                    FiveModsUiHooks.Enable(fiveModsHarmony, AppDomain.CurrentDomain.GetAssemblies());
                }
                catch (Exception error)
                {
                    FiveModsUiHooks.Disable();
                    fiveModsHarmony?.UnpatchSelf();
                    Logger.LogWarning("Five mods optional UI hooks disabled: " + error.Message);
                }
                // Optional scoped UI hooks for eight independently reviewed mods.
                try
                {
                    eightModsHarmony = new Harmony("spt.korean.modtranslation.addon.eight-mods-ui");
                    EightModsUiHooks.Enable(eightModsHarmony, AppDomain.CurrentDomain.GetAssemblies());
                }
                catch (Exception error)
                {
                    EightModsUiHooks.Disable();
                    eightModsHarmony?.UnpatchSelf();
                    Logger.LogWarning("Eight mods optional UI hooks disabled: " + error.Message);
                }
                initialized = true;
            }
            catch (Exception ex)
            {
                SmallUiLiteralHooks.Disable();
                smallUiHarmony?.UnpatchSelf();
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
                if (smallUiHarmony == null)
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

            // AutomaticWeaponsmith owns a language-file-backed UI in its build editor.
            // Hook only its Lang.T result; keep this family independent of other mod UI hooks.
            try
            {
                automaticWeaponsmithHarmony = new Harmony("spt.korean.modtranslation.addon.automaticweaponsmith-ui");
                var count = AutomaticWeaponsmithUiHooks.Enable(automaticWeaponsmithHarmony, assemblies, data);
                Logger.LogInfo("AutomaticWeaponsmith UI translation hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                AutomaticWeaponsmithUiHooks.Disable();
                automaticWeaponsmithHarmony?.UnpatchSelf();
                Logger.LogWarning("AutomaticWeaponsmith optional UI translation disabled: " + ex.Message);
            }

            // Dr K's SPZombies R001: optional, version-guarded menu and map presentation.
            try
            {
                kennerZombiesMenuHarmony = new Harmony("spt.korean.modtranslation.addon.kenner-zombies-menu");
                var count = KennerZombiesMenuHooks.Enable(kennerZombiesMenuHarmony, assemblies, data);
                Logger.LogInfo("SPZombies R001 menu hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                KennerZombiesMenuHooks.Disable();
                kennerZombiesMenuHarmony?.UnpatchSelf();
                Logger.LogWarning("SPZombies optional R001 menu translation disabled: " + ex.Message);
            }

            // Dr K's SPZombies R002: lobby and server-notice display only (separate owner).
            try
            {
                kennerZombiesLobbyHarmony = new Harmony("spt.korean.modtranslation.addon.kenner-zombies-lobby");
                var count = KennerZombiesLobbyHooks.Enable(kennerZombiesLobbyHarmony, assemblies, data);
                Logger.LogInfo("SPZombies R002 lobby hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                KennerZombiesLobbyHooks.Disable();
                kennerZombiesLobbyHarmony?.UnpatchSelf();
                Logger.LogWarning("SPZombies optional R002 lobby translation disabled: " + ex.Message);
            }

            // SPZombies R003: in-raid display-only labels, notices, and extraction UI.
            try
            {
                kennerZombiesGameHarmony = new Harmony("spt.korean.modtranslation.addon.kenner-zombies-game");
                var count = KennerZombiesGameHooks.Enable(kennerZombiesGameHarmony, assemblies, data);
                Logger.LogInfo("SPZombies R003 in-raid hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                KennerZombiesGameHooks.Disable();
                kennerZombiesGameHarmony?.UnpatchSelf();
                Logger.LogWarning("SPZombies optional R003 in-raid translation disabled: " + ex.Message);
            }

            // SPZombies R004: downed, game-over, report and statistics IMGUI display only.
            try
            {
                kennerZombiesResultsHarmony = new Harmony("spt.korean.modtranslation.addon.kenner-zombies-results");
                var count = KennerZombiesResultsHooks.Enable(kennerZombiesResultsHarmony, assemblies, data);
                Logger.LogInfo("SPZombies R004 result and statistics hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                KennerZombiesResultsHooks.Disable();
                kennerZombiesResultsHarmony?.UnpatchSelf();
                Logger.LogWarning("SPZombies optional R004 result translation disabled: " + ex.Message);
            }

            // SPZombies R005: F12 uses config-ui; the separate in-menu keybind/sound
            // pages need tightly scoped IMGUI display-only patches.
            try
            {
                kennerZombiesSettingsHarmony = new Harmony("spt.korean.modtranslation.addon.kenner-zombies-settings");
                var count = KennerZombiesSettingsHooks.Enable(kennerZombiesSettingsHarmony, assemblies, data);
                Logger.LogInfo("SPZombies R005 settings UI hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                KennerZombiesSettingsHooks.Disable();
                kennerZombiesSettingsHarmony?.UnpatchSelf();
                Logger.LogWarning("SPZombies optional R005 settings translation disabled: " + ex.Message);
            }

            // SPZombies R006: source-audited tips, loading labels, pause labels and
            // table gun names; the item locale rows use the existing overlay.
            try
            {
                kennerZombiesFinalHarmony = new Harmony("spt.korean.modtranslation.addon.kenner-zombies-final");
                var count = KennerZombiesFinalHooks.Enable(kennerZombiesFinalHarmony, assemblies, data);
                Logger.LogInfo("SPZombies R006 display hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                KennerZombiesFinalHooks.Disable();
                kennerZombiesFinalHarmony?.UnpatchSelf();
                Logger.LogWarning("SPZombies optional R006 display translation disabled: " + ex.Message);
            }

            // Donuts 4.0.4: translate its own F9 drawing code, never its config values.
            try
            {
                swagDonutsHarmony = new Harmony("spt.korean.modtranslation.addon.swag-donuts-ui");
                var count = SwagDonutsUiHooks.Enable(swagDonutsHarmony, assemblies, data);
                Logger.LogInfo("SWAG + Donuts UI display hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                SwagDonutsUiHooks.Disable();
                swagDonutsHarmony?.UnpatchSelf();
                Logger.LogWarning("SWAG + Donuts optional UI translation skipped: " + ex.Message);
            }

            // Pack-a-Punch 2.2.0 R001: only the F12 restart button's visible label.
            // Shared normal-gameplay dialog remains for the separate R002 audit.
            try
            {
                papHideoutUiHarmony = new Harmony("spt.korean.modtranslation.addon.pap-f12");
                var count = PaPHideoutUiHooks.Enable(papHideoutUiHarmony, assemblies, data);
                Logger.LogInfo("Pack-a-Punch F12 label hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                PaPHideoutUiHooks.Disable();
                papHideoutUiHarmony?.UnpatchSelf();
                Logger.LogWarning("Pack-a-Punch optional F12 label skipped: " + ex.Message);
            }

            // RayGunSPT 1.2.0: keep UI translation independent of other hook families.
            try
            {
                raygunUiHarmony = new Harmony("spt.korean.modtranslation.addon.raygun-ui");
                var count = RaygunUiHooks.Enable(raygunUiHarmony, assemblies, data);
                Logger.LogInfo("RayGunSPT UI display hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                RaygunUiHooks.Disable();
                raygunUiHarmony?.UnpatchSelf();
                Logger.LogWarning("RayGunSPT optional UI translation skipped: " + ex.Message);
            }

            // ThunderGunSPT 1.2.0: independent source-identified UI, ammo and stats paths.
            try
            {
                thundergunUiHarmony = new Harmony("spt.korean.modtranslation.addon.thundergun-ui");
                var count = ThundergunUiHooks.Enable(thundergunUiHarmony, assemblies, data);
                Logger.LogInfo("ThunderGunSPT UI display hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                ThundergunUiHooks.Disable();
                thundergunUiHarmony?.UnpatchSelf();
                Logger.LogWarning("ThunderGunSPT optional UI translation skipped: " + ex.Message);
            }

            // Tutorial Backport 1.1.0: intro TMP labels and its own hint/note/tape font surfaces.
            try
            {
                tutorialBackportUiHarmony = new Harmony("spt.korean.modtranslation.addon.tutorial-backport-ui");
                var count = TutorialBackportUiHooks.Enable(tutorialBackportUiHarmony, assemblies, data);
                Logger.LogInfo("Tutorial Backport UI display hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                TutorialBackportUiHooks.Disable();
                tutorialBackportUiHarmony?.UnpatchSelf();
                Logger.LogWarning("Tutorial Backport optional display translation skipped: " + ex.Message);
            }

            // Hideout Interiors 0.1.0 R002: fixed TMP labels and category tabs only.
            try
            {
                hideoutInteriorsHarmony = new Harmony("spt.korean.modtranslation.addon.hideout-interiors-ui");
                var count = HideoutInteriorsUiHooks.Enable(hideoutInteriorsHarmony, assemblies, data);
                Logger.LogInfo("Hideout Interiors R002 display hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                HideoutInteriorsUiHooks.Disable();
                hideoutInteriorsHarmony?.UnpatchSelf();
                Logger.LogWarning("Hideout Interiors optional static UI translation skipped: " + ex.Message);
            }

            // Hideout Last Stand: IMGUI menu/HUD labels are a distinct display path.
            // Leave SiegeState.Banner (including the Fika payload) and menu input intact.
            try
            {
                hideoutLastStandHarmony = new Harmony("spt.korean.modtranslation.addon.hideout-last-stand-ui");
                var count = HideoutLastStandUiHooks.Enable(hideoutLastStandHarmony, assemblies, data);
                Logger.LogInfo("Hideout Last Stand menu/HUD hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                HideoutLastStandUiHooks.Disable();
                hideoutLastStandHarmony?.UnpatchSelf();
                Logger.LogWarning("Hideout Last Stand optional menu/HUD translation skipped: " + ex.Message);
            }

            // R003: Hideout Last Stand mannequin/station/stash/door display-only actions.
            try
            {
                hideoutLastStandInteractionHarmony = new Harmony("spt.korean.modtranslation.addon.hideout-last-stand-interactions");
                var count = HideoutLastStandInteractionHooks.Enable(hideoutLastStandInteractionHarmony, assemblies, data);
                Logger.LogInfo("Hideout Last Stand interaction display hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                HideoutLastStandInteractionHooks.Disable();
                hideoutLastStandInteractionHarmony?.UnpatchSelf();
                Logger.LogWarning("Hideout Last Stand interaction translation skipped: " + ex.Message);
            }

            // R004: copied EFT statistics/TMP path is separate from the IMGUI defeat fallback.
            try
            {
                hideoutLastStandDefeatHarmony = new Harmony("spt.korean.modtranslation.addon.hideout-last-stand-defeat");
                var count = HideoutLastStandDefeatHooks.Enable(hideoutLastStandDefeatHarmony, assemblies, data);
                Logger.LogInfo("Hideout Last Stand defeat TMP hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                HideoutLastStandDefeatHooks.Disable();
                hideoutLastStandDefeatHarmony?.UnpatchSelf();
                Logger.LogWarning("Hideout Last Stand optional defeat TMP translation skipped: " + ex.Message);
            }

            // A PitFireTeam side-selection screen rebuilds its Add Teammate button
            // from a different Unity template. Preserve the working Korean TMP font
            // on that exact squad UI, separately from its own localization pipeline.
            try
            {
                pitFireTeamFontHarmony = new Harmony("spt.korean.modtranslation.addon.pitfireteam-font");
                var count = PitFireTeamButtonFontHooks.Enable(pitFireTeamFontHarmony, assemblies);
                pitFireTeamFontPending = count == 0;
                Logger.LogInfo("PitFireTeam squad label font hooks: " + count +
                    (count == 0 ? " (PitFireTeam UI not found)." : " installed."));
            }
            catch (Exception ex)
            {
                partial = true;
                PitFireTeamButtonFontHooks.Disable();
                pitFireTeamFontHarmony?.UnpatchSelf();
                Logger.LogWarning("PitFireTeam optional squad font hooks skipped: " + ex.Message);
            }

            // Keep K9's DOG display pipeline independent from every other small-UI plugin.
            // This still runs if one of the other third-party optional hooks failed above.
            try
            {
                k9UiHarmony = new Harmony("spt.korean.modtranslation.addon.k9-display");
                var k9Hooks = K9CompanionUiHooks.Enable(k9UiHarmony, assemblies, data);
                Logger.LogInfo("K9 Companion independent UI hooks: " + k9Hooks +
                    (k9Hooks == 0 ? " (pending assembly/profile checks)." : " installed."));
            }
            catch (Exception ex)
            {
                partial = true;
                K9CompanionUiHooks.Disable();
                k9UiHarmony?.UnpatchSelf();
                Logger.LogError("K9 Companion DOG display translation disabled: " + ex.Message);
            }

            // K9 1.3.2 translates through its own Lang.T(string) lookup. This is
            // separate from the gated legacy 1.1.0 DOG display patches above.
            try
            {
                k9LangHarmony = new Harmony("spt.korean.modtranslation.addon.k9-lang-132");
                var k9LanguageHooks = K9CompanionLangHooks.Enable(k9LangHarmony, assemblies, data);
                Logger.LogInfo("K9 Companion 1.3.2 language lookup hooks: " + k9LanguageHooks +
                    (k9LanguageHooks == 0 ? " (pending assembly/profile/version checks)." : " installed."));
            }
            catch (Exception ex)
            {
                partial = true;
                K9CompanionLangHooks.Disable();
                k9LangHarmony?.UnpatchSelf();
                Logger.LogWarning("K9 Companion 1.3.2 language lookup unavailable: " + ex.Message);
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

            // Dynamic The Quartermaster quest descriptions, community contract UI and
            // its per-second countdown are not covered by SmallUiLiteralHooks' IL strings.
            try
            {
                quartermasterHarmony = new Harmony("spt.korean.modtranslation.addon.quartermaster-runtime");
                var count = QuartermasterRuntimeHooks.Enable(quartermasterHarmony, assemblies, quartermasterCatalog);
            }
            catch (Exception ex)
            {
                partial = true;
                QuartermasterRuntimeHooks.Disable();
                quartermasterHarmony?.UnpatchSelf();
                Logger.LogError("The Quartermaster runtime hooks disabled: " + ex.Message);
            }

            // BTR/Lightkeeper Skipper 1.1.0: only its Tasks-menu button text and
            // compiled confirmation lambdas; quest progress and UI controls stay original.
            try
            {
                btrSkipperUiHarmony = new Harmony("spt.korean.modtranslation.addon.btr-lightkeeper-skipper-ui");
                var count = BtrLightkeeperSkipperUiHooks.Enable(btrSkipperUiHarmony, assemblies, data);
                Logger.LogInfo("BTR/Lightkeeper Skipper UI hooks: " + count + " installed (optional).");
            }
            catch (Exception ex)
            {
                partial = true;
                BtrLightkeeperSkipperUiHooks.Disable();
                btrSkipperUiHarmony?.UnpatchSelf();
                Logger.LogWarning("BTR/Lightkeeper Skipper display translation unavailable: " + ex.Message);
            }

            // Configuration Manager is optional. We do not reference its assembly at compile time;
            // this display-only hook discovers it at runtime and leaves ConfigEntry/.cfg data untouched.
            try
            {
                configManagerHarmony = new Harmony("spt.korean.modtranslation.addon.config-manager");
                var configUi = new ConfigManagerProfiles(addonRoot);
                var count = ConfigurationManagerHooks.Enable(configManagerHarmony, assemblies, configUi);
                var customCount = F12CustomDrawerHooks.Enable(configManagerHarmony, assemblies);
                var betterMenuCount = BetterBepInExF12Hooks.Enable(configManagerHarmony, assemblies, configUi);
            }
            catch (Exception ex)
            {
                partial = true;
                BetterBepInExF12Hooks.Disable();
                F12CustomDrawerHooks.Disable();
                ConfigurationManagerHooks.Disable();
                configManagerHarmony?.UnpatchSelf();
                Logger.LogError("Configuration Manager scaffold disabled: " + ex.Message);
            }

            // BetterBepInEx's standalone IMGUI is a separate consumer from the F12 model.
            // Keep its narrowly scoped display hooks in an independent Harmony owner.
            try
            {
                betterBepInExUiHarmony = new Harmony("spt.korean.modtranslation.addon.betterbepinex-ui");
                BetterBepInExUiHooks.Enable(betterBepInExUiHarmony, assemblies, data);
            }
            catch (Exception ex)
            {
                partial = true;
                BetterBepInExUiHooks.Disable();
                betterBepInExUiHarmony?.UnpatchSelf();
                Logger.LogError("BetterBepInEx standalone UI hooks disabled: " + ex.Message);
            }

            // Direct NPC messages are server-authored literals, so the server cannot infer
            // Korean Patch Fix's custom kr/kr-en mode on its own. Send only the existing
            // CurrentCulture value; failure is isolated and leaves all client UI hooks intact.
            TrySyncServerCulture(lastCulture);
            Logger.LogInfo("SPT Mod Korean Addon 1.0.6 loaded" + (partial || SPT.EditableTranslations.MinimalLog.HasWarnings ? " (with warnings)." : "."));
        }

        private void Update()
        {
            if (!initialized) return;

            // CurrentCulture is reflection-backed. Poll roughly twice per second rather than every frame.
            if (++localePollFrames < 30) return;
            localePollFrames = 0;

            var culture = LocaleMode.Refresh();
            // Retry the independently scoped K9 display hooks after delayed assembly load.
            // This never controls or disables other translation families.
            K9CompanionUiHooks.RetryPending();
            K9CompanionLangHooks.RetryPending();
            K9CompanionLangHooks.RefreshDogGroupLabels();
            // PitFireTeam may load after this addon's Start. Retry only while its
            // scoped factory hooks are absent; never patch a global TMP setter.
            if (pitFireTeamFontPending && pitFireTeamFontHarmony != null)
            {
                try
                {
                    var found = PitFireTeamButtonFontHooks.Enable(
                        pitFireTeamFontHarmony, AppDomain.CurrentDomain.GetAssemblies());
                    if (found > 0)
                    {
                        pitFireTeamFontPending = false;
                        Logger.LogInfo("PitFireTeam deferred squad label font hooks: " + found + " installed.");
                    }
                }
                catch (Exception ex)
                {
                    pitFireTeamFontPending = false;
                    SPT.EditableTranslations.MinimalLog.WarnOnce("pit-squad-font-deferred", () =>
                        "PitFireTeam deferred squad font hook failed: " + ex.Message);
                }
            }

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
            try { TutorialBackportUiHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("tutorial-backport-refresh", () => "Tutorial Backport UI refresh skipped: " + ex.Message); }
            try { BtrLightkeeperSkipperUiHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("btr-skipper-refresh", () => "BTR/Lightkeeper Skipper button refresh skipped: " + ex.Message); }
            try { SmallUiLiteralHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("Plugin:199", () => "Small UI language refresh skipped: " + ex.Message); }
            try { DynamicMapsUiHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("Plugin:201", () => "Dynamic Maps UI language refresh skipped: " + ex.Message); }
            try { KennerZombiesMenuHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r001-refresh", () => "SPZombies language refresh skipped: " + ex.Message); }
            try { HideoutInteriorsUiHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("hideout-interiors-r002-refresh", () => "Hideout Interiors language refresh skipped: " + ex.Message); }
            try { HideoutLastStandInteractionHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("hideout-last-stand-r003-refresh", () => "Hideout Last Stand interaction refresh skipped: " + ex.Message); }
            try { HideoutLastStandDefeatHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("hideout-last-stand-r004-refresh", () => "Hideout Last Stand defeat refresh skipped: " + ex.Message); }
            try { HideoutLastStandUiHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("hideout-last-stand-r002-refresh", () => "Hideout Last Stand UI language refresh skipped: " + ex.Message); }
            try { ConfigurationManagerHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("Plugin:203", () => "Configuration Manager language refresh skipped: " + ex.Message); }
            try { BetterBepInExF12Hooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("betterbepinex-refresh", () => "BetterBepInEx language refresh skipped: " + ex.Message); }
            try { BetterBepInExUiHooks.RefreshLanguage(); }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("betterbepinex-ui-refresh", () => "BetterBepInEx UI refresh skipped: " + ex.Message); }

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
            QuartermasterRuntimeHooks.Disable();
            quartermasterHarmony?.UnpatchSelf();
            FiveModsUiHooks.Disable();
            fiveModsHarmony?.UnpatchSelf();
            EightModsUiHooks.Disable();
            eightModsHarmony?.UnpatchSelf();
            ModUiLiteralHooks.Disable();
            SmallUiLiteralHooks.Disable();
            AutomaticWeaponsmithUiHooks.Disable();
            automaticWeaponsmithHarmony?.UnpatchSelf();
            TutorialBackportUiHooks.Disable();
            tutorialBackportUiHarmony?.UnpatchSelf();
            BtrLightkeeperSkipperUiHooks.Disable();
            btrSkipperUiHarmony?.UnpatchSelf();
            ThundergunUiHooks.Disable();
            thundergunUiHarmony?.UnpatchSelf();
            PaPHideoutUiHooks.Disable();
            papHideoutUiHarmony?.UnpatchSelf();
            RaygunUiHooks.Disable();
            raygunUiHarmony?.UnpatchSelf();
            SwagDonutsUiHooks.Disable();
            swagDonutsHarmony?.UnpatchSelf();
            KennerZombiesFinalHooks.Disable();
            kennerZombiesFinalHarmony?.UnpatchSelf();
            KennerZombiesSettingsHooks.Disable();
            kennerZombiesSettingsHarmony?.UnpatchSelf();
            KennerZombiesResultsHooks.Disable();
            kennerZombiesResultsHarmony?.UnpatchSelf();
            KennerZombiesGameHooks.Disable();
            kennerZombiesGameHarmony?.UnpatchSelf();
            KennerZombiesLobbyHooks.Disable();
            kennerZombiesLobbyHarmony?.UnpatchSelf();
            KennerZombiesMenuHooks.Disable();
            kennerZombiesMenuHarmony?.UnpatchSelf();
            HideoutInteriorsUiHooks.Disable();
            hideoutInteriorsHarmony?.UnpatchSelf();
            HideoutLastStandUiHooks.Disable();
            hideoutLastStandHarmony?.UnpatchSelf();
            HideoutLastStandDefeatHooks.Disable();
            hideoutLastStandDefeatHarmony?.UnpatchSelf();
            HideoutLastStandInteractionHooks.Disable();
            hideoutLastStandInteractionHarmony?.UnpatchSelf();
            K9CompanionUiHooks.Disable();
            k9UiHarmony?.UnpatchSelf();
            K9CompanionLangHooks.Disable();
            k9LangHarmony?.UnpatchSelf();
            PitFireTeamButtonFontHooks.Disable();
            pitFireTeamFontHarmony?.UnpatchSelf();
            CasinoUiHooks.Disable();
            casinoHarmony?.UnpatchSelf();
            DynamicMapsUiHooks.Disable();
            BetterBepInExUiHooks.Disable();
            betterBepInExUiHarmony?.UnpatchSelf();
            BetterBepInExF12Hooks.Disable();
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
