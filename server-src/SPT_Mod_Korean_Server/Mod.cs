using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.Commerce;

namespace SPT_Mod_Korean_Server;

[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class Mod(ISptLogger<Mod> logger, TradersTable tradersTable) : IOnLoad
{
    private static MessageTranslator? translator;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var harmony = new Harmony("local.spt41.modkorean.directmessages");
        try
        {
            var assemblyRoot = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Server assembly location unavailable");
            // SPT 4.1.6 server mods are loaded from SPT_Runtime/user/mods/<mod-folder>/ with
            // the mod DLL at that folder's top level. Walk exactly four levels back to the SPT
            // root, then consume the same editable translation files used by the client addon.
            var sptRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(assemblyRoot, "../../../.."));
            var translations = System.IO.Path.Combine(
                sptRoot,
                "BepInEx", "plugins", "SPT_Mod_Korean_Addon", "translations");
            SPT.EditableTranslations.MinimalLog.WarningSink = message => logger.Warning(message);
            SPT.EditableTranslations.MinimalLog.Reset();
            translator = MessageTranslator.FromProfiles(translations, (file,message) => SPT.EditableTranslations.MinimalLog.WarnOnce("server-profile:"+file,()=>message), tradersTable);

            var parameterTypes = new Type[]
            {
                typeof(MongoId),
                typeof(string),
                typeof(MessageType),
                typeof(string),
                typeof(List<Item>),
                typeof(long?),
                typeof(SystemData),
                typeof(MessageContentRagfair)
            };
            var target = typeof(MailSendService).GetMethod(
                nameof(MailSendService.SendDirectNpcMessageToPlayer),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: parameterTypes,
                modifiers: null);
            if (target is null || target.ReturnType != typeof(void))
                throw new MissingMethodException("Unexpected SendDirectNpcMessageToPlayer signature");

            var prefix = typeof(Mod).GetMethod(nameof(BeforeMessage), BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(BeforeMessage));
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));


            EnableServerMessageHooks(logger);
            logger.Info("SPT Mod Korean Server 1.0.0 loaded" + (SPT.EditableTranslations.MinimalLog.HasWarnings ? " (with warnings)." : "."));
        }
        catch (Exception ex)
        {
            translator = null;
            harmony.UnpatchSelf();
            logger.Error("Direct-message addon disabled: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    private static void EnableServerMessageHooks(ISptLogger<Mod> logger)
    {
        var serverMessageHarmony = new Harmony("local.spt41.modkorean.servermessages");
        try
        {
            var userTarget = typeof(MailSendService).GetMethod(
                nameof(MailSendService.SendUserMessageToPlayer),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new Type[]
                {
                    typeof(MongoId),
                    typeof(UserDialogInfo),
                    typeof(string),
                    typeof(List<Item>),
                    typeof(long?)
                },
                modifiers: null);
            if (userTarget is null || userTarget.ReturnType != typeof(void))
                throw new MissingMethodException("Unexpected SendUserMessageToPlayer signature");

            var systemTarget = typeof(MailSendService).GetMethod(
                nameof(MailSendService.SendSystemMessageToPlayer),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new Type[]
                {
                    typeof(MongoId),
                    typeof(string),
                    typeof(List<Item>),
                    typeof(long?),
                    typeof(List<ProfileChangeEvent>)
                },
                modifiers: null);
            if (systemTarget is null || systemTarget.ReturnType != typeof(void))
                throw new MissingMethodException("Unexpected SendSystemMessageToPlayer signature");

            var userPrefix = typeof(Mod).GetMethod(nameof(BeforeUserMessage), BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(BeforeUserMessage));
            var systemPrefix = typeof(Mod).GetMethod(nameof(BeforeSystemMessage), BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(BeforeSystemMessage));

            serverMessageHarmony.Patch(userTarget, prefix: new HarmonyMethod(userPrefix));
            serverMessageHarmony.Patch(systemTarget, prefix: new HarmonyMethod(systemPrefix));

        }
        catch (Exception ex)
        {
            serverMessageHarmony.UnpatchSelf();
            SPT.EditableTranslations.MinimalLog.WarnOnce("server-message-hooks",()=>"Server-message translation disabled; npc_messages remains active: " + ex.Message);
        }
    }

    private static void BeforeUserMessage(MongoId __0, ref string __2)
    {
        TranslateServerMessage(__0, ref __2);
    }

    private static void BeforeSystemMessage(MongoId __0, ref string __1)
    {
        TranslateServerMessage(__0, ref __1);
    }

    private static void TranslateServerMessage(MongoId sessionId, ref string message)
    {
        try
        {
            if (translator is null || message is null) return;
            message = translator.TranslateServerMessage(message, CultureRegistry.Get(sessionId));
        }
        catch (Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("server-message-runtime",()=>"Server message kept unchanged: " + ex.Message);
        }
    }

    private static void BeforeMessage(MongoId __0, string? __1, ref string __3)
    {
        try
        {
            if (translator is null || __3 is null) return;
            __3 = translator.Translate(__1, __3, CultureRegistry.Get(__0));
        }
        catch (Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("npc-message-runtime",()=>"Direct message kept unchanged: " + ex.Message);
        }
    }
}
