using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

internal sealed class MessageTranslator
{
    internal const string ModdedInsuranceProvidersScope = "@modded_insurance_providers";
    private const string PraporId = "54cb50c76803fa8b248b4571";
    private const string TherapistId = "54cb57776803fa99248b456e";
    private readonly Dictionary<string, ServerTextRules> byTrader;
    private readonly ServerTextRules? moddedInsuranceRules;
    private readonly ServerTextRules? serverMessageRules;
    private readonly TradersTable tradersTable;

    private MessageTranslator(
        Dictionary<string, ServerTextRules> byTrader,
        ServerTextRules? moddedInsuranceRules,
        ServerTextRules? serverMessageRules,
        TradersTable tradersTable)
    {
        this.byTrader = byTrader;
        this.moddedInsuranceRules = moddedInsuranceRules;
        this.serverMessageRules = serverMessageRules;
        this.tradersTable = tradersTable;
    }

    internal static MessageTranslator FromProfiles(
        string translationsDirectory,
        Action<string, string> warn,
        TradersTable tradersTable)
    {
        var rowsByTrader = new Dictionary<string, List<TextRule>>(StringComparer.OrdinalIgnoreCase);
        var moddedInsuranceRows = new List<TextRule>();
        var serverMessageRows = new List<TextRule>();
        var modIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(translationsDirectory, "*.json"))
        {
            try
            {
                var document = JsonSerializer.Deserialize<ModTexts>(File.ReadAllText(file));
                if (document is null || document.schema_version != 1 || document.target_spt != "4.1.6" ||
                    string.IsNullOrWhiteSpace(document.mod_id) || !modIds.Add(document.mod_id))
                    throw new InvalidDataException("Invalid profile/version");

                if (document.channels.TryGetValue("npc_messages", out var rows))
                {
                    foreach (var row in rows.Where(x => x.enabled && !string.IsNullOrEmpty(x.translation)))
                    {
                        if (string.IsNullOrWhiteSpace(row.trader_id))
                            throw new InvalidDataException("Missing trader_id");
                        var traderScope = row.trader_id!;
                        if (string.Equals(traderScope, ModdedInsuranceProvidersScope, StringComparison.Ordinal))
                        {
                            if (!string.Equals(document.mod_id, "RealisticInsurance", StringComparison.Ordinal))
                                throw new InvalidDataException("Modded-insurance scope is reserved for RealisticInsurance");
                            moddedInsuranceRows.Add(row);
                            continue;
                        }
                        if (traderScope.StartsWith("@", StringComparison.Ordinal))
                            throw new InvalidDataException("Unknown trader scope: " + traderScope);
                        if (!rowsByTrader.TryGetValue(row.trader_id, out var list))
                            rowsByTrader[row.trader_id] = list = [];
                        list.Add(row);
                    }
                }

                if (document.channels.TryGetValue("server_messages", out var serverRows))
                {
                    foreach (var row in serverRows.Where(x => x.enabled && !string.IsNullOrEmpty(x.translation)))
                    {
                        var mode = row.match_mode ?? "exact";
                        if (mode is not ("exact" or "segment"))
                            throw new InvalidDataException("server_messages only supports exact/segment");
                        if (!string.IsNullOrWhiteSpace(row.trader_id))
                            throw new InvalidDataException("server_messages must not define trader_id");
                        serverMessageRows.Add(row);
                    }
                }
            }
            catch (Exception ex)
            {
                warn(Path.GetFileName(file), "Profile rejected: " + Path.GetFileName(file) + ": " + ex.Message);
            }
        }

        var compiled = rowsByTrader.ToDictionary(
            pair => pair.Key,
            pair => new ServerTextRules(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var scoped = moddedInsuranceRows.Count > 0 ? new ServerTextRules(moddedInsuranceRows) : null;
        var serverScoped = serverMessageRows.Count > 0 ? new ServerTextRules(serverMessageRows) : null;
        return new MessageTranslator(compiled, scoped, serverScoped, tradersTable);
    }

    internal string Translate(string? traderId, string source, string culture)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(traderId)) return source;
        if (CultureRegistry.Normalize(culture) == CultureRegistry.Source) return source;

        var current = source;
        if (byTrader.TryGetValue(traderId, out var exactRules))
        {
            current = exactRules.Translate(current, culture);
        }

        if (moddedInsuranceRules is not null && IsModdedInsuranceProvider(traderId))
        {
            current = moddedInsuranceRules.Translate(current, culture);
        }

        if (!string.Equals(current, source, StringComparison.Ordinal)) return current;
        return source;
    }


    internal string TranslateServerMessage(string source, string culture)
    {
        if (string.IsNullOrEmpty(source) || serverMessageRules is null) return source;
        if (CultureRegistry.Normalize(culture) == CultureRegistry.Source) return source;
        return serverMessageRules.Translate(source, culture);
    }

    private bool IsModdedInsuranceProvider(string traderId)
    {
        if (string.Equals(traderId, PraporId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(traderId, TherapistId, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var id = new MongoId(traderId);
            return tradersTable.TryGetValue(id, out var trader) && trader?.Base?.Insurance?.Availability == true;
        }
        catch
        {
            return false;
        }
    }

}
