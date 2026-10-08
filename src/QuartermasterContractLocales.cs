using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SPT.ModKoreanAddon
{
    // A versioned public-contract snapshot. Only game-visible text is translated;
    // no contract ID, reward, objective condition, server JSON or game logic is changed.
    internal sealed class QuartermasterContractLocales
    {
        private sealed class Phrase
        {
            internal string English, Korean;
        }
        private sealed class Contract
        {
            internal string Id;
            internal Dictionary<string, Phrase> Fields = new Dictionary<string, Phrase>(StringComparer.Ordinal);
            internal Dictionary<int, Phrase> Objectives = new Dictionary<int, Phrase>();
            internal HashSet<string> KnownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, Contract> titles = new Dictionary<string, Contract>(StringComparer.Ordinal);
        private readonly Dictionary<string, Contract> knownIds = new Dictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> uiPhrases = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, string>> uiLongestFirst = new List<KeyValuePair<string, string>>();
        private static readonly string[] OriginalPrefixes = { "[ONE TIME] ", "[WEEKLY] ", "[WEEKEND] ", "[DAILY] " };
        private static readonly string[] KoreanPrefixes = { "[1회] ", "[주간] ", "[주말] ", "[일일] " };
        private static readonly Regex QuestTitleKey = new Regex(@"\A(?<id>[a-fA-F0-9]{24}) name\z", RegexOptions.CultureInvariant);
        private static readonly Regex ExpiryPart = new Regex(@"\r?\n\r?\n\[QM_EXPIRY:(?<iso>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z)\]\r?\nExpires in [^\r\n]*", RegexOptions.CultureInvariant);
        private static readonly Regex Countdown = new Regex(@"Expires in (?:(?<h>\d+)h\s*)?(?:(?<m>\d+)m\s*)?(?:(?<s>\d+)s)?", RegexOptions.CultureInvariant);
        private static readonly Regex CountdownOnly = new Regex(@"(?m)^Expired(?=\s*$)", RegexOptions.CultureInvariant);

        internal int Count => titles.Count;
        internal QuartermasterContractLocales(string addonRoot)
        {
            var file = Path.Combine(addonRoot, "contract-locales", "TheQuartermaster.v25531.json");
            if (!File.Exists(file))
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("quartermaster-catalog-missing", () => "Quartermaster online catalog missing; dynamic quest translation disabled.");
                return;
            }
            try
            {
                var root = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                if ((int?)root["schema_version"] != 1) throw new InvalidDataException("Unsupported catalog schema");
                foreach (var c in (JArray)root["contracts"])
                {
                    var entry = new Contract { Id = (string)c["contract_id"] };
                    var fields = (JObject)c["fields"];
                    foreach (var field in fields.Properties())
                    {
                        var phrase = ReadPhrase(field.Value);
                        if (phrase == null) continue;
                        entry.Fields.Add(field.Name, phrase);
                        AddUiPhrase(phrase);
                    }
                    if (!entry.Fields.TryGetValue("title", out var title)) continue;
                    if (titles.ContainsKey(title.English)) throw new InvalidDataException("Duplicate contract title: " + title.English);
                    titles.Add(title.English, entry);
                    foreach (var objective in (JArray)c["objectives"])
                    {
                        var phrase = ReadPhrase(objective);
                        var index = (int?)objective["index"];
                        if (phrase != null && index.HasValue)
                        {
                            entry.Objectives.Add(index.Value, phrase);
                            AddUiPhrase(phrase);
                        }
                    }
                    foreach (var id in (JArray)c["known_quest_ids"])
                    {
                        var questId = (string)id;
                        if (questId != null && QuestTitleKey.IsMatch(questId + " name"))
                        {
                            entry.KnownIds.Add(questId);
                            knownIds[questId] = entry;
                        }
                    }
                }
                foreach (var tier in (JArray)root["reward_tiers"])
                {
                    AddUiPhrase(ReadPhrase(tier["name"]));
                    AddUiPhrase(ReadPhrase(tier["description"]));
                }
                AddUiPhrase(ReadPhrase(root["weekly_reward_message"]));
                uiLongestFirst.AddRange(uiPhrases.OrderByDescending(x => x.Key.Length));
            }
            catch (Exception ex)
            {
                titles.Clear(); knownIds.Clear(); uiPhrases.Clear(); uiLongestFirst.Clear();
                SPT.EditableTranslations.MinimalLog.WarnOnce("quartermaster-catalog-invalid", () => "Quartermaster online catalog rejected: " + ex.Message);
            }
        }

        private static Phrase ReadPhrase(JToken value)
        {
            var source = (string)value?["source"];
            var translated = (string)value?["translation"];
            return string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translated) ? null :
                new Phrase { English = source, Korean = translated };
        }
        private void AddUiPhrase(Phrase phrase)
        {
            if (phrase != null && !uiPhrases.ContainsKey(phrase.English)) uiPhrases.Add(phrase.English, phrase.Korean);
        }

        // Only exact English source text is replaced. Dynamic quest IDs are matched by the
        // corresponding quest title in the same locale dictionary; ID guesses are not used.
        internal int Apply(string culture, IDictionary<string, string> raw, Dictionary<string, string> result)
        {
            if (!LocaleMode.IsKoreanCulture(culture) || titles.Count == 0) return 0;
            var matches = new Dictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in raw)
            {
                var match = QuestTitleKey.Match(pair.Key);
                if (!match.Success) continue;
                if (!TryResolveTitle(pair.Value, out var contract, out var koreanTitle)) continue;
                var id = match.Groups["id"].Value;
                // Refuse to overwrite an existing parent's translation.
                if (result.TryGetValue(pair.Key, out var current) && current == pair.Value)
                    result[pair.Key] = LocaleDisplayRules.Select(pair.Value, koreanTitle, null,
                        LocaleDisplayRules.QuestTitle, "The Quartermaster", culture);
                matches[id] = contract;
            }
            var count = 0;
            foreach (var pair in matches)
            {
                var id = pair.Key;
                var c = pair.Value;
                count++;
                var rawTitle = raw[id + " name"];
                string translatedTitle = result.TryGetValue(id + " name", out var currentTitle) ? currentTitle : rawTitle;
                if (c.Fields.TryGetValue("description", out var description))
                    ApplyDescription(id + " description", description, culture, raw, result);

                var started = GetMessage(c, "started_message", "Contract accepted: " + rawTitle, "계약 수락: " + translatedTitle);
                var success = GetMessage(c, "success_message", "Contract complete: " + rawTitle, "계약 완료: " + translatedTitle);
                var fail = GetMessage(c, "fail_message", "", "");
                ApplyField(id + " startedMessageText", started, raw, result);
                ApplyField(id + " acceptPlayerMessage", started, raw, result);
                ApplyField(id + " successMessageText", success, raw, result);
                ApplyField(id + " completePlayerMessage", success, raw, result);
                ApplyField(id + " failMessageText", fail, raw, result);
                foreach (var obj in c.Objectives)
                {
                    var key = DeriveObjectiveKey(id, obj.Key);
                    ApplyObjective(key, obj.Value, culture, raw, result);
                }
            }
            return count;
        }

        private static Phrase GetMessage(Contract c, string name, string englishDefault, string koreanDefault)
        {
            return c.Fields.TryGetValue(name, out var p) ? p :
                new Phrase { English = englishDefault, Korean = koreanDefault };
        }
        private bool TryResolveTitle(string rawTitle, out Contract c, out string korean)
        {
            c = null; korean = null;
            if (string.IsNullOrWhiteSpace(rawTitle)) return false;
            for (int i = 0; i < OriginalPrefixes.Length; ++i)
            {
                if (rawTitle.StartsWith(OriginalPrefixes[i], StringComparison.Ordinal))
                {
                    if (!titles.TryGetValue(rawTitle.Substring(OriginalPrefixes[i].Length), out c)) return false;
                    korean = KoreanPrefixes[i] + c.Fields["title"].Korean;
                    return true;
                }
            }
            if (!titles.TryGetValue(rawTitle, out c)) return false;
            korean = c.Fields["title"].Korean;
            return true;
        }
        private static void ApplyField(string key, Phrase p, IDictionary<string,string> raw, Dictionary<string,string> result)
        {
            if (p == null || !raw.TryGetValue(key, out var incoming) || !result.TryGetValue(key, out var current)) return;
            if (string.Equals(incoming, p.English, StringComparison.Ordinal) && string.Equals(current, incoming, StringComparison.Ordinal))
                result[key] = p.Korean;
        }
        private static void ApplyDescription(string key, Phrase p, string culture,
            IDictionary<string,string> raw, Dictionary<string,string> result)
        {
            if (!raw.TryGetValue(key, out var english) || !result.TryGetValue(key, out var current) || current != english) return;
            if (english == p.English)
            {
                result[key] = LocaleDisplayRules.Select(p.English, p.Korean, null,
                    LocaleDisplayRules.QuestDescription, "The Quartermaster", culture);
                return;
            }
            // Keep QM_EXPIRY marker and English countdown source intact: upstream
            // LiveCountdownBehaviour requires both before it can initialize.
            var match = ExpiryPart.Match(english);
            if (match.Success && match.Index == p.English.Length && english.Substring(0, match.Index) == p.English)
            {
                var translated = LocaleDisplayRules.Select(p.English, p.Korean, null,
                    LocaleDisplayRules.QuestDescription, "The Quartermaster", culture);
                result[key] = translated + match.Value;
            }
        }
        private static void ApplyObjective(string key, Phrase p, string culture,
            IDictionary<string,string> raw, Dictionary<string,string> result)
        {
            if (raw.TryGetValue(key, out var incoming) && result.TryGetValue(key, out var current) && incoming == current && incoming == p.English)
                result[key] = LocaleDisplayRules.Select(p.English, p.Korean, null,
                    LocaleDisplayRules.QuestObjective, "The Quartermaster", culture);
        }
        private static string DeriveObjectiveKey(string questId, int index)
        {
            var seed = questId + ":obj" + index + ":cond";
            using (var md5 = MD5.Create())
            {
                var digest = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
                var sb = new StringBuilder(24);
                for (var i = 0; i < 12; i++) sb.Append(digest[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // Used only after TheQuartermaster.Client's LiveCountdownBehaviour writes its
        // English presentation. Never modify the [QM_EXPIRY:ISO] token.
        internal static string TranslateCountdown(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var value = Countdown.Replace(input, m =>
            {
                var s = "만료까지 ";
                if (m.Groups["h"].Success) s += m.Groups["h"].Value + "시간 ";
                if (m.Groups["m"].Success) s += m.Groups["m"].Value + "분 ";
                if (m.Groups["s"].Success) s += m.Groups["s"].Value + "초";
                return s.TrimEnd();
            });
            return CountdownOnly.Replace(value, "만료됨");
        }
        internal string TranslateKnownDescription(string text, string culture)
        {
            if (!LocaleMode.IsKoreanCulture(culture) || string.IsNullOrEmpty(text)) return text;
            foreach (var c in titles.Values)
            {
                if (!c.Fields.TryGetValue("description", out var p)) continue;
                if (text == p.English ||
                    text.StartsWith(p.English + "\n\n[QM_EXPIRY:", StringComparison.Ordinal) ||
                    text == p.English + "\n\nExpired")
                {
                    var suffix = text.Substring(p.English.Length);
                    return LocaleDisplayRules.Select(p.English, p.Korean, null, LocaleDisplayRules.QuestDescription,
                        "The Quartermaster", culture) + suffix;
                }
            }
            return text;
        }
        internal string TranslateCommunityText(string english)
        {
            if (string.IsNullOrWhiteSpace(english)) return english;
            // Exact long free-text fields plus title inside formatted author/status labels.
            if (uiPhrases.TryGetValue(english, out var translated)) return translated;
            var result = english;
            foreach (var row in uiLongestFirst)
            {
                if (row.Key.Length < 4) continue;
                result = result.Replace(row.Key, row.Value);
            }
            // CommunityPanel composes these labels from network values at runtime,
            // so they cannot be reached by transpiling its hard-coded IL strings.
            foreach (var pair in new[] {
                new[] { "Recurrence: ", "반복 주기: " }, new[] { "Selected: ", "선택: " },
                new[] { "Support: ", "지지율: " }, new[] { "  by ", "  작성자: " },
                new[] { "  Up: ", "  찬성: " }, new[] { "  Down: ", "  반대: " },
                new[] { "Objectives", "목표" }, new[] { "Rewards", "보상" },
                new[] { "Found in Raid", "레이드에서 발견" },
                new[] { "trader standing", "상인 평판" }
            }) result = result.Replace(pair[0], pair[1]);
            for (int i = 0; i < OriginalPrefixes.Length; ++i)
                result = result.Replace(OriginalPrefixes[i], KoreanPrefixes[i]);
            return TranslateCommunityObjectiveTemplate(result);
        }

        private static readonly Dictionary<string, string> LocationNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Customs", "세관" }, { "Factory (Day)", "팩토리(주간)" },
                { "Factory (Night)", "팩토리(야간)" }, { "Woods", "우드" },
                { "Shoreline", "해안선" }, { "Interchange", "인터체인지" },
                { "Lighthouse", "등대" }, { "Reserve", "리저브" },
                { "The Lab", "연구소" }, { "Streets of Tarkov", "타르코프 시내" },
                { "Ground Zero", "그라운드 제로" }, { "Tarkov", "타르코프" }
            };
        private static string Location(string value)
        {
            return LocationNames.TryGetValue(value, out var kor) ? kor : value;
        }
        internal static string TranslateCommunityObjectiveTemplate(string text)
        {
            // Anchor every pattern: only render-time known template shapes are changed.
            if (string.IsNullOrWhiteSpace(text)) return text;
            var match = Regex.Match(text, @"\AEliminate (?<count>\d+) (?<target>Scavs?|PMCs?)(?: on (?<map>.+))?\z", RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var target = match.Groups["target"].Value.StartsWith("PMC", StringComparison.Ordinal) ? "PMC" : "스캐브";
                var location = match.Groups["map"].Success ? Location(match.Groups["map"].Value) + "에서 " : "";
                return location + target + " " + match.Groups["count"].Value + "명 처치";
            }
            match = Regex.Match(text, @"\AEliminate (?<count>\d+) (?<target>.+?)(?: on (?<map>Customs|Woods|Shoreline|Interchange|Lighthouse|Reserve|The Lab|Streets of Tarkov|Ground Zero))?\z", RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var location = match.Groups["map"].Success ? Location(match.Groups["map"].Value) + "에서 " : "";
                return location + match.Groups["target"].Value + " " + match.Groups["count"].Value + "회 처치";
            }
            match = Regex.Match(text, @"\A(?<action>Hand over|Find|Plant) (?<count>\d+)x (?<item>.+)\z", RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var action = match.Groups["action"].Value == "Find" ? "찾기" :
                    match.Groups["action"].Value == "Plant" ? "설치" : "건네주기";
                return match.Groups["item"].Value + " " + match.Groups["count"].Value + "개 " + action;
            }
            match = Regex.Match(text, @"\ASurvive (?<count>\d+) raids? on (?<map>.+)\z", RegexOptions.CultureInvariant);
            if (match.Success) return Location(match.Groups["map"].Value) + "에서 " + match.Groups["count"].Value + "회 생존";
            match = Regex.Match(text, @"\AExtract from (?<map>.+) (?<count>\d+) times?\z", RegexOptions.CultureInvariant);
            if (match.Success) return Location(match.Groups["map"].Value) + "에서 " + match.Groups["count"].Value + "회 탈출";
            return text;
        }
    }
}
