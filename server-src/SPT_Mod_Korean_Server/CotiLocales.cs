using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

// COTI 3.4.1 creates its nine custom templates with English-only LocaleDetails.
// The ordinary client locale overlay may run before a missing Korean locale is
// resolved by the game; complete these exact keys in the server's lazy locale map.
// Never edit the COTI mod, its templates or any existing non-English text.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 75)]
public sealed class CotiLocales(
    ISptLogger<CotiLocales> logger,
    LocaleTable localeTable,
    TemplateTable templateTable) : IOnLoad
{
    private const string ProfileId = "mod_3b2b877cc80c";
    private const string SourceVersion = "3.4.1";
    private const string ModName = "ECOTI - Enhanced Clip-On Thermal Imager";
    private const string MainTemplateId = "6a7f15387b68c336e18e8977";
    private static readonly HashSet<string> VerifiedItemIds = new(StringComparer.Ordinal)
    {
        "6a7f15387b68c336e18e8977",
        "6ad3016bab020957abb6ca3b", "6ad408a96bf1f761d970a4ff",
        "6ad5739462c39577194eedc5", "6ad573a014ebd436fbd1dfda",
        "6ad5775e327076f93b3b36bf", "6ad57762f51afaf4379f19a2",
        "6ad577679385f594db176ad6", "6ad5776a93ea4db35ed55dcf"
    };

    private sealed record Rule(string Source, string Translation, string Field, bool SeedMissing);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Do not add any locale values if the separately installed COTI mod is absent.
            if (!templateTable.Items.Keys.Any(k => k.ToString() == MainTemplateId))
                return Task.CompletedTask;

            var assemblyRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Server assembly location unavailable");
            var sptRoot = Path.GetFullPath(Path.Combine(assemblyRoot, "../../../.."));
            var profilePath = Path.Combine(sptRoot, "BepInEx", "plugins", "SPT_Mod_Korean_Addon", "translations", "ECOTI - Enhanced Clip-On Thermal Imager.json");
            if (!File.Exists(profilePath)) return Task.CompletedTask;
            var rules = ReadRules(profilePath);
            var installedTemplates = new HashSet<string>(templateTable.Items.Keys.Select(k => k.ToString()), StringComparer.Ordinal);
            var count = 0;
            foreach (var language in localeTable.Global)
            {
                if (!IsKorean(language.Key)) continue;
                var culture = language.Key;
                var logged = 0;
                language.Value.AddTransformer(localeData =>
                {
                    if (localeData == null) return localeData;
                    var applied = 0;
                    var preserved = 0;
                    foreach (var row in rules)
                    {
                        var itemId = row.Key.Split(' ')[0];
                        if (!installedTemplates.Contains(itemId)) continue;
                        var rule = row.Value;
                        if (localeData.TryGetValue(row.Key, out var existing))
                        {
                            // A previous translator's Korean value always wins, including
                            // when this profile's translation is different from that value.
                            if (!string.Equals(existing, rule.Source, StringComparison.Ordinal))
                            {
                                preserved++;
                                continue;
                            }
                        }
                        else if (!rule.SeedMissing)
                        {
                            preserved++;
                            continue;
                        }
                        localeData[row.Key] = Format(row.Key, rule, culture, rules);
                        applied++;
                    }
                    if (Interlocked.Exchange(ref logged, 1) == 0)
                        logger.Info($"[COTI Korean] {culture}: {applied} source-verified item fields localized, {preserved} other/mismatched values preserved (static source path; game display not verified).");
                    return localeData;
                });
                count++;
            }
            logger.Info($"[COTI Korean] {rules.Count} verified COTI 3.4.1 item locale rules; {count} Korean transformers registered.");
        }
        catch (Exception error)
        {
            logger.Warning("[COTI Korean] Optional item locale adapter skipped: " + error.Message);
        }
        return Task.CompletedTask;
    }

    private static bool IsKorean(string? locale) =>
        string.Equals(locale, "kr", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(locale, "kr-en", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, Rule> ReadRules(string profilePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 ||
            root.GetProperty("target_spt").GetString() != "4.1.6" ||
            root.GetProperty("mod_id").GetString() != ProfileId ||
            root.GetProperty("mod_version").GetString() != SourceVersion ||
            root.GetProperty("mod_name").GetString() != ModName)
            throw new InvalidDataException("COTI translation profile does not match the verified mod version");

        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        var rows = root.GetProperty("channels").GetProperty("locale");
        foreach (var row in rows.EnumerateArray())
        {
            var key = row.GetProperty("key").GetString() ?? "";
            var separator = key.IndexOf(' ');
            if (separator <= 0 || !VerifiedItemIds.Contains(key[..separator]))
                throw new InvalidDataException("Unexpected COTI locale key: " + key);
            var field = key[(separator + 1)..];
            if (field != "Name" && field != "ShortName" && field != "Description")
                throw new InvalidDataException("Unexpected COTI locale field: " + key);
            var source = row.GetProperty("source").GetString() ?? "";
            var translation = row.GetProperty("translation").GetString() ?? "";
            var seed = row.TryGetProperty("seed_missing", out var seedFlag) && seedFlag.ValueKind == JsonValueKind.True;
            if (row.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                continue;
            // This optional server path is strictly for exact-source, independently
            // audited items; decorated matches remain in the client overlay.
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translation) ||
                (row.TryGetProperty("match_mode", out var mode) && mode.GetString() != "exact") ||
                !rules.TryAdd(key, new Rule(source, translation, field, seed)))
                throw new InvalidDataException("Invalid/duplicate COTI locale rule: " + key);
        }
        if (rules.Count != 26)
            throw new InvalidDataException($"COTI 3.4.1 expected 26 item locale rules; found {rules.Count}");
        return rules;
    }

    private static string Format(string key, Rule rule, string culture, Dictionary<string, Rule> rules)
    {
        if (rule.Field == "Name" && string.Equals(culture, "kr-en", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rule.Translation.Trim(), rule.Source.Trim(), StringComparison.Ordinal))
            return rule.Translation + "\n(" + rule.Source.Trim() + ")";
        if (rule.Field == "Description")
        {
            var nameKey = key[..^" Description".Length] + " Name";
            var header = rules.TryGetValue(nameKey, out var name) ? "[" + name.Source.Trim() + "]\n" : "";
            return header + rule.Translation.TrimEnd() + "\n(" + ModName + ")";
        }
        return rule.Translation;
    }
}
