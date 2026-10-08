using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Localizes short hard-coded labels/buttons inserted by explicitly scoped third-party mods.
    // IMPORTANT: never enumerate and patch every method in a third-party assembly. The previous
    // implementation did that and crashed while entering Tyfon.UIFixes on the user's SPT 4.1.6
    // installation. All targets below are source-audited, exact assembly/type/method scopes.
    internal static class SmallUiLiteralHooks
    {
        private const string Channel = "small_ui";
        private const string CotiProfileId = "mod_3b2b877cc80c";
        private const string CotiDpadChannel = "coti_dpad";
        private const string CotiAssemblyName = "Coti.Client";
        private const string HideoutUiRevampProfileId = "smallui.hideoutuirevamp";
        private const string HideoutUiRevampAssemblyName = "tarkin.hideoutuirevamp";
        private const string UnloadAllMagazinesProfileId = "UnloadAllMagazines";
        private const string UnloadAllMagazinesAssemblyName = "maschine-UnloadAllMagazines";
        private const string TraumaCoreProfileId = "smallui.traumacore";
        private const string TraumaCoreAssemblyName = "TraumaCore";
        private const string BossNotifierProfileId = "BossNotifier";
        private const string BossNotifierAssemblyName = "BossNotifier";
        private const string SainProfileId = "smallui.sain";
        private const string SainAssemblyName = "SAIN";
        private const string StatTrackProfileId = "StatTrack";
        private const string StatTrackAssemblyName = "acidphantasm-stattrack";
        private const string CineKitProfileId = "smallui.cinekit";
        private const string CineKitAssemblyName = "CineKit";
        private const string SuomiPrtProfileId = "source3.suomi_prt";
        private const string SuomiPrtAssemblyName = "RadioMod.Client";
        private const string DoomArcadeProfileId = "source3.wtt_doomarcade";
        private const string DoomArcadeAssemblyName = "DoomArcade.Runtime";

        private sealed class Target
        {
            internal readonly string ProfileId;
            internal readonly string AssemblyName;
            internal readonly string TypeName;
            internal readonly string MethodName;
            internal readonly int ParameterCount;
            internal readonly bool IncludeNestedTypes;

            internal Target(string profileId, string assemblyName, string typeName, string methodName,
                int parameterCount = -1, bool includeNestedTypes = false)
            {
                ProfileId = profileId;
                AssemblyName = assemblyName;
                TypeName = typeName;
                MethodName = methodName;
                ParameterCount = parameterCount;
                IncludeNestedTypes = includeNestedTypes;
            }
        }

        private sealed class TrackedGeneratedLabel
        {
            internal readonly WeakReference Target;
            internal readonly string Source;

            internal TrackedGeneratedLabel(object target, string source)
            {
                Target = new WeakReference(target);
                Source = source;
            }
        }

        private sealed class TrackedTextPropertyLabel
        {
            internal readonly WeakReference Target;
            internal readonly string ProfileId;
            internal readonly string Source;

            internal TrackedTextPropertyLabel(object target, string profileId, string source)
            {
                Target = new WeakReference(target);
                ProfileId = profileId;
                Source = source;
            }
        }

        private sealed class SainTabSelectionState
        {
            internal readonly string[] SourceOptions;
            internal readonly string[] DisplayOptions;

            internal SainTabSelectionState(string[] sourceOptions, string[] displayOptions)
            {
                SourceOptions = sourceOptions;
                DisplayOptions = displayOptions;
            }
        }

        private static readonly Target[] Targets =
        {
            new Target("AmandsGraphics", "AmandsGraphics", "AmandsGraphics.AmandsGraphicsClass", "AmandsToggleText", 1),
            new Target("CookingGrenades", "CookingGrenades", "CookingGrenades.Patches.MenuScreenPatch", "PatchPostfix", 1),
            // SPT Casino 1.3.3: the cloned task-bar tooltip bypasses CasinoLobby.NewText.
            new Target("com.mybutthasarash.sptcasino", "Casino.Client", "Casino.Client.CasinoTab", "Neuter", 1),

            // COTI's standalone pose/mask editors are ordinary IMGUI owned entirely by Coti.Client.
            // Keep every hook on an audited COTI method: no GUI/GUILayout global patch and no type scan.
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiInspectButton", "AddButton", 1),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "Draw", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "DrawWindow", 1),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "DrawHeader", 1),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "DrawViewport", 1),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "DrawAnchorRow", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "DrawFlipTestRow", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "DrawPads", 3),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "DrawReadout", 2),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiTunerPanel", "DrawFooter", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiMaskPanel", "Reset", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiMaskPanel", "Publish", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiMaskPanel", "Draw", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiMaskPanel", "DrawContents", 1),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiPoseTuner", "get_OpenHostName", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiPoseTuner", "FormatAnchorBone", 1),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiPoseTuner", "get_FlipUnavailableReason", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiPoseTuner", "get_MeasuredBoundsLabel", 0),
            new Target("mod_3b2b877cc80c", "Coti.Client", "Coti.Client.CotiPublishReport", "Describe", 3),

            // Inventory Organizing Features builds its confirmation callbacks as compiler-generated
            // nested methods. Scope the wildcard to this one audited UI helper type and its direct
            // generated closures; ContainsTranslatableLiteral still gates every individual method.
            new Target("InventoryOrganizingFeatures", "flir.iof", "flir.iof.UI.UserInterfaceElements", "*", -1, true),

            // ChooChoo's Postfix is declared async. Its direct TMP labels live in the generated
            // state-machine MoveNext, so search only this one audited patch type and its nested types.
            new Target("ChooChoo.TraderModding", "ChooChoo-TraderModding", "TraderModding.EditBuildScreenPatch", "*", -1, true),
            new Target("ChooChoo.TraderModding", "ChooChoo-TraderModding", "TraderModding.EditBuildScreenShowPatch", "Postfix", 2),
            new Target("ChooChoo.TraderModding", "ChooChoo-TraderModding", "TraderModding.EditBuildScreenClosePatch", "Postfix", 1),
            new Target("ChooChoo.TraderModding", "ChooChoo-TraderModding", "TraderModding.TraderModdingUtils", "UpdateBuildCost", 0),

            new Target("ItemPreviewQoL", "JBOBYH_ItemPreviewQoL", "JBOBYH_ItemPreviewQoL.Patches.ItemSpecifications_Show_Patch", "Postfix", 2),
            new Target("mod_b776e1dd87a1", "Trenchfoot-BeltSlot", "BeltSlot.Helpers.UI_Mappings", "setBeltSlot_Settings", 1),
            new Target("QuickSellFlea", "QuickSellFlea", "QuickSellFlea.MultiSell", "HandleMultiSelectSell", 3),
            new Target("QuickSellFlea", "QuickSellFlea", "QuickSellFlea.Patches.ItemUiContext_GetItemContextInteractions_Patch", "Postfix", 5),
            new Target("QuickSellFlea", "QuickSellFlea", "QuickSellFlea.MultiSell", "SetPrices", 0),
            new Target("QuickSellFlea", "QuickSellFlea", "QuickSellFlea.Patches.ItemUiContext_GetItemContextInteractions_Patch", "SetPrices", 1),
            // The Quartermaster: community screen is built directly in its client UI class.
            // Restrict translation to the explicit UI type and compiler-generated closures.
            new Target("TheQuartermaster", "TheQuartermaster.Client", "TheQuartermaster.Client.UI.CommunityPanel", "*", -1, true),
            // Armor Expert 2.0.0: item attribute labels and details are constructed in these audited helpers.
            // Compiler-generated display delegates are nested in ArmorAttributes.
            new Target("smallui.armorexpert", "Liquidwarp.ArmorExpert", "Liquidwarp.ArmorExpert.ArmorAttributes", "*", -1, true),
            new Target("smallui.armorexpert", "Liquidwarp.ArmorExpert", "Liquidwarp.ArmorExpert.Tone", "*", -1, true),
            new Target("smallui.armorexpert", "Liquidwarp.ArmorExpert", "Liquidwarp.ArmorExpert.Deflection", "*", -1, true),
            new Target("smallui.armorexpert", "Liquidwarp.ArmorExpert", "Liquidwarp.ArmorExpert.Blunt", "*", -1, true),
            new Target("smallui.armorexpert", "Liquidwarp.ArmorExpert", "Liquidwarp.ArmorExpert.Hearing", "*", -1, true),
            new Target("smallui.armorexpert", "Liquidwarp.ArmorExpert", "Liquidwarp.ArmorExpert.Durability", "*", -1, true),
            new Target("smallui.transparentsights", "7Bpencil.TransparentSights", "SevenBoldPencil.TransparentSights.Plugin", "GetScopeTransparencyModeName", 1),
            new Target("UnloadAllMagazines", "maschine-UnloadAllMagazines", "UnloadAllMagazines.Patches.UnloadAllMagazinesButtonPatch", "Postfix", 1),
            new Target("smallui.weaponbuildersearch", "maschine-WeaponBuilderSearch", "WeaponBuilderSearch.UI.AttachmentSearchController", "EnsureSearchField", 1),
            new Target("smallui.hideoutcat", "hideoutcat", "HideoutCat.Patches.BonusPanelPatches.UpdateViewPatch", "Postfix", 1),
            new Target("smallui.hideoutcat", "hideoutcat", "HideoutCat.Utils.InteractionStateUtils", "GetCatAvailableActions", 2),
            new Target("smallui.ladders", "tarkin.ladders.bep", "tarkin.ladders.bep.Patch_InteractionContextHelper_GetAvailableActions", "PatchPrefix", 3),
            new Target("smallui.flareeventnotifier", "Terkoiz-FlareEventNotifier", "Terkoiz.FlareEventNotifier.ExfilFlareSuccessNotification", "get_Description", 0),
            new Target("smallui.holstereverything", "HolsterEverything", "HolsterEverything.F12Config.HolsterEverythingClientPlugin+HolsterSlotSizeClientPatch", "SetIncompatibleOperation", 1),
            new Target("smallui.holstereverything", "HolsterEverything", "HolsterEverything.F12Config.HolsterEverythingClientPlugin+HolsterInventoryMoveSizePatch", "CreateFailureResult", 1),
            new Target("Vagabond", "Vagabond.Client", "Vagabond.Client.Patches.HealthTreatmentScreenShowPatch", "PatchPostfix", 5),
            new Target("Vagabond", "Vagabond.Client", "Vagabond.Client.Patches.MatchMakerSideSelectionScreenPatch", "PatchPostfix", 4),

            // TraumaCore health-effect text is produced by these exact source-audited methods.
            // Keep these scoped to TraumaCore's own presentation helpers; never patch EFT HealthHelper globally.
            new Target("smallui.traumacore", "TraumaCore", "TraumaCore.Patches.HealthEffects.NativeEffectLabels", "BuildBleedLabels", 1),
            new Target("smallui.traumacore", "TraumaCore", "TraumaCore.Patches.HealthEffects.NativeEffectLabels", "BuildActiveLabels", 1),
            new Target("smallui.traumacore", "TraumaCore", "TraumaCore.Patches.HealthEffects.NativeEffectDisplayPatch", "ReplaceNativeEffectText", 2),
            new Target("smallui.traumacore", "TraumaCore", "TraumaCore.Patches.HealthEffects.BloodLossStimDescriptionPatch", "PatchPostfix", 2),
            new Target("smallui.traumacore", "TraumaCore", "TraumaCore.Patches.HealthEffects.BloodLossStimBuffRowPatch", "PatchPostfix", 2),
            // The exam-panel display-name/value delegates compile into nested generated methods.
            // Search only this one audited patch type and its direct generated closures.
            new Target("smallui.traumacore", "TraumaCore", "TraumaCore.Patches.HealthEffects.BloodLossStimExamPanelPatch", "*", -1, true),
            new Target("smallui.traumacore", "TraumaCore", "TraumaCore.Patches.WoundInspection.AddCorpseWoundInspectionActionPatch", "AddWoundInspectionActions", 3),

            // SOURCE2: source-audited custom presentation surfaces only.
            new Target("smallui.mapvariants", "MapVariants.Client", "MapVariants.Client.MapChoiceWindow", "Build", 5),
            new Target("smallui.mapvariants", "MapVariants.Shared", "MapVariants.Shared.WarningPolicy", "AcceptCaption", 2),
            new Target("smallui.mapvariants", "MapVariants.Client", "MapVariants.Client.MapVariantPrompt", "Ask", 3),
            new Target("smallui.mapvariants", "MapVariants.Client", "MapVariants.Client.TransitChoicePrompt+OnScreen", "Postfix", 2),
            new Target("smallui.questbriefingapi", "QuestBriefingAPI", "Manimal.QuestBriefingAPI.Briefings.BriefingPlayer", "*", -1, false),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.Configuration", "EquipmentSlotListDrawer", -1, false),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.ItemUseDelayTimer.ItemUseDelayTimerView", "*", -1, false),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.QuickUseWheel.QuickUseWheelView", "*", -1, false),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.QuickUseWheel.QuickUseWheelController", "*", -1, false),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory", "*", -1, false),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.QuickUseWheel.WeaponDeviceWheelInventory", "*", -1, false),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.QuickUseWheel.QuickUseCategoryNames", "DisplayName", 1),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.UI.ItemAccessDelayText", "*", -1, false),
            new Target("smallui.useitemsanywhere", "UseItemsAnywhere", "UseItemsAnywhere.UI.RuntimeUiService", "GetSlotName", 1),
            new Target("smallui.wttcommonlib", "WTT-ClientCommonLib", "WTTClientCommonLib.Helpers.ZoneUiHelpers", "*", -1, false),
            new Target("mod_700ec7e0d1d4", "C11-TN4-Client", "C11_TN4_Client.amp_arms.AmpArmsEditor", "DrawHud", 0),
            // MODS.zip phase 3: hard-coded ammo attribute labels live in compiler-generated lambdas
            // under this audited extension type. Include only its direct generated nested types.
            new Target("smallui.munitionsexpert", "IcyClawz.MunitionsExpert", "IcyClawz.MunitionsExpert.AmmoTemplateExtensions", "*", -1, true),
            // SOURCE3 CineKit 1.2.0: standalone IMGUI. Every target below is an exact source-audited draw method.
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "OnGUI", 0),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawMovementKey", 2),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawStandaloneHotkey", 1),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawToggleWithHotkey", 2),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawWindow", 1),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawFreecamPanel", 0),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawPathPanel", 0),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawSelectedPointStartStop", 0),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawRecordingPanel", 0),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawOtherPanel", 0),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawSchematicsPanel", 0),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawSelectedPointInspector", 0),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawEntityPointSettings", 1),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawEntityChoice", 2),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawCurveEditor", 1),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawCurveProjection", 2),
            new Target("smallui.cinekit", "CineKit", "CineKit.Plugin", "DrawPathPointRow", 1),
            // SOURCE3: Terkoiz.Skipper hard-codes the quest-objective button and confirmation dialog in this audited postfix.
            // SOURCE3 Blackout 4.1.1: fixed emergency announcement subtitle, its visible speaker label,
            // and the blackout-only keycard-door interaction that opens the emergency-code keypad.
            new Target("smallui.blackout", "Blackout", "Blackout.BlackoutPlugin", "Update", 0),
            new Target("smallui.blackout", "Blackout", "Blackout.BlackoutPlugin", "OnGUI", 0),
            new Target("smallui.blackout", "Blackout", "Blackout.BlackoutPlugin+KeycardActionsPatch", "Postfix", 4),
            // SOURCE3 Menu Overhaul 1.3.0: custom TMP experience label created in this exact method.
            new Target("smallui.menuoverhaul", "MoxoPixel.MenuOverhaul", "MoxoPixel.MenuOverhaul.Helpers.Services.PlayerProfileViewService", "CreateExperienceRow", 1),
            new Target("smallui.skipper", "terkoiz-skipper", "Terkoiz.Skipper.QuestObjectiveViewPatch", "PatchPostfix", 5),
            // SOURCE3 Suomi-PRT 1.0.2: these are the only visible English literals that bypass
            // the mod's own L(...) localization helper. Scope both to their exact source methods.
            new Target("source3.suomi_prt", "RadioMod.Client", "RadioMod.Client.Plugin", "DrawClearRecordingsButton", 1),
            new Target("source3.suomi_prt", "RadioMod.Client", "RadioMod.Client.Plugin", "StopRadioTransmit", 1)
        };

        private const string WikiLinksProfileId = "smallui.wikilinks";
        private const string WikiLinksAssemblyName = "Tyfon.WikiLinks";

        private static readonly object Gate = new object();
        private static readonly HashSet<MethodBase> PatchedMethods = new HashSet<MethodBase>();
        private static readonly List<TrackedGeneratedLabel> GeneratedLabels = new List<TrackedGeneratedLabel>();
        private static readonly List<TrackedTextPropertyLabel> TextPropertyLabels = new List<TrackedTextPropertyLabel>();
        private static readonly MethodInfo RuntimeTranslator = typeof(SmallUiLiteralHooks).GetMethod(
            nameof(TranslateRuntime), BindingFlags.Static | BindingFlags.NonPublic);
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static PropertyInfo bossNotifierInfoTextMeshProperty;
        private static PropertyInfo bossNotifierTextProperty;
        private static FieldInfo sainSaveContentField;
        private static PropertyInfo sainGuiContentTextProperty;
        private static FieldInfo cineKitPanelNamesField;
        private static FieldInfo cineKitAntialiasingNamesField;
        private static FieldInfo cineKitRecordingFpsNamesField;
        private static FieldInfo cineKitFreecamContentField;
        private static FieldInfo cineKitRecordingStatusField;
        private static MethodInfo cineKitRecordingStatusMethod;
        private static FieldInfo suomiPrtUiLanguageOverrideField;
        [ThreadStatic] private static int sainPresetListDepth;
        private static bool enabled;

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> loadedAssemblies, EditableProfiles data)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            if (data == null) throw new ArgumentNullException(nameof(data));

            lock (Gate)
            {
                if (enabled) return 0;
                harmony = patcher;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }

            var assemblies = (loadedAssemblies ?? Enumerable.Empty<Assembly>())
                .Where(a => a != null)
                .GroupBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var total = 0;
            foreach (var target in Targets)
            {
                if (!profiles.HasRules(target.ProfileId, Channel)) continue;
                if (!assemblies.TryGetValue(target.AssemblyName, out var assembly)) continue;
                total += PatchTarget(assembly, target);
            }

            // SOURCE3 WikiLinks 2.0.0 builds its visible label as Localized("OPEN") + " WIKI".
            // Translate only the final rendered label so the internal action key "OPEN WIKI" remains untouched.
            if (profiles.HasRules(WikiLinksProfileId, Channel) &&
                assemblies.TryGetValue(WikiLinksAssemblyName, out var wikiLinksAssembly))
                total += PatchWikiLinksOpenLabels(wikiLinksAssembly);

            if (profiles.HasRules(CotiProfileId, CotiDpadChannel) &&
                assemblies.TryGetValue(CotiAssemblyName, out var cotiAssembly))
                total += PatchCotiDpadLabels(cotiAssembly);

            if (profiles.HasRules(HideoutUiRevampProfileId, Channel) &&
                assemblies.TryGetValue(HideoutUiRevampAssemblyName, out var hideoutUiRevampAssembly))
                total += PatchHideoutUiRevamp(hideoutUiRevampAssembly);

            if (profiles.HasRules(UnloadAllMagazinesProfileId, Channel) &&
                assemblies.TryGetValue(UnloadAllMagazinesAssemblyName, out var unloadAllMagazinesAssembly))
                total += PatchUnloadAllMagazinesTooltip(unloadAllMagazinesAssembly);

            if (profiles.HasRules(TraumaCoreProfileId, Channel) &&
                assemblies.TryGetValue(TraumaCoreAssemblyName, out var traumaCoreAssembly))
            {
                total += PatchTraumaCoreWoundLabels(traumaCoreAssembly);
                total += PatchTraumaCoreDeathScreenText(traumaCoreAssembly);
            }

            if (profiles.HasRules(BossNotifierProfileId, Channel) &&
                assemblies.TryGetValue(BossNotifierAssemblyName, out var bossNotifierAssembly))
                total += PatchBossNotifierMarkerText(bossNotifierAssembly);

            if (profiles.HasRules(StatTrackProfileId, Channel) &&
                assemblies.TryGetValue(StatTrackAssemblyName, out var statTrackAssembly))
                total += PatchStatTrack(statTrackAssembly);

            if (profiles.HasRules(SainProfileId, Channel) &&
                assemblies.TryGetValue(SainAssemblyName, out var sainAssembly))
                total += PatchSainEditor(sainAssembly);

            if (profiles.HasRules(CineKitProfileId, Channel) &&
                assemblies.TryGetValue(CineKitAssemblyName, out var cineKitAssembly))
            {
                PatchCineKitCachedText(cineKitAssembly);
                total += PatchCineKitRecordingStatus(cineKitAssembly);
            }

            // Suomi-PRT already centralises all 8 built-in languages through Plugin.L. Patch that
            // one language boundary instead of transpiling every IMGUI draw method. Auto falls back
            // to English for Korean in the upstream mod, so the addon translates only that fallback.
            if (profiles.HasRules(SuomiPrtProfileId, Channel) &&
                assemblies.TryGetValue(SuomiPrtAssemblyName, out var suomiPrtAssembly))
                total += PatchSuomiPrtLocalization(suomiPrtAssembly);

            // WTT DoomArcade loads DoomArcade.Runtime.dll dynamically from its client plugin Awake.
            // Patch the already-loaded copy here when present; OnAssemblyLoad below handles the normal
            // late-load path without scanning or modifying unrelated assemblies.
            if (profiles.HasRules(DoomArcadeProfileId, Channel) &&
                assemblies.TryGetValue(DoomArcadeAssemblyName, out var doomArcadeAssembly))
                total += PatchDoomArcadeMenu(doomArcadeAssembly);

            return total;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                harmony = null;
                profiles = null;
                PatchedMethods.Clear();
                GeneratedLabels.Clear();
                TextPropertyLabels.Clear();
                bossNotifierInfoTextMeshProperty = null;
                bossNotifierTextProperty = null;
                sainSaveContentField = null;
                sainGuiContentTextProperty = null;
                cineKitPanelNamesField = null;
                cineKitAntialiasingNamesField = null;
                cineKitRecordingFpsNamesField = null;
                cineKitFreecamContentField = null;
                cineKitRecordingStatusField = null;
                cineKitRecordingStatusMethod = null;
                suomiPrtUiLanguageOverrideField = null;
            }
        }

        internal static void RefreshLanguage()
        {
            EditableProfiles data;
            TrackedGeneratedLabel[] generatedLabels;
            TrackedTextPropertyLabel[] textPropertyLabels;
            lock (Gate)
            {
                if (!enabled || profiles == null) return;
                if (GeneratedLabels.Count == 0 && TextPropertyLabels.Count == 0 &&
                    cineKitPanelNamesField == null && cineKitAntialiasingNamesField == null &&
                    cineKitRecordingFpsNamesField == null && cineKitFreecamContentField == null) return;
                data = profiles;
                generatedLabels = GeneratedLabels.ToArray();
                textPropertyLabels = TextPropertyLabels.ToArray();
            }

            var staleGenerated = new List<TrackedGeneratedLabel>();
            var staleText = new List<TrackedTextPropertyLabel>();
            var changed = 0;
            foreach (var entry in generatedLabels)
            {
                var target = entry.Target.Target;
                if (target == null || !ApplyGeneratedLabel(target, entry.Source, data))
                {
                    staleGenerated.Add(entry);
                    continue;
                }
                changed++;
            }

            foreach (var entry in textPropertyLabels)
            {
                var target = entry.Target.Target;
                if (target == null ||
                    !ApplyTextPropertyLabel(target, entry.ProfileId, entry.Source, data))
                {
                    staleText.Add(entry);
                    continue;
                }
                changed++;
            }

            if (staleGenerated.Count > 0 || staleText.Count > 0)
            {
                lock (Gate)
                {
                    foreach (var entry in staleGenerated) GeneratedLabels.Remove(entry);
                    foreach (var entry in staleText) TextPropertyLabels.Remove(entry);
                }
            }

            RefreshCineKitCachedText();

            if (changed > 0)
                {}
        }

        private static void PatchCineKitCachedText(Assembly assembly)
        {
            try
            {
                var type = assembly?.GetType("CineKit.Plugin", false);
                if (type == null) return;
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                cineKitPanelNamesField = type.GetField("PanelNames", flags);
                cineKitAntialiasingNamesField = type.GetField("AntialiasingNames", flags);
                cineKitRecordingFpsNamesField = type.GetField("RecordingFpsNames", flags);
                cineKitFreecamContentField = type.GetField("FreecamContent", flags);
                cineKitRecordingStatusField = type.GetField("_recordingStatus", BindingFlags.Instance | BindingFlags.NonPublic);
                RefreshCineKitCachedText();
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:436", () => "CineKit cached UI translation skipped: " + ex.Message);
            }
        }

        private static void RefreshCineKitCachedText()
        {
            if (profiles == null) return;
            try
            {
                TranslateCineKitArray(cineKitPanelNamesField,
                    new[] { "CAMERA", "PATH", "SCHEMATICS", "RECORD / PLAY", "OTHER" });
                TranslateCineKitArray(cineKitAntialiasingNamesField,
                    new[] { "Gameplay", "Off", "FXAA", "TAA Low", "TAA High" });
                TranslateCineKitArray(cineKitRecordingFpsNamesField,
                    new[] { "Native FPS", "Custom FPS" });

                var content = cineKitFreecamContentField?.GetValue(null) as GUIContent;
                if (content != null)
                {
                    content.text = profiles.Translate(CineKitProfileId, Channel,
                        "Freecam — Enable in raid after loading finishes");
                    content.tooltip = profiles.Translate(CineKitProfileId, Channel,
                        "Freecam can only be enabled after the raid has fully started.");
                }
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:463", () => "CineKit cached UI refresh skipped: " + ex.Message);
            }
        }

        private static void TranslateCineKitArray(FieldInfo field, string[] sources)
        {
            var values = field?.GetValue(null) as string[];
            if (values == null || sources == null) return;
            var count = Math.Min(values.Length, sources.Length);
            for (var i = 0; i < count; i++)
                values[i] = profiles.Translate(CineKitProfileId, Channel, sources[i]);
        }

        private static int PatchCineKitRecordingStatus(Assembly assembly)
        {
            try
            {
                var type = assembly?.GetType("CineKit.Plugin", false);
                if (type == null || cineKitRecordingStatusField == null) return 0;
                var method = type.GetMethod("DrawRecordingPanel",
                    BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (method == null || ReferenceEquals(cineKitRecordingStatusMethod, method)) return 0;

                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                        nameof(BeforeCineKitRecordingPanel), BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.Last },
                    postfix: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                        nameof(AfterCineKitRecordingPanel), BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.First });
                cineKitRecordingStatusMethod = method;
                return 1;
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:500", () => "Small UI CineKit recording-status patch skipped: " + ex.Message);
                return 0;
            }
        }

        private static void BeforeCineKitRecordingPanel(object __instance, out string __state)
        {
            __state = null;
            if (__instance == null || profiles == null || cineKitRecordingStatusField == null) return;
            try
            {
                var source = cineKitRecordingStatusField.GetValue(__instance) as string;
                if (source == null) return;
                var translated = profiles.Translate(CineKitProfileId, Channel, source);
                if (string.Equals(source, translated, StringComparison.Ordinal)) return;
                __state = source;
                cineKitRecordingStatusField.SetValue(__instance, translated);
            }
            catch { __state = null; }
        }

        private static void AfterCineKitRecordingPanel(object __instance, string __state)
        {
            if (__instance == null || __state == null || cineKitRecordingStatusField == null) return;
            try { cineKitRecordingStatusField.SetValue(__instance, __state); }
            catch { }
        }

        private static int PatchSuomiPrtLocalization(Assembly assembly)
        {
            var type = assembly?.GetType("RadioMod.Client.Plugin", false);
            if (type == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:533", () => "Small UI Suomi-PRT hook skipped: RadioMod.Client.Plugin missing");
                return 0;
            }

            suomiPrtUiLanguageOverrideField = type.GetField("_uiLanguageOverride",
                BindingFlags.Static | BindingFlags.NonPublic);

            var patched = 0;
            var string8 = new[]
            {
                typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string), typeof(string)
            };
            var languageMethod = type.GetMethod("L",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, string8, null);
            if (languageMethod != null)
                patched += PatchSuomiPrtPostfix(languageMethod, nameof(AfterSuomiPrtLanguage));

            var historyMethod = type.GetMethod("GetRadioHistory",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            if (historyMethod != null)
                patched += PatchSuomiPrtPostfix(historyMethod, nameof(AfterSuomiPrtHistory));

            if (languageMethod == null)
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:559", () => "Small UI Suomi-PRT hook skipped: exact Plugin.L signature missing");
            if (historyMethod == null)
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:561", () => "Small UI Suomi-PRT history hook skipped: GetRadioHistory(string) missing");

            return patched;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                var assembly = args?.LoadedAssembly;
                if (assembly == null) return;

                var assemblyName = assembly.GetName().Name;
                var isDoomArcade = string.Equals(assemblyName, DoomArcadeAssemblyName,
                    StringComparison.OrdinalIgnoreCase);
                // Only these two newly supported mods need the late-load fallback.
                // Never wildcard-patch other assemblies when a DLL arrives after Plugin.Start.
                var isQuartermaster = string.Equals(assemblyName, "TheQuartermaster.Client",
                    StringComparison.OrdinalIgnoreCase);
                var isArmorExpert = string.Equals(assemblyName, "Liquidwarp.ArmorExpert",
                    StringComparison.OrdinalIgnoreCase);
                if (!isDoomArcade && !isQuartermaster && !isArmorExpert) return;

                EditableProfiles data;
                lock (Gate)
                {
                    if (!enabled || harmony == null || profiles == null) return;
                    data = profiles;
                }

                if (isDoomArcade)
                {
                    if (!data.HasRules(DoomArcadeProfileId, Channel)) return;
                    PatchDoomArcadeMenu(assembly);
                    return;
                }

                // PatchTarget and PatchedMethods share the same duplicate guard as
                // startup registration, so an assembly seen twice is not repatched.
                foreach (var target in Targets)
                {
                    if (!string.Equals(target.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase) ||
                        !data.HasRules(target.ProfileId, Channel)) continue;
                    PatchTarget(assembly, target);
                }

            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:597", () => "Small UI late-load hook skipped: " + ex.Message);
            }
        }

        private static int PatchDoomArcadeMenu(Assembly assembly)
        {
            var type = assembly?.GetType("DoomArcade.Scripts.Arcade.DoomArcadeUI", false);
            if (type == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:606", () => "Small UI WTT DoomArcade hook skipped: DoomArcadeUI type missing");
                return 0;
            }

            var method = FindExactDeclaredMethod(type, "RefreshPage", 0, typeof(void));
            if (method == null) return 0;

            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                var postfix = typeof(SmallUiLiteralHooks).GetMethod(nameof(AfterDoomArcadeRefreshPage),
                    BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:629", () => "Small UI WTT DoomArcade patch skipped: " +
                    FormatMethod(method) + " - " + ex.Message);
                return 0;
            }
        }

        private static void AfterDoomArcadeRefreshPage(object __instance)
        {
            if (__instance == null || profiles == null) return;

            try
            {
                var type = __instance.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                var menuItems = type.GetField("_menuItems", flags)?.GetValue(__instance) as System.Collections.IEnumerable;
                if (menuItems != null)
                {
                    foreach (var entry in menuItems)
                    {
                        if (entry == null) continue;
                        var label = entry.GetType().GetField("Label", flags)?.GetValue(entry);
                        TranslateAndTrackDoomArcadeText(label);
                    }
                }

                var description = type.GetField("descriptionText", flags)?.GetValue(__instance);
                TranslateAndTrackDoomArcadeText(description);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:660", () => "Small UI WTT DoomArcade text kept unchanged: " + ex.Message);
            }
        }

        private static void TranslateAndTrackDoomArcadeText(object textTarget)
        {
            if (textTarget == null || profiles == null) return;
            var textProperty = textTarget.GetType().GetProperty("text",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanRead ||
                !textProperty.CanWrite) return;

            var source = textProperty.GetValue(textTarget, null) as string;
            if (string.IsNullOrEmpty(source) || !profiles.CanTranslate(DoomArcadeProfileId, Channel, source)) return;

            if (!ApplyTextPropertyLabel(textTarget, DoomArcadeProfileId, source, profiles)) return;
            TrackOrUpdateTextPropertyLabel(textTarget, DoomArcadeProfileId, source);
        }

        private static void TrackOrUpdateTextPropertyLabel(object target, string profileId, string source)
        {
            if (target == null || string.IsNullOrEmpty(source)) return;
            lock (Gate)
            {
                if (!enabled) return;
                for (var i = TextPropertyLabels.Count - 1; i >= 0; i--)
                {
                    var existing = TextPropertyLabels[i].Target.Target;
                    if (existing == null)
                    {
                        TextPropertyLabels.RemoveAt(i);
                        continue;
                    }
                    if (!ReferenceEquals(existing, target)) continue;
                    if (string.Equals(TextPropertyLabels[i].ProfileId, profileId, StringComparison.Ordinal) &&
                        string.Equals(TextPropertyLabels[i].Source, source, StringComparison.Ordinal))
                        return;
                    TextPropertyLabels[i] = new TrackedTextPropertyLabel(target, profileId, source);
                    return;
                }
                TextPropertyLabels.Add(new TrackedTextPropertyLabel(target, profileId, source));
            }
        }

        private static int PatchSuomiPrtPostfix(MethodInfo method, string postfixName)
        {
            if (method == null) return 0;
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                var postfix = typeof(SmallUiLiteralHooks).GetMethod(postfixName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (postfix == null) throw new MissingMethodException(typeof(SmallUiLiteralHooks).FullName, postfixName);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix) { priority = Priority.First });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:724", () => "Small UI Suomi-PRT patch skipped: " + FormatMethod(method) + " - " + ex.Message);
                return 0;
            }
        }

        private static bool SuomiPrtUsesAutomaticLanguage()
        {
            if (!LocaleMode.IsKoreanMode()) return false;

            try
            {
                var entry = suomiPrtUiLanguageOverrideField?.GetValue(null);
                if (entry == null) return true;

                var property = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                var value = property?.GetValue(entry, null);
                return value == null || string.Equals(value.ToString(), "Auto", StringComparison.Ordinal);
            }
            catch
            {
                // Failing open is safer here: this hook only runs in Korean Patch Fix's Korean modes,
                // and the upstream Auto path otherwise falls back to English.
                return true;
            }
        }

        private static void AfterSuomiPrtLanguage(object[] __args, ref string __result)
        {
            if (profiles == null || !SuomiPrtUsesAutomaticLanguage() || __args == null || __args.Length < 2)
                return;

            var english = __args[1] as string;
            if (string.IsNullOrEmpty(english) || !string.Equals(__result, english, StringComparison.Ordinal))
                return;

            __result = profiles.Translate(SuomiPrtProfileId, Channel, english);
        }

        private static void AfterSuomiPrtHistory(ref string __result)
        {
            if (profiles == null || !SuomiPrtUsesAutomaticLanguage() || string.IsNullOrEmpty(__result))
                return;

            __result = profiles.Translate(SuomiPrtProfileId, Channel, __result);
        }

        private static int PatchWikiLinksOpenLabels(Assembly assembly)
        {
            var patched = 0;
            patched += PatchWikiLinksOpenLabelMethod(assembly,
                "WikiLinks.ContextMenuPatches+AddWikiButtonPatch", "Prefix", 2);
            patched += PatchWikiLinksOpenLabelMethod(assembly,
                "WikiLinks.QuestPatches", "GetOrCreateButton", 3);
            return patched;
        }

        private static int PatchWikiLinksOpenLabelMethod(Assembly assembly, string typeName, string methodName, int parameterCount)
        {
            var type = assembly?.GetType(typeName, false);
            if (type == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:785", () => "Small UI WikiLinks hook skipped: type missing " + typeName);
                return 0;
            }

            MethodInfo[] candidates;
            try
            {
                candidates = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                             BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method => method != null && !method.IsAbstract && !method.ContainsGenericParameters &&
                        string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                        method.GetParameters().Length == parameterCount)
                    .ToArray();
            }
            catch { return 0; }

            if (candidates.Length != 1)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:803", () => "Small UI WikiLinks hook skipped: expected one exact target " +
                    typeName + "." + methodName + ", found " + candidates.Length);
                return 0;
            }

            var method = candidates[0];
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                harmony.Patch(method,
                    transpiler: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                        nameof(TranslateWikiLinksOpenLabelConcat), BindingFlags.Static | BindingFlags.NonPublic))
                        { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:827", () => "Small UI WikiLinks patch skipped: " + FormatMethod(method) + " - " + ex.Message);
                return 0;
            }
        }

        private static IEnumerable<CodeInstruction> TranslateWikiLinksOpenLabelConcat(IEnumerable<CodeInstruction> instructions)
        {
            var concat = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) });
            var runtime = typeof(SmallUiLiteralHooks).GetMethod(nameof(TranslateWikiLinksOpenLabelRuntime),
                BindingFlags.Static | BindingFlags.NonPublic);

            foreach (var instruction in instructions)
            {
                yield return instruction;
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    instruction.operand is MethodInfo called && concat != null && called.Equals(concat))
                {
                    yield return new CodeInstruction(OpCodes.Call, runtime);
                }
            }
        }

        private static string TranslateWikiLinksOpenLabelRuntime(string rendered)
        {
            if (profiles == null || !LocaleMode.IsKoreanMode() || string.IsNullOrEmpty(rendered)) return rendered;
            if (!rendered.EndsWith(" WIKI", StringComparison.Ordinal)) return rendered;
            return profiles.Translate(WikiLinksProfileId, Channel, "OPEN WIKI");
        }

        private static int PatchTarget(Assembly assembly, Target target)
        {
            var rootType = assembly.GetType(target.TypeName, false);
            if (rootType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:861", () => "Small UI exact hook skipped: type missing " + target.TypeName);
                return 0;
            }

            var scope = new List<Type> { rootType };
            if (target.IncludeNestedTypes)
            {
                try { scope.AddRange(rootType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)); }
                catch { }
            }

            var patched = 0;
            foreach (var type in scope)
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch { continue; }

                foreach (var method in methods)
                {
                    if (method == null || method.IsAbstract || method.ContainsGenericParameters) continue;
                    if (target.MethodName != "*" && !string.Equals(method.Name, target.MethodName, StringComparison.Ordinal)) continue;
                    if (target.ParameterCount >= 0 && method.GetParameters().Length != target.ParameterCount) continue;
                    if (!ContainsTranslatableLiteral(method, target.ProfileId)) continue;

                    lock (Gate)
                    {
                        if (!enabled || harmony == null || !PatchedMethods.Add(method)) continue;
                    }

                    // This log is deliberately emitted before Harmony.Patch. If a native detour failure
                    // ever occurs again, LogOutput identifies the exact method instead of only the family.
                    try
                    {
                        harmony.Patch(method,
                            prefix: null,
                            postfix: null,
                            transpiler: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                                nameof(TranslateSmallUiLiterals), BindingFlags.Static | BindingFlags.NonPublic))
                                { priority = Priority.Last },
                            finalizer: null,
                            ilmanipulator: null);
                        patched++;
                    }
                    catch (Exception ex)
                    {
                        lock (Gate) PatchedMethods.Remove(method);
                        SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:916", () => "Small UI exact patch skipped: " + assembly.GetName().Name + " :: " +
                            type.FullName + "." + method.Name + " - " + ex.Message);
                    }
                }
            }

            return patched;
        }

        private static int PatchStatTrack(Assembly assembly)
        {
            var patched = 0;

            var utilityType = assembly.GetType("StattrackClient.Utils.Utility", false);
            if (utilityType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:932", () => "Small UI StatTrack hook skipped: Utility type missing");
                return 0;
            }

            var statIdType = utilityType.GetNestedType("EStatTrackAttributeId", BindingFlags.Public | BindingFlags.NonPublic);
            if (statIdType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:939", () => "Small UI StatTrack hook skipped: EStatTrackAttributeId type missing");
                return 0;
            }

            var getName = FindExactDeclaredMethod(utilityType, "GetName", typeof(string), statIdType);
            if (getName != null)
                patched += PatchSainMethod(assembly, utilityType, getName, null, nameof(AfterStatTrackGetName), null);

            var jsonType = assembly.GetType("StattrackClient.Utils.JsonFileUtils", false);
            if (jsonType != null)
            {
                var getData = FindExactDeclaredMethod(jsonType, "GetData", typeof(string),
                    typeof(string), statIdType, typeof(bool), typeof(string), typeof(bool));
                if (getData != null)
                    patched += PatchSainMethod(assembly, jsonType, getData, null, null, nameof(TranslateStatTrackLiterals));
            }

            return patched;
        }

        private static void AfterStatTrackGetName(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || profiles == null) return;
            __result = profiles.Translate(StatTrackProfileId, Channel, __result);
        }

        private static IEnumerable<CodeInstruction> TranslateStatTrackLiterals(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                var shouldTranslate = instruction.opcode == OpCodes.Ldstr &&
                    instruction.operand is string source &&
                    profiles?.CanTranslate(StatTrackProfileId, Channel, source) == true;
                yield return instruction;
                if (shouldTranslate)
                {
                    yield return new CodeInstruction(OpCodes.Ldstr, StatTrackProfileId);
                    yield return new CodeInstruction(OpCodes.Call, RuntimeTranslator);
                }
            }
        }

        private static int PatchSainEditor(Assembly assembly)
        {
            var patched = 0;

            // SAIN owns a standalone IMGUI editor. Translate only SAIN's own layout wrappers and
            // a handful of audited direct-GUI methods; never patch UnityEngine.GUI/GUILayout globally.
            var layoutType = assembly.GetType("SAIN.Editor.SAINLayout", false);
            if (layoutType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:990", () => "Small UI SAIN hook skipped: SAIN.Editor.SAINLayout missing");
                return 0;
            }

            MethodInfo[] layoutMethods;
            try
            {
                layoutMethods = layoutType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(IsSainLayoutSurfaceTarget)
                    .ToArray();
            }
            catch { layoutMethods = Array.Empty<MethodInfo>(); }

            // SAIN 4.5.1 source contract: patch only methods that actually call Unity GUI/GUILayout.
            // Chained Label/Button/Toggle wrappers are deliberately excluded so the same text is not
            // translated two or three times during one IMGUI draw. Box overloads all render directly.
            // Expected render surfaces: 5 Box + 3 Label + 1 Button + 1 Toggle = 10.
            // TextField/TextArea remain excluded: user-entered text must never be translated.
            if (layoutMethods.Length != 10)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1010", () => "Small UI SAIN hook skipped: expected 10 audited SAINLayout render surfaces, found " + layoutMethods.Length);
                return 0;
            }

            foreach (var method in layoutMethods)
            {
                lock (Gate)
                {
                    if (!enabled || harmony == null || !PatchedMethods.Add(method)) continue;
                }

                try
                {
                    var prefixMethod = GetSainLayoutTypedPrefix(method);
                    if (prefixMethod == null)
                        throw new InvalidOperationException("No typed SAINLayout prefix for " + FormatMethod(method));
                    harmony.Patch(method,
                        prefix: new HarmonyMethod(prefixMethod) { priority = Priority.Last });
                    patched++;
                }
                catch (Exception ex)
                {
                    lock (Gate) PatchedMethods.Remove(method);
                    SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1035", () => "Small UI SAIN layout patch skipped: " + FormatMethod(method) + " - " + ex.Message);
                }
            }

            patched += PatchSainTabSelectionGrid(assembly);

            var editorType = assembly.GetType("SAIN.Editor.SAINEditor", false);
            if (editorType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1044", () => "Small UI SAIN direct hook skipped: SAIN.Editor.SAINEditor missing");
            }
            else
            {
                patched += PatchSainLiteralMethod(assembly, editorType, "OnGUI", 0);
                patched += PatchSainLiteralMethod(assembly, editorType, "CreateDragBar", 0);
                patched += PatchSainTopBar(assembly, editorType);
            }

            var presetType = assembly.GetType("SAIN.Editor.GUISections.PresetSelection", false);
            var presetDefinitionType = FindLoadedType("SAIN.Preset.Shared.Preset.SAINPresetDefinition");
            if (presetType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1057", () => "Small UI SAIN preset hook skipped: PresetSelection missing");
            }
            else
            {
                // These callers contain display-only literals that are visible in the Home tab.
                // Patch the literals directly as a fallback to the final SAINLayout boundary.
                patched += PatchSainLiteralMethod(assembly, presetType, "PresetSelectionMenu", 0);
                patched += PatchSainLiteralMethod(assembly, presetType, "baseSelectionOptions", 0);

                if (presetDefinitionType == null)
                {
                    SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1068", () => "Small UI SAIN preset detail hooks skipped: SAINPresetDefinition missing");
                }
                else
                {
                    var warning = FindExactDeclaredMethod(presetType, "checkCreateWarning", typeof(void), presetDefinitionType);
                    if (warning != null)
                        patched += PatchSainMethod(assembly, presetType, warning, null, null, nameof(TranslateSainLiterals));

                    foreach (var name in new[] { "selectDefault", "selectCustom" })
                    {
                        var method = FindExactDeclaredMethod(presetType, name, presetDefinitionType, presetDefinitionType);
                        if (method != null)
                        {
                            // Keep preset.Name exactly as SAIN supplied it. Only the section heading and
                            // preset description are localized while these methods render their Toggle rows.
                            patched += PatchSainMethod(assembly, presetType, method,
                                nameof(BeforeSainPresetList), nameof(AfterSainPresetList), nameof(TranslateSainLiterals));
                        }
                    }
                }
            }

            // The source-audited callers below contain the exact English literals visible in the
            // user's Home/Bot Settings screenshots. Keeping these targeted fallbacks costs very little
            // with the 1.7.6 translation cache and avoids depending on one renderer path for static labels.
            var botSettingsEditorType = assembly.GetType("SAIN.Editor.GUISections.BotSettingsEditor", false);
            if (botSettingsEditorType != null)
            {
                patched += PatchSainLiteralMethod(assembly, botSettingsEditorType, "ShowAllSettingsGUI", 6);
                patched += PatchSainLiteralMethod(assembly, botSettingsEditorType, "CheckIfOpen", 2);
            }

            var botSelectionType = assembly.GetType("SAIN.Editor.BotSelectionClass", false);
            if (botSelectionType != null)
            {
                patched += PatchSainLiteralMethod(assembly, botSelectionType, "Menu", 0);
                patched += PatchSainLiteralMethod(assembly, botSelectionType, "SelectProperties", 0);
            }

            var builderType = assembly.GetType("SAIN.Editor.BuilderClass", false);
            if (builderType != null)
            {
                var searchParamsType = builderType.GetNestedType("SearchParams", BindingFlags.Public | BindingFlags.NonPublic);
                if (searchParamsType != null)
                {
                    var searchBox = FindExactDeclaredMethod(builderType, "SearchBox", typeof(string),
                        typeof(string), typeof(float), searchParamsType);
                    if (searchBox != null)
                        patched += PatchSainMethod(assembly, builderType, searchBox, null, null, nameof(TranslateSainLiterals));
                }

                var saveChanges = FindExactDeclaredMethod(builderType, "SaveChanges", typeof(bool), typeof(string), typeof(float));
                if (saveChanges != null)
                    patched += PatchSainMethod(assembly, builderType, saveChanges, null, null, nameof(TranslateSainLiterals));
            }

            var trackerType = assembly.GetType("SAIN.Plugin.ConfigEditingTracker", false);
            if (trackerType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1127", () => "Small UI SAIN unsaved-tooltip hook skipped: ConfigEditingTracker missing");
            }
            else
            {
                var method = FindExactDeclaredMethod(trackerType, "GetUnsavedValuesString", 0, typeof(string));
                if (method != null)
                    patched += PatchSainMethod(assembly, trackerType, method, null,
                        nameof(AfterSainUnsavedValuesString), null);
            }

            // Runtime values (category names, bot sections, bot names and enum difficulties) still
            // localize at the 10 final SAINLayout render surfaces. The small caller list above exists
            // only for source-audited static literals and is cheap because TextRules caches exact hits.

            return patched;
        }

        private static bool IsSainLayoutSurfaceTarget(MethodInfo method)
        {
            if (method == null || method.IsGenericMethod || method.ContainsGenericParameters) return false;
            if (!method.IsStatic || !method.IsPublic) return false;

            var parameters = method.GetParameters();
            if (string.Equals(method.Name, "Box", StringComparison.Ordinal))
            {
                // All five Box overloads render directly to GUILayout.Box in SAIN 4.5.1.
                return true;
            }

            if (string.Equals(method.Name, "Label", StringComparison.Ordinal))
            {
                // Direct renderers are Label(GUIContent, GUIStyle, ...), Label(Rect, GUIContent, GUIStyle),
                // and Label(Rect, string, GUIStyle). Other Label overloads delegate to the first one.
                var hasRect = parameters.Any(p => string.Equals(p.ParameterType.FullName, "UnityEngine.Rect", StringComparison.Ordinal));
                if (hasRect) return true;
                return parameters.Any(p => p.ParameterType == typeof(GUIContent)) &&
                       parameters.Any(p => p.ParameterType == typeof(GUIStyle));
            }

            if (string.Equals(method.Name, "Button", StringComparison.Ordinal))
            {
                // Only the GUIContent + GUIStyle overload calls GUILayout.Button; all others delegate to it.
                return parameters.Any(p => p.ParameterType == typeof(GUIContent)) &&
                       parameters.Any(p => p.ParameterType == typeof(GUIStyle));
            }

            if (string.Equals(method.Name, "Toggle", StringComparison.Ordinal))
            {
                // Only the GUIContent + GUIStyle overload calls GUILayout.Toggle; all others delegate to it.
                return parameters.Any(p => p.ParameterType == typeof(GUIContent)) &&
                       parameters.Any(p => p.ParameterType == typeof(GUIStyle));
            }

            return false;
        }

        private static int PatchSainTabSelectionGrid(Assembly assembly)
        {
            var builderType = assembly.GetType("SAIN.Editor.BuilderClass", false);
            if (builderType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1188", () => "Small UI SAIN tab hook skipped: SAIN.Editor.BuilderClass missing");
                return 0;
            }

            MethodInfo[] candidates;
            try
            {
                candidates = builderType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(method =>
                    {
                        if (!string.Equals(method.Name, "SelectionGridExpandHeight", StringComparison.Ordinal) ||
                            method.ReturnType != typeof(string)) return false;
                        var p = method.GetParameters();
                        return p.Length == 8 &&
                            string.Equals(p[0].ParameterType.FullName, "UnityEngine.Rect", StringComparison.Ordinal) &&
                            p[1].ParameterType == typeof(string[]) &&
                            p[2].ParameterType == typeof(string) &&
                            string.Equals(p[3].ParameterType.FullName, "UnityEngine.Rect[]", StringComparison.Ordinal) &&
                            p[4].ParameterType == typeof(float) &&
                            p[5].ParameterType == typeof(float) &&
                            p[6].ParameterType == typeof(float) &&
                            p[7].ParameterType == typeof(string[]);
                    })
                    .ToArray();
            }
            catch { candidates = Array.Empty<MethodInfo>(); }

            if (candidates.Length != 1)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1217", () => "Small UI SAIN tab hook skipped: expected one exact SelectionGridExpandHeight target, found " + candidates.Length);
                return 0;
            }

            return PatchSainMethod(assembly, builderType, candidates[0],
                nameof(BeforeSainTabSelectionGrid), nameof(AfterSainTabSelectionGrid), null);
        }

        private static void BeforeSainTabSelectionGrid(ref string[] __1, ref string __2, ref string[] __7,
            out SainTabSelectionState __state)
        {
            __state = null;
            if (profiles == null || __1 == null || __1.Length == 0) return;

            var sourceOptions = __1;
            var displayOptions = TranslateSainStringArray(sourceOptions);
            var displayTooltips = TranslateSainStringArray(__7);
            var displaySelected = string.IsNullOrEmpty(__2) ? __2 : profiles.Translate(SainProfileId, Channel, __2);

            var optionsChanged = !ReferenceEquals(sourceOptions, displayOptions);
            var tooltipsChanged = !ReferenceEquals(__7, displayTooltips);
            var selectedChanged = !string.Equals(__2, displaySelected, StringComparison.Ordinal);
            if (!optionsChanged && !tooltipsChanged && !selectedChanged) return;

            __state = new SainTabSelectionState(sourceOptions, displayOptions);
            __1 = displayOptions;
            __2 = displaySelected;
            __7 = displayTooltips;
        }

        private static void AfterSainTabSelectionGrid(ref string __result, SainTabSelectionState __state)
        {
            if (__state == null || string.IsNullOrEmpty(__result)) return;
            var source = __state.SourceOptions;
            var display = __state.DisplayOptions;
            if (source == null || display == null || source.Length != display.Length) return;

            for (var i = 0; i < display.Length; i++)
            {
                if (!string.Equals(__result, display[i], StringComparison.Ordinal)) continue;
                __result = source[i];
                return;
            }
        }

        private static string[] TranslateSainStringArray(string[] source)
        {
            if (source == null || source.Length == 0 || profiles == null) return source;
            string[] translated = null;
            for (var i = 0; i < source.Length; i++)
            {
                var value = source[i];
                var replacement = string.IsNullOrEmpty(value) ? value : profiles.Translate(SainProfileId, Channel, value);
                if (translated == null && string.Equals(value, replacement, StringComparison.Ordinal)) continue;
                if (translated == null) translated = (string[])source.Clone();
                translated[i] = replacement;
            }
            return translated ?? source;
        }

        private static int PatchSainTopBar(Assembly assembly, Type editorType)
        {
            var method = FindExactDeclaredMethod(editorType, "CreateTopBarOptions", 0, typeof(void));
            if (method == null) return 0;

            try
            {
                var field = editorType.GetField("SaveContent", BindingFlags.Static | BindingFlags.NonPublic);
                var contentType = field?.FieldType;
                var textProperty = contentType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (field != null && textProperty != null && textProperty.CanRead && textProperty.CanWrite)
                {
                    sainSaveContentField = field;
                    sainGuiContentTextProperty = textProperty;
                }
                else
                {
                    SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1294", () => "Small UI SAIN top-bar note: SaveContent GUIContent contract missing; literal hook only");
                }
            }
            catch { }

            return PatchSainMethod(assembly, editorType, method,
                nameof(BeforeSainCreateTopBarOptions), nameof(AfterSainCreateTopBarOptions), nameof(TranslateSainLiterals));
        }

        private static int PatchSainLiteralMethod(Assembly assembly, Type type, string name, int parameterCount)
        {
            var method = FindExactDeclaredMethod(type, name, parameterCount, null);
            if (method == null) return 0;
            // Exact SAIN 4.5.1 methods are source-audited. Do not gate the Harmony patch on the
            // hand-written IL scanner: newer compiler IL can make the pre-scan fail even though the
            // transpiler itself is safe and only rewrites ldstr values that have an exact rule.
            return PatchSainMethod(assembly, type, method, null, null, nameof(TranslateSainLiterals));
        }

        private static int PatchSainMethod(Assembly assembly, Type type, MethodInfo method,
            string prefixName, string postfixName, string transpilerName)
        {
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                var prefix = string.IsNullOrEmpty(prefixName) ? null : new HarmonyMethod(
                    typeof(SmallUiLiteralHooks).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.Last };
                var postfix = string.IsNullOrEmpty(postfixName) ? null : new HarmonyMethod(
                    typeof(SmallUiLiteralHooks).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.Last };
                var transpiler = string.IsNullOrEmpty(transpilerName) ? null : new HarmonyMethod(
                    typeof(SmallUiLiteralHooks).GetMethod(transpilerName, BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.Last };
                harmony.Patch(method, prefix: prefix, postfix: postfix, transpiler: transpiler);
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1340", () => "Small UI SAIN exact patch skipped: " + assembly.GetName().Name + " :: " +
                    FormatMethod(method) + " - " + ex.Message);
                return 0;
            }
        }


        private static Type FindLoadedType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = loaded?.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        private static MethodInfo FindExactDeclaredMethod(Type type, string name, Type returnType, params Type[] parameterTypes)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            try
            {
                var method = type.GetMethod(name, flags, null, parameterTypes ?? Type.EmptyTypes, null);
                if (method != null && method.DeclaringType == type &&
                    (returnType == null || method.ReturnType == returnType))
                    return method;
            }
            catch { }

            var signature = string.Join(",", (parameterTypes ?? Type.EmptyTypes).Select(x => x?.Name ?? "<null>"));
            SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1376", () => "Small UI exact hook skipped: missing exact " + type.FullName + "." +
                name + "(" + signature + ")");
            return null;
        }

        private static MethodInfo FindExactDeclaredMethod(Type type, string name, int parameterCount, Type returnType)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal) &&
                        method.GetParameters().Length == parameterCount &&
                        (returnType == null || method.ReturnType == returnType))
                    .ToArray();
            }
            catch { return null; }

            if (methods.Length == 1) return methods[0];
            SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1396", () => "Small UI SAIN exact hook skipped: expected one " + type.FullName + "." +
                name + "/" + parameterCount + ", found " + methods.Length);
            return null;
        }

        private static string FormatMethod(MethodBase method)
        {
            if (method == null) return "<null>";
            string parameters;
            try
            {
                parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name));
            }
            catch { parameters = "?"; }
            return method.DeclaringType?.FullName + "." + method.Name + "(" + parameters + ")";
        }

        private static MethodInfo GetSainLayoutTypedPrefix(MethodInfo method)
        {
            if (method == null) return null;
            var p = method.GetParameters();
            string prefixName = null;

            // Use Harmony's numeric argument aliases (__0, __1) rather than compiled parameter names.
            // This keeps the patch stable even if a release build changes/strips parameter-name metadata.
            if (string.Equals(method.Name, "Box", StringComparison.Ordinal))
            {
                if (p.Length > 0 && p[0].ParameterType == typeof(GUIContent))
                    prefixName = nameof(BeforeSainArg0Content);
                else if (p.Length > 1 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string))
                    prefixName = nameof(BeforeSainArg0TextArg1Tooltip);
                else if (p.Length > 0 && p[0].ParameterType == typeof(string))
                    prefixName = nameof(BeforeSainArg0Text);
            }
            else if (string.Equals(method.Name, "Label", StringComparison.Ordinal))
            {
                if (p.Length > 1 && p[0].ParameterType == typeof(Rect) && p[1].ParameterType == typeof(GUIContent))
                    prefixName = nameof(BeforeSainArg1Content);
                else if (p.Length > 1 && p[0].ParameterType == typeof(Rect) && p[1].ParameterType == typeof(string))
                    prefixName = nameof(BeforeSainArg1Text);
                else if (p.Length > 0 && p[0].ParameterType == typeof(GUIContent))
                    prefixName = nameof(BeforeSainArg0Content);
            }
            else if (string.Equals(method.Name, "Button", StringComparison.Ordinal))
            {
                if (p.Length > 0 && p[0].ParameterType == typeof(GUIContent))
                    prefixName = nameof(BeforeSainArg0Content);
            }
            else if (string.Equals(method.Name, "Toggle", StringComparison.Ordinal))
            {
                if (p.Length > 1 && p[1].ParameterType == typeof(GUIContent))
                    prefixName = nameof(BeforeSainToggleArg1Content);
            }

            return prefixName == null
                ? null
                : typeof(SmallUiLiteralHooks).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static string TranslateSainDisplayText(string value)
        {
            if (profiles == null || string.IsNullOrEmpty(value)) return value;
            return profiles.Translate(SainProfileId, Channel, value);
        }

        private static GUIContent TranslateSainContent(GUIContent content, bool preserveText = false)
        {
            if (profiles == null || content == null) return content;

            var sourceText = content.text;
            var sourceTooltip = content.tooltip;
            var translatedText = preserveText ? sourceText : TranslateSainDisplayText(sourceText);
            var translatedTooltip = TranslateSainDisplayText(sourceTooltip);

            if (string.Equals(sourceText, translatedText, StringComparison.Ordinal) &&
                string.Equals(sourceTooltip, translatedTooltip, StringComparison.Ordinal))
                return content;

            // Never mutate SAIN's shared GUIContent instances. Clone only when visible text changes.
            return new GUIContent(content)
            {
                text = translatedText,
                tooltip = translatedTooltip
            };
        }

        private static void BeforeSainArg0Text(ref string __0)
        {
            __0 = TranslateSainDisplayText(__0);
        }

        private static void BeforeSainArg1Text(ref string __1)
        {
            __1 = TranslateSainDisplayText(__1);
        }

        private static void BeforeSainArg0TextArg1Tooltip(ref string __0, ref string __1)
        {
            __0 = TranslateSainDisplayText(__0);
            __1 = TranslateSainDisplayText(__1);
        }

        private static void BeforeSainArg0Content(ref GUIContent __0)
        {
            __0 = TranslateSainContent(__0);
        }

        private static void BeforeSainArg1Content(ref GUIContent __1)
        {
            __1 = TranslateSainContent(__1);
        }

        private static void BeforeSainToggleArg1Content(ref GUIContent __1)
        {
            // selectDefault/selectCustom use Toggle text for preset.Name. Preset names are user/data
            // identifiers and must stay exactly as SAIN supplied them; their descriptions may localize.
            __1 = TranslateSainContent(__1, sainPresetListDepth > 0);
        }

        private static void BeforeSainPresetList()
        {
            sainPresetListDepth++;
        }

        private static void AfterSainPresetList()
        {
            if (sainPresetListDepth > 0) sainPresetListDepth--;
        }

        private static void BeforeSainCreateTopBarOptions(out string __state)
        {
            __state = null;
            if (profiles == null || sainSaveContentField == null || sainGuiContentTextProperty == null) return;
            try
            {
                var content = sainSaveContentField.GetValue(null);
                if (content == null) return;
                __state = sainGuiContentTextProperty.GetValue(content, null) as string;
                var source = string.IsNullOrEmpty(__state) ? "Save All Changes" : __state;
                sainGuiContentTextProperty.SetValue(content,
                    profiles.Translate(SainProfileId, Channel, source), null);
            }
            catch { __state = null; }
        }

        private static void AfterSainCreateTopBarOptions(string __state)
        {
            if (__state == null || sainSaveContentField == null || sainGuiContentTextProperty == null) return;
            try
            {
                var content = sainSaveContentField.GetValue(null);
                if (content != null) sainGuiContentTextProperty.SetValue(content, __state, null);
            }
            catch { }
        }

        private static void AfterSainUnsavedValuesString(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || profiles == null) return;
            __result = profiles.Translate(SainProfileId, Channel, __result);
        }

        private static IEnumerable<CodeInstruction> TranslateSainLiterals(IEnumerable<CodeInstruction> instructions)
        {
            // These methods are exact SAIN 4.5.1 UI methods. Do not decide at patch time whether a
            // literal is translatable; always defer that decision to TranslateRuntime. This prevents
            // a registered transpiler from becoming a permanent no-op because of initialization timing.
            foreach (var instruction in instructions)
            {
                var shouldTranslate = instruction.opcode == OpCodes.Ldstr && instruction.operand is string;
                yield return instruction;
                if (shouldTranslate)
                {
                    yield return new CodeInstruction(OpCodes.Ldstr, SainProfileId);
                    yield return new CodeInstruction(OpCodes.Call, RuntimeTranslator);
                }
            }
        }

        private static bool ContainsSainTranslatableLiteral(MethodBase method)
        {
            return ContainsTranslatableLiteral(method, SainProfileId);
        }

        private static int PatchBossNotifierMarkerText(Assembly assembly)
        {
            var monoType = assembly.GetType("BossNotifier.BossNotifierMono", false);
            var markerInfoType = assembly.GetType("BossNotifier.BossMarkerInfo", false);
            if (monoType == null || markerInfoType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1586", () => "Small UI BossNotifier marker hook skipped: exact types missing");
                return 0;
            }

            MethodInfo[] candidates;
            try
            {
                candidates = monoType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method =>
                    {
                        var parameters = method.GetParameters();
                        return string.Equals(method.Name, "UpdateMarkerText", StringComparison.Ordinal) &&
                            method.ReturnType == typeof(void) && parameters.Length == 2 &&
                            parameters[0].ParameterType == markerInfoType &&
                            parameters[1].ParameterType == typeof(float);
                    }).ToArray();
            }
            catch { return 0; }

            if (candidates.Length != 1)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1607", () => "Small UI BossNotifier marker hook skipped: expected one exact UpdateMarkerText target, found " + candidates.Length);
                return 0;
            }

            var infoProperty = markerInfoType.GetProperty("InfoTextMesh", BindingFlags.Instance | BindingFlags.Public);
            var textMeshType = infoProperty?.PropertyType;
            var textProperty = textMeshType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (infoProperty == null || textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanRead || !textProperty.CanWrite)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1616", () => "Small UI BossNotifier marker hook skipped: InfoTextMesh.text contract missing");
                return 0;
            }

            var method = candidates[0];
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
                bossNotifierInfoTextMeshProperty = infoProperty;
                bossNotifierTextProperty = textProperty;
            }

            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                    nameof(AfterBossNotifierUpdateMarkerText), BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate)
                {
                    PatchedMethods.Remove(method);
                    bossNotifierInfoTextMeshProperty = null;
                    bossNotifierTextProperty = null;
                }
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1647", () => "Small UI exact patch skipped: " + assembly.GetName().Name + " :: " +
                    monoType.FullName + "." + method.Name + " - " + ex.Message);
                return 0;
            }
        }

        private static void AfterBossNotifierUpdateMarkerText(object __0)
        {
            if (__0 == null || profiles == null || !LocaleMode.IsKoreanMode()) return;
            try
            {
                var infoText = bossNotifierInfoTextMeshProperty?.GetValue(__0, null);
                if (infoText == null) return;
                var source = bossNotifierTextProperty?.GetValue(infoText, null) as string;
                if (string.IsNullOrEmpty(source)) return;
                var translated = profiles.Translate(BossNotifierProfileId, Channel, source);
                if (!string.Equals(source, translated, StringComparison.Ordinal))
                    bossNotifierTextProperty.SetValue(infoText, translated, null);
            }
            catch { }
        }

        private static int PatchCotiDpadLabels(Assembly assembly)
        {
            var type = assembly.GetType("Coti.Client.CotiDpad", false);
            if (type == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1674", () => "Small UI exact hook skipped: type missing Coti.Client.CotiDpad");
                return 0;
            }

            MethodInfo[] pads;
            MethodInfo[] pairs;
            try
            {
                var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
                pads = methods.Where(method =>
                {
                    var parameters = method.GetParameters();
                    return string.Equals(method.Name, "Pad", StringComparison.Ordinal) &&
                        parameters.Length == 8 &&
                        parameters.Take(6).All(p => p.ParameterType == typeof(string)) &&
                        parameters.Skip(6).All(p => p.ParameterType == typeof(float)) &&
                        string.Equals(method.ReturnType.FullName, "UnityEngine.Vector2", StringComparison.Ordinal);
                }).ToArray();
                pairs = methods.Where(method =>
                {
                    var parameters = method.GetParameters();
                    return string.Equals(method.Name, "Pair", StringComparison.Ordinal) &&
                        parameters.Length == 7 &&
                        parameters.Take(4).All(p => p.ParameterType == typeof(string)) &&
                        parameters.Skip(4).All(p => p.ParameterType == typeof(float)) &&
                        method.ReturnType == typeof(float);
                }).ToArray();
            }
            catch { return 0; }

            if (pads.Length != 1 || pairs.Length != 1)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1706", () => "Small UI COTI D-pad hook skipped: exact Pad/Pair target missing or ambiguous");
                return 0;
            }

            var patched = 0;
            patched += PatchExactPrefix(assembly, type, pads[0], nameof(BeforeCotiDpadPad));
            patched += PatchExactPrefix(assembly, type, pairs[0], nameof(BeforeCotiDpadPair));
            return patched;
        }

        private static int PatchExactPrefix(Assembly assembly, Type type, MethodInfo method, string prefixName)
        {
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                    prefixName, BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1736", () => "Small UI exact patch skipped: " + assembly.GetName().Name + " :: " +
                    type.FullName + "." + method.Name + " - " + ex.Message);
                return 0;
            }
        }

        private static void BeforeCotiDpadPad(ref string __1, ref string __2, ref string __3,
            ref string __4, ref string __5)
        {
            if (profiles == null) return;
            __1 = profiles.Translate(CotiProfileId, CotiDpadChannel, __1);
            __2 = profiles.Translate(CotiProfileId, CotiDpadChannel, __2);
            __3 = profiles.Translate(CotiProfileId, CotiDpadChannel, __3);
            __4 = profiles.Translate(CotiProfileId, CotiDpadChannel, __4);
            __5 = profiles.Translate(CotiProfileId, CotiDpadChannel, __5);
        }

        private static void BeforeCotiDpadPair(ref string __1, ref string __2, ref string __3)
        {
            if (profiles == null) return;
            __1 = profiles.Translate(CotiProfileId, CotiDpadChannel, __1);
            __2 = profiles.Translate(CotiProfileId, CotiDpadChannel, __2);
            __3 = profiles.Translate(CotiProfileId, CotiDpadChannel, __3);
        }

        private static int PatchUnloadAllMagazinesTooltip(Assembly assembly)
        {
            var type = assembly.GetType("UnloadAllMagazines.Patches.UnloadAllMagazinesButtonPatch", false);
            if (type == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1766", () => "Small UI exact hook skipped: type missing UnloadAllMagazines.Patches.UnloadAllMagazinesButtonPatch");
                return 0;
            }

            MethodInfo[] candidates;
            try
            {
                candidates = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => string.Equals(m.Name, "DescribeAction", StringComparison.Ordinal) &&
                                m.ReturnType == typeof(string) &&
                                m.GetParameters().Length == 0 &&
                                ContainsTranslatableLiteral(m, UnloadAllMagazinesProfileId))
                    .ToArray();
            }
            catch { return 0; }

            if (candidates.Length != 1)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1784", () => "Small UI Unload All Magazines tooltip hook skipped: expected one exact DescribeAction target, found " + candidates.Length);
                return 0;
            }

            var method = candidates[0];
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                    nameof(AfterUnloadAllMagazinesDescribeAction), BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1808", () => "Small UI exact patch skipped: " + assembly.GetName().Name + " :: " +
                    type.FullName + "." + method.Name + " - " + ex.Message);
                return 0;
            }
        }

        private static void AfterUnloadAllMagazinesDescribeAction(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || profiles == null) return;
            __result = profiles.Translate(UnloadAllMagazinesProfileId, Channel, __result);
        }

        private static int PatchTraumaCoreDeathScreenText(Assembly assembly)
        {
            var patched = 0;

            var presenterType = assembly.GetType(
                "TraumaCore.Features.DeathScreen.HitMarkers.DeathScreenHitMarkerPresenter", false);
            if (presenterType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1828", () => "Small UI TraumaCore death-screen hook skipped: presenter type missing");
            }
            else
            {
                MethodInfo[] woundDescriptions;
                MethodInfo[] markerVisuals;
                try
                {
                    var methods = presenterType.GetMethods(
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    woundDescriptions = methods.Where(method =>
                        string.Equals(method.Name, "BuildWoundDescription", StringComparison.Ordinal) &&
                        method.ReturnType == typeof(string) &&
                        method.GetParameters().Length == 4).ToArray();
                    markerVisuals = methods.Where(method =>
                        string.Equals(method.Name, "CreateMarkerVisuals", StringComparison.Ordinal) &&
                        method.ReturnType == typeof(void) &&
                        method.GetParameters().Length == 2).ToArray();
                }
                catch
                {
                    woundDescriptions = Array.Empty<MethodInfo>();
                    markerVisuals = Array.Empty<MethodInfo>();
                }

                if (woundDescriptions.Length == 1)
                    patched += PatchExactPostfix(assembly, presenterType, woundDescriptions[0],
                        nameof(AfterTraumaCoreStringResult));
                else
                    SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1857", () => "Small UI TraumaCore wound-description hook skipped: expected one exact target, found " + woundDescriptions.Length);

                if (markerVisuals.Length == 1)
                    patched += PatchExactPostfix(assembly, presenterType, markerVisuals[0],
                        nameof(AfterTraumaCoreCreateMarkerVisuals));
                else
                    SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1863", () => "Small UI TraumaCore marker-label hook skipped: expected one exact target, found " + markerVisuals.Length);
            }

            var tooltipType = assembly.GetType(
                "TraumaCore.Features.DeathScreen.Tooltips.BleedDamageTooltipBuilder", false);
            if (tooltipType == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1870", () => "Small UI TraumaCore bleed-tooltip hook skipped: tooltip type missing");
            }
            else
            {
                MethodInfo[] tooltipMethods;
                try
                {
                    tooltipMethods = tooltipType.GetMethods(
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(method =>
                        {
                            var parameters = method.GetParameters();
                            return string.Equals(method.Name, "TryBuildTooltip", StringComparison.Ordinal) &&
                                method.ReturnType == typeof(bool) &&
                                parameters.Length == 2 &&
                                parameters[1].ParameterType == typeof(string).MakeByRefType();
                        }).ToArray();
                }
                catch { tooltipMethods = Array.Empty<MethodInfo>(); }

                if (tooltipMethods.Length == 1)
                    patched += PatchExactPostfix(assembly, tooltipType, tooltipMethods[0],
                        nameof(AfterTraumaCoreBleedTooltip));
                else
                    SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1894", () => "Small UI TraumaCore bleed-tooltip hook skipped: expected one exact target, found " + tooltipMethods.Length);
            }

            return patched;
        }

        private static int PatchExactPostfix(
            Assembly assembly, Type type, MethodInfo method, string postfixName)
        {
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                    postfixName, BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1921", () => "Small UI exact patch skipped: " + assembly.GetName().Name + " :: " +
                    type.FullName + "." + method.Name + " - " + ex.Message);
                return 0;
            }
        }

        private static void AfterTraumaCoreStringResult(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || profiles == null) return;
            __result = profiles.Translate(TraumaCoreProfileId, Channel, __result);
        }

        private static void AfterTraumaCoreBleedTooltip(bool __result, ref string __1)
        {
            if (!__result || string.IsNullOrEmpty(__1) || profiles == null) return;
            __1 = profiles.Translate(TraumaCoreProfileId, Channel, __1);
        }

        private static void AfterTraumaCoreCreateMarkerVisuals(object __1)
        {
            if (__1 == null || profiles == null) return;

            try
            {
                var markerType = __1.GetType();
                object textTarget = markerType.GetField("Text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__1);
                if (textTarget == null)
                    textTarget = markerType.GetProperty("Text",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__1, null);
                if (textTarget == null) return;

                var textProperty = textTarget.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (textProperty == null || textProperty.PropertyType != typeof(string) ||
                    !textProperty.CanRead || !textProperty.CanWrite) return;

                var source = textProperty.GetValue(textTarget, null) as string;
                if (string.IsNullOrEmpty(source) ||
                    !profiles.CanTranslate(TraumaCoreProfileId, Channel, source)) return;

                if (!ApplyTextPropertyLabel(
                    textTarget, TraumaCoreProfileId, source, profiles)) return;
                TrackTextPropertyLabel(textTarget, TraumaCoreProfileId, source);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1968", () => "Small UI TraumaCore marker label kept unchanged: " + ex.Message);
            }
        }

        private static int PatchTraumaCoreWoundLabels(Assembly assembly)
        {
            var type = assembly.GetType("TraumaCore.Features.WoundInspection.WoundInspectionView", false);
            if (type == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:1977", () => "Small UI exact hook skipped: type missing TraumaCore.Features.WoundInspection.WoundInspectionView");
                return 0;
            }

            MethodInfo[] candidates;
            try
            {
                candidates = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method =>
                    {
                        var parameters = method.GetParameters();
                        return string.Equals(method.Name, "CreateLabel", StringComparison.Ordinal) &&
                            string.Equals(method.ReturnType.FullName, "TMPro.TMP_Text", StringComparison.Ordinal) &&
                            parameters.Length == 3 &&
                            string.Equals(parameters[0].ParameterType.FullName, "TMPro.TMP_Text", StringComparison.Ordinal) &&
                            string.Equals(parameters[1].ParameterType.FullName, "UnityEngine.Transform", StringComparison.Ordinal) &&
                            parameters[2].ParameterType == typeof(string);
                    }).ToArray();
            }
            catch { return 0; }

            if (candidates.Length != 1)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:2000", () => "Small UI TraumaCore wound-label hook skipped: expected one exact CreateLabel target, found " + candidates.Length);
                return 0;
            }

            var method = candidates[0];
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                    nameof(AfterTraumaCoreCreateLabel), BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:2024", () => "Small UI exact patch skipped: " + assembly.GetName().Name + " :: " +
                    type.FullName + "." + method.Name + " - " + ex.Message);
                return 0;
            }
        }

        private static void AfterTraumaCoreCreateLabel(object __result, string __2)
        {
            if (__result == null || string.IsNullOrEmpty(__2) || profiles == null ||
                !profiles.CanTranslate(TraumaCoreProfileId, Channel, __2)) return;

            if (!ApplyTextPropertyLabel(__result, TraumaCoreProfileId, __2, profiles)) return;

            TrackTextPropertyLabel(__result, TraumaCoreProfileId, __2);
        }

        private static void TrackTextPropertyLabel(
            object target, string profileId, string source)
        {
            if (target == null || string.IsNullOrEmpty(source)) return;

            lock (Gate)
            {
                if (!enabled) return;
                for (var i = TextPropertyLabels.Count - 1; i >= 0; i--)
                {
                    var existing = TextPropertyLabels[i].Target.Target;
                    if (existing == null)
                    {
                        TextPropertyLabels.RemoveAt(i);
                        continue;
                    }
                    if (ReferenceEquals(existing, target)) return;
                }
                TextPropertyLabels.Add(new TrackedTextPropertyLabel(
                    target, profileId, source));
            }
        }

        private static bool ApplyTextPropertyLabel(
            object target, string profileId, string source, EditableProfiles data)
        {
            try
            {
                var textProperty = target.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (textProperty == null || textProperty.PropertyType != typeof(string) ||
                    !textProperty.CanWrite) return false;

                var text = data.Translate(profileId, Channel, source, LocaleMode.CurrentCulture());
                textProperty.SetValue(target, text, null);
                return true;
            }
            catch { return false; }
        }

        private static int PatchHideoutUiRevamp(Assembly assembly)
        {
            var type = assembly.GetType("tarkin.hideoutuirevamp.UIFactory", false);
            if (type == null)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:2085", () => "Small UI exact hook skipped: type missing tarkin.hideoutuirevamp.UIFactory");
                return 0;
            }

            MethodInfo[] candidates;
            try
            {
                candidates = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => string.Equals(m.Name, "CreateTab", StringComparison.Ordinal) &&
                                string.Equals(m.ReturnType.FullName, "EFT.UI.Tab", StringComparison.Ordinal) &&
                                m.GetParameters().Length == 4 &&
                                string.Equals(m.GetParameters()[0].ParameterType.FullName, "UnityEngine.GameObject", StringComparison.Ordinal) &&
                                string.Equals(m.GetParameters()[1].ParameterType.FullName, "EFT.UI.Tab", StringComparison.Ordinal) &&
                                string.Equals(m.GetParameters()[2].ParameterType.FullName,
                                    "tarkin.hideoutuirevamp.Category", StringComparison.Ordinal))
                    .ToArray();
            }
            catch { return 0; }

            if (candidates.Length != 1)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:2106", () => "Small UI Hideout UI Revamp hook skipped: expected one exact CreateTab target, found " + candidates.Length);
                return 0;
            }

            var method = candidates[0];
            lock (Gate)
            {
                if (!enabled || harmony == null || !PatchedMethods.Add(method)) return 0;
            }

            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(SmallUiLiteralHooks).GetMethod(
                    nameof(AfterHideoutUiRevampCreateTab), BindingFlags.Static | BindingFlags.NonPublic))
                    { priority = Priority.Last });
                return 1;
            }
            catch (Exception ex)
            {
                lock (Gate) PatchedMethods.Remove(method);
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:2130", () => "Small UI exact patch skipped: " + assembly.GetName().Name + " :: " +
                    type.FullName + "." + method.Name + " - " + ex.Message);
                return 0;
            }
        }

        private static void AfterHideoutUiRevampCreateTab(object __result, object __2)
        {
            if (__result == null || __2 == null || profiles == null) return;

            try
            {
                var source = __2.ToString().ToUpperInvariant();
                if (!profiles.CanTranslate(HideoutUiRevampProfileId, Channel, source)) return;

                var localizedText = FindLocalizedTextOnTab(__result);
                if (localizedText == null || !ApplyGeneratedLabel(localizedText, source, profiles)) return;

                lock (Gate)
                {
                    if (!enabled) return;
                    for (var i = GeneratedLabels.Count - 1; i >= 0; i--)
                    {
                        var existing = GeneratedLabels[i].Target.Target;
                        if (existing == null)
                        {
                            GeneratedLabels.RemoveAt(i);
                            continue;
                        }
                        if (ReferenceEquals(existing, localizedText)) return;
                    }
                    GeneratedLabels.Add(new TrackedGeneratedLabel(localizedText, source));
                }
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("SmallUiLiteralHooks:2166", () => "Small UI Hideout UI Revamp label kept unchanged: " + ex.Message);
            }
        }

        private static object FindLocalizedTextOnTab(object tab)
        {
            var gameObjectProperty = tab.GetType().GetProperty("gameObject",
                BindingFlags.Instance | BindingFlags.Public);
            var gameObject = gameObjectProperty?.GetValue(tab, null);
            if (gameObject == null) return null;

            var localizedTextType = tab.GetType().Assembly.GetType("EFT.UI.LocalizedText", false);
            if (localizedTextType == null) return null;

            var getComponent = gameObject.GetType().GetMethod("GetComponent",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Type) }, null);
            return getComponent?.Invoke(gameObject, new object[] { localizedTextType });
        }

        private static bool ApplyGeneratedLabel(object target, string source, EditableProfiles data)
        {
            try
            {
                var setLabelText = target.GetType().GetMethod("SetLabelText",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string) }, null);
                if (setLabelText == null) return false;

                var culture = LocaleMode.CurrentCulture();
                var text = data.Translate(HideoutUiRevampProfileId, Channel, source, culture);
                setLabelText.Invoke(target, new object[] { text });
                return true;
            }
            catch { return false; }
        }

        private static IEnumerable<CodeInstruction> TranslateSmallUiLiterals(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var assemblyName = __originalMethod?.DeclaringType?.Assembly?.GetName().Name;
            var target = Targets.FirstOrDefault(t => string.Equals(t.AssemblyName, assemblyName,
                StringComparison.OrdinalIgnoreCase) && profiles?.HasRules(t.ProfileId, Channel) == true &&
                IsInTargetScope(__originalMethod, t));

            if (target == null || profiles == null)
            {
                foreach (var i in instructions) yield return i;
                yield break;
            }

            foreach (var instruction in instructions)
            {
                var shouldTranslate = instruction.opcode == OpCodes.Ldstr &&
                                      instruction.operand is string source &&
                                      profiles.CanTranslate(target.ProfileId, Channel, source);
                yield return instruction;
                if (shouldTranslate)
                {
                    yield return new CodeInstruction(OpCodes.Ldstr, target.ProfileId);
                    yield return new CodeInstruction(OpCodes.Call, RuntimeTranslator);
                }
            }
        }

        private static bool IsInTargetScope(MethodBase method, Target target)
        {
            var type = method?.DeclaringType;
            if (type == null) return false;
            if (string.Equals(type.FullName, target.TypeName, StringComparison.Ordinal)) return true;
            if (!target.IncludeNestedTypes) return false;
            var parent = type.DeclaringType;
            while (parent != null)
            {
                if (string.Equals(parent.FullName, target.TypeName, StringComparison.Ordinal)) return true;
                parent = parent.DeclaringType;
            }
            return false;
        }

        private static string TranslateRuntime(string source, string profileId)
        {
            if (profiles == null || !LocaleMode.IsKoreanMode()) return source;
            return profiles.Translate(profileId, Channel, source);
        }

        private static bool ContainsTranslatableLiteral(MethodBase method, string profileId)
        {
            MethodBody body;
            try { body = method?.GetMethodBody(); }
            catch { return false; }
            var il = body?.GetILAsByteArray();
            if (il == null) return false;

            // Only resolve real ldstr instructions. The decoder advances by each opcode's defined
            // operand width, so arbitrary bytes inside other operands are never treated as string tokens.
            var offset = 0;
            while (offset < il.Length)
            {
                OpCode op;
                var first = il[offset++];
                if (first == 0xfe)
                {
                    if (offset >= il.Length) return false;
                    op = TwoByteOpCodes[il[offset++]];
                }
                else op = OneByteOpCodes[first];

                var operandStart = offset;
                var size = GetOperandSize(op.OperandType, il, operandStart);
                if (size < 0 || operandStart + size > il.Length) return false;
                if (op.OperandType == OperandType.InlineString && size == 4)
                {
                    try
                    {
                        var source = method.Module.ResolveString(BitConverter.ToInt32(il, operandStart));
                        if (profiles?.CanTranslate(profileId, Channel, source) == true) return true;
                    }
                    catch { }
                }
                offset += size;
            }
            return false;
        }

        private static readonly OpCode[] OneByteOpCodes = BuildOneByteOpcodes();
        private static readonly OpCode[] TwoByteOpCodes = BuildTwoByteOpcodes();

        private static OpCode[] BuildOneByteOpcodes()
        {
            var result = new OpCode[0x100];
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode)) continue;
                var op = (OpCode)field.GetValue(null);
                var value = unchecked((ushort)op.Value);
                if (value < 0x100) result[value] = op;
            }
            return result;
        }

        private static OpCode[] BuildTwoByteOpcodes()
        {
            var result = new OpCode[0x100];
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode)) continue;
                var op = (OpCode)field.GetValue(null);
                var value = unchecked((ushort)op.Value);
                if ((value & 0xff00) == 0xfe00) result[value & 0xff] = op;
            }
            return result;
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
    }
}
