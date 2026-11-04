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
    private readonly ServerTextRules? pitSquadMessageRules;
    private readonly TradersTable tradersTable;
    private readonly Dictionary<string, string> isbWebHtml;
    private readonly Dictionary<string, string> isbWebJs;

    private MessageTranslator(
        Dictionary<string, ServerTextRules> byTrader,
        ServerTextRules? moddedInsuranceRules,
        ServerTextRules? serverMessageRules,
        ServerTextRules? pitSquadMessageRules,
        TradersTable tradersTable,
        Dictionary<string, string> isbWebHtml,
        Dictionary<string, string> isbWebJs)
    {
        this.byTrader = byTrader;
        this.moddedInsuranceRules = moddedInsuranceRules;
        this.serverMessageRules = serverMessageRules;
        this.pitSquadMessageRules = pitSquadMessageRules;
        this.tradersTable = tradersTable;
        this.isbWebHtml = isbWebHtml;
        this.isbWebJs = isbWebJs;
    }

    internal static MessageTranslator FromProfiles(
        string translationsDirectory,
        Action<string, string> warn,
        TradersTable tradersTable)
    {
        var rowsByTrader = new Dictionary<string, List<TextRule>>(StringComparer.OrdinalIgnoreCase);
        var moddedInsuranceRows = new List<TextRule>();
        var serverMessageRows = new List<TextRule>();
        var pitSquadMessageRows = new List<TextRule>();
        var modIds = new HashSet<string>(StringComparer.Ordinal);
        var isbWebHtml = new Dictionary<string, string>(StringComparer.Ordinal);
        var isbWebJs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(translationsDirectory, "*.json"))
        {
            try
            {
                var document = JsonSerializer.Deserialize<ModTexts>(File.ReadAllText(file));
                if (document is null || document.schema_version != 1 || document.target_spt != "4.1.6" ||
                    string.IsNullOrWhiteSpace(document.mod_id) || !modIds.Add(document.mod_id))
                    throw new InvalidDataException("Invalid profile/version");

                // Reuse the existing small_ui channel; only explicit audited web resource
                // fragments for this one mod are permitted here. No API response is touched.
                if (string.Equals(document.mod_id, "com.samc137.isbspecialforces", StringComparison.Ordinal) &&
                    document.channels.TryGetValue("small_ui", out var webRows))
                {
                    foreach (var row in webRows.Where(x => x.enabled && !string.IsNullOrEmpty(x.translation)))
                    {
                        Dictionary<string, string>? destination = row.id?.StartsWith("sf.web.html.", StringComparison.Ordinal) == true
                            ? isbWebHtml : row.id?.StartsWith("sf.web.js.", StringComparison.Ordinal) == true ? isbWebJs : null;
                        if (destination == null) continue;
                        if (string.IsNullOrEmpty(row.source) || row.source == row.translation ||
                            (row.match_mode != null && row.match_mode != "exact") ||
                            !destination.TryAdd(row.source, row.translation!))
                            throw new InvalidDataException("Invalid or duplicate ISB web display-only snippet");
                    }
                }

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
                        // Dynamic USER/SYSTEM text may contain an audited full-string
                        // placeholder. The existing ServerTextRules implementation
                        // verifies placeholder parity and anchors template matches.
                        if (mode is not ("exact" or "segment" or "template"))
                            throw new InvalidDataException("server_messages supports exact/segment/template only");
                        if (!string.IsNullOrWhiteSpace(row.trader_id))
                            throw new InvalidDataException("server_messages must not define trader_id");
                        serverMessageRows.Add(row);
                        if (string.Equals(document.mod_id, "pitfireteam.trader.locale", StringComparison.Ordinal))
                            pitSquadMessageRows.Add(row);
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
        var pitScoped = pitSquadMessageRows.Count > 0 ? new ServerTextRules(pitSquadMessageRows) : null;
        return new MessageTranslator(compiled, scoped, serverScoped, pitScoped, tradersTable, isbWebHtml, isbWebJs);
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

    internal string TranslatePitSquadMessage(string source, string culture)
    {
        if (string.IsNullOrEmpty(source) || pitSquadMessageRules is null) return source;
        if (CultureRegistry.Normalize(culture) == CultureRegistry.Source) return source;
        return pitSquadMessageRules.Translate(source, culture);
    }

    // The caller verifies the complete, exact upstream embedded-resource hash before
    // applying any snippet. Replacements are display-only literal fragments, never IDs,
    // form paths, JSON request/response properties or security/session tokens.
    internal string TranslateIsbWebResource(string source, bool javascript)
    {
        var snippets = javascript ? isbWebJs : isbWebHtml;
        if (snippets.Count == 0) return source;
        foreach (var pair in snippets)
            source = source.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return source;
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
