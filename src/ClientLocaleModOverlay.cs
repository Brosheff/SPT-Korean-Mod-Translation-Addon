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
                        SPT.EditableTranslations.MinimalLog.WarnOnce("ClientLocaleModOverlay:70", () => "[Editable JSON] Locale source fallbacks rejected: " + loadError);
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
                        SPT.EditableTranslations.MinimalLog.WarnOnce("ClientLocaleModOverlay:139", () => "[Editable JSON] Locale clone suffixes rejected: " + loadError);
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
                        var displayType = displayRules.Resolve(r);
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
                catch (Exception ex) { loadError = document.mod_id + ": " + ex.Message; SPT.EditableTranslations.MinimalLog.WarnOnce("ClientLocaleModOverlay:181", () => "[Editable JSON] Locale group rejected: " + loadError); }
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

        internal void Apply(string locale, IDictionary<string, string> raw, Dictionary<string, string> result, int decoratedBase, IDictionary<string, string> english)
        {
            if (!LocaleMode.IsKoreanCulture(locale)) return;
#if LOCALE_DIAGNOSTICS
            int applied = 0, already = 0;
#endif
#if LOCALE_DIAGNOSTICS
            var excluded = new JArray();
            var conflicts = new JArray();
            var missingNameSources = new JArray();
#endif
            foreach (var pair in entries)
            {
                var key = pair.Key;
                if (!raw.TryGetValue(key, out var incoming))
                {
#if LOCALE_DIAGNOSTICS
                    excluded.Add(Excluded(pair.Value[0], "not_present_in_server_locale", null));
#endif
                    continue;
                }
                string selected = null;
                JObject selectedEntry = null;
#if LOCALE_DIAGNOSTICS
                var isAlreadyTranslated = false;
#endif
                foreach (var e in pair.Value)
                {
                    var source = (string)e["source"];
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
                    var translation = LocaleDisplayRules.Select(source, (string)e["translation"], (string)e["translation_bilingual"],
                        (string)e["display_type"], (string)e["mod_name"], locale, nameSource);
                    var allowDecorations = (string)e["match_mode"] == "item_decorated";
                    var value = Translate(key, incoming, source, translation, allowDecorations);
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
#if LOCALE_DIAGNOSTICS
                    else if (Translate(key, incoming, translation, translation, allowDecorations) != null) isAlreadyTranslated = true;
#endif
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
                else excluded.Add(Excluded(pair.Value[0], "source_changed_or_other_translation", incoming));
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

                if (string.Equals(locale, LocaleMode.Bilingual, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(englishParentName))
                    result[childNameKey] = koreanParentName.TrimEnd() + " " + translatedSuffix +
                        "\n(" + englishParentName.Trim() + " " + sourceSuffix + ")";
                else
                    result[childNameKey] = translatedParentName.TrimEnd() + " " + translatedSuffix;

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

                if (raw.TryGetValue(childShortNameKey, out var rawChildShortName) &&
                    raw.TryGetValue(parentShortNameKey, out var rawParentShortName) &&
                    result.TryGetValue(parentShortNameKey, out var translatedParentShortName) &&
                    string.Equals(rawChildShortName, rawParentShortName, StringComparison.Ordinal))
                    result[childShortNameKey] = translatedParentShortName;

#if LOCALE_DIAGNOSTICS
                cloneApplied++;
#endif
            }

#if LOCALE_DIAGNOSTICS
            int sourceFallbackApplied = 0;
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
                ["clone_suffix_applied"] = cloneApplied, ["clone_suffix_unapplied"] = cloneExcluded,
                ["source_fallback_applied"] = sourceFallbackApplied,
                ["runtime_locale_additions"] = added, ["runtime_locale_additions_already_translated"] = additionsAlready, ["runtime_locale_addition_conflicts"] = additionConflicts,
                ["decorated_base_translations"] = decoratedBase, ["load_error"] = loadError == null ? JValue.CreateNull() : new JValue(loadError), ["unapplied"] = excluded,
                ["candidate_conflicts"] = conflicts, ["missing_item_name_sources"] = missingNameSources };
            Diagnostic?.Invoke(Path.Combine(reportRoot,locale+".json"),report);
#endif
        }

#if LOCALE_DIAGNOSTICS
        private static JObject CloneExcluded(JObject entry, string reason)
        {
            var copy = (JObject)entry.DeepClone();
            copy["reason"] = reason;
            return copy;
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
