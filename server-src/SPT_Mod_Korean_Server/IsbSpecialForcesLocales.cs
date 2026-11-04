using System.Reflection;
using System.Text.Json;
using System.Threading;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

// ISB Special Forces 2.0.2 registers English fallback values into every server
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
    private const string SourceVersion = "2.0.2";
    private const string ProfileName = "ISB Special Forces.json";
    private sealed record Rule(string Source, string Korean, string DisplayType);

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

            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Missing server addon assembly directory");
            var sptRoot = Path.GetFullPath(Path.Combine(assemblyDir, "../../../.."));
            var profilePath = Path.Combine(sptRoot, "BepInEx", "plugins", "SPT_Mod_Korean_Addon", "translations", ProfileName);
            if (!File.Exists(profilePath))
                throw new FileNotFoundException("ISB Special Forces locale profile was not found", profilePath);
            var rules = ReadRules(profilePath);
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

    private static bool IsKorean(string? locale) =>
        string.Equals(locale, "kr", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(locale, "kr-en", StringComparison.OrdinalIgnoreCase);

    // Only the 2.0.2 profile's explicit, individually source-audited
    // seed_missing rules can populate server locales. The profile is the single
    // source of truth; never ship a second translated key/value catalog.
    private static Dictionary<string, Rule> ReadRules(string path)
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
            if (!row.TryGetProperty("seed_missing", out var seed) || seed.ValueKind != JsonValueKind.True)
                continue;
            var key = row.GetProperty("key").GetString() ?? "";
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
    private static string Format(Rule rule, string culture)
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
