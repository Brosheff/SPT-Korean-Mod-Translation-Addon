using System.Collections;
using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

// Only source-verified Manimal Terminal 0.2.2 keys are eligible. The original
// mod's files, quest logic and other mods' locale values are never rewritten.
// Its WTT quest loader falls back from kr to en; its dialogue loader uses a
// separate TemplateTable.Dialogue.Elements localization dictionary.
[Injectable(TypePriority = OnLoadOrder.Preload + 90001)]
public sealed class ManimalTerminalLocales(
    ISptLogger<ManimalTerminalLocales> logger,
    LocaleTable localeTable,
    TemplateTable templateTable) : IOnLoad
{
    private const string ModId = "com.manimal.terminal";
    private const string ModName = "Manimal Terminal";
    private static readonly HashSet<string> DialogueIds = new(StringComparer.Ordinal)
    {
        "6a8f4d5e90206485fa1d2f00",
        "6a8f4d5e90206485fa1d2f50"
    };

    private sealed record Rule(string Source, string Translation, string DisplayType);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var assemblyRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Server assembly location unavailable");
            var sptRoot = Path.GetFullPath(Path.Combine(assemblyRoot, "../../../.."));
            var profilePath = Path.Combine(sptRoot, "BepInEx", "plugins", "SPT_Mod_Korean_Addon", "translations", "Manimal Terminal.json");
            if (!File.Exists(profilePath)) return Task.CompletedTask;

            var rules = ReadRules(profilePath);
            if (rules.Count == 0) return Task.CompletedTask;

            // Post-quest/pre-runtime registration: the original mod registers its
            // English-only WTT quest transformers at Preload+3 and its map/cabinet
            // transformers at Preload+90000. Add our scoped fallback after both.
            var registered = 0;
            foreach (var pair in localeTable.Global)
            {
                var culture = pair.Key;
                if (!IsKorean(culture)) continue;
                var reported = 0;
                pair.Value.AddTransformer(localeData =>
                {
                    if (localeData == null) return localeData;
                    var applied = 0;
                    foreach (var row in rules)
                    {
                        if (!localeData.TryGetValue(row.Key, out var current) ||
                            !string.Equals(current, row.Value.Source, StringComparison.Ordinal))
                            continue; // preserve other translators and changed originals
                        localeData[row.Key] = Format(row.Key, row.Value, culture, rules);
                        applied++;
                    }
                    if (Interlocked.Exchange(ref reported, 1) == 0)
                        logger.Info($"[Manimal Korean] Server global locale {culture}: {applied} source-verified keys localized (other or missing values retained).");
                    return localeData;
                });
                registered++;
            }

            // The WTT dialogue service registers these as nested, per-dialogue
            // localization dictionaries; the global locale transformer cannot
            // reach them. Add only kr/kr-en for the two exact Manimal dialogue IDs.
            var dialogues = TranslateDialogues(rules);
            logger.Info($"[Manimal Korean] {rules.Count} exact-source locale rules ready; {registered} Korean global locale transformer(s), {dialogues} custom dialogue(s) extended.");
            if (registered == 0)
                logger.Warning("[Manimal Korean] No server kr/kr-en global locale exists; client-side locale overlay is still required.");
            if (dialogues != DialogueIds.Count)
                logger.Warning($"[Manimal Korean] Expected {DialogueIds.Count} WTT dialogue entries; found {dialogues}. Verify Manimal server mod and its version.");
        }
        catch (Exception e)
        {
            // A third-party mod may not be installed or may change its schema.
            // Never take down the direct-message server module as a side effect.
            logger.Warning("[Manimal Korean] Optional localization skipped: " + e.Message);
        }
        return Task.CompletedTask;
    }

    private static bool IsKorean(string? culture) =>
        string.Equals(culture, "kr", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(culture, "kr-en", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, Rule> ReadRules(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 ||
            root.GetProperty("target_spt").GetString() != "4.1.6" ||
            root.GetProperty("mod_id").GetString() != ModId ||
            root.GetProperty("mod_version").GetString() != "0.2.2")
            throw new InvalidDataException("Unexpected Manimal profile version or ID");
        var result = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in root.GetProperty("channels").GetProperty("locale").EnumerateArray())
        {
            if (r.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                continue;
            var key = r.GetProperty("key").GetString();
            var source = r.GetProperty("source").GetString();
            var translation = r.GetProperty("translation").GetString();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(translation)) continue;
            var display = r.TryGetProperty("display_type", out var type) ? type.GetString() ?? "" : "";
            if (!result.TryAdd(key, new Rule(source, translation, display)))
                throw new InvalidDataException("Duplicate Manimal locale key: " + key);
        }
        return result;
    }

    private static string Format(string key, Rule rule, string culture, Dictionary<string, Rule> rules)
    {
        var display = rule.DisplayType;
        if (string.IsNullOrEmpty(display))
        {
            if (key.EndsWith(" Name", StringComparison.OrdinalIgnoreCase)) display = "item_name";
            else if (key.EndsWith(" Description", StringComparison.OrdinalIgnoreCase)) display = "item_description";
        }
        switch (display)
        {
            case "quest_title": return rule.Translation + " (" + rule.Source + ")";
            case "quest_objective": return rule.Translation + "\n(" + rule.Source + ")";
            case "quest_description":
            case "achievement_description": return rule.Translation + "\n(" + ModName + ")";
            case "achievement_title":
                return culture.Equals("kr-en", StringComparison.OrdinalIgnoreCase)
                    ? rule.Translation + " (" + rule.Source + ")" : rule.Translation;
            case "item_name":
                return culture.Equals("kr-en", StringComparison.OrdinalIgnoreCase)
                    ? rule.Translation + "\n(" + rule.Source + ")" : rule.Translation;
            case "item_description":
            {
                var nameKey = key.EndsWith(" Description", StringComparison.OrdinalIgnoreCase)
                    ? key[..^" Description".Length] + " Name" : "";
                var header = rules.TryGetValue(nameKey, out var nameRule)
                    ? "[" + nameRule.Source + "]\n" : "";
                return header + rule.Translation + "\n(" + ModName + ")";
            }
            default: return rule.Translation;
        }
    }

    private int TranslateDialogues(Dictionary<string, Rule> rules)
    {
        var updated = 0;
        foreach (var dialogue in templateTable.Dialogue.Elements)
        {
            if (!DialogueIds.Contains(dialogue.Id.ToString())) continue;
            // The shared server model may expose a concrete generic dictionary.
            // IDictionary lets us update only the localization sub-dictionary
            // without assuming that WTT uses it as a global locale payload.
            var property = dialogue.GetType().GetProperty("Localization", BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetValue(dialogue) is not IDictionary languages ||
                languages["en"] is not IDictionary english)
            {
                logger.Warning("[Manimal Korean] Dialogue localization model is incompatible: " + dialogue.Id);
                continue;
            }
            foreach (var culture in new[] { "kr", "kr-en" })
            {
                IDictionary? localized = languages[culture] as IDictionary;
                if (localized == null)
                {
                    localized = Activator.CreateInstance(english.GetType()) as IDictionary;
                    if (localized == null) continue;
                    languages[culture] = localized;
                }
                foreach (DictionaryEntry entry in english)
                {
                    var key = entry.Key?.ToString();
                    if (key == null || entry.Value is not string source ||
                        !rules.TryGetValue(key, out var rule) ||
                        !string.Equals(source, rule.Source, StringComparison.Ordinal)) continue;
                    if (localized.Contains(entry.Key) &&
                        !string.Equals(localized[entry.Key] as string, source, StringComparison.Ordinal))
                        continue; // an existing Korean translation wins
                    localized[entry.Key] = rule.Translation;
                }
            }
            updated++;
        }
        return updated;
    }
}
