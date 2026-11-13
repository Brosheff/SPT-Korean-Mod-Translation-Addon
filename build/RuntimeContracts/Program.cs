using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SPT.ModKoreanAddon;
using SPT.EditableTranslations;

// Only the parent's language provider is a fixture. All translation/selection,
// profile parsing, caches and display formatting below use production sources.
namespace KoreanPatchFix
{
    internal static class ClientLocaleRuntime
    {
        internal static string Culture = "kr";
        internal static string CurrentCulture() => Culture;
    }
}

internal static class Program
{
    private const string Item = "aaaaaaaaaaaaaaaaaaaaaaaa";
    private static int checks;
    private static readonly List<string> Warnings = new List<string>();

    private static void Equal<T>(T expected, T actual, string scenario)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(scenario + "\nExpected: " + expected + "\nActual: " + actual);
        checks++;
    }

    private static void Culture(string value)
    {
        KoreanPatchFix.ClientLocaleRuntime.Culture = value;
        LocaleMode.Refresh();
    }

    private static JObject Row(string key, string source, string translation,
        string mode = "exact", string display = null)
    {
        var row = new JObject { ["key"] = key, ["source"] = source,
            ["translation"] = translation, ["match_mode"] = mode };
        if (display != null) row["display_type"] = display;
        return row;
    }

    private static void Profile(string root, string file, string id, JArray rows, JObject extra = null)
    {
        var channels = extra ?? new JObject();
        channels["locale"] = rows;
        var doc = new JObject { ["schema_version"] = 1, ["target_spt"] = "4.1.6",
            ["mod_id"] = id, ["mod_name"] = "Fixture Mod", ["channels"] = channels };
        Directory.CreateDirectory(Path.Combine(root, "translations"));
        File.WriteAllText(Path.Combine(root, "translations", file), doc.ToString());
    }

    private static Dictionary<string, string> Apply(ClientLocaleModOverlay overlay, string culture,
        Dictionary<string, string> raw, Dictionary<string, string> merged = null)
    {
        var result = merged ?? new Dictionary<string, string>(raw, StringComparer.Ordinal);
        overlay.Apply(culture, raw, result, 0, new Dictionary<string, string>());
        return result;
    }

    private static void Selection(string root)
    {
        var name = Row(Item + " Name", "Test rifle", "시험 소총", "item_decorated", "item_name");
        name["source_variants"] = new JArray("Test Rifle");
        name["source_alternatives"] = new JArray(new JObject {
            ["source"] = "Scoped rifle", ["translation"] = "조준경 장착 소총" });
        var desc = Row(Item + " Description", "A rifle.", "소총입니다.", "item_decorated", "item_description");
        desc["source_alternatives"] = new JArray(new JObject {
            ["source"] = "A scoped rifle.", ["translation"] = "조준경 장착 소총입니다." });
        Profile(root, "a.json", "fixture.first", new JArray(name, desc,
            Row("shared", "Old text", "이전 문구"),
            Row("same", "Same text", "첫 후보"),
            Row("Alias Name", "Upper", "대문자", display: "default"),
            Row("alias name", "Lower", "소문자", display: "default"),
            Row("quest", "Test quest", "시험 퀘스트", display: "quest_title"),
            Row("objective", "Find 2 items", "아이템 2개 찾기", display: "quest_objective")),
            new JObject { ["small_ui"] = new JArray(new JObject { ["id"] = "count",
                ["source"] = "Count: {0}", ["translation"] = "개수: {0}", ["match_mode"] = "template" }) });
        Profile(root, "b.json", "fixture.second", new JArray(
            Row("shared", "New text", "새 문구"), Row("same", "Same text", "둘째 후보")));
        var profiles = new EditableProfiles(root);
        Equal(2, profiles.Documents.Count, "valid profiles loaded: " + string.Join("\n", Warnings));
        var overlay = new ClientLocaleModOverlay(root, "4.1.6", profiles);
        var raw = new Dictionary<string, string>(StringComparer.Ordinal) {
            [Item + " Name"] = "Test rifle", [Item + " Description"] = "A rifle.",
            ["shared"] = "New text", ["same"] = "Same text", ["Alias Name"] = "Upper",
            ["alias name"] = "Lower", ["quest"] = "Test quest", ["objective"] = "Find 2 items" };
        foreach (var language in new[] { "kr", "kr-en", "en", "kr" })
        {
            Culture(language);
            var r = Apply(overlay, language, raw);
            var korean = language != "en";
            Equal(language == "kr-en" ? "시험 소총\n(Test rifle)" : korean ? "시험 소총" : "Test rifle", r[Item + " Name"], language + " item name");
            Equal(korean ? "[Test rifle]\n소총입니다.\n(Fixture Mod)" : "A rifle.", r[Item + " Description"], language + " item description");
            Equal(korean ? "새 문구" : "New text", r["shared"], language + " source-based candidate selection");
            Equal(korean ? "첫 후보" : "Same text", r["same"], language + " deterministic duplicate selection");
            Equal(korean ? "대문자" : "Upper", r["Alias Name"], language + " uppercase alias");
            Equal(korean ? "소문자" : "Lower", r["alias name"], language + " lowercase alias");
            Equal(korean ? "시험 퀘스트 (Test quest)" : "Test quest", r["quest"], language + " quest title");
            Equal(korean ? "아이템 2개 찾기\n(Find 2 items)" : "Find 2 items", r["objective"], language + " objective");
            Equal(korean ? "개수: 12" : "Count: 12", profiles.Translate("fixture.first", "small_ui", "Count: 12"), language + " UI template/cache roundtrip");
            if (korean)
                Equal(r[Item + " Description"], Apply(overlay, language, raw, r)[Item + " Description"], language + " repeated merge does not duplicate decoration");
        }
        var protectedResult = new Dictionary<string, string>(raw) { [Item + " Name"] = "부모 번역" };
        Equal("부모 번역", Apply(overlay, "kr", raw, protectedResult)[Item + " Name"], "parent translation preserved");
        var changed = new Dictionary<string, string>(raw) { [Item + " Name"] = "Unknown new rifle" };
        Equal("Unknown new rifle", Apply(overlay, "kr", changed)[Item + " Name"], "unknown source not force-translated");
        var variant = new Dictionary<string, string>(raw) { [Item + " Name"] = "Test Rifle" };
        Equal("시험 소총\n(Test Rifle)", Apply(overlay, "kr-en", variant)[Item + " Name"], "verified variant uses actual original");
        var alternate = new Dictionary<string, string>(raw) { [Item + " Name"] = "Scoped rifle", [Item + " Description"] = "A scoped rifle." };
        Equal("[Scoped rifle]\n조준경 장착 소총입니다.\n(Fixture Mod)", Apply(overlay, "kr", alternate)[Item + " Description"], "alternate meaning uses live name");
        alternate.Remove(Item + " Name");
        Equal("A scoped rifle.", Apply(overlay, "kr", alternate)[Item + " Description"], "alternate description without verified name fails closed");
        var decorated = new Dictionary<string, string>(raw) { [Item + " Name"] = "Test rifle <color=#808080>[5/10]</color>" };
        Equal("시험 소총 <color=#808080>[5/10]</color>", Apply(overlay, "kr", decorated)[Item + " Name"], "ammo decoration preserved");
        Equal("Test rifle", raw[Item + " Name"], "raw dictionary unchanged");
    }

    private static void RejectionsAndF12(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "translations"));
        Directory.CreateDirectory(Path.Combine(root, "config-ui"));
        foreach (var folder in new[] { "translations", "config-ui" })
            foreach (var file in new[] { "bad-a.json", "bad-b.json" })
                File.WriteAllText(Path.Combine(root, folder, file), "{broken");
        MinimalLog.Reset(); Warnings.Clear();
        new EditableProfiles(root); new ConfigManagerProfiles(root);
        Equal(4, Warnings.Count, "each rejected file in each loader is reported");
        new EditableProfiles(root); new ConfigManagerProfiles(root);
        Equal(4, Warnings.Count, "same rejected files are not repeatedly logged");
        var config = new JObject { ["schema_version"] = 1, ["target_spt"] = "4.1.6", ["plugin_guid"] = "fixture.config",
            ["entries"] = new JArray(new JObject { ["section"] = "General", ["key"] = "Enabled",
                ["source_display_name"] = "Enabled", ["translation_display_name"] = "사용",
                ["source_description"] = "Enable feature", ["translation_description"] = "기능을 사용합니다." }) };
        File.WriteAllText(Path.Combine(root, "config-ui", "fixture.config.json"), config.ToString());
        var configs = new ConfigManagerProfiles(root);
        var input = new ConfigUiSource { plugin_guid = "fixture.config", section = "General", key = "Enabled", source_category = "General",
            source_display_name = "Enabled", source_description = "Enable feature" };
        foreach (var language in new[] { "kr", "kr-en", "en", "kr" })
        {
            Culture(language);
            Equal(language == "en" ? "Enabled" : "사용", configs.Resolve(input).display_name, language + " F12 roundtrip");
        }
        input.source_display_name = "New option name";
        Equal(null, configs.Resolve(input)?.display_name, "changed F12 name does not use stale translation");
        Equal("Enabled", input.key, "F12 internal key preserved");
    }

    private static void CurrentProfiles(string project, string temp)
    {
        MinimalLog.Reset(); Warnings.Clear();
        var data = new EditableProfiles(project);
        Equal(Directory.GetFiles(Path.Combine(project, "translations"), "*.json").Length,
            data.Documents.Count, "all current production profiles load in C#: " + string.Join("\n", Warnings));
        var configs = new ConfigManagerProfiles(project);
        Equal(Directory.GetFiles(Path.Combine(project, "config-ui"), "*.json").Count(p => !Path.GetFileName(p).StartsWith("_")),
            configs.ProfileCount, "all current F12 profiles load in C#");
        Directory.CreateDirectory(Path.Combine(temp, "contract-locales"));
        foreach (var file in Directory.GetFiles(Path.Combine(project, "contract-locales"), "*.json"))
            File.Copy(file, Path.Combine(temp, "contract-locales", Path.GetFileName(file)), true);
        new ClientLocaleModOverlay(temp, "4.1.6", data);
        Equal("", string.Join("\n", Warnings), "current loader/formatter initialization has no rejected groups");
    }

    public static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: RuntimeContracts.exe <project-root>"); return 2; }
        var temp = Path.Combine(Path.GetTempPath(), "spt-runtime-contracts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            MinimalLog.WarningSink = message => Warnings.Add(message);
            ClientLocaleModOverlay.Diagnostic = (path, report) => { };
            LocaleMode.Initialize(typeof(KoreanPatchFix.ClientLocaleRuntime).Assembly);
            Selection(Path.Combine(temp, "selection"));
            RejectionsAndF12(Path.Combine(temp, "rejections"));
            CurrentProfiles(Path.GetFullPath(args[0]), Path.Combine(temp, "current"));
            Console.WriteLine("PASS: " + checks + " production C# loader assertions (.NET Framework 4.8). Game rendering/Harmony installation not exercised.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }
}
