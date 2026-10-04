using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SPT.ModKoreanAddon;

namespace SPT.EditableTranslations
{
    internal sealed class ModTexts
    {
        public int schema_version { get; set; }
        public string target_spt { get; set; }
        public string mod_id { get; set; }
        public string mod_name { get; set; }
        public Dictionary<string, List<TextRule>> channels { get; set; } = new Dictionary<string, List<TextRule>>();
    }

    internal sealed class TextRule
    {
        public string id { get; set; }
        public string key { get; set; }
        public string source { get; set; }
        public string translation { get; set; }
        public string translation_bilingual { get; set; }
        public string display_type { get; set; }
        public string name_key { get; set; }
        public string match_mode { get; set; }
        public string exclusive_group { get; set; }
        public bool enabled { get; set; } = true;
        public string trader_id { get; set; }
        public string note { get; set; }
        public string file { get; set; }
        public string path { get; set; }
        // locale_clone_suffix: source/translation are the appended name suffix, while parent_key
        // identifies the vanilla template whose localized Name/ShortName/Description are inherited.
        public string parent_key { get; set; }
        public string description_source { get; set; }
        public string description_translation { get; set; }
        public string reason { get; set; }
    }

    internal sealed class TextRules
    {
        private const int TranslationCacheLimit = 8192;
        private const int CanTranslateCacheLimit = 4096;

        private static readonly Regex Placeholder = new Regex(@"\{([A-Za-z_][A-Za-z0-9_]*|[0-9]+)\}", RegexOptions.CultureInvariant);

        // Exact rules are the overwhelmingly common case (SAIN: 1195/1204). Keep them out of the
        // sequential scan while preserving the original rule ordering through the stored index.
        private readonly Dictionary<string, IndexedRule> exactRules = new Dictionary<string, IndexedRule>(StringComparer.Ordinal);
        private readonly List<IndexedRule> dynamicRules = new List<IndexedRule>();
        private readonly Dictionary<string, string> reverseExact = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> ambiguousReverseExact = new HashSet<string>(StringComparer.Ordinal);

