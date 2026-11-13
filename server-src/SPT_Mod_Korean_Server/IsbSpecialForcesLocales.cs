using System.Reflection;
using System.Text.Json;
using System.Threading;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

// ISB Special Forces 2.0.4 registers English fallback values into every server
// locale via WTT CommonLib's CreateCustomLocales/CreateCustomQuests transformers.
// The client-only MergeGlobal hook cannot guarantee that a late server locale
// registration makes its way into the displayed quest/map locale dictionary.
// Add one exact-source-guarded Korean transformer immediately after ISB's
// TraderRegistration + 1 hook, before later locale-table consumers.
// Neither the original ISB mod nor another translator's Korean entries are edited.
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 2)]
public sealed class IsbSpecialForcesLocales(
    ISptLogger<IsbSpecialForcesLocales> logger,
    LocaleTable localeTable,
    IReadOnlyList<SptMod> installedMods) : IOnLoad
{
    private const string ModGuid = "com.samc137.isbspecialforces";
    private const string SourceVersion = "2.0.4";
    private const string ProfileName = "ISB Special Forces.json";
    // Exact keys registered from C# by ISBAmnestyQuestPatch, not en.json.
    internal static readonly string[] AmnestyKeys =
    {
        "ISB/Amnesty/name",
        "ISB/Amnesty/description",
        "ISB/Amnesty/startedMessageText",
        "ISB/Amnesty/handover",
        "ISB/Amnesty/successMessageText"
    };
    internal sealed record Rule(string Source, string Korean, string DisplayType);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!installedMods.Any(m => string.Equals(m.ModMetadata.ModGuid, ModGuid, StringComparison.OrdinalIgnoreCase)))
            {
                logger.Info("[ISB Korean] ISB Special Forces is not loaded; locale adapter inactive.");
                return Task.CompletedTask;
            }

            var rules = ReadRulesFromAddon();
            if (rules.Count == 0)
                throw new InvalidDataException("No source-verified ISB locale seed rules");

            var count = 0;
            foreach (var locale in localeTable.Global)
            {
                if (!IsKorean(locale.Key)) continue;
                var localeName = locale.Key;
                int firstLog = 0;
                locale.Value.AddTransformer(localeData =>
                {
                    if (localeData == null) return localeData;
                    int applied = 0, protectedExisting = 0;
                    foreach (var pair in rules)
                    {
                        if (localeData.TryGetValue(pair.Key, out var current) &&
                            !string.Equals(current, pair.Value.Source, StringComparison.Ordinal) &&
                            !string.Equals(current, pair.Key, StringComparison.Ordinal))
                        {
                            protectedExisting++;
                            continue;
                        }
                        var formatted = Format(pair.Value, localeName);
                        if (localeData.TryGetValue(pair.Key, out var already) &&
                            string.Equals(already, formatted, StringComparison.Ordinal))
                            continue;
                        localeData[pair.Key] = formatted;
                        applied++;
                    }
                    if (Interlocked.Exchange(ref firstLog, 1) == 0)
                        logger.Info($"[ISB Korean] {localeName}: {applied} ISB source-verified entries localized; {protectedExisting} non-English or changed values preserved.");
                    return localeData;
                });
                count++;
            }
            logger.Info($"[ISB Korean] {rules.Count} exact-source locale rules; {count} Korean server locale transformers registered.");
        }
        catch (Exception error)
        {
            logger.Warning("[ISB Korean] Server locale adapter unavailable; other translations unchanged: " + error);
        }
        return Task.CompletedTask;
    }

    internal static bool IsKorean(string? locale) =>
        string.Equals(locale, "kr", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(locale, "kr-en", StringComparison.OrdinalIgnoreCase);

    // Shared by the early general transformer and the late Amnesty-only pass.
    // Both consume the same version-checked translation profile; no second catalog.
    internal static Dictionary<string, Rule> ReadRulesFromAddon(bool includeDynamicAmnesty = false)
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Missing server addon assembly directory");
        var sptRoot = Path.GetFullPath(Path.Combine(assemblyDir, "../../../.."));
        var profilePath = Path.Combine(sptRoot, "BepInEx", "plugins", "SPT_Mod_Korean_Addon", "translations", ProfileName);
        if (!File.Exists(profilePath))
            throw new FileNotFoundException("ISB Special Forces locale profile was not found", profilePath);
        return ReadRules(profilePath, includeDynamicAmnesty);
    }

    // Only the 2.0.4 profile's explicit, individually source-audited
    // seed_missing rules can populate server locales. The profile is the single
    // source of truth; never ship a second translated key/value catalog.
    private static Dictionary<string, Rule> ReadRules(string path, bool includeDynamicAmnesty)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 ||
            root.GetProperty("target_spt").GetString() != "4.1.6" ||
            root.GetProperty("mod_id").GetString() != ModGuid ||
            root.GetProperty("mod_version").GetString() != SourceVersion)
            throw new InvalidDataException("Incompatible ISB locale profile version/identity");

        var result = new Dictionary<string, Rule>(StringComparer.Ordinal);
        var lastMode = "exact";
        foreach (var row in root.GetProperty("channels").GetProperty("locale").EnumerateArray())
        {
            if (row.TryGetProperty("match_mode", out var mode))
                lastMode = mode.GetString() ?? "";
            if (row.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                continue;
            var key = row.GetProperty("key").GetString() ?? "";
            var isSeed = row.TryGetProperty("seed_missing", out var seed) && seed.ValueKind == JsonValueKind.True;
            // Dynamic Amnesty locale keys are supplied by the source C# transformer,
            // not by an English JSON file. Never mark them seed_missing in data.
            if (!isSeed && !(includeDynamicAmnesty && AmnestyKeys.Contains(key, StringComparer.Ordinal)))
                continue;
            var source = row.GetProperty("source").GetString() ?? "";
            var translated = row.GetProperty("translation").GetString() ?? "";
            var display = row.TryGetProperty("display_type", out var d) ? d.GetString() ?? "default" : "default";
            if (lastMode != "exact" || string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translated) ||
                !AllowedDisplay(display) || !result.TryAdd(key, new Rule(source, translated, display)))
                throw new InvalidDataException("Invalid ISB source/seed locale row: " + key);
        }
        return result;
    }

    private static bool AllowedDisplay(string type) =>
        type is "default" or "quest_title" or "quest_objective" or "quest_description" or
            "achievement_title" or "achievement_description" or "achievement_condition";

    // Mirror src/LocaleDisplayRules.Select for the ISB seed_missing display types.
    internal static string Format(Rule rule, string culture)
    {
        var korean = rule.Korean;
        var source = rule.Source;
        switch (rule.DisplayType)
        {
            case "quest_description":
            case "achievement_description":
                var credit = "\n(ISB Special Forces)";
                return korean.TrimEnd().EndsWith(credit, StringComparison.Ordinal)
                    ? korean : korean.TrimEnd() + credit;
            case "quest_title":
            case "quest_objective":
                if (korean.Trim() == source.Trim()) return korean;
                var suffix = (rule.DisplayType == "quest_title" ? " " : "\n") + "(" + source.Trim() + ")";
                return korean.TrimEnd().EndsWith(suffix, StringComparison.Ordinal)
                    ? korean : korean.TrimEnd() + suffix;
            case "achievement_title":
                if (!string.Equals(culture, "kr-en", StringComparison.OrdinalIgnoreCase) ||
                    korean.Trim() == source.Trim()) return korean;
                var ending = " (" + source.Trim() + ")";
                return korean.TrimEnd().EndsWith(ending, StringComparison.Ordinal) ? korean : korean.TrimEnd() + ending;
            case "achievement_condition":
                if (!string.Equals(culture, "kr-en", StringComparison.OrdinalIgnoreCase) ||
                    korean.Trim() == source.Trim()) return korean;
                var full = "\n(" + source.Trim() + ")";
                return korean.TrimEnd().EndsWith(full, StringComparison.Ordinal) ? korean : korean.TrimEnd() + full;
            default:
                return korean;
        }
    }
}

