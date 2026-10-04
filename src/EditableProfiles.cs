using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.EditableTranslations;

namespace SPT.ModKoreanAddon
{
    internal sealed class EditableProfiles
    {
        internal readonly List<ModTexts> Documents = new List<ModTexts>();

        // Keep profile/channel as two dictionary levels. The old hot path built "mod/channel"
        // on every Translate/CanTranslate call, allocating a new string for every IMGUI label.
        private readonly Dictionary<string, Dictionary<string, TextRules>> rules =
            new Dictionary<string, Dictionary<string, TextRules>>(StringComparer.Ordinal);

        internal EditableProfiles(string addonRoot)
        {
            var folder = Path.Combine(addonRoot, "translations");
            if (!Directory.Exists(folder))
            { MinimalLog.WarnOnce("profiles-missing",()=>"Translation folder missing; mod translations unavailable."); return; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                try
                {
                    JObject json;
                    using (var reader = new JsonTextReader(new StreamReader(file)))
                    {
                        json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                        if (reader.Read()) throw new InvalidDataException("Trailing content");
                    }

                    var doc = json.ToObject<ModTexts>();
                    if (doc.schema_version != 1 || doc.target_spt != "4.1.6" || string.IsNullOrWhiteSpace(doc.mod_id) || !ids.Add(doc.mod_id))
                        throw new InvalidDataException("Invalid/duplicate mod profile");

                    var channels = new Dictionary<string, TextRules>(StringComparer.Ordinal);
                    foreach (var channel in doc.channels)
                    {
                        if (channel.Key == "locale" || channel.Key == "locale_additions" || channel.Key == "locale_source_fallback" ||
                            channel.Key == "locale_clone_suffix" || channel.Key == "reference_only" ||
                            channel.Key == "npc_messages" || channel.Key == "server_messages")
                            continue;
                        channels.Add(channel.Key, new TextRules(channel.Value));
                    }

                    rules.Add(doc.mod_id, channels);
                    Documents.Add(doc);
                }
                catch (Exception ex)
                {
                    SPT.EditableTranslations.MinimalLog.WarnOnce("EditableProfiles:55", () => "[Editable JSON] Rejected " + Path.GetFileName(file) + ": " + ex.Message);
                }
            }
        }

        private bool TryGetRules(string mod, string channel, out TextRules result)
        {
            result = null;
            if (mod == null || channel == null) return false;
            return rules.TryGetValue(mod, out var channels) && channels.TryGetValue(channel, out result);
        }

        internal bool HasRules(string mod, string channel)
        {
            return TryGetRules(mod, channel, out _);
        }

        internal bool CanTranslate(string mod, string channel, string text)
        {
            return TryGetRules(mod, channel, out var r) && r.CanTranslate(text);
        }

        internal string Translate(string mod, string channel, string text)
        {
            return Translate(mod, channel, text, LocaleMode.CurrentCulture());
        }

        internal string Translate(string mod, string channel, string text, string culture)
        {
            return TryGetRules(mod, channel, out var r) ? r.Translate(text, culture) : text;
        }

        internal string RestoreExact(string mod, string channel, string text)
        {
            return TryGetRules(mod, channel, out var r) ? r.RestoreExact(text) : text;
        }
    }
}
