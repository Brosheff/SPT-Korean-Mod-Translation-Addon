using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SPT.ModKoreanAddon
{
    // Editable, display-only translation profiles for BepInEx Configuration Manager.
    // This layer intentionally never changes ConfigDefinition, ConfigDescription or .cfg files.
    internal sealed class ConfigManagerProfiles
    {
        private const int SchemaVersion = 1;
        private const string TargetSpt = "4.1.6";
        private readonly string folder;
        private readonly Dictionary<string, ConfigUiProfile> profiles =
            new Dictionary<string, ConfigUiProfile>(StringComparer.OrdinalIgnoreCase);

        internal int ProfileCount => profiles.Count;
        internal int TranslationEntryCount { get; private set; }

        internal ConfigManagerProfiles(string addonRoot)
        {
            folder = Path.Combine(addonRoot, "config-ui");
            if (!Directory.Exists(folder)) return;

            // Ignore historical discovery files; only root-level translation profiles apply.
            foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(file).StartsWith("_", StringComparison.Ordinal)) continue;
                try
                {
                    ConfigUiProfile profile;
                    using (var reader = new JsonTextReader(new StreamReader(file)))
                    {
                        var json = JObject.Load(reader, new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        });
                        if (reader.Read()) throw new InvalidDataException("Trailing JSON content");
                        profile = json.ToObject<ConfigUiProfile>();
                    }

                    Validate(profile, Path.GetFileName(file));
                    if (profiles.ContainsKey(profile.plugin_guid))
                        throw new InvalidDataException("Duplicate plugin_guid: " + profile.plugin_guid);

                    profiles.Add(profile.plugin_guid, profile);
                    TranslationEntryCount += profile.entries.Count(HasActiveEntryTranslation);
                }
                catch (Exception ex)
                {
                    SPT.EditableTranslations.MinimalLog.WarnOnce("ConfigManagerProfiles:57", () => "[Config UI] Rejected " + Path.GetFileName(file) + ": " + ex.Message);
                }
            }
        }

        internal ConfigUiTranslation Resolve(ConfigUiSource source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.plugin_guid)) return null;
            if (!profiles.TryGetValue(source.plugin_guid, out var profile)) return null;

            var result = new ConfigUiTranslation();
            if (!string.IsNullOrEmpty(source.source_category))
            {
                var category = profile.categories.FirstOrDefault(x =>
                    string.Equals(x.source, source.source_category, StringComparison.Ordinal));
                if (category != null && !string.IsNullOrEmpty(category.translation))
                    result.category = LocaleText.Select(category.source, category.translation, category.translation_bilingual);
            }

            var entry = profile.entries.FirstOrDefault(x =>
                string.Equals(x.section, source.section, StringComparison.Ordinal) &&
                string.Equals(x.key, source.key, StringComparison.Ordinal));

            // Some mods build their section string through constants/formatters at runtime. Offline
            // source analysis cannot always recover that exact string. A profile section of "*"
            // therefore means "this key in this plugin, regardless of section". It is only used
            // when the key is unique in the profile, and the source display-name guard below still
            // has to match. This keeps the fallback update-safe without touching ConfigDefinition.
            if (entry == null)
            {
                var wildcard = profile.entries.Where(x =>
                    string.Equals(x.section, "*", StringComparison.Ordinal) &&
                    string.Equals(x.key, source.key, StringComparison.Ordinal)).ToList();
                if (wildcard.Count == 1) entry = wildcard[0];
            }
            if (entry == null) return result.HasAny ? result : null;

            // Source guards make updates fail open. If an upstream mod changes wording, leave the
            // new source text visible rather than applying an old translation to a changed option.
            if (!string.IsNullOrEmpty(entry.source_display_name) &&
                !string.Equals(entry.source_display_name, source.source_display_name, StringComparison.Ordinal))
                return result.HasAny ? result : null;

            if (!string.IsNullOrEmpty(entry.translation_display_name))
                result.display_name = LocaleText.Select(source.source_display_name, entry.translation_display_name,
                    entry.translation_display_name_bilingual);

            if (!string.IsNullOrEmpty(entry.translation_description))
            {
                if (entry.source_description == null ||
                    string.Equals(entry.source_description, source.source_description, StringComparison.Ordinal))
                    result.description = LocaleText.Select(source.source_description, entry.translation_description,
                        entry.translation_description_bilingual);
            }

            return result.HasAny ? result : null;
        }

        internal string ResolveValue(ConfigUiSource source, object rawValue, string renderedSource)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.plugin_guid) || string.IsNullOrEmpty(renderedSource)) return null;
            if (!profiles.TryGetValue(source.plugin_guid, out var profile)) return null;

            var entry = profile.entries.FirstOrDefault(x =>
                string.Equals(x.section, source.section, StringComparison.Ordinal) &&
                string.Equals(x.key, source.key, StringComparison.Ordinal));
            if (entry == null)
            {
                var wildcard = profile.entries.Where(x =>
                    string.Equals(x.section, "*", StringComparison.Ordinal) &&
                    string.Equals(x.key, source.key, StringComparison.Ordinal)).ToList();
                if (wildcard.Count == 1) entry = wildcard[0];
            }
            if (entry == null || entry.values == null || entry.values.Count == 0) return null;
            if (!string.IsNullOrEmpty(entry.source_display_name) &&
                !string.Equals(entry.source_display_name, source.source_display_name, StringComparison.Ordinal)) return null;

            var rawText = rawValue?.ToString();
            var rule = entry.values.FirstOrDefault(x =>
                string.Equals(x.source, renderedSource, StringComparison.Ordinal));
            if (rule == null && !string.IsNullOrEmpty(rawText))
                rule = entry.values.FirstOrDefault(x => string.Equals(x.source, rawText, StringComparison.Ordinal));
            if (rule == null || string.IsNullOrEmpty(rule.translation)) return null;
            return LocaleText.Select(renderedSource, rule.translation, rule.translation_bilingual);
        }

        private static void Validate(ConfigUiProfile profile, string file)
        {
            if (profile == null || profile.schema_version != SchemaVersion || profile.target_spt != TargetSpt ||
                string.IsNullOrWhiteSpace(profile.plugin_guid))
                throw new InvalidDataException("Invalid profile header in " + file);

            profile.categories = profile.categories ?? new List<ConfigUiCategoryRule>();
            profile.entries = profile.entries ?? new List<ConfigUiEntryRule>();

            var categories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in profile.categories)
            {
                if (row == null || string.IsNullOrEmpty(row.source) || !categories.Add(row.source))
                    throw new InvalidDataException("Missing/duplicate category source");
            }

            var entries = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in profile.entries)
            {
                // BepInEx ConfigDefinition allows an empty Section string. PitFireTeam 0.10.3
                // uses ConfigDefinition("", key) for its kill-marker timing settings, and MunitionsExpert
                // uses ConfigDefinition(section, "") for its master toggle. Reject only null identities;
                // an empty Section or Key is a valid exact ConfigDefinition identity.
                if (row == null || row.section == null || row.key == null ||
                    !entries.Add(row.section + "\n" + row.key))
                    throw new InvalidDataException("Missing/duplicate section+key entry");
                row.values = row.values ?? new List<ConfigUiValueRule>();
            }
        }

        private static bool HasActiveEntryTranslation(ConfigUiEntryRule row)
        {
            if (row == null) return false;
            return !string.IsNullOrEmpty(row.translation_display_name) ||
                   !string.IsNullOrEmpty(row.translation_display_name_bilingual) ||
                   !string.IsNullOrEmpty(row.translation_description) ||
                   !string.IsNullOrEmpty(row.translation_description_bilingual) ||
                   (row.values != null && row.values.Any(value => value != null &&
                       (!string.IsNullOrEmpty(value.translation) || !string.IsNullOrEmpty(value.translation_bilingual))));
        }
    }

    internal sealed class ConfigUiProfile
    {
        public int schema_version { get; set; }
        public string target_spt { get; set; }
        public string plugin_guid { get; set; }
        public string plugin_name { get; set; }
        public List<ConfigUiCategoryRule> categories { get; set; } = new List<ConfigUiCategoryRule>();
        public List<ConfigUiEntryRule> entries { get; set; } = new List<ConfigUiEntryRule>();
    }

    internal sealed class ConfigUiCategoryRule
    {
        public string source { get; set; }
        public string translation { get; set; }
        public string translation_bilingual { get; set; }
    }

    internal sealed class ConfigUiEntryRule
    {
        public string section { get; set; }
        public string key { get; set; }
        public string source_display_name { get; set; }
        public string source_description { get; set; }
        public string translation_display_name { get; set; }
        public string translation_display_name_bilingual { get; set; }
        public string translation_description { get; set; }
        public string translation_description_bilingual { get; set; }
        // Optional display-only translations for enum/list choices. Underlying ConfigEntry values are never changed.
        public List<ConfigUiValueRule> values { get; set; } = new List<ConfigUiValueRule>();
    }

    internal sealed class ConfigUiValueRule
    {
        public string source { get; set; }
        public string translation { get; set; }
        public string translation_bilingual { get; set; }
    }

    internal sealed class ConfigUiSource
    {
        public string plugin_guid;
        public string section;
        public string key;
        public string source_category;
        public string source_display_name;
        public string source_description;
    }

    internal sealed class ConfigUiTranslation
    {
        public string category;
        public string display_name;
        public string description;
        internal bool HasAny => !string.IsNullOrEmpty(category) || !string.IsNullOrEmpty(display_name) || !string.IsNullOrEmpty(description);
    }
}
