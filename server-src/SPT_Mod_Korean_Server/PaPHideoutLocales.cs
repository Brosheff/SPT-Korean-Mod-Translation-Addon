using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

// Source-limited locale bridge for Pack-a-Punch 2.2.0. Its quest/area transformers
// write English to every language; this registration occurs after both originals.
// Never change a quest template, profile, trader reward or non-Korean locale.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public sealed class PaPHideoutLocales(
    ISptLogger<PaPHideoutLocales> logger,
    LocaleTable localeTable,
    TemplateTable templateTable) : IOnLoad
{
    private const string ModId = "com.kennerz.paphideoutmodule";
    private const string ModName = "Pack-a-Punch";
    private static readonly MongoId QuestId = new("6650a115a115a115a115b115");
    private sealed record Rule(string Source, string Translation, string DisplayType, bool SeedMissing);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!templateTable.Quests.ContainsKey(QuestId)) return Task.CompletedTask;
            var assemblyRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Server assembly location unavailable");
            var sptRoot = Path.GetFullPath(Path.Combine(assemblyRoot, "../../../.."));
            var path = Path.Combine(sptRoot, "BepInEx", "plugins", "SPT_Mod_Korean_Addon", "translations", "Pack-a-Punch.json");
            if (!File.Exists(path)) return Task.CompletedTask;
            var rules = ReadRules(path);
            if (rules.Count == 0) return Task.CompletedTask;
            foreach (var (culture, locales) in localeTable.Global)
            {
                if (culture != "kr" && culture != "kr-en") continue;
                var reported = 0;
                locales.AddTransformer(values =>
                {
                    if (values == null) return values;
                    var count = 0;
                    foreach (var (key, rule) in rules)
                    {
                        if (values.TryGetValue(key, out var value))
                        {
                            // An existing Korean translation takes precedence; changed
                            // custom English sources are not silently translated.
                            if (!string.Equals(value, rule.Source, StringComparison.Ordinal)) continue;
                        }
                        else if (!rule.SeedMissing) continue;
                        values[key] = Format(key, rule, culture, rules);
                        count++;
                    }
                    if (Interlocked.Exchange(ref reported, 1) == 0)
                        logger.Info($"[Pack-a-Punch Korean] {culture}: {count} exact-source locale values applied.");
                    return values;
                });
            }
            logger.Info($"[Pack-a-Punch Korean] {rules.Count} locale keys available for verified source replacement.");
        }
        catch (Exception ex)
        {
            logger.Warning("[Pack-a-Punch Korean] Optional locale bridge unavailable: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    private static Dictionary<string, Rule> ReadRules(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 ||
            root.GetProperty("target_spt").GetString() != "4.1.6" ||
            root.GetProperty("mod_id").GetString() != ModId ||
            root.GetProperty("mod_version").GetString() != "2.2.0")
            throw new InvalidDataException("Unexpected Pack-a-Punch translation profile");
        var result = new Dictionary<string, Rule>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("channels").GetProperty("locale").EnumerateArray())
        {
            if (row.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) continue;
            var key = row.GetProperty("key").GetString();
            var source = row.GetProperty("source").GetString();
            var translated = row.GetProperty("translation").GetString();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translated)) continue;
            var display = row.TryGetProperty("display_type", out var type) ? type.GetString() ?? "" : "";
            var seed = row.TryGetProperty("seed_missing", out var missing) && missing.ValueKind == JsonValueKind.True;
            if (!result.TryAdd(key, new Rule(source, translated, display, seed)))
                throw new InvalidDataException("Duplicate Pack-a-Punch locale key: " + key);
        }
        return result;
    }

    private static string Format(string key, Rule rule, string culture, Dictionary<string, Rule> rules)
    {
        switch (rule.DisplayType)
        {
            case "quest_title": return rule.Translation + " (" + rule.Source + ")";
            case "quest_objective": return rule.Translation + "\n(" + rule.Source + ")";
            case "quest_description": return rule.Translation + "\n(" + ModName + ")";
            case "item_name": return culture == "kr-en" ? rule.Translation + "\n(" + rule.Source + ")" : rule.Translation;
            case "item_description":
            {
                var prefix = key.EndsWith(" Description", StringComparison.Ordinal)
                    ? key[..^" Description".Length] + " Name" : "";
                var name = rules.TryGetValue(prefix, out var named) ? "[" + named.Source + "]\n" : "";
                return name + rule.Translation + "\n(" + ModName + ")";
            }
            default: return rule.Translation;
        }
    }
}
