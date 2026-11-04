using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Services.Commerce;

namespace SPT_Mod_Korean_Server;

/// <summary>
/// PitFireTeam 1.1.0 mail entry points not covered by the vanilla direct-NPC
/// mail or USER/SYSTEM mail patches. Only the message body is translated;
/// attachment items, sender identity, delivery state and timers are untouched.
/// </summary>
internal static class PitFireTeamMailHooks
{
    internal const string CourierTraderId = "67d3a28a3d6f4f7dbd09ed13";
    internal const string FenceTraderId = "579dc571d53a0658a154fbec";
    private const string LanguageAssembly = "pitFireTeam.Server";
    private const string LanguageType = "pitTeam.Server.Services.FriendlyLanguageService";
    private const string RemovalKey = "RemovedTeammateEquipmentDelivery";
    private static readonly Harmony MailHarmony = new("spt.korean.modtranslation.pitfireteam-mail");
    private static MessageTranslator? translator;

    internal static void Enable(MessageTranslator? serverTranslator, ISptLogger<Mod> logger)
    {
        if (serverTranslator is null) return;
        translator = serverTranslator;

        // Courier/Scav cargo/previous-kit mail use SendMessageToPlayer(details),
        // *not* the three MailSendService methods handled by Mod.cs.
        try
        {
            var method = typeof(MailSendService).GetMethod(
                nameof(MailSendService.SendMessageToPlayer),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                [typeof(SendMessageDetails)], null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException("MailSendService.SendMessageToPlayer(SendMessageDetails)");
            MailHarmony.Patch(method, prefix: new HarmonyMethod(
                typeof(PitFireTeamMailHooks).GetMethod(nameof(BeforeCourierDelivery), BindingFlags.Static | BindingFlags.NonPublic)!));
        }
        catch (Exception ex)
        {
            logger.Warning("PitFireTeam courier mail translation unavailable: " + ex.Message);
        }

        // Removed-teammate equipment messages are created as Message objects
        // directly and persisted in an outbox; they never call MailSendService.
        // Translate the single socialUi value returned to that creator instead.
        try
        {
            var serverAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, LanguageAssembly, StringComparison.Ordinal));
            var languageType = serverAssembly?.GetType(LanguageType, false);
            var method = languageType?.GetMethod("GetStringMap", BindingFlags.Instance | BindingFlags.Public,
                null, [typeof(MongoId), typeof(string)], null);
            if (method == null || method.ReturnType != typeof(Dictionary<string, string>))
                throw new MissingMethodException("pitTeam FriendlyLanguageService.GetStringMap(MongoId, string)");
            MailHarmony.Patch(method, postfix: new HarmonyMethod(
                typeof(PitFireTeamMailHooks).GetMethod(nameof(AfterSocialUiMap), BindingFlags.Static | BindingFlags.NonPublic)!));
        }
        catch (Exception ex)
        {
            logger.Warning("PitFireTeam removed-teammate mail translation unavailable: " + ex.Message);
        }

        // Team escape conversations are written by NotificationSendHelper,
        // bypassing the regular MailSendService USER/SYSTEM entry points.
        try
        {
            var method = typeof(NotificationSendHelper).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(m => m.Name == "SendMessageToPlayerAsync"
                    && m.GetParameters().Length == 4
                    && m.GetParameters()[0].ParameterType == typeof(MongoId)
                    && m.GetParameters()[2].ParameterType == typeof(string)
                    && m.GetParameters()[3].ParameterType == typeof(MessageType));
            if (method == null)
                throw new MissingMethodException("NotificationSendHelper.SendMessageToPlayerAsync(MongoId, sender, string, MessageType)");
            MailHarmony.Patch(method, prefix: new HarmonyMethod(
                typeof(PitFireTeamMailHooks).GetMethod(nameof(BeforeSquadNotification), BindingFlags.Static | BindingFlags.NonPublic)!));
        }
        catch (Exception ex)
        {
            logger.Warning("PitFireTeam squad-message translation unavailable: " + ex.Message);
        }
    }

    private static void BeforeCourierDelivery(SendMessageDetails __0)
    {
        try
        {
            if (__0 == null || translator is null || string.IsNullOrEmpty(__0.MessageText)) return;
            if (!string.Equals(__0.Trader, CourierTraderId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(__0.Trader, FenceTraderId, StringComparison.OrdinalIgnoreCase)) return;
            __0.MessageText = translator.Translate(__0.Trader, __0.MessageText, CultureRegistry.Get(__0.RecipientId));
        }
        catch (Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("pit-mail-courier", () => "PitFireTeam delivery text kept unchanged: " + ex.Message);
        }
    }

    private static void AfterSocialUiMap(MongoId __0, string __1, ref Dictionary<string, string> __result)
    {
        try
        {
            if (translator is null || !string.Equals(__1, "socialUi", StringComparison.Ordinal) ||
                __result == null || !__result.TryGetValue(RemovalKey, out var english)) return;
            var translated = translator.Translate(CourierTraderId, english, CultureRegistry.Get(__0));
            if (!string.Equals(translated, english, StringComparison.Ordinal))
            {
                // Keep every other social UI value unchanged.
                __result = new Dictionary<string, string>(__result, __result.Comparer);
                __result[RemovalKey] = translated;
            }
        }
        catch (Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("pit-mail-removal", () => "PitFireTeam removal text kept unchanged: " + ex.Message);
        }
    }

    private static void BeforeSquadNotification(MongoId __0, ref string __2, MessageType __3)
    {
        try
        {
            if (translator is null || __3 != MessageType.UserMessage || string.IsNullOrEmpty(__2)) return;
            __2 = translator.TranslatePitSquadMessage(__2, CultureRegistry.Get(__0));
        }
        catch (Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("pit-mail-squad", () => "PitFireTeam squad message kept unchanged: " + ex.Message);
        }
    }
}