// ISBAmnestyQuestPatch (PostLoad + 1100) registers a *later* locale transformer
// that unconditionally rewrites its five nonempty quest strings in English.
// The regular TraderRegistration + 2 pass must stay early for other consumers.
// Reapply only these five verified strings after the original Amnesty transformer,
// without changing the quest logic, IDs, other mods, or existing non-English values.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1101)]
public sealed class IsbSpecialForcesAmnestyLocales(
    ISptLogger<IsbSpecialForcesAmnestyLocales> logger,
    LocaleTable localeTable,
    IReadOnlyList<SptMod> installedMods) : IOnLoad
{
    private const string ModGuid = "com.samc137.isbspecialforces";
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!installedMods.Any(m => string.Equals(m.ModMetadata.ModGuid, ModGuid, StringComparison.OrdinalIgnoreCase)))
                return Task.CompletedTask;

            var catalog = IsbSpecialForcesLocales.ReadRulesFromAddon(includeDynamicAmnesty: true);
            var selected = new List<KeyValuePair<string, IsbSpecialForcesLocales.Rule>>();
            foreach (var key in IsbSpecialForcesLocales.AmnestyKeys)
            {
                if (!catalog.TryGetValue(key, out var rule))
                    throw new InvalidDataException("Missing source-verified ISB Amnesty locale rule: " + key);
                selected.Add(new KeyValuePair<string, IsbSpecialForcesLocales.Rule>(key, rule));
            }

            foreach (var locale in localeTable.Global)
            {
                if (!IsbSpecialForcesLocales.IsKorean(locale.Key)) continue;
                var localeName = locale.Key;
                int firstLog = 0;
                locale.Value.AddTransformer(localeData =>
                {
                    if (localeData == null) return localeData;
                    int applied = 0;
                    foreach (var pair in selected)
                    {
                        // Unlike the early seed pass, do not create an Amnesty key
                        // if the originating quest transformer did not register it.
                        if (!localeData.TryGetValue(pair.Key, out var current)) continue;
                        if (!string.Equals(current, pair.Value.Source, StringComparison.Ordinal) &&
                            !string.Equals(current, pair.Key, StringComparison.Ordinal)) continue;
                        var korean = IsbSpecialForcesLocales.Format(pair.Value, localeName);
                        if (string.Equals(current, korean, StringComparison.Ordinal)) continue;
                        localeData[pair.Key] = korean;
                        applied++;
                    }
                    if (Interlocked.Exchange(ref firstLog, 1) == 0)
                        logger.Info($"[ISB Korean] {localeName}: {applied} late Amnesty locale values localized.");
                    return localeData;
                });
            }
        }
        catch (Exception error)
        {
            logger.Warning("[ISB Korean] Late Amnesty locale adapter unavailable; other translations unchanged: " + error);
        }
        return Task.CompletedTask;
    }
}
