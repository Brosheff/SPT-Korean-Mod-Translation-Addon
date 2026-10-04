using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace SPT.ModKoreanAddon
{
    // Patch the original method in memory only. Never replace or rewrite the original plugin or payload.
    internal static class AddonBridge
    {
        private static FieldInfo korean, bilingual, baseline;
        private static Dictionary<string, string> english;
        private static ClientLocaleModOverlay overlay;
        internal static void Enable(Harmony harmony, Assembly original, string originalRoot, string addonRoot, EditableProfiles profiles)
        {
            var type = original.GetType("KoreanPatchFix.ClientLocaleBundle", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            korean = RequireField(type, "korean");
            bilingual = RequireField(type, "bilingual");
            baseline = RequireField(type, "baseline");
            var merge = type.GetMethod("MergeGlobal", flags, null, new[] { typeof(string), typeof(IDictionary<string,string>) }, null);
            if (merge == null || merge.ReturnType != typeof(Dictionary<string,string>)) throw new MissingMethodException("Unsupported MergeGlobal signature");
            // Only read a source whose bytes agree with the original manifest. The original loader
            // independently validates all payloads and the installed game before it calls MergeGlobal.
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(originalRoot, "manifest.json")));
            var profile = manifest["profiles"]?["4.1.5"];
            if ((string)profile?["translationVersion"] != "4.1.5") throw new InvalidDataException("Unsupported original locale profile");
            var bytes = File.ReadAllBytes(Path.Combine(originalRoot, "locales/4.1.5/en.json"));
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != (string)profile["sha256"]?["en.json"])
                    throw new InvalidDataException("Original English payload hash mismatch");
            english = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = new StreamReader(new MemoryStream(bytes)))
                foreach (var p in JObject.Parse(reader.ReadToEnd()).Properties()) english[p.Name] = (string)p.Value;
            overlay = new ClientLocaleModOverlay(addonRoot, "4.1.6", profiles);
            harmony.Patch(merge, postfix: new HarmonyMethod(typeof(AddonBridge).GetMethod(nameof(AfterMerge), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last });
        }
        private static FieldInfo RequireField(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Dictionary<string,string>)) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static void AfterMerge(object __instance, string __0, IDictionary<string,string> __1, ref Dictionary<string,string> __result)
        {
            if (__0 != "kr" && __0 != "kr-en") return;
            try
            {
                // Work on a copy: any extension failure leaves the original return value intact.
                var result = new Dictionary<string,string>(__result, StringComparer.OrdinalIgnoreCase);
                var raw = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in __1) raw[e.Key] = e.Value;
                var patch = (Dictionary<string,string>)(__0 == "kr" ? korean : bilingual).GetValue(__instance);
                var source = (Dictionary<string,string>)baseline.GetValue(__instance);
                int count = 0;
                foreach (var e in patch)
                {
                    if (!raw.TryGetValue(e.Key, out var incoming) || !result.TryGetValue(e.Key, out var current) || incoming != current) continue;
                    string value = null;
                    if (source.TryGetValue(e.Key, out var expected)) value = ClientLocaleModOverlay.Translate(e.Key, incoming, expected, e.Value, true);
                    if (value == null && english.TryGetValue(e.Key, out expected)) value = ClientLocaleModOverlay.Translate(e.Key, incoming, expected, e.Value, true);
                    if (value != null && value != incoming) { result[e.Key] = value; count++; }
                }
                overlay.Apply(__0, raw, result, count, english);
                __result = result;
            }
            catch (Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("AddonBridge:71", () => "Addon merge failed; original result preserved: " + ex.Message); }
        }
    }
}
