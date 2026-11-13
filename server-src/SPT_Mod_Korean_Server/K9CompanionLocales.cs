using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

// K9 Companion 1.3.2 registers Dr. Yang and quest locale transformers at
// Preload+3/+4, and cloned items/gear at Preload+1/+2. Its K9Lang.Locales()
// has no Korean file in the supplied source, so generated item fields can be
// absent from the Korean locale. Patch only the verified K9 keys, after its
// last server registration (K9Shop, Preload+5). Never modify the original mod,
// an existing non-English localization, quest progress, trader stock or items.
[Injectable(TypePriority = OnLoadOrder.Preload + 6)]
public sealed class K9CompanionLocales(
    ISptLogger<K9CompanionLocales> logger,
    LocaleTable localeTable,
    TemplateTable templates,
    IReadOnlyList<SptMod> installedMods) : IOnLoad
{
    private const string ModId = "jrs.k9companion";
    private const string SourceVersion = "1.3.2";
    private const string ProfileName = "K9Companion.json";
    private const string ModName = "K9Companion";
    private sealed record Rule(string Source, string Translation, string DisplayType, bool SeedMissing);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Do not take over a different K9 version; it might supply different
            // English text, new locale keys or a native Korean translation.
            if (!installedMods.Any(mod =>
                string.Equals(mod.ModMetadata.ModGuid, ModId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(mod.ModMetadata.Version.ToString(), SourceVersion, StringComparison.Ordinal)))
                return Task.CompletedTask;

            var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Server addon assembly folder unavailable");
            var path = Path.Combine(Path.GetFullPath(Path.Combine(folder, "../../../..")),
                "BepInEx", "plugins", "SPT_Mod_Korean_Addon", "translations", ProfileName);
            if (!File.Exists(path)) return Task.CompletedTask;
            var rules = ReadRules(path);
            if (rules.Count == 0) return Task.CompletedTask;

            var nameSources = rules
                .Where(x => x.Key.EndsWith(" Name", StringComparison.Ordinal))
                .ToDictionary(x => x.Key[..^5], x => x.Value.Source, StringComparer.Ordinal);

            var count = 0;
            foreach (var (culture, language) in localeTable.Global)
            {
                if (!IsKorean(culture)) continue;
                var languageCode = culture;
                var first = 0;
                language.AddTransformer(data =>
                {
                    if (data is null) return data;
                    var changed = 0;
                    var protectedValues = 0;
                    foreach (var (key, rule) in rules)
                    {
                        var hasValue = data.TryGetValue(key, out var current);
                        if (hasValue && !string.Equals(current, rule.Source, StringComparison.Ordinal))
                        {
                            // A bare key can result from K9's absent kr item
                            // locale; it is not permission to overwrite others.
                            if (current != key || !CanSeedItem(key, rule, templates))
                            {
                                protectedValues++;
                                continue;
                            }
                        }
                        else if (!hasValue && !CanSeedItem(key, rule, templates))
                        {
                            // Only item fields with proven upstream K9 item IDs
                            // can be seeded. Quest/trader/slot text must actually
                            // have been registered by the original K9 transformer.
                            continue;
                        }
                        var output = Format(key, rule, languageCode, nameSources);
                        if (hasValue && string.Equals(current, output, StringComparison.Ordinal)) continue;
                        data[key] = output;
                        changed++;
                    }
                    if (Interlocked.Exchange(ref first, 1) == 0)
                        logger.Info($"[K9 Korean] {languageCode}: {changed} source-verified locale fields localized; {protectedValues} other values preserved (game display unverified).");
                    return data;
                });
                count++;
            }
            logger.Info($"[K9 Korean] {rules.Count} source-verified server locale rules; {count} Korean locale transformers registered.");
        }
        catch (Exception ex)
        {
            logger.Warning("[K9 Korean] Optional 1.3.2 locale integration skipped; other translations unaffected: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    private static bool IsKorean(string? value) =>
        string.Equals(value, "kr", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "kr-en", StringComparison.OrdinalIgnoreCase);

    private static bool CanSeedItem(string key, Rule rule, TemplateTable templates)
    {
        if (!rule.SeedMissing) return false;
        var split = key.LastIndexOf(' ');
        if (split != 24) return false; // the 24-character K9 item ID
        var suffix = key[(split + 1)..];
        if (suffix is not ("Name" or "ShortName" or "Description")) return false;
        var id = key[..split];
        if (!id.StartsWith("6a5d0c0c4fb9a3c5e1d", StringComparison.Ordinal)) return false;
        return templates.Items.ContainsKey(new MongoId(id));
    }

    // Mirror the addon's existing LocaleDisplayRules.Select behavior, using
    // only the existing profile as the Korean text source. Never put generated
    // bilingual suffixes or credits into the JSON translation field.
    private static string Format(string key, Rule rule, string culture,
        IReadOnlyDictionary<string, string> names)
    {
        var korean = rule.Translation;
        if (rule.DisplayType == "item_description")
        {
            var id = key[..^12]; // remove " Description"
            if (names.TryGetValue(id, out var name)) korean = "[" + name + "]\n" + korean;
            return korean.TrimEnd() + "\n(" + ModName + ")";
        }
        if (rule.DisplayType == "quest_description")
            return korean.TrimEnd() + "\n(" + ModName + ")";
        if (rule.DisplayType is "quest_title" or "quest_objective" ||
            (rule.DisplayType == "item_name" && culture.Equals("kr-en", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.Equals(rule.Source.Trim(), korean.Trim(), StringComparison.Ordinal)) return korean;
            return korean.TrimEnd() + (rule.DisplayType == "quest_objective" ? "\n(" :
                rule.DisplayType == "quest_title" ? " (" : "\n(") + rule.Source.Trim() + ")";
        }
        return korean;
    }

    private static Dictionary<string, Rule> ReadRules(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 ||
            root.GetProperty("target_spt").GetString() != "4.1.6" ||
            root.GetProperty("mod_id").GetString() != ModId ||
            root.GetProperty("mod_name").GetString() != ModName ||
            root.GetProperty("mod_version").GetString() != SourceVersion)
            throw new InvalidDataException("Unexpected K9 locale profile identity or version");
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        var mode = "exact";
        foreach (var row in root.GetProperty("channels").GetProperty("locale").EnumerateArray())
        {
            if (row.TryGetProperty("match_mode", out var modeNode))
                mode = modeNode.GetString() ?? "";
            if (row.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                continue;
            var key = row.GetProperty("key").GetString() ?? "";
            var source = row.GetProperty("source").GetString() ?? "";
            var translation = row.GetProperty("translation").GetString() ?? "";
            var displayType = row.TryGetProperty("display_type", out var display)
                ? display.GetString() ?? "default" : "default";
            var seedMissing = row.TryGetProperty("seed_missing", out var seed) &&
                seed.ValueKind == JsonValueKind.True;
            if (mode != "exact" || string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translation) ||
                displayType is not ("default" or "item_name" or "item_description" or
                    "quest_title" or "quest_objective" or "quest_description") ||
                (seedMissing && !(key.EndsWith(" Name", StringComparison.Ordinal) ||
                    key.EndsWith(" ShortName", StringComparison.Ordinal) ||
                    key.EndsWith(" Description", StringComparison.Ordinal))) ||
                !rules.TryAdd(key, new Rule(source, translation, displayType, seedMissing)))
                throw new InvalidDataException("Invalid K9 locale rule: " + key);
        }
        return rules;
    }
}
