using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SPT_Mod_Korean_Server;

// Hideout Last Stand 0.1.0-0.1.3 reuses the Suburbs map slot and the developer junkbox
// template. Only its confirmed siege scene is eligible for these translations.
// The original mod registers its locale transformers at Preload+50; this
// optional adapter registers afterwards, without patching the original mod.
[Injectable(TypePriority = OnLoadOrder.Preload + 51)]
public sealed class HideoutLastStandLocales(
    ISptLogger<HideoutLastStandLocales> logger,
    LocationTable locations,
    LocaleTable localeTable) : IOnLoad
{
    private const string ProfileId = "jrs.hideoutsiege";
    private const string ModName = "Tushonka: Hideout Last Stand";
    private const string ScenePath = "assets/content/locations/_presets/bunker.scenespreset.bundle";
    private const string BoxTemplate = "5c0a5f6c86f774753654890e";
    private const string MapNameKey = "Suburbs Name";
    private const string MapDescriptionKey = "Suburbs Description";
    private const string BoxNameKey = BoxTemplate + " Name";
    private const string BoxShortNameKey = BoxTemplate + " ShortName";
    private const string BoxDescriptionKey = BoxTemplate + " Description";
    // Reviewed original 0.1.0 uses a shorter stash description. Keep its
    // original exact display rule without applying 0.1.3 loot-loss wording.
    private const string LegacyBoxDescriptionSource =
        "Your stash. Whatever you put in here during the siege is in your real stash afterwards.";
    private const string LegacyBoxDescriptionTranslation =
        "방어전 중 이 보관함에 넣은 물품은 방어전이 끝난 뒤 실제 은신처 보관함에 보관됩니다.";

    private sealed record Rule(string Source, string Translation, string DisplayType);

    private static readonly Dictionary<string, string> ExpectedSources = new(StringComparer.Ordinal)
    {
        [MapNameKey] = "Your Hideout",
        [MapDescriptionKey] = "Your hideout is under attack. Hold it.",
        [BoxNameKey] = "Stash",
        [BoxShortNameKey] = "Stash",
        [BoxDescriptionKey] = "Your stash. Take what you need: what you take out and lose is gone from it. Whatever you put in here is in your real stash afterwards."
    };

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Without the original siege location registration, this Suburbs
            // slot still belongs to the base game (or another mod). Do nothing.
            var slot = locations.GetLocation("Suburbs");
            if (slot?.Base?.Scene?.Path != ScenePath) return Task.CompletedTask;
            var mapIdField = slot.Base.IdField;
            if (string.IsNullOrWhiteSpace(mapIdField))
            {
                logger.Warning("[Hideout Last Stand Korean] Siege location ID field is missing; skipped.");
                return Task.CompletedTask;
            }

            var assemblyRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Server assembly location unavailable");
            var sptRoot = Path.GetFullPath(Path.Combine(assemblyRoot, "../../../.."));
            var path = Path.Combine(sptRoot, "BepInEx", "plugins", "SPT_Mod_Korean_Addon",
                "translations", "Hideout Last Stand.json");
            if (!File.Exists(path)) return Task.CompletedTask;
            var rules = ReadRules(path);
            var registered = 0;
            foreach (var entry in localeTable.Global)
            {
                if (!IsKorean(entry.Key)) continue;
                var culture = entry.Key;
                var reported = 0;
                entry.Value.AddTransformer(data =>
                {
                    if (data is null) return data;
                    var localized = 0;
                    var preserved = 0;
                    // The original mod uses slot.Base.IdField as the actual map
                    // locale prefix. Do not assume it has the same capitalization
                    // as the client route ID "Suburbs".
                    Apply(data, mapIdField + " Name", rules[MapNameKey], culture, true,
                        ref localized, ref preserved);
                    Apply(data, mapIdField + " Description", rules[MapDescriptionKey], culture, true,
                        ref localized, ref preserved);
                    // The siege mod registers the three box values in every
                    // language at Preload+50; only translate source-matching ones.
                    Apply(data, BoxNameKey, rules[BoxNameKey], culture, false,
                        ref localized, ref preserved);
                    Apply(data, BoxShortNameKey, rules[BoxShortNameKey], culture, false,
                        ref localized, ref preserved);
                    Apply(data, BoxDescriptionKey, rules[BoxDescriptionKey], culture, false,
                        ref localized, ref preserved);
                    if (Interlocked.Exchange(ref reported, 1) == 0)
                        logger.Info($"[Hideout Last Stand Korean] {culture}: {localized} verified locale fields localized, {preserved} existing non-source values preserved. Game display not verified.");
                    return data;
                });
                registered++;
            }
            logger.Info($"[Hideout Last Stand Korean] {rules.Count} source-verified entries, {registered} Korean locale transformers registered.");
        }
        catch (Exception error)
        {
            logger.Warning("[Hideout Last Stand Korean] Optional locale adapter skipped: " + error.Message);
        }
        return Task.CompletedTask;
    }

    private static void Apply(IDictionary<string, string> data, string key, Rule rule,
        string culture, bool seedMissing, ref int localized, ref int preserved)
    {
        if (data.TryGetValue(key, out var current))
        {
            // Never erase a Korean string supplied by another translator or
            // an unknown future upstream version.
            if (!string.Equals(current, rule.Source, StringComparison.Ordinal))
            {
                // 0.1.0 installs a different, source-verified description.
                // Apply only that original wording, and only to this item key.
                if (key == BoxDescriptionKey &&
                    string.Equals(current, LegacyBoxDescriptionSource, StringComparison.Ordinal))
                {
                    data[key] = Format(new Rule(LegacyBoxDescriptionSource,
                        LegacyBoxDescriptionTranslation, rule.DisplayType), culture);
                    localized++;
                    return;
                }
                preserved++;
                return;
            }
        }
        else if (!seedMissing)
        {
            preserved++;
            return;
        }
        data[key] = Format(rule, culture);
        localized++;
    }

    private static string Format(Rule rule, string culture)
    {
        return rule.DisplayType switch
        {
            "item_name" when culture.Equals("kr-en", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(rule.Source, rule.Translation, StringComparison.Ordinal)
                => rule.Translation + "\n(" + rule.Source + ")",
            "item_description" => "[Stash]\n" + rule.Translation + "\n(" + ModName + ")",
            _ => rule.Translation
        };
    }

    private static Dictionary<string, Rule> ReadRules(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 ||
            root.GetProperty("target_spt").GetString() != "4.1.6" ||
            root.GetProperty("mod_id").GetString() != ProfileId ||
            root.GetProperty("mod_name").GetString() != ModName ||
            !KnownSourceVersion(root.GetProperty("mod_version").GetString()))
            throw new InvalidDataException("Unexpected Hideout Last Stand translation profile header");

        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("channels").GetProperty("locale").EnumerateArray())
        {
            if (row.TryGetProperty("enabled", out var active) && active.ValueKind == JsonValueKind.False)
                continue;
            var key = row.GetProperty("key").GetString() ?? "";
            var source = row.GetProperty("source").GetString() ?? "";
            var translation = row.GetProperty("translation").GetString() ?? "";
            var display = row.GetProperty("display_type").GetString() ?? "";
            if (!ExpectedSources.TryGetValue(key, out var expected) ||
                !string.Equals(source, expected, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(translation) ||
                (row.TryGetProperty("match_mode", out var mode) && mode.GetString() != "exact") ||
                (key.StartsWith("Suburbs ", StringComparison.Ordinal) && display != "default") ||
                (key == BoxNameKey && display != "item_name") ||
                (key == BoxShortNameKey && display != "default") ||
                (key == BoxDescriptionKey && display != "item_description") ||
                !rules.TryAdd(key, new Rule(source, translation, display)))
                throw new InvalidDataException("Unexpected or duplicate Hideout Last Stand locale rule: " + key);
        }
        if (rules.Count != ExpectedSources.Count)
            throw new InvalidDataException($"Expected {ExpectedSources.Count} locale rules; got {rules.Count}");
        return rules;
    }

    // Legacy profile 0.1.0 and reviewed 0.1.1-0.1.3 use identical
    // localized source keys in the siege scene. Future versions must be
    // separately audited before permitting their source payloads.
    private static bool KnownSourceVersion(string? version) =>
        version is "0.1.0" or "0.1.1" or "0.1.2" or "0.1.3";

    private static bool IsKorean(string? culture) =>
        string.Equals(culture, "kr", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(culture, "kr-en", StringComparison.OrdinalIgnoreCase);
}
