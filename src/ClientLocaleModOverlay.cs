#define LOCALE_DIAGNOSTICS
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SPT.ModKoreanAddon
{
    // Local extension of SPT Korean Project. Base payload validation remains unchanged.
    internal sealed class ClientLocaleModOverlay
    {

        // A key may have case aliases or source variants in different mod profiles.
        // Keep candidates; a collision must not discard every unrelated row in that profile.
        private readonly Dictionary<string, List<JObject>> entries = new Dictionary<string, List<JObject>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JObject> additions = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        // Some server mods create localized clone items by appending their own suffix/description to a
        // vanilla parent at runtime. Those rows cannot use the ordinary source guard because the parent
        // part is already localized before this client overlay sees it. Keep the audited clone relation
        // and only translate when the runtime child is exactly parent + the configured source fragment.
        private readonly Dictionary<string, JObject> cloneSuffixes = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        // Exact source-value fallbacks are for mod-added locale rows whose runtime key is not stable/known.
        // They only apply when the post-SPT-KR value is still exactly the English source, so existing
        // SPT-KR translations always win and are never overwritten.
        private readonly Dictionary<string, JObject> sourceFallbacks = new Dictionary<string, JObject>(StringComparer.Ordinal);
#if LOCALE_DIAGNOSTICS
        private readonly string reportRoot;
        internal static Action<string,JObject> Diagnostic;
        private readonly Dictionary<string, string> reportSnapshots =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
#endif
        private readonly QuartermasterContractLocales quartermasterContracts;
        private string loadError;
        private static readonly Regex Price = new Regex(@"\A(?:(?:Per Slot:|Total:) [^\r\n]+\s*\n)+(?:\s*<color=#[0-9a-fA-F]+>(?:Not Flea Banned|Flea Banned)</color>\s*\n)?\s*\n", RegexOptions.CultureInvariant);
        private static readonly Regex Ammo = new Regex(@"\s*<color=#808080>\[\d+(?:\.\d+)?/\d+(?:\.\d+)?\]</color>\z", RegexOptions.CultureInvariant);
        private static readonly Regex ShortAmmo = new Regex(@"\A<sup><color=#808080>\[\d+(?:\.\d+)?/\d+(?:\.\d+)?\]</color></sup>\s*", RegexOptions.CultureInvariant);

        internal ClientLocaleModOverlay(string root, string version, EditableProfiles shared = null)
        {
#if LOCALE_DIAGNOSTICS
            reportRoot = Path.Combine(root, "mod-locales", "reports");
#endif
            var profiles = shared ?? new EditableProfiles(root);
            quartermasterContracts = new QuartermasterContractLocales(root);
            foreach (var document in profiles.Documents)
            {
                if (document.channels.TryGetValue("locale_source_fallback", out var sourceFallbackRows))
                {
                    try
                    {
                        foreach (var r in sourceFallbackRows)
                        {
                            if (!r.enabled || string.IsNullOrEmpty(r.translation)) continue;
                            if (string.IsNullOrWhiteSpace(r.source) || sourceFallbacks.ContainsKey(r.source))
                                throw new InvalidDataException("Invalid/duplicate locale source fallback: " + r.source);
                            if (!string.IsNullOrWhiteSpace(r.match_mode) && r.match_mode != "exact")
                                throw new InvalidDataException("Locale source fallback must use exact match mode");
                            sourceFallbacks.Add(r.source, new JObject
                            {
                                ["source"] = r.source,
                                ["translation"] = r.translation,
                                ["translation_bilingual"] = r.translation_bilingual == null ? JValue.CreateNull() : new JValue(r.translation_bilingual),
                                ["mod_name"] = document.mod_name
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        loadError = document.mod_id + ": " + ex.Message;
                        SPT.EditableTranslations.MinimalLog.WarnOnce("locale-fallback-rejected:" + document.mod_id, () => "[Editable JSON] Locale source fallbacks rejected: " + loadError);
                    }
                }

                if (document.channels.TryGetValue("locale_additions", out var additionRows))
                {
                    foreach (var r in additionRows)
                    {
                        try
                        {
                            if (!r.enabled || string.IsNullOrEmpty(r.translation)) continue;
                            if (string.IsNullOrWhiteSpace(r.key) || string.IsNullOrWhiteSpace(r.source))
                                throw new InvalidDataException("Invalid locale addition key/source: " + r.key);
                            if (additions.TryGetValue(r.key, out var existing))
                            {
                                // Merged and legacy files may contain the same UI key.
                                // Only identical behavior is safe to coalesce; retain real conflicts.
                                if ((string)existing["source"] == r.source &&
                                    (string)existing["translation"] == r.translation &&
                                    (string)existing["translation_bilingual"] == r.translation_bilingual) continue;
                                throw new InvalidDataException("Conflicting locale addition key: " + r.key);
                            }
                            additions.Add(r.key, new JObject
                            {
                                ["key"] = r.key, ["source"] = r.source, ["translation"] = r.translation,
                                ["translation_bilingual"] = r.translation_bilingual == null ? JValue.CreateNull() : new JValue(r.translation_bilingual),
                                ["mod_name"] = document.mod_name
                            });
                        }
                        catch (Exception ex)
                        {
                            var error = document.mod_id + ": " + ex.Message;
                            loadError = loadError == null ? error : loadError + "\n" + error;
                            SPT.EditableTranslations.MinimalLog.WarnOnce("ClientLocaleModOverlay:103", () => "[Editable JSON] Locale addition rejected: " + error);
                        }
                    }
                }

                if (document.channels.TryGetValue("locale_clone_suffix", out var cloneRows))
                {
                    try
                    {
                        foreach (var r in cloneRows)
                        {
                            if (!r.enabled) continue;
                            if (string.IsNullOrWhiteSpace(r.key) || string.IsNullOrWhiteSpace(r.parent_key) ||
                                string.IsNullOrWhiteSpace(r.source) || string.IsNullOrWhiteSpace(r.translation) ||
                                cloneSuffixes.ContainsKey(r.key))
                                throw new InvalidDataException("Invalid/duplicate locale clone suffix key: " + r.key);
                            if (!string.IsNullOrWhiteSpace(r.match_mode) && r.match_mode != "exact")
                                throw new InvalidDataException("Locale clone suffix must use exact match mode");
                            if (!string.IsNullOrEmpty(r.description_source) && string.IsNullOrEmpty(r.description_translation))
                                throw new InvalidDataException("Locale clone suffix description translation missing: " + r.key);
                            cloneSuffixes.Add(r.key, new JObject
                            {
                                ["key"] = r.key,
                                ["parent_key"] = r.parent_key,
                                ["source"] = r.source,
                                ["translation"] = r.translation,
                                ["description_source"] = r.description_source == null ? JValue.CreateNull() : new JValue(r.description_source),
                                ["description_translation"] = r.description_translation == null ? JValue.CreateNull() : new JValue(r.description_translation),
                                ["mod_name"] = document.mod_name,
                                ["mod_id"] = document.mod_id
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        loadError = document.mod_id + ": " + ex.Message;
                        SPT.EditableTranslations.MinimalLog.WarnOnce("locale-clone-rejected:" + document.mod_id, () => "[Editable JSON] Locale clone suffixes rejected: " + loadError);
                    }
                }

                if (!document.channels.TryGetValue("locale", out var rows)) continue;
                try
                {
                    var local = new Dictionary<string, JObject>(StringComparer.Ordinal);
                    var displayRules = new LocaleDisplayRules(rows);
                    var missingModNameReported = false;
                    string inheritedMatchMode = null;
                    foreach (var r in rows)
                    {
                        // Compact locale format: declare match_mode only when it changes.
                        // The first locale row in every profile must declare it explicitly.
                        if (!string.IsNullOrWhiteSpace(r.match_mode)) inheritedMatchMode = r.match_mode;
                        var matchMode = inheritedMatchMode;
                        if (!r.enabled || string.IsNullOrEmpty(r.translation)) continue;
                        if (string.IsNullOrWhiteSpace(r.key) || string.IsNullOrWhiteSpace(r.source) || r.key.StartsWith("_note", StringComparison.Ordinal)
                            || local.ContainsKey(r.key)) throw new InvalidDataException("Invalid/duplicate locale key: " + r.key);
                        if (matchMode != "item_decorated" && matchMode != "exact") throw new InvalidDataException("Invalid locale match mode");
                        // A verified server-side rename must be scoped to this exact locale key.
                        // Reject empty, repeated and unbounded aliases instead of introducing fuzzy matching.
                        if (r.source_variants != null)
                        {
                            if (r.source_variants.Count == 0 || r.source_variants.Count > 8 ||
                                r.source_variants.Any(v => string.IsNullOrWhiteSpace(v) || v == r.source) ||
                                r.source_variants.Distinct(StringComparer.Ordinal).Count() != r.source_variants.Count)
                                throw new InvalidDataException("Invalid locale source variants: " + r.key);
                        }
                        var displayType = displayRules.Resolve(r);
                        // Different meanings at the same key need distinct translations.
                        // Keep every alternative exact and scoped to the audited item key;
                        // never accept fuzzy/global aliases or a different display policy.
                        if (r.source_alternatives != null)
                        {
                            if ((r.display_type != LocaleDisplayRules.ItemName &&
                                 r.display_type != LocaleDisplayRules.ItemDescription) ||
                                displayType != r.display_type)
                                throw new InvalidDataException("Locale source alternatives require an explicit audited item display type: " + r.key);
                            if (displayType == LocaleDisplayRules.ItemDescription && string.IsNullOrWhiteSpace(document.mod_name))
                                throw new InvalidDataException("Locale item alternative description requires mod_name: " + r.key);
                            if (r.source_alternatives.Count == 0 || r.source_alternatives.Count > 8)
                                throw new InvalidDataException("Invalid locale source alternatives count: " + r.key);
                            var used = new HashSet<string>(StringComparer.Ordinal) { r.source };
                            foreach (var v in r.source_variants ?? new List<string>()) used.Add(v);
                            foreach (var alternative in r.source_alternatives)
                            {
                                if (alternative == null || string.IsNullOrWhiteSpace(alternative.source) ||
                                    string.IsNullOrWhiteSpace(alternative.translation) || !used.Add(alternative.source))
                                    throw new InvalidDataException("Empty/duplicate locale source alternative: " + r.key);
                                // Automatic additions belong to LocaleDisplayRules,
                                // never to the human-authored translation body.
                                if (displayType == LocaleDisplayRules.ItemName &&
                                    Regex.IsMatch(alternative.translation, @"\n\([^\n]*[A-Za-z][^\n]*\)\s*$"))
                                    throw new InvalidDataException("Manual English suffix in item alternative: " + r.key);
                                if (displayType == LocaleDisplayRules.ItemDescription &&
                                    alternative.translation.TrimEnd().EndsWith("\n(" + document.mod_name.Trim() + ")", StringComparison.Ordinal))
                                    throw new InvalidDataException("Manual mod credit in item alternative: " + r.key);
                            }
                        }
                        if (displayType != LocaleDisplayRules.Default && !string.IsNullOrEmpty(r.translation_bilingual))
                            throw new InvalidDataException("Automatic locale display must not have translation_bilingual: " + r.key);
                        if (displayType == LocaleDisplayRules.ItemDescription && string.IsNullOrWhiteSpace(document.mod_name) && !missingModNameReported)
                        {
                            SPT.EditableTranslations.MinimalLog.WarnOnce("ClientLocaleModOverlay:165", () => "[Editable JSON] Missing mod_name in " + document.mod_id + "; preserving Korean description without empty parentheses.");
                            missingModNameReported = true;
                        }
                        local.Add(r.key, new JObject { ["key"] = r.key, ["source"] = r.source, ["translation"] = r.translation, ["translation_bilingual"] = r.translation_bilingual == null ? JValue.CreateNull() : new JValue(r.translation_bilingual),
                            ["match_mode"] = matchMode, ["mod_name"] = document.mod_name, ["display_type"] = displayType,
                            ["mod_id"] = document.mod_id,
                            ["seed_missing"] = r.seed_missing,
                            ["source_variants"] = r.source_variants == null ? JValue.CreateNull() : new JArray(r.source_variants),
                            ["source_alternatives"] = r.source_alternatives == null ? JValue.CreateNull() :
                                new JArray(r.source_alternatives.Select(v => new JObject
                                { ["source"] = v.source, ["translation"] = v.translation })),
                            ["name_key"] = displayType == LocaleDisplayRules.ItemDescription ? displayRules.NameKey(r) : null,
                            ["name_source"] = displayType == LocaleDisplayRules.ItemDescription ? displayRules.NameSource(r) : null });
                    }
                    foreach (var e in local)
                    {
                        if (!entries.TryGetValue(e.Key, out var candidates))
                            entries.Add(e.Key, candidates = new List<JObject>());
                        candidates.Add(e.Value);
                    }
                }
                catch (Exception ex) { loadError = document.mod_id + ": " + ex.Message; SPT.EditableTranslations.MinimalLog.WarnOnce("locale-group-rejected:" + document.mod_id, () => "[Editable JSON] Locale group rejected: " + loadError); }
            }
        }

        internal static string Translate(string key, string incoming, string expected, string translation, bool decorated)
        {
            if (incoming == expected) return translation;
            if (incoming != null && !string.IsNullOrWhiteSpace(expected) && incoming.Trim() == expected.Trim())
            {
                var left = incoming.Length - incoming.TrimStart().Length;
                var right = incoming.TrimEnd().Length;
                return incoming.Substring(0, left) + translation + incoming.Substring(right);
            }
            if (!decorated || incoming == null || expected == null) return null;
            var prefix = "";
            var suffix = "";
            var body = incoming;
            if (key.EndsWith(" Description", StringComparison.OrdinalIgnoreCase))
            {
                var m = Price.Match(body);
                if (m.Success) { prefix = m.Value; body = body.Substring(m.Length); }
            }
            else if (key.EndsWith(" ShortName", StringComparison.OrdinalIgnoreCase))
            {
                var m = ShortAmmo.Match(body);
                if (m.Success) { prefix = m.Value; body = body.Substring(m.Length); }
            }
            else if (key.EndsWith(" Name", StringComparison.OrdinalIgnoreCase))
            {
                var m = Ammo.Match(body);
                if (m.Success) { suffix = m.Value; body = body.Substring(0, m.Index); }
            }
            if (prefix.Length == 0 && suffix.Length == 0) return null;
            // Preserve whitespace around the body as well as every original decoration byte.
            var start = body.Length - body.TrimStart().Length;
            var end = body.TrimEnd().Length;
            if (body.Trim() != expected.Trim() || end < start) return null;
            return prefix + body.Substring(0, start) + translation + body.Substring(end) + suffix;
        }

        private static bool VerifiedSource(JObject candidate, string runtimeSource)
        {
            if (string.Equals((string)candidate["source"], runtimeSource, StringComparison.Ordinal)) return true;
            if (!(candidate["source_variants"] is JArray variants))
            {
                // A Name alternative can supply the verified English header for
                // the Description of the *same* runtime item, too.
                return candidate["source_alternatives"] is JArray alternativesOnly &&
                    alternativesOnly.Any(v => string.Equals((string)v["source"], runtimeSource, StringComparison.Ordinal));
            }
            if (variants.Any(v => string.Equals((string)v, runtimeSource, StringComparison.Ordinal))) return true;
            return candidate["source_alternatives"] is JArray alternatives &&
                alternatives.Any(v => string.Equals((string)v["source"], runtimeSource, StringComparison.Ordinal));
        }

        // Keep candidate spellings distinct when the actual locale dictionary contains both
        // "Name" and "name" for one ID. A case-insensitive candidate index is useful for
        // ordinary servers (and for source alternatives), but iterating only its first
        // spelling silently leaves the other live key untranslated.
        private sealed class LocaleTarget
        {
            internal string Key;
            internal string RawKey;
            internal List<JObject> Candidates;
        }

        private static Dictionary<string, List<string>> IndexLocaleKeys(IEnumerable<string> keys)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                if (!index.TryGetValue(key, out var names))
                    index.Add(key, names = new List<string>());
                names.Add(key);
            }
            return index;
        }

        private static IEnumerable<LocaleTarget> ExpandLocaleTargets(
            Dictionary<string, List<JObject>> groups, IDictionary<string, string> raw,
            IDictionary<string, string> result)
        {
            // A per-merge index avoids scanning thousands of raw keys for every profile
            // entry; lookup remains linear in the total number of keys and candidates.
            var rawIndex = IndexLocaleKeys(raw.Keys);
            var resultIndex = IndexLocaleKeys(result.Keys);
            foreach (var group in groups)
            {
                if (!rawIndex.TryGetValue(group.Key, out var rawKeys)) rawKeys = new List<string>();
                if (!resultIndex.TryGetValue(group.Key, out var resultKeys)) resultKeys = new List<string>();
                // Only distinct keys within the same runtime dictionary prove an alias;
                // a single differently-cased key in raw/result is not an alias.
                if (rawKeys.Count <= 1 && resultKeys.Count <= 1)
                {
                    var rawKey = rawKeys.FirstOrDefault();
                    var resultKey = resultKeys.FirstOrDefault();
                    var preferredKey = rawKey ?? resultKey ?? group.Key;
                    // An exact-case profile gets priority if both "name" and "Name"
                    // are configured, without breaking a lone case-insensitive lookup.
                    var ordered = group.Value.Where(e => string.Equals((string)e["key"], preferredKey, StringComparison.Ordinal))
                        .Concat(group.Value.Where(e => !string.Equals((string)e["key"], preferredKey, StringComparison.Ordinal)))
                        .ToList();
                    yield return new LocaleTarget { Key = resultKey ?? rawKey ?? group.Key, RawKey = rawKey, Candidates = ordered };
                    continue;
                }

                // With two live spellings, only the matching exact-case candidates are
                // permitted to write each spelling. Do not borrow the other key's source,
                // display_type or translation merely because their folded IDs coincide.
                foreach (var key in rawKeys.Concat(resultKeys).Distinct(StringComparer.Ordinal))
                {
                    var exact = group.Value.Where(e => string.Equals((string)e["key"], key, StringComparison.Ordinal)).ToList();
                    if (exact.Count == 0) continue;
                    yield return new LocaleTarget {
                        Key = key,
                        RawKey = rawKeys.FirstOrDefault(k => string.Equals(k, key, StringComparison.Ordinal)),
                        Candidates = exact
                    };
                }
            }
        }

        internal void Apply(string locale, IDictionary<string, string> raw, Dictionary<string, string> result, int decoratedBase, IDictionary<string, string> english)
        {
            if (!LocaleMode.IsKoreanCulture(locale)) return;
            var rawNameKeyIndex = IndexLocaleKeys(raw.Keys);
#if LOCALE_DIAGNOSTICS
            int applied = 0, already = 0, absentFromRaw = 0;
#endif
#if LOCALE_DIAGNOSTICS
            var excluded = new JArray();
            var conflicts = new JArray();
            var missingNameSources = new JArray();
#endif
            foreach (var target in ExpandLocaleTargets(entries, raw, result))
            {
                var key = target.Key;
                var candidates = target.Candidates;
                // Some source-audited server mods register quest/dialogue locales in
                // English only. Their stable keys can be absent from the Korean server
                // payload, even though the game later falls back to their English values.
                // Only explicitly opted-in profiles may supply such missing keys. Never
                // replace an existing non-English value supplied by another translator.
                string incoming = null;
                var presentInRaw = target.RawKey != null && raw.TryGetValue(target.RawKey, out incoming);
                if (!presentInRaw)
                {
                    if (!candidates.Any(e => (bool)e["seed_missing"]))
                    {
#if LOCALE_DIAGNOSTICS
                        // Most profiles describe mods that are not installed.
                        // Count them but do not allocate thousands of false-positive
                        // per-key incident objects on every game locale reload.
                        absentFromRaw++;
#endif
                        continue;
                    }
                    result.TryGetValue(key, out incoming);
                }
                // MergeGlobal may already have localized this key through the parent
                // Korean patch or another earlier translator. Compare the merged
                // output with the raw server value before applying a mod profile:
                // raw matching alone is not permission to overwrite that translation.
                if (presentInRaw && result.TryGetValue(key, out var mergedValue) &&
                    !string.Equals(mergedValue, incoming, StringComparison.Ordinal))
                {
#if LOCALE_DIAGNOSTICS
                    excluded.Add(Excluded(candidates[0], "existing_merged_translation_preserved", mergedValue));
#endif
                    continue;
                }
                string selected = null;
                JObject selectedEntry = null;
#if LOCALE_DIAGNOSTICS
                var isAlreadyTranslated = false;
#endif
                foreach (var e in candidates)
                {
                    if (!presentInRaw && !(bool)e["seed_missing"]) continue;
                    var source = (string)e["source"];
                    // Seed only a verified, stable source-key pair. When another
                    // translator has already supplied a different value, the source
                    // check below rejects it without modifying that value.
                    var candidateIncoming = presentInRaw ? incoming :
                        (incoming == null || string.Equals(incoming, key, StringComparison.Ordinal) ? source : incoming);
                    var nameSource = (string)e["name_source"];
                    var nameKey = (string)e["name_key"];
                    if (string.IsNullOrWhiteSpace(nameSource) && nameKey != null)
                    {
                        // English parent payload first; raw server names cover untranslated
                        // ammo names that intentionally have no translation row.
                        if (english != null) english.TryGetValue(nameKey, out nameSource);
                        if (string.IsNullOrWhiteSpace(nameSource)) raw.TryGetValue(nameKey, out nameSource);
                        if (nameSource != null && Regex.IsMatch(nameSource, @"[\uac00-\ud7a3\u3131-\u318e]")) nameSource = null;
                        if (nameSource != null) nameSource = Ammo.Replace(nameSource, "");
                    }
                    // Description headers must reflect the actual verified runtime item name,
                    // not the older ZIP's spelling, if the Name row explicitly whitelists it.
                    if (nameKey != null && raw.TryGetValue(nameKey, out var runtimeName) &&
                        !string.Equals(nameSource, runtimeName, StringComparison.Ordinal) &&
                        entries.TryGetValue(nameKey, out var nameCandidates) &&
                        nameCandidates.Any(n =>
                            (!rawNameKeyIndex.TryGetValue(nameKey, out var availableNames) || availableNames.Count <= 1 ||
                             string.Equals((string)n["key"], nameKey, StringComparison.Ordinal)) &&
                            VerifiedSource(n, runtimeName)))
                        nameSource = runtimeName;

                    var allowDecorations = (string)e["match_mode"] == "item_decorated";
                    string value = null;
                    string translation = null;
                    // Apply the primary source and only explicitly verified exact alternatives.
                    // Do not globally replace VSK, ignore the source guard, or override another translation.
                    var variants = e["source_variants"] as JArray;
                    for (int sourceIndex = -1; sourceIndex < (variants == null ? 0 : variants.Count); sourceIndex++)
                    {
                        var sourceCandidate = sourceIndex == -1 ? source : (string)variants[sourceIndex];
                        translation = LocaleDisplayRules.Select(sourceCandidate, (string)e["translation"],
                            (string)e["translation_bilingual"], (string)e["display_type"],
                            (string)e["mod_name"], locale, nameSource);
                        value = Translate(key, candidateIncoming, sourceCandidate, translation, allowDecorations);
                        if (value != null) break;
#if LOCALE_DIAGNOSTICS
                        if (Translate(key, incoming, translation, translation, allowDecorations) != null) isAlreadyTranslated = true;
#endif
                    }
                    // Each audited alternative owns its separate Korean meaning.
                    // Reuse the SAME mandatory item formatter and the original
                    // key-specific source guard; no raw-value/global fallback.
                    if (value == null && e["source_alternatives"] is JArray alternatives)
                    {
                        // Fail closed if a different-meaning item Description cannot
                        // prove the live English Name on the SAME key. Reusing an old
                        // Name from the MMGA/JSOC ZIP would silently print the wrong
                        // [English name] header and violate the mandatory rule.
                        var hasVerifiedLiveName = (string)e["display_type"] != LocaleDisplayRules.ItemDescription ||
                            (nameKey != null && raw.TryGetValue(nameKey, out var actualItemName) &&
                             !string.IsNullOrWhiteSpace(actualItemName) &&
                             string.Equals(nameSource, actualItemName, StringComparison.Ordinal));
                        if (!hasVerifiedLiveName)
                        {
#if LOCALE_DIAGNOSTICS
                            missingNameSources.Add(new JObject { ["key"] = key, ["name_key"] = nameKey,
                                ["mod_id"] = (string)e["mod_id"], ["reason"] = "alternative_item_description_requires_verified_live_name" });
#endif
                        }
                        else
                        {
                            foreach (var alternative in alternatives)
                            {
                                var alternativeSource = (string)alternative["source"];
                                var alternativeTranslation = LocaleDisplayRules.Select(
                                    alternativeSource, (string)alternative["translation"], null,
                                    (string)e["display_type"], (string)e["mod_name"], locale, nameSource);
                                value = Translate(key, candidateIncoming, alternativeSource,
                                    alternativeTranslation, allowDecorations);
                                if (value != null) break;
                            }
                        }
                    }
                    if (value != null)
                    {
                        if (selected == null)
                        {
                            selected = value; selectedEntry = e;
#if LOCALE_DIAGNOSTICS
                            if ((string)e["display_type"] == LocaleDisplayRules.ItemDescription && string.IsNullOrWhiteSpace(nameSource))
                                missingNameSources.Add(new JObject { ["key"] = key, ["name_key"] = nameKey, ["mod_id"] = (string)e["mod_id"] });
#endif
                        }
                        else if (value != selected)
                        {
                            SPT.EditableTranslations.MinimalLog.WarnOnce("locale-conflict:" + (string)e["mod_id"], () => "Multiple translations match; first profile retained. Mod=" + (string)e["mod_id"]);
#if LOCALE_DIAGNOSTICS
                            conflicts.Add(new JObject { ["key"] = key, ["selected_mod"] = (string)selectedEntry["mod_id"],
                                ["other_mod"] = (string)e["mod_id"], ["reason"] = "multiple_matching_translations_first_profile_retained" });
#endif
                        }
                    }
                }
                if (selected != null)
                {
                    result[key] = selected;
#if LOCALE_DIAGNOSTICS
                    applied++;
#endif
                }
#if LOCALE_DIAGNOSTICS
                else if (isAlreadyTranslated) already++;
                else excluded.Add(ExcludedCandidates(candidates, "source_changed_or_other_translation", incoming));
#endif
            }
#if LOCALE_DIAGNOSTICS
            int cloneApplied = 0;
#endif
#if LOCALE_DIAGNOSTICS
            var cloneExcluded = new JArray();
#endif
            foreach (var pair in cloneSuffixes)
            {
                var item = pair.Value;
                var child = (string)item["key"];
                var parent = (string)item["parent_key"];
                var sourceSuffix = (string)item["source"];
                var translatedSuffix = (string)item["translation"];
                var sourceDescription = (string)item["description_source"] ?? "";
                var translatedDescription = (string)item["description_translation"] ?? "";
                var modName = (string)item["mod_name"] ?? "";

                var childNameKey = child + " Name";
                var childShortNameKey = child + " ShortName";
                var childDescriptionKey = child + " Description";
                var parentNameKey = parent + " Name";
                var parentShortNameKey = parent + " ShortName";
                var parentDescriptionKey = parent + " Description";

                if (!raw.TryGetValue(childNameKey, out var rawChildName) ||
                    !raw.TryGetValue(parentNameKey, out var rawParentName) ||
                    !result.TryGetValue(parentNameKey, out var translatedParentName) ||
                    !raw.TryGetValue(childDescriptionKey, out var rawChildDescription) ||
                    !raw.TryGetValue(parentDescriptionKey, out var rawParentDescription) ||
                    !result.TryGetValue(parentDescriptionKey, out var translatedParentDescription))
                {
#if LOCALE_DIAGNOSTICS
                    cloneExcluded.Add(CloneExcluded(item, "missing_child_or_parent_locale"));
#endif
                    continue;
                }

                var expectedChildName = rawParentName + " " + sourceSuffix;
                var expectedChildDescription = rawParentDescription + "\n" + sourceDescription;
                if (!string.Equals(rawChildName, expectedChildName, StringComparison.Ordinal) ||
                    !string.Equals(rawChildDescription, expectedChildDescription, StringComparison.Ordinal))
                {
#if LOCALE_DIAGNOSTICS
                    cloneExcluded.Add(CloneExcluded(item, "runtime_clone_source_changed"));
#endif
                    continue;
                }

                string koreanParentName = translatedParentName;
                string englishParentName = null;
                english?.TryGetValue(parentNameKey, out englishParentName);
                if (string.Equals(locale, LocaleMode.Bilingual, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(englishParentName))
                {
                    var annotation = "\n(" + englishParentName.Trim() + ")";
                    if (koreanParentName.EndsWith(annotation, StringComparison.Ordinal))
                        koreanParentName = koreanParentName.Substring(0, koreanParentName.Length - annotation.Length);
                }

                // The clone relation validates the server source, but it does not grant
                // permission to replace another translator's already-merged child value.
                // Preserve each field independently so an untranslated description can
                // still be localized when only the child name has an earlier translation.
                var childNamePreserved = result.TryGetValue(childNameKey, out var currentChildName) &&
                    !string.Equals(currentChildName, rawChildName, StringComparison.Ordinal);
                var childDescriptionPreserved = result.TryGetValue(childDescriptionKey, out var currentChildDescription) &&
                    !string.Equals(currentChildDescription, rawChildDescription, StringComparison.Ordinal);
#if LOCALE_DIAGNOSTICS
                bool appliedChildField = false;
#endif
                if (!childNamePreserved)
                {
                    if (string.Equals(locale, LocaleMode.Bilingual, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(englishParentName))
                        result[childNameKey] = koreanParentName.TrimEnd() + " " + translatedSuffix +
                            "\n(" + englishParentName.Trim() + " " + sourceSuffix + ")";
                    else
                        result[childNameKey] = translatedParentName.TrimEnd() + " " + translatedSuffix;
#if LOCALE_DIAGNOSTICS
                    appliedChildField = true;
#endif
                }

                if (!childDescriptionPreserved)
                {
                    var descriptionBody = translatedParentDescription.TrimEnd();
                    if (!string.IsNullOrWhiteSpace(englishParentName))
                    {
                        var parentHeader = "[" + englishParentName.Trim() + "]\n";
                        if (descriptionBody.StartsWith(parentHeader, StringComparison.Ordinal))
                            descriptionBody = descriptionBody.Substring(parentHeader.Length);
                    }
                    if (!string.IsNullOrEmpty(sourceDescription) && !string.IsNullOrWhiteSpace(translatedDescription))
                        descriptionBody += "\n" + translatedDescription.Trim();
                    result[childDescriptionKey] = LocaleDisplayRules.Select(rawChildDescription, descriptionBody, null,
                        LocaleDisplayRules.ItemDescription, modName, locale,
                        string.IsNullOrWhiteSpace(englishParentName) ? null : englishParentName.Trim() + " " + sourceSuffix);
#if LOCALE_DIAGNOSTICS
                    appliedChildField = true;
#endif
                }

                if (raw.TryGetValue(childShortNameKey, out var rawChildShortName) &&
                    raw.TryGetValue(parentShortNameKey, out var rawParentShortName) &&
                    result.TryGetValue(parentShortNameKey, out var translatedParentShortName) &&
                    string.Equals(rawChildShortName, rawParentShortName, StringComparison.Ordinal) &&
                    (!result.TryGetValue(childShortNameKey, out var currentChildShortName) ||
                     string.Equals(currentChildShortName, rawChildShortName, StringComparison.Ordinal)))
                {
                    result[childShortNameKey] = translatedParentShortName;
#if LOCALE_DIAGNOSTICS
                    appliedChildField = true;
#endif
                }

#if LOCALE_DIAGNOSTICS
                if (appliedChildField) cloneApplied++;
#endif
            }

#if LOCALE_DIAGNOSTICS
            int sourceFallbackApplied = 0;
            var fallbackItemFormatReview = new JArray();
#endif
            // Before the generic fallback stage, resolve runtime-generated Quartermaster
            // quest IDs against the known contract titles and objective seed algorithm.
            if (quartermasterContracts != null)
                quartermasterContracts.Apply(locale, raw, result);

            if (sourceFallbacks.Count != 0)
            {
                // Iterate the merged result, not the raw English payload. If SPT-KR already translated
                // a row, its current value is Korean and therefore cannot match an English fallback source.
                foreach (var pair in result.ToArray())
                {
                    if (!sourceFallbacks.TryGetValue(pair.Value, out var e)) continue;
                    var source = (string)e["source"];
                    var translation = LocaleText.Select(source, (string)e["translation"], (string)e["translation_bilingual"], locale);
                    if (string.Equals(pair.Value, translation, StringComparison.Ordinal)) continue;
                    result[pair.Key] = translation;
#if LOCALE_DIAGNOSTICS
                    sourceFallbackApplied++;
                    // These older source-only rows may refer to item fields, but
                    // have no verified key->display_type/name-source contract.
                    // Record possible policy bypasses without guessing and
                    // silently changing existing non-item UI/quest translations.
                    if (pair.Key.EndsWith(" Name", StringComparison.Ordinal) ||
                        pair.Key.EndsWith(" Description", StringComparison.Ordinal))
                        fallbackItemFormatReview.Add(new JObject {
                            ["key"] = pair.Key, ["source"] = source,
                            ["mod_name"] = (string)e["mod_name"],
                            ["reason"] = "keyless_fallback_item_display_policy_review"
                        });
#endif
                }
            }

#if LOCALE_DIAGNOSTICS
            int added = 0, additionConflicts = 0, additionsAlready = 0;
#endif
            foreach (var pair in additions)
            {
                var key = pair.Key;
                var e = pair.Value;
                var source = (string)e["source"];
                var translation = LocaleText.Select(source, (string)e["translation"], (string)e["translation_bilingual"], locale);
                if (result.TryGetValue(key, out var existingValue) && string.Equals(existingValue, translation, StringComparison.Ordinal))
                {
#if LOCALE_DIAGNOSTICS
                    additionsAlready++;
#endif
                    continue;
                }
                if (result.TryGetValue(key, out var current) &&
                    !string.Equals(current, source, StringComparison.Ordinal) &&
                    !string.Equals(current, key, StringComparison.Ordinal))
                {
#if LOCALE_DIAGNOSTICS
                    additionConflicts++;
#endif
                    SPT.EditableTranslations.MinimalLog.WarnOnce("locale-addition-existing", () => "Some additional UI keys already have another translation; existing values retained.");
                    continue;
                }
                result[key] = translation;
#if LOCALE_DIAGNOSTICS
                added++;
#endif
            }

#if LOCALE_DIAGNOSTICS
            var report = new JObject { ["locale"] = locale, ["applied"] = applied, ["already_translated"] = already,
                ["profile_keys_absent_from_server_raw"] = absentFromRaw,
                ["clone_suffix_applied"] = cloneApplied, ["clone_suffix_unapplied"] = cloneExcluded,
                ["source_fallback_applied"] = sourceFallbackApplied,
                ["source_fallback_item_display_review"] = fallbackItemFormatReview,
                ["runtime_locale_additions"] = added, ["runtime_locale_additions_already_translated"] = additionsAlready, ["runtime_locale_addition_conflicts"] = additionConflicts,
                ["decorated_base_translations"] = decoratedBase, ["load_error"] = loadError == null ? JValue.CreateNull() : new JValue(loadError), ["unapplied"] = excluded,
                ["candidate_conflicts"] = conflicts, ["missing_item_name_sources"] = missingNameSources };
            // Runtime locale audits do not modify displayed text. They are
            // emitted after the parent and all overlay branches have run.
            // A report is a review candidate, not proof the UI was rendered.
            var unregistered = new JArray();
            var omittedCandidates = 0;
            // Do not discard an unknown item merely because its English parent
            // locale contains the same key. Nearly every mod-added item can have
            // an English parent value; the previous test hid precisely the kind
            // of unregistered item the user needs this report to discover.
            // Prioritize keys absent from English parent, then record bounded
            // English-parent matches with the explicit weaker evidence label.
            for (var priority = 0; priority <= 1; priority++)
            {
                foreach (var pair in raw)
                {
                    var key = pair.Key;
                    if (!key.EndsWith(" Name", StringComparison.OrdinalIgnoreCase) &&
                        !key.EndsWith(" Description", StringComparison.OrdinalIgnoreCase) &&
                        !key.EndsWith(" ShortName", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entries.ContainsKey(key) || additions.ContainsKey(key)) continue;
                    var hasEnglishParent = english != null && english.ContainsKey(key);
                    if ((hasEnglishParent ? 1 : 0) != priority) continue;
                    if (string.IsNullOrEmpty(pair.Value) || !Regex.IsMatch(pair.Value, "[A-Za-z]")) continue;
                    if (result.TryGetValue(key, out var current) &&
                        !string.Equals(current, pair.Value, StringComparison.Ordinal)) continue;
                    if (unregistered.Count >= 2000) { omittedCandidates++; continue; }
                    unregistered.Add(new JObject {
                        ["key"] = key, ["current_text"] = pair.Value,
                        ["english_parent_exists"] = hasEnglishParent,
                        ["reason"] = hasEnglishParent
                            ? "raw_english_unchanged_with_english_parent_review_not_confirmed_untranslated"
                            : "server_item_key_without_profile_review_not_confirmed_untranslated"
                    });
                }
            }
            report["unregistered_server_item_candidates"] = unregistered;
            report["unregistered_candidates_omitted"] = omittedCandidates;
            report["source_stage"] = "client_MergeGlobal_postfix_server_raw_before_addon_and_merged_result";
            report["limitations"] = new JArray(
                "locale only; direct UI, F12, mail and later screen overwrites not observed",
                "same key or English-looking string is not proof of translation failure",
                "supplier, mod version and visible UI must be verified separately");
            var auditPath = Path.Combine(reportRoot, locale + ".json");
            try
            {
                var reportJson = report.ToString(Newtonsoft.Json.Formatting.Indented);
                // No repeated disk writes for an identical MergeGlobal result.
                if (!reportSnapshots.TryGetValue(locale, out var previous) ||
                    !string.Equals(previous, reportJson, StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(reportRoot);
                    File.WriteAllText(auditPath, reportJson);
                    reportSnapshots[locale] = reportJson;
                }
            }
            catch (Exception error)
            {
                // Diagnostics must never break a real game locale merge.
                SPT.EditableTranslations.MinimalLog.WarnOnce("locale-audit-io", () =>
                    "Locale diagnostic report could not be written; translation continues: " + error.Message);
            }
            try { Diagnostic?.Invoke(auditPath, report); }
            catch (Exception error)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("locale-audit-callback", () =>
                    "Locale diagnostic callback failed; translation continues: " + error.Message);
            }
#endif
        }

#if LOCALE_DIAGNOSTICS
        private static JObject CloneExcluded(JObject entry, string reason)
        {
            var copy = (JObject)entry.DeepClone();
            copy["reason"] = reason;
            return copy;
        }

        // Include every configured source at the actual key so a user can
        // diagnose both JSOC and MMGA candidates without looking up IDs in SIC.
        // No candidate is automatically accepted from this report.
        private static JObject ExcludedCandidates(List<JObject> candidates, string reason, string incoming)
        {
            var item = Excluded(candidates[0], reason, incoming);
            item["candidates_at_key"] = new JArray(candidates.Select(e => new JObject
            {
                ["mod_id"] = (string)e["mod_id"],
                ["source"] = (string)e["source"],
                ["source_variants"] = e["source_variants"]?.DeepClone(),
                ["source_alternatives"] = e["source_alternatives"]?.DeepClone()
            }));
            return item;
        }

        private static JObject Excluded(JObject entry, string reason, string incoming)
        {
            var copy = (JObject)entry.DeepClone();
            copy["reason"] = reason;
            copy["current_text"] = incoming;
            return copy;
        }
#endif
    }
}
