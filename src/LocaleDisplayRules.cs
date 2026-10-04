using System;
using System.Collections.Generic;
using System.IO;
using SPT.EditableTranslations;

namespace SPT.ModKoreanAddon
{
    // Parent 2.1.1 / 4.1.5 payload patterns, NOT a general Korean-English formatter.
    internal sealed class LocaleDisplayRules
    {
        internal const string Default = "default";
        internal const string ItemName = "item_name";
        internal const string ItemDescription = "item_description";
        internal const string QuestTitle = "quest_title";
        internal const string QuestObjective = "quest_objective";
        internal const string QuestDescription = "quest_description";
        internal const string AchievementTitle = "achievement_title";
        internal const string AchievementDescription = "achievement_description";
        internal const string AchievementCondition = "achievement_condition";
        private readonly HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> nameSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> traders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal LocaleDisplayRules(IEnumerable<TextRule> rows)
        {
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.key)) continue;
                keys.Add(row.key);
                if (!nameSources.ContainsKey(row.key)) nameSources.Add(row.key, row.source);
                var split = row.key.LastIndexOf(' ');
                if (split < 1) continue;
                var field = row.key.Substring(split + 1);
                if (string.Equals(field, "FirstName", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(field, "LastName", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(field, "FullName", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(field, "Nickname", StringComparison.OrdinalIgnoreCase))
                    traders.Add(row.key.Substring(0, split));
            }
        }

        internal string Resolve(TextRule row)
        {
            if (row.display_type != null)
            {
                if (row.display_type != Default && row.display_type != ItemName &&
                    row.display_type != ItemDescription && row.display_type != QuestTitle && row.display_type != QuestObjective &&
                    row.display_type != AchievementTitle && row.display_type != AchievementCondition &&
                    row.display_type != QuestDescription && row.display_type != AchievementDescription)
                    throw new InvalidDataException("Invalid locale display_type: " + row.key);
                return row.display_type;
            }
            var key = row.key ?? "";
            var split = key.LastIndexOf(' ');
            if (split < 1) return Default;
            var id = key.Substring(0, split);
            if (traders.Contains(id)) return Default;
            var field = key.Substring(split + 1);
            // WTT has uppercase aliases for quest fields: inspect the group before item suffixes.
            var quest = keys.Contains(id + " startedMessageText") || keys.Contains(id + " successMessageText") ||
                keys.Contains(id + " acceptPlayerMessage") || keys.Contains(id + " completePlayerMessage");
            if (quest) return string.Equals(field, "name", StringComparison.OrdinalIgnoreCase) ? QuestTitle : Default;
            // Lowercase clothing/bare IDs need explicit audited display_type; not every name is a quest.
            if (field == "Name") return ItemName;
            if (field == "Description") return ItemDescription;
            return Default;
        }

        internal string NameKey(TextRule row)
        {
            if (!string.IsNullOrWhiteSpace(row.name_key)) return row.name_key;
            var split = (row.key ?? "").LastIndexOf(' ');
            if (split < 1) return null;
            var id = row.key.Substring(0, split);
            // Some clothing profiles use the bare item id as the name key.
            if (keys.Contains(id) && !keys.Contains(id + " Name")) return id;
            return id + " Name";
        }

        internal string NameSource(TextRule row)
        {
            var key = NameKey(row);
            return key != null && nameSources.TryGetValue(key, out var source) ? source : null;
        }

        internal static string Select(string source, string korean, string bilingualOverride,
            string displayType, string modName, string culture, string itemNameSource = null)
        {
            if (displayType == Default) return LocaleText.Select(source, korean, bilingualOverride, culture);
            if (!LocaleMode.IsKoreanCulture(culture) || string.IsNullOrWhiteSpace(korean)) return source;
            if (displayType == ItemDescription || displayType == QuestDescription || displayType == AchievementDescription)
            {
                // Parent item descriptions have [English NAME], not English DESCRIPTION,
                // in both cultures. Mod credit is an independent addon feature in both.
                var body = korean;
                if (displayType == ItemDescription && !string.IsNullOrWhiteSpace(itemNameSource))
                {
                    var header = "[" + itemNameSource.Trim() + "]\n";
                    if (!body.StartsWith(header, StringComparison.Ordinal)) body = header + body;
                }
                if (!string.IsNullOrWhiteSpace(modName))
                {
                    body = body.TrimEnd();
                    var credit = "\n(" + modName.Trim() + ")";
                    if (!body.EndsWith(credit, StringComparison.Ordinal)) body += credit;
                }
                return body;
            }
            // Parent objectives use newline originals in both Korean cultures. Only audited
            // objective IDs receive this explicit type; arbitrary bare IDs remain unchanged.
            if (displayType != QuestTitle && displayType != QuestObjective && !string.Equals(culture, LocaleMode.Bilingual, StringComparison.OrdinalIgnoreCase)) return korean;
            if (string.IsNullOrWhiteSpace(source) || korean.Trim() == source.Trim()) return korean;
            var separator = displayType == QuestTitle || displayType == AchievementTitle ? " " : "\n";
            var suffix = separator + "(" + source.Trim() + ")";
            return korean.TrimEnd().EndsWith(suffix, StringComparison.Ordinal) ? korean : korean.TrimEnd() + suffix;
        }
    }
}
