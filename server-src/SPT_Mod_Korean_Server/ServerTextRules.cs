using System.Text.RegularExpressions;

namespace SPT_Mod_Korean_Server;

internal sealed class ServerTextRules
{
    private static readonly Regex Placeholder = new(@"\{([A-Za-z_][A-Za-z0-9_]*|[0-9]+)\}", RegexOptions.CultureInvariant);
    private readonly List<CompiledRule> rules = [];

    internal ServerTextRules(IEnumerable<TextRule> input)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in input ?? [])
        {
            if (!row.enabled || string.IsNullOrEmpty(row.translation)) continue;
            if (string.IsNullOrWhiteSpace(row.id) || !ids.Add(row.id))
                throw new InvalidDataException("Missing/duplicate rule id");
            if (string.IsNullOrEmpty(row.source) || !sources.Add(row.source))
                throw new InvalidDataException("Empty/duplicate source: " + row.source);

            var mode = row.match_mode ?? "exact";
            if (mode is not ("exact" or "template" or "segment" or "segment_template" or "regex_segment"))
                throw new InvalidDataException("Unsupported text match mode: " + mode);
            rules.Add(new CompiledRule(row, mode));
        }
    }

    internal bool CanTranslate(string text) => text is not null && rules.Any(rule => rule.Matches(text));

    internal string Translate(string text, string culture)
    {
        if (text is null) return text;
        if (CultureRegistry.Normalize(culture) == CultureRegistry.Source) return text;

        var current = text;
        var usedExclusiveGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (!string.IsNullOrEmpty(rule.ExclusiveGroup) && usedExclusiveGroups.Contains(rule.ExclusiveGroup)) continue;
            var translated = rule.TryTranslate(current, culture);
            if (translated is null) continue;
            current = translated;
            if (!string.IsNullOrEmpty(rule.ExclusiveGroup)) usedExclusiveGroups.Add(rule.ExclusiveGroup);
        }
        return current;
    }

    private sealed class CompiledRule
    {
        private readonly string source;
        private readonly string korean;
        private readonly string? bilingual;
        private readonly string mode;
        private readonly Regex? regex;
        private readonly Dictionary<string, string>? groups;
        internal string? ExclusiveGroup { get; }

        internal CompiledRule(TextRule row, string mode)
        {
            source = row.source!;
            korean = row.translation!;
            bilingual = row.translation_bilingual;
            this.mode = mode;
            ExclusiveGroup = row.exclusive_group;

            if (mode == "regex_segment")
            {
                regex = new Regex(source, RegexOptions.CultureInvariant | RegexOptions.Singleline);
                var groupNames = regex.GetGroupNames().Where(name => !int.TryParse(name, out _)).ToArray();
                groups = groupNames.ToDictionary(name => name, name => name, StringComparer.Ordinal);
                ValidateReplacementPlaceholders(source, korean, bilingual, new HashSet<string>(groupNames, StringComparer.Ordinal));
                return;
            }

            if (mode is not ("template" or "segment_template")) return;

            var sourceNames = Placeholder.Matches(source).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToArray();
            ValidateReplacementPlaceholders(source, korean, bilingual, new HashSet<string>(sourceNames, StringComparer.Ordinal));
            groups = sourceNames.Select((name, i) => new { name, group = "p" + i })
                .ToDictionary(x => x.name, x => x.group, StringComparer.Ordinal);
            var pattern = BuildPattern(source, groups);
            if (mode == "template") pattern = "\\A" + pattern + "\\z";
            regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }

        internal bool Matches(string input)
        {
            return mode switch
            {
                "exact" => input == source,
                "segment" => input.Contains(source, StringComparison.Ordinal),
                "template" or "segment_template" or "regex_segment" => regex!.IsMatch(input),
                _ => false
            };
        }

        internal string? TryTranslate(string input, string culture)
        {
            if (mode == "regex_segment")
            {
                var match = regex!.Match(input);
                if (!match.Success) return null;
                var renderedKorean = RenderReplacement(korean, match);
                var replacement = renderedKorean;
                return input[..match.Index] + replacement + input[(match.Index + match.Length)..];
            }

            var translation = Select(source, korean, bilingual, culture);
            if (translation == source) return Matches(input) ? input : null;

            switch (mode)
            {
                case "exact":
                    return input == source ? translation : null;
                case "segment":
                    return input.Contains(source, StringComparison.Ordinal) ? input.Replace(source, translation, StringComparison.Ordinal) : null;
                case "template":
                case "segment_template":
                    var match = regex!.Match(input);
                    if (!match.Success) return null;
                    var replacement = translation;
                    foreach (var pair in groups!)
                        replacement = replacement.Replace("{" + pair.Key + "}", match.Groups[pair.Value].Value, StringComparison.Ordinal);
                    if (mode == "template") return replacement;
                    return input[..match.Index] + replacement + input[(match.Index + match.Length)..];
                default:
                    return null;
            }
        }

        private string RenderReplacement(string replacement, Match match)
        {
            var result = replacement;
            foreach (var pair in groups!)
                result = result.Replace("{" + pair.Key + "}", match.Groups[pair.Value].Value, StringComparison.Ordinal);
            return result;
        }

        private static string Select(string source, string korean, string? bilingual, string culture)
        {
            var normalized = CultureRegistry.Normalize(culture);
            if (normalized == CultureRegistry.Korean || normalized == CultureRegistry.Bilingual)
                return string.IsNullOrEmpty(korean) ? source : korean;
            return source;
        }

        private static void ValidateReplacementPlaceholders(string source, string korean, string? bilingual, HashSet<string> expected)
        {
            var translatedNames = new HashSet<string>(Placeholder.Matches(korean).Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);
            if (!expected.SetEquals(translatedNames)) throw new InvalidDataException("Placeholder mismatch: " + source);
            if (!string.IsNullOrEmpty(bilingual))
            {
                var bilingualNames = new HashSet<string>(Placeholder.Matches(bilingual).Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);
                if (!expected.SetEquals(bilingualNames)) throw new InvalidDataException("Bilingual placeholder mismatch: " + source);
            }
        }

        private static string BuildPattern(string text, Dictionary<string, string> groups)
        {
            var result = string.Empty;
            var offset = 0;
            foreach (Match match in Placeholder.Matches(text))
            {
                result += Regex.Escape(text.Substring(offset, match.Index - offset));
                var name = match.Groups[1].Value;
                result += "(?<" + groups[name] + ">.+?)";
                offset = match.Index + match.Length;
            }
            result += Regex.Escape(text[offset..]);
            return result;
        }
    }
}