        // Translation is pure for a fixed rule set + culture, so repeated IMGUI strings can be cached.
        // SAIN redraws the same labels many times per second; this keeps the hot path at dictionary lookup cost.
        private readonly object cacheGate = new object();
        private readonly Dictionary<TranslationCacheKey, string> translationCache = new Dictionary<TranslationCacheKey, string>();
        private readonly Dictionary<string, bool> canTranslateCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        internal TextRules(IEnumerable<TextRule> input)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var row in input ?? Enumerable.Empty<TextRule>())
            {
                if (!row.enabled || string.IsNullOrEmpty(row.translation)) continue;
                if (string.IsNullOrWhiteSpace(row.id) || !ids.Add(row.id)) throw new InvalidOperationException("Missing/duplicate rule id");
                if (string.IsNullOrEmpty(row.source) || !sources.Add(row.source)) throw new InvalidOperationException("Empty/duplicate source: " + row.source);
                var mode = row.match_mode ?? "exact";
                if (mode != "exact" && mode != "template" && mode != "segment" && mode != "segment_template" && mode != "regex_segment")
                    throw new InvalidOperationException("Unsupported text match mode: " + mode);

                var entry = new IndexedRule(index++, new CompiledRule(row, mode, Placeholder));
                if (mode == "exact")
                {
                    exactRules.Add(row.source, entry);
                    AddReverse(row.translation, row.source);
                    AddReverse(LocaleText.Select(row.source, row.translation, row.translation_bilingual, LocaleMode.Bilingual), row.source);
                }
                else
                {
                    dynamicRules.Add(entry);
                }
            }
        }

        internal bool CanTranslate(string text)
        {
            if (text == null) return false;

            lock (cacheGate)
            {
                if (canTranslateCache.TryGetValue(text, out var cached)) return cached;
            }

            var result = exactRules.ContainsKey(text);
            if (!result)
            {
                for (var i = 0; i < dynamicRules.Count; i++)
                {
                    if (!dynamicRules[i].Rule.Matches(text)) continue;
                    result = true;
                    break;
                }
            }

            lock (cacheGate)
            {
                if (canTranslateCache.Count >= CanTranslateCacheLimit) canTranslateCache.Clear();
                canTranslateCache[text] = result;
            }
            return result;
        }

        internal string Translate(string text, string culture)
        {
            if (text == null) return null;

            // All rule implementations return the input unchanged for non-Korean cultures. Avoid every
            // lookup/allocation in that mode instead of walking rules that cannot change the result.
            if (!LocaleMode.IsKoreanCulture(culture)) return text;

            var cultureKind = string.Equals(culture, LocaleMode.Bilingual, StringComparison.OrdinalIgnoreCase) ? (byte)2 : (byte)1;
            var key = new TranslationCacheKey(text, cultureKind);
            lock (cacheGate)
            {
                if (translationCache.TryGetValue(key, out var cached)) return cached;
            }

            var translated = TranslateUncached(text, culture);

            lock (cacheGate)
            {
                if (translationCache.Count >= TranslationCacheLimit) translationCache.Clear();
                translationCache[key] = translated;
            }
            return translated;
        }

        private string TranslateUncached(string text, string culture)
        {
            var current = text;
            var nextRuleIndex = 0;
            HashSet<string> usedExclusiveGroups = null;

            // This reproduces the old foreach(rules) semantics without scanning every exact rule.
            // At each step we select the earliest rule (by original index) that can match the current
            // string, apply it once, then continue strictly after that index.
            while (true)
            {
                IndexedRule exact = null;
                if (exactRules.TryGetValue(current, out var exactCandidate) &&
                    exactCandidate.Index >= nextRuleIndex &&
                    !IsExclusiveGroupUsed(exactCandidate.Rule.ExclusiveGroup, usedExclusiveGroups))
                {
                    exact = exactCandidate;
                }

                IndexedRule dynamic = null;
                for (var i = 0; i < dynamicRules.Count; i++)
                {
                    var candidate = dynamicRules[i];
                    if (candidate.Index < nextRuleIndex) continue;
                    if (exact != null && candidate.Index > exact.Index) break;
                    if (IsExclusiveGroupUsed(candidate.Rule.ExclusiveGroup, usedExclusiveGroups)) continue;
                    if (!candidate.Rule.Matches(current)) continue;
                    dynamic = candidate;
                    break;
                }

                IndexedRule selected;
                if (exact == null) selected = dynamic;
                else if (dynamic == null) selected = exact;
                else selected = dynamic.Index < exact.Index ? dynamic : exact;

                if (selected == null) break;

                var translated = selected.Rule.TryTranslate(current, culture);
                nextRuleIndex = selected.Index + 1;
                if (translated == null) continue;

                current = translated;
                var group = selected.Rule.ExclusiveGroup;
                if (!string.IsNullOrEmpty(group))
                {
                    if (usedExclusiveGroups == null) usedExclusiveGroups = new HashSet<string>(StringComparer.Ordinal);
                    usedExclusiveGroups.Add(group);
                }
            }

            return current;
        }

        private static bool IsExclusiveGroupUsed(string group, HashSet<string> used)
        {
            return !string.IsNullOrEmpty(group) && used != null && used.Contains(group);
        }

        private void AddReverse(string translated, string source)
        {
            if (string.IsNullOrEmpty(translated) || string.Equals(source, translated, StringComparison.Ordinal)) return;
            if (reverseExact.TryGetValue(translated, out var existing) &&
                !string.Equals(existing, source, StringComparison.Ordinal))
            {
                reverseExact.Remove(translated);
                ambiguousReverseExact.Add(translated);
            }
            else if (!ambiguousReverseExact.Contains(translated))
            {
                reverseExact[translated] = source;
            }
        }

        internal string RestoreExact(string text)
        {
            if (text == null) return null;
            return reverseExact.TryGetValue(text, out var source) ? source : text;
        }

        private sealed class IndexedRule
        {
            internal readonly int Index;
            internal readonly CompiledRule Rule;

            internal IndexedRule(int index, CompiledRule rule)
            {
                Index = index;
                Rule = rule;
            }
        }

        private struct TranslationCacheKey : IEquatable<TranslationCacheKey>
        {
            private readonly string text;
            private readonly byte cultureKind;

            internal TranslationCacheKey(string text, byte cultureKind)
            {
                this.text = text;
                this.cultureKind = cultureKind;
            }

            public bool Equals(TranslationCacheKey other)
            {
                return cultureKind == other.cultureKind && string.Equals(text, other.text, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is TranslationCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((text != null ? StringComparer.Ordinal.GetHashCode(text) : 0) * 397) ^ cultureKind;
                }
            }
        }

        private sealed class CompiledRule
        {
            private readonly string source;
            private readonly string korean;
            private readonly string bilingual;
            private readonly string mode;
            private readonly Regex regex;
            private readonly Dictionary<string, string> groups;
            internal string ExclusiveGroup { get; }

            internal CompiledRule(TextRule row, string mode, Regex placeholder)
            {
                source = row.source;
                korean = row.translation;
                bilingual = row.translation_bilingual;
                this.mode = mode;
                ExclusiveGroup = row.exclusive_group;

                if (mode == "regex_segment")
                {
                    regex = new Regex(source, RegexOptions.CultureInvariant | RegexOptions.Singleline);
                    var groupNames = regex.GetGroupNames().Where(name => !int.TryParse(name, out _)).ToArray();
                    groups = groupNames.ToDictionary(name => name, name => name, StringComparer.Ordinal);
                    ValidateReplacementPlaceholders(source, korean, bilingual, placeholder, new HashSet<string>(groupNames, StringComparer.Ordinal));
                    return;
                }

                if (mode != "template" && mode != "segment_template") return;

                var sourceNames = placeholder.Matches(source).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToArray();
                ValidateReplacementPlaceholders(source, korean, bilingual, placeholder, new HashSet<string>(sourceNames, StringComparer.Ordinal));

                groups = sourceNames.Select((name, i) => new { name, group = "p" + i })
                    .ToDictionary(x => x.name, x => x.group, StringComparer.Ordinal);

                var pattern = BuildPattern(source, placeholder, groups);
                if (mode == "template") pattern = "\\A" + pattern + "\\z";
                regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline);
            }

            internal bool Matches(string input)
            {
                if (input == null) return false;
                switch (mode)
                {
                    case "exact": return input == source;
                    case "segment": return input.Contains(source);
                    case "template":
                    case "segment_template":
                    case "regex_segment": return regex.IsMatch(input);
                    default: return false;
                }
            }

            internal string TryTranslate(string input, string culture)
            {
                if (mode == "regex_segment")
                {
                    var regexMatch = regex.Match(input);
                    if (!regexMatch.Success) return null;
                    if (!LocaleMode.IsKoreanCulture(culture)) return input;

                    var renderedKorean = RenderReplacement(korean, regexMatch);
                    var replacement = renderedKorean;

                    return input.Substring(0, regexMatch.Index) + replacement + input.Substring(regexMatch.Index + regexMatch.Length);
                }

                var translation = LocaleText.Select(source, korean, bilingual, culture);
                if (string.Equals(translation, source, StringComparison.Ordinal)) return Matches(input) ? input : null;
                switch (mode)
                {
                    case "exact": return input == source ? translation : null;
                    case "segment": return input.Contains(source) ? input.Replace(source, translation) : null;
                    case "template":
                    case "segment_template":
                        var match = regex.Match(input);
                        if (!match.Success) return null;
                        var replacement = translation;
                        foreach (var pair in groups)
                            replacement = replacement.Replace("{" + pair.Key + "}", match.Groups[pair.Value].Value);
                        if (mode == "template") return replacement;
                        return input.Substring(0, match.Index) + replacement + input.Substring(match.Index + match.Length);
                    default: return null;
                }
            }

            private string RenderReplacement(string replacement, Match match)
            {
                var result = replacement;
                foreach (var pair in groups)
                    result = result.Replace("{" + pair.Key + "}", match.Groups[pair.Value].Value);
                return result;
            }

            private static void ValidateReplacementPlaceholders(string source, string korean, string bilingual, Regex placeholder, HashSet<string> expected)
            {
                var translatedNames = new HashSet<string>(placeholder.Matches(korean).Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);
                if (!expected.SetEquals(translatedNames)) throw new InvalidOperationException("Placeholder mismatch: " + source);
                if (!string.IsNullOrEmpty(bilingual))
                {
                    var bilingualNames = new HashSet<string>(placeholder.Matches(bilingual).Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);
                    if (!expected.SetEquals(bilingualNames)) throw new InvalidOperationException("Bilingual placeholder mismatch: " + source);
                }
            }

            private static string BuildPattern(string text, Regex placeholder, Dictionary<string, string> groups)
            {
                var result = "";
                var offset = 0;
                foreach (Match match in placeholder.Matches(text))
                {
                    result += Regex.Escape(text.Substring(offset, match.Index - offset));
                    var name = match.Groups[1].Value;
                    result += "(?<" + groups[name] + ">.+?)";
                    offset = match.Index + match.Length;
                }
                result += Regex.Escape(text.Substring(offset));
                return result;
            }
        }
    }
}
