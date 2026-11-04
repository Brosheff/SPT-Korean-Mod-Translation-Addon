using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

// Source-verified Scorpion 1.1.1 quest localization only. Does not modify the
// Scorpion mod or blindly replace values from other localization providers.
// Scorpion registers its locale transformers at TraderRegistration. PostLoad
// registers this transformer afterwards; the client overlay preserves this
// already-translated value rather than translating it a second time.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public sealed class ScorpionLocales(
    ISptLogger<ScorpionLocales> logger,
    LocaleTable localeTable,
    TemplateTable templateTable) : IOnLoad
{
    private const string ProfileId = "mod_b03daac1dc68";
    private const string QuestFileName = "Scorpion_quests.json";
    private const string LocaleFileName = "ScorpionLocale.json";
    private const string AlwaysActiveQuestId = "668858720221bbe5f306b2ea";
    private const string ModName = "Scorpion";
    private const int ExpectedRows = 902;
    private const int ExpectedQuests = 122;
    private sealed record Rule(string Source, string Translation, string DisplayType);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var assemblyRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Server assembly location unavailable");
            var sptRoot = Path.GetFullPath(Path.Combine(assemblyRoot, "../../../.."));
            var profilePath = Path.Combine(sptRoot, "BepInEx", "plugins", "SPT_Mod_Korean_Addon", "translations", "Scorpion.json");
            var modsRoot = Path.Combine(sptRoot, "SPT_Runtime", "user", "mods");
            if (!File.Exists(profilePath) || !Directory.Exists(modsRoot)) return Task.CompletedTask;

            // A source project may be present without an installed Scorpion mod.
            if (!templateTable.Quests.Keys.Any(id => id.ToString() == AlwaysActiveQuestId))
            {
                logger.Info("[Scorpion Korean] Scorpion quest table not present; optional override skipped.");
                return Task.CompletedTask;
            }
            var sources = FindSourceRoots(modsRoot).ToList();
            if (sources.Count != 1)
            {
                logger.Warning($"[Scorpion Korean] Expected exactly one installed Scorpion data root; found {sources.Count}. Override skipped.");
                return Task.CompletedTask;
            }
            var sourceRoot = sources[0];
            var originalEnglish = ReadLocale(Path.Combine(sourceRoot, "locales", "en", LocaleFileName));
            var nativeKorean = ReadLocale(Path.Combine(sourceRoot, "locales", "kr", LocaleFileName));
            var (rules, aliases) = ReadProfileAndAliases(profilePath, sourceRoot, originalEnglish);
            if (rules.Count != ExpectedRows) throw new InvalidDataException("Incomplete Scorpion 1.1.1 quest profile");

            var installed = 0;
            foreach (var language in localeTable.Global)
            {
                if (!IsKorean(language.Key)) continue;
                var culture = language.Key;
                int logged = 0;
                language.Value.AddTransformer(localeData =>
                {
                    if (localeData == null) return localeData;
                    var replaced = 0;
                    var canonicalAdded = 0;
                    var preserved = 0;
                    foreach (var entry in rules)
                    {
                        var key = entry.Key;
                        var rule = entry.Value;
                        if (!localeData.TryGetValue(key, out var current))
                        {
                            // Original Scorpion itself registers these keys.
                            // Missing originals must not authorize invented keys.
                            preserved++;
                            continue;
                        }
                        if (!IsOriginal(current, rule.Source, nativeKorean.GetValueOrDefault(key)))
                        {
                            preserved++;
                            continue; // changed source / other translation
                        }
                        localeData[key] = Format(rule);
                        replaced++;
                    }
                    // Original 1.1.1 accidentally uses successMessagetext and
                    // failMessagetext while quest definitions request *MessageText.
                    // The canonical mapping is generated only from installed
                    // quest JSON and the exact original locale keys, not guesses.
                    foreach (var alias in aliases)
                    {
                        if (!rules.TryGetValue(alias.Value, out var originalRule)) continue;
                        if (localeData.TryGetValue(alias.Key, out var current))
                        {
                            // Case-insensitive locale dictionaries can resolve
                            // a legacy key via the canonical spelling already.
                            if (!IsOriginal(current, originalRule.Source,
                                    nativeKorean.GetValueOrDefault(alias.Value)))
                                continue;
                        }
                        else if (!localeData.TryGetValue(alias.Value, out var legacy) ||
                                 (!IsOriginal(legacy, originalRule.Source,
                                     nativeKorean.GetValueOrDefault(alias.Value)) &&
                                  !string.Equals(legacy, originalRule.Translation, StringComparison.Ordinal)))
                        {
                            // Unrecognized/missing legacy key: do not fabricate it.
                            continue;
                        }
                        localeData[alias.Key] = Format(originalRule);
                        canonicalAdded++;
                    }
                    if (Interlocked.Exchange(ref logged, 1) == 0)
                        logger.Info($"[Scorpion Korean] {culture}: {replaced} quest locale values replaced, {canonicalAdded} canonical message keys handled, {preserved} unmatched/missing keys preserved (source-verified; not a game-display test).");
                    return localeData;
                });
                installed++;
            }
            logger.Info($"[Scorpion Korean] {rules.Count} verified English quest keys, {aliases.Count} message-key aliases, {installed} Korean locale transformers registered.");
        }
        catch (Exception ex)
        {
            // Scorpion is an optional mod; never disable the remaining addon.
            logger.Warning("[Scorpion Korean] Optional quest localization skipped: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    private static bool IsKorean(string language) =>
        language.Equals("kr", StringComparison.OrdinalIgnoreCase) ||
        language.Equals("kr-en", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> FindSourceRoots(string modsRoot)
    {
        foreach (var mod in Directory.EnumerateDirectories(modsRoot))
        {
            // The Scorpion release and a source-style installation are both
            // supported. Avoid recursive searches of unrelated large mod trees.
            foreach (var possible in new[] {
                Path.Combine(mod, "data"), Path.Combine(mod, "Server", "data") })
            {
                var questFile = Path.Combine(possible, "quests", QuestFileName);
                var englishFile = Path.Combine(possible, "locales", "en", LocaleFileName);
                var koreanFile = Path.Combine(possible, "locales", "kr", LocaleFileName);
                if (File.Exists(questFile) && File.Exists(englishFile) && File.Exists(koreanFile))
                    yield return possible;
            }
        }
    }

    private static Dictionary<string, string> ReadLocale(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
        ?? throw new InvalidDataException("Invalid Scorpion locale file: " + path);

    private static (Dictionary<string, Rule> Rules, Dictionary<string, string> Aliases) ReadProfileAndAliases(
        string profilePath, string installedSourceRoot, Dictionary<string, string> english)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
        var profile = document.RootElement;
        if (profile.GetProperty("schema_version").GetInt32() != 1 ||
            profile.GetProperty("target_spt").GetString() != "4.1.6" ||
            profile.GetProperty("mod_id").GetString() != ProfileId ||
            profile.GetProperty("mod_version").GetString() != "1.1.1")
            throw new InvalidDataException("Scorpion profile schema, ID or source version differs");

        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        foreach (var row in profile.GetProperty("channels").GetProperty("locale").EnumerateArray())
        {
            if (row.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                continue;
            var key = row.GetProperty("key").GetString() ?? "";
            if (!english.TryGetValue(key, out var original)) continue; // trader profile rows
            var source = row.GetProperty("source").GetString();
            var translated = row.GetProperty("translation").GetString();
            var display = row.TryGetProperty("display_type", out var field) ? field.GetString() ?? "" : "";
            if (source != original || string.IsNullOrWhiteSpace(translated) ||
                (row.TryGetProperty("match_mode", out var mode) && mode.GetString() != "exact") ||
                !rules.TryAdd(key, new Rule(original, translated, display)))
                throw new InvalidDataException("Scorpion source mismatch / duplicate / invalid rule: " + key);
        }
        // Do not run against a differently versioned Scorpion locale source.
        if (rules.Count != ExpectedRows || english.Count != ExpectedRows)
            throw new InvalidDataException("Scorpion quest locale source coverage/version mismatch");

        using var questDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(installedSourceRoot, "quests", QuestFileName)));
        if (questDoc.RootElement.EnumerateObject().Count() != ExpectedQuests)
            throw new InvalidDataException("Scorpion quest definitions/version mismatch");
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var quest in questDoc.RootElement.EnumerateObject())
        {
            foreach (var (canonical, legacy) in new[] {
                ("successMessageText", "successMessagetext"),
                ("failMessageText", "failMessagetext") })
            {
                var requested = quest.Name + " " + canonical;
                var oldKey = quest.Name + " " + legacy;
                if (quest.Value.TryGetProperty(canonical, out var registered) &&
                    registered.GetString() == requested &&
                    rules.ContainsKey(oldKey) && !english.ContainsKey(requested))
                    aliases.Add(requested, oldKey);
            }
        }
        if (aliases.Count != 124)
            throw new InvalidDataException($"Scorpion quest message aliases changed: {aliases.Count}");
        return (rules, aliases);
    }

    private static bool IsOriginal(string? current, string english, string? nativeKorean) =>
        string.Equals(current, english, StringComparison.Ordinal) ||
        (nativeKorean != null && string.Equals(current, nativeKorean, StringComparison.Ordinal));

    private static string Format(Rule rule) => rule.DisplayType switch
    {
        "quest_title" => rule.Translation + " (" + rule.Source + ")",
        "quest_objective" => rule.Translation + "\n(" + rule.Source + ")",
        "quest_description" => rule.Translation + "\n(" + ModName + ")",
        _ => rule.Translation
    };
}
