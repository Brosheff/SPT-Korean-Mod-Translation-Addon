using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.EditableTranslations;
using SPT.ModKoreanAddon;

internal static class Program
{
    private static int checks;
    private static readonly Dictionary<string,JObject> Reports=new Dictionary<string,JObject>(StringComparer.OrdinalIgnoreCase);
    private static JObject ReadReport(string path)=>Reports[Path.GetFullPath(path)];
    private static void Equal<T>(T expected, T actual, string name)
    {
        checks++;
        if (!Equals(expected, actual)) throw new Exception(name + ": expected=" + expected + ", actual=" + actual);
    }
    private static TextRule Row(string key, string source = "English", string korean = "한국어", string type = null)
        => new TextRule { key = key, source = source, translation = korean, match_mode = "item_decorated", display_type = type };
    private static string Show(string type, string ko = "한국어", string en = "English", string culture = "kr-en", string mod = "Test Mod")
        => LocaleDisplayRules.Select(en, ko, null, type, mod, culture);

    private static void UnitContracts()
    {
        foreach(var type in new[]{"quest_description","achievement_description"})
        {
            foreach(var culture in new[]{"kr","kr-en"})
            {
                Equal("한국어\n(Test Mod)",Show(type,culture:culture),type+" credit "+culture);
                Equal("한국어\n(Test Mod)",Show(type,"한국어\n(Test Mod)",culture:culture),type+" no duplicate");
                Equal("한국어",Show(type,culture:culture,mod:null),type+" missing credit");
                Equal("본문\n(Mod)",LocaleDisplayRules.Select("English body","본문",null,type,"Mod",culture,"English title"),type+" no item-name header or English body");
            }
            Equal("English",Show(type,culture:"en"),type+" English unchanged");
        }
        Equal("한국어",Show("default",culture:"kr-en"),"dialogue and messages do not gain credits");
        Equal("한국어 (English)",Show("achievement_title",culture:"kr-en"),"achievement bilingual inline original");
        Equal("한국어",Show("achievement_title",culture:"kr"),"achievement Korean without original");
        Equal("한국어\n(English)", Show("item_name"), "short name newline");
        Equal("한국어 (English)", Show("quest_title"), "quest inline in bilingual");
        Equal("한국어 (English)", Show("quest_title", culture:"kr"), "quest inline in Korean too");
        Equal("한국어\n(English)", Show("quest_objective", culture:"kr"), "quest objective original in Korean");
        Equal("한국어\n(English)", Show("quest_objective"), "quest objective original in bilingual");
        Equal("English", Show("quest_objective", culture:"en"), "quest objective English unchanged");
        Equal("[물건] (2개)\n(Hand over items)", Show("quest_objective", "[물건] (2개)", "Hand over items", "kr"), "objective semantic brackets preserved");
        Equal("한국어\n(English)", Show("quest_objective", "한국어\n(English)", culture:"kr"), "objective annotation idempotent");
        Equal("한국어\n(Test Mod)", Show("item_description"), "description credit only");
        Equal("한국어\n(Test Mod)", Show("item_description", culture: "kr"), "Korean has mod credit");
        Equal("English", Show("item_description", culture: "en"), "English preserved");
        Equal("English", Show("quest_title", culture: null), "unknown culture preserved");
        Equal("M855", Show("item_name", "M855", " M855 "), "ammo without duplicate");
        Equal("앞 English 뒤\n(English)", Show("item_name", "앞 English 뒤"), "partial match is not full equality");
        Equal("탄약 팩 (50발)\n(Ammo pack (50 pcs))", Show("item_name", "탄약 팩 (50발)", "Ammo pack (50 pcs)"), "nested parentheses");
        Equal("첫 문단\n\n둘째 (FMJ)\n(Mod (Beta))", Show("item_description", "첫 문단\n\n둘째 (FMJ)", mod:"Mod (Beta)"), "paragraphs and nested mod name");
        Equal("한국어", Show("item_description", mod:null), "missing mod has no empty parentheses");
        Equal("English", Show("item_name", ko:null), "missing Korean source fallback");
        Equal("  한국어\n(English)", Show("item_name", "  한국어 \r\n", " English \r\n"), "boundary whitespace");
        Equal("한국어", Show("default"), "generic text has no source annotation");
        Equal("한국어", LocaleText.Select("English","한국어","한국어 (English)","kr-en"), "legacy override cannot force generic annotation");
        Equal("업적 (Achievement)",Show("achievement_title","업적","Achievement"),"achievement title inline");
        Equal("업적",Show("achievement_title","업적","Achievement","kr"),"achievement Korean only");
        Equal("한국어 (" + new string('X',100) + ")", Show("quest_title", en:new string('X',100)), "long quest remains inline");

        var rows = new[] { Row("item Name"), Row("item Description"), Row("item ShortName"), Row("q name"),
            Row("q startedMessageText"), Row("q Name"), Row("q Description"), Row("trader FirstName"),
            Row("trader Description"), Row("a name"), Row("a description"), Row("a Name",type:"default"),
            Row("clothes",type:"item_name"), Row("clothes description",type:"item_description") };
        var resolver = new LocaleDisplayRules(rows);
        var expected = new[] {"item_name","item_description","default","quest_title","default","quest_title","default","default","default","default","default","default","item_name","item_description"};
        for (int i=0;i<rows.Length;i++) Equal(expected[i],resolver.Resolve(rows[i]),rows[i].key);
        bool rejected=false;
        try { resolver.Resolve(Row("bad",type:"invalid")); } catch(InvalidDataException) { rejected=true; }
        Equal(true,rejected,"bad display_type rejected");

        string price="Per Slot: 10\nTotal: 20\n<color=#ff0000>Flea Banned</color>\n\n";
        Equal(price+"한국어\n(Test Mod)", ClientLocaleModOverlay.Translate("x Description",price+"English","English",Show("item_description"),true),"price decoration");
        string ammo=" <color=#808080>[40/30]</color>";
        Equal("한국어\n(English)"+ammo,ClientLocaleModOverlay.Translate("x Name","English"+ammo,"English",Show("item_name"),true),"ammo decoration");
        Equal(null,ClientLocaleModOverlay.Translate("x Name","Changed","English","한국어",true),"source mismatch");
        Equal(" 한국어 ",ClientLocaleModOverlay.Translate("x Name"," English ","English","한국어",true),"matching boundary spaces");
    }

    private static ModTexts Profile(string id, params TextRule[] rows)
        => new ModTexts { schema_version=1,target_spt="4.1.6",mod_id=id,mod_name=id,channels=new Dictionary<string,List<TextRule>> { ["locale"]=rows.ToList() } };

    private static void AdditionContracts(string work)
    {
        var root=Path.Combine(work,"addition-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"translations"));
        var a=Profile("Legacy"); a.channels["locale_additions"]=new List<TextRule>{Row("ui","UI","버튼")};
        var b=Profile("Merged"); b.channels["locale_additions"]=new List<TextRule>{Row("ui","UI","버튼"),Row("next","NEXT","다음")};
        var pa=Path.Combine(root,"translations","A.json");var pb=Path.Combine(root,"translations","B.json");
        File.WriteAllText(pa,JsonConvert.SerializeObject(a));File.WriteAllText(pb,JsonConvert.SerializeObject(b));
        foreach(var culture in new[]{"kr","kr-en"})
        {
            var raw=new Dictionary<string,string>();var result=new Dictionary<string,string>{{"ui","버튼"}};
            new ClientLocaleModOverlay(root,"4.1.6").Apply(culture,raw,result,0,null);
            var report=ReadReport(Path.Combine(root,"mod-locales","reports",culture+".json"));
            Equal(JTokenType.Null,report["load_error"].Type,"identical duplicate accepted: "+report["load_error"]);
            Equal("다음",result["next"],"following addition preserved");
            Equal(0,(int)report["runtime_locale_addition_conflicts"],"already Korean is not conflict");
            Equal(1,(int)report["runtime_locale_additions_already_translated"],"already Korean counted");
        }
        b.channels["locale_additions"][0].translation="충돌";File.WriteAllText(pb,JsonConvert.SerializeObject(b));
        var output=new Dictionary<string,string>{{"ui","다른 번역"}};
        new ClientLocaleModOverlay(root,"4.1.6").Apply("kr",new Dictionary<string,string>(),output,0,null);
        var error=ReadReport(Path.Combine(root,"mod-locales","reports","kr.json"));
        Equal(true,((string)error["load_error"]).Contains("Conflicting locale addition"),"real duplicate conflict reported");
        Equal("다음",output["next"],"conflict does not discard subsequent rows");
        Equal("다른 번역",output["ui"],"existing other translation protected");
        Equal(1,(int)error["runtime_locale_addition_conflicts"],"real runtime conflict retained");
    }

    private static void OverlayContracts(string work)
    {
        var root=Path.Combine(work,"fixture-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"translations"));
        var first=Profile("First",Row("x Name"),Row("x Description"),Row("q name"),Row("q startedMessageText"),Row("q Name"),Row("absent Name"));
        first.channels["locale_additions"]=new List<TextRule> {Row("ui","UI","버튼")};
        first.channels["locale_source_fallback"]=new List<TextRule> {new TextRule { source="Achievement",translation="업적",match_mode="exact" } };
        File.WriteAllText(Path.Combine(root,"translations","A.json"),JsonConvert.SerializeObject(first));
        File.WriteAllText(Path.Combine(root,"translations","B.json"),JsonConvert.SerializeObject(Profile("Second",Row("x Name","English","다른 번역"),Row("x Name alternate","Alternate","대체"),Row("unique Name"))));
        // A source variant for the same key in another profile must still be usable.
        File.WriteAllText(Path.Combine(root,"translations","C.json"),JsonConvert.SerializeObject(Profile("Third",Row("x Name","Alternate","대체"))));
        var overlay=new ClientLocaleModOverlay(root,"4.1.6");
        var raw=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) { ["x Name"]="English",["x Description"]="English",["q name"]="English",["q startedMessageText"]="English",["unique Name"]="English",["achievement"]="Achievement" };
        foreach(var culture in new[] {"kr","kr-en","en","kr","kr-en","kr-en"})
        {
            var result=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase);
            result["parentTranslated"]="부모 번역";
            overlay.Apply(culture,raw,result,0,raw);
            Equal(culture=="en"?"English":culture=="kr"?"한국어":"한국어\n(English)",result["x Name"],"switch name "+culture);
            Equal(culture=="en"?"English":"[English]\n한국어\n(First)",result["x Description"],"switch description "+culture);
            Equal("부모 번역",result["parentTranslated"],"parent translation preserved");
            if(culture!="en")
            {
                Equal(culture=="kr"?"한국어":"한국어\n(English)",result["unique Name"],"duplicate doesn't reject whole profile");
                Equal("버튼",result["ui"],"addition has no automatic annotation");
                Equal("업적",result["achievement"],"unclassified source fallback has no annotation");
                overlay.Apply(culture,raw,result,0,raw);
                Equal("한국어 (English)",result["q name"],"repeat merge no accumulation");
            }
        }
        var alternate=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase); alternate["x Name"]="Alternate";
        var changed=new Dictionary<string,string>(alternate,StringComparer.OrdinalIgnoreCase);
        overlay.Apply("kr-en",alternate,changed,0,raw);
        Equal("대체\n(Alternate)",changed["x Name"],"matching source variant");
        var conflict=ReadReport(Path.Combine(root,"mod-locales/reports/kr.json"));
        Equal(true,((JArray)conflict["candidate_conflicts"]).Count>0,"duplicate conflict reported");
        var copyRaw=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase); copyRaw["x Name"]="Changed";
        var copyResult=new Dictionary<string,string>(copyRaw,StringComparer.OrdinalIgnoreCase);
        copyResult["achievement"]="부모 업적"; overlay.Apply("kr-en",copyRaw,copyResult,0,raw);
        Equal("Changed",copyResult["x Name"],"overlay mismatch untouched");
        Equal("부모 업적",copyResult["achievement"],"fallback parent protection");
        first.mod_name="Renamed";
        File.WriteAllText(Path.Combine(root,"translations","A.json"),JsonConvert.SerializeObject(first));
        var reloaded=new ClientLocaleModOverlay(root,"4.1.6");
        reloaded.Apply("kr-en",raw,copyResult,0,raw);
        Equal("[English]\n한국어\n(Renamed)",copyResult["x Description"],"mod name single source after reload");
    }

    private static void CloneSuffixContracts(string work)
    {
        var root=Path.Combine(work,"clone-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"translations"));
        var clone=new ModTexts
        {
            schema_version=1,target_spt="4.1.6",mod_id="Clone",mod_name="Clone Mod",
            channels=new Dictionary<string,List<TextRule>>
            {
                ["locale_clone_suffix"]=new List<TextRule>
                {
                    new TextRule
                    {
                        id="clone",key="child",parent_key="parent",source="[Anti]",translation="[안티]",
                        description_source="Extra",description_translation="추가 설명입니다.",match_mode="exact"
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(root,"translations","Clone.json"),JsonConvert.SerializeObject(clone));
        var overlay=new ClientLocaleModOverlay(root,"4.1.6");
        var raw=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["parent Name"]="Parent",["child Name"]="Parent [Anti]",
            ["parent ShortName"]="P",["child ShortName"]="P",
            ["parent Description"]="Base",["child Description"]="Base\nExtra"
        };
        var english=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase)
        {
            ["parent Name"]="Parent English"
        };

        var kr=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase)
        {
            ["parent Name"]="부모 이름",["parent ShortName"]="부모",["parent Description"]="부모 설명"
        };
        overlay.Apply("kr",raw,kr,0,english);
        Equal("부모 이름 [안티]",kr["child Name"],"clone Korean name");
        Equal("부모",kr["child ShortName"],"clone Korean short name");
        Equal("[Parent English [Anti]]\n부모 설명\n추가 설명입니다.\n(Clone Mod)",kr["child Description"],"clone Korean description");

        var bilingual=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase)
        {
            ["parent Name"]="부모 이름\n(Parent English)",["parent ShortName"]="부모",["parent Description"]="부모 설명"
        };
        overlay.Apply("kr-en",raw,bilingual,0,english);
        Equal("부모 이름 [안티]\n(Parent English [Anti])",bilingual["child Name"],"clone bilingual name");
        Equal("[Parent English [Anti]]\n부모 설명\n추가 설명입니다.\n(Clone Mod)",bilingual["child Description"],"clone bilingual description");

        var changedRaw=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase) { ["child Name"]="Parent [Changed]" };
        var changed=new Dictionary<string,string>(changedRaw,StringComparer.OrdinalIgnoreCase)
        {
            ["parent Name"]="부모 이름",["parent Description"]="부모 설명"
        };
        overlay.Apply("kr",changedRaw,changed,0,english);
        Equal("Parent [Changed]",changed["child Name"],"clone source guard");
    }

    private static void DataContracts(string addon,string manifest,string work)
    {
        var report=JObject.Parse(File.ReadAllText(manifest));
        var expected=((JArray)report["rows"]).ToDictionary(r=>(string)r["file"]+"\n"+(string)r["key"],r=>(string)r["type"],StringComparer.Ordinal);
        var copy=Path.Combine(work,"data-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(copy,"translations"));
        foreach(var p in Directory.GetFiles(Path.Combine(addon,"translations"),"*.json")) File.Copy(p,Path.Combine(copy,"translations",Path.GetFileName(p)));
        var logs=new List<string>(); MinimalLog.Reset(); MinimalLog.WarningSink=logs.Add;
        var profiles=new EditableProfiles(copy);
        Equal(Directory.GetFiles(Path.Combine(copy,"translations"),"*.json").Length,profiles.Documents.Count,"all profiles load");
        int total=0;
        foreach(var p in Directory.GetFiles(Path.Combine(copy,"translations"),"*.json"))
        {
            var doc=JsonConvert.DeserializeObject<ModTexts>(File.ReadAllText(p));
            if(!doc.channels.TryGetValue("locale",out var rows)) continue;
            var resolver=new LocaleDisplayRules(rows);
            foreach(var row in rows)
            {
                var kind=resolver.Resolve(row); total++;
                Equal(expected[Path.GetFileName(p)+"\n"+row.key],kind,"classification "+row.key);
                var expectedKorean = row.translation;
                if(kind=="item_description" || kind=="quest_description" || kind=="achievement_description") expectedKorean=row.translation.TrimEnd()+"\n("+doc.mod_name.Trim()+")";
                if(kind=="quest_title" && row.translation.Trim()!=row.source.Trim()) expectedKorean=row.translation.TrimEnd()+" ("+row.source.Trim()+")";
                if(kind=="quest_objective" && row.translation.Trim()!=row.source.Trim()) expectedKorean=row.translation.TrimEnd()+"\n("+row.source.Trim()+")";
                Equal(expectedKorean,LocaleDisplayRules.Select(row.source,row.translation,row.translation_bilingual,kind,doc.mod_name,"kr"),"Korean "+row.key);
                Equal(row.source,LocaleDisplayRules.Select(row.source,row.translation,row.translation_bilingual,kind,doc.mod_name,"en"),"English "+row.key);
                if(kind!="default")
                {
                    var shown=LocaleDisplayRules.Select(row.source,row.translation,null,kind,doc.mod_name,"kr-en");
                    if(kind=="item_description" || kind=="quest_description" || kind=="achievement_description") Equal(row.translation.TrimEnd()+"\n("+doc.mod_name.Trim()+")",shown,"credit "+row.key);
                    else if(row.translation.Trim()!=row.source.Trim()) Equal(row.translation.TrimEnd()+(kind=="quest_title"||kind=="achievement_title"?" (":"\n(")+row.source.Trim()+")",shown,"annotation "+row.key);
                }
            }
        }
        Equal(expected.Count,total,"all rows checked");
        var profileSnapshot=JsonConvert.SerializeObject(profiles.Documents);
        var overlay=new ClientLocaleModOverlay(copy,"4.1.6",profiles);
        Equal(profileSnapshot,JsonConvert.SerializeObject(profiles.Documents),"shared source rows remain unchanged");
        var release=Assembly.LoadFrom(Path.Combine(addon,"bin/Release/net48/SPT_Mod_Korean_Addon.dll"));
        var overlayType=release.GetType("SPT.ModKoreanAddon.ClientLocaleModOverlay",true);
        var releaseOverlay=Activator.CreateInstance(overlayType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{copy,"4.1.6",null},null);
        var releaseApply=overlayType.GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic);
        object baselineOverlay=null; MethodInfo baselineApply=null;
        var baselinePath=Environment.GetEnvironmentVariable("SPT_CONTRACT_BASELINE");
        if(!string.IsNullOrEmpty(baselinePath))
        {
            var baselineType=Assembly.LoadFile(Path.GetFullPath(baselinePath)).GetType("SPT.ModKoreanAddon.ClientLocaleModOverlay",true);
            var baselineRoot=Path.Combine(work,"baseline-data");Directory.CreateDirectory(Path.Combine(baselineRoot,"translations"));
            foreach(var file in Directory.GetFiles(Path.Combine(addon,"translations"),"*.json")) File.Copy(file,Path.Combine(baselineRoot,"translations",Path.GetFileName(file)),true);
            baselineOverlay=Activator.CreateInstance(baselineType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{baselineRoot,"4.1.6"},null);
            baselineApply=baselineType.GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic);
        }
        Equal(false,logs.Any(l=>l.Contains("Rejected")||l.Contains("rejected")),"no profile/group rejection");
        var raw=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var doc in profiles.Documents)
            if(doc.channels.TryGetValue("locale",out var rows)) foreach(var row in rows) if(!raw.ContainsKey(row.key)) raw.Add(row.key,row.source);
        foreach(var culture in new[] {"kr","kr-en"})
        {
            var result=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase);
            overlay.Apply(culture,raw,result,0,raw);
            var productionResult=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase);
            releaseApply.Invoke(releaseOverlay,new object[]{culture,raw,productionResult,0,raw});
            if(baselineApply!=null)
            {
                var before=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase);
                baselineApply.Invoke(baselineOverlay,new object[]{culture,raw,before,0,raw});
                Equal(before.Count,productionResult.Count,"1.7.20 key count unchanged");
                foreach(var pair in before) Equal(pair.Value,productionResult[pair.Key],"1.7.20 output preserved "+pair.Key);
            }
            Equal(result.Count,productionResult.Count,"production overlay key count");
            foreach(var pair in result) Equal(pair.Value,productionResult[pair.Key],"release output equals contract output "+pair.Key);
            Equal(false,Directory.Exists(Path.Combine(copy,"mod-locales")),"no runtime diagnostic directory");
            var diagnostic=ReadReport(Path.Combine(copy,"mod-locales/reports",culture+".json"));
            Equal(JTokenType.Null,diagnostic["load_error"].Type,"no locale group load error");
            Equal(0,((JArray)diagnostic["unapplied"]).Count,"all present source keys applied");
            Equal(raw.Count,(int)diagnostic["applied"],"all unique keys applied");
            Console.WriteLine(culture+": applied="+diagnostic["applied"]+", candidate conflicts="+((JArray)diagnostic["candidate_conflicts"]).Count);
        }
        Console.WriteLine("Profiles="+profiles.Documents.Count+", locale rows="+total);
    }

    private static void CorrectedPolicyContracts()
    {
        foreach(var culture in new[]{"kr","kr-en"})
        {
            Equal("[Original (50 pcs)]\n본문 (FMJ)\n\n둘째 문단\n(Mod)",
                LocaleDisplayRules.Select("English description","본문 (FMJ)\n\n둘째 문단",null,"item_description","Mod",culture,"Original (50 pcs)"),"description name header and semantic parentheses");
            Equal("[Original]\n본문\n(Mod)",LocaleDisplayRules.Select("Description","[Original]\n본문\n(Mod)",null,"item_description","Mod",culture,"Original"),"already annotated description not doubled");
            Equal("본문\n(Mod)",LocaleDisplayRules.Select("Description","본문",null,"item_description","Mod",culture,null),"missing name never substitutes description");
            foreach(var mode in new[]{"exact","segment","template","segment_template","regex_segment"})
            {
                var dynamic = mode.Contains("template") || mode=="regex_segment";
                var source = mode=="regex_segment" ? @"Count (?<count>\d+)" : dynamic ? "Count {count}" : "Count";
                var ko=dynamic?"개수 {count}":"개수";
                var rules=new TextRules(new[]{new TextRule {id="message",source=source,translation=ko,translation_bilingual=ko+" (Count"+(dynamic?" {count}":"")+")",match_mode=mode}});
                var input=dynamic?"Count 12":"Count";
                Equal(dynamic?"개수 12":"개수",rules.Translate(input,culture),"UI/message "+mode+" "+culture);
                Equal(input,rules.Translate(input,"en"),"source message "+mode);
            }
        }
        var names=new LocaleDisplayRules(new[]{Row("clothes","Original","의상"),Row("clothes description","Description","설명","item_description")});
        Equal("Original",names.NameSource(Row("clothes description")),"bare clothing name lookup");
    }

    private static void ParentPayloadContracts(string folder)
    {
        var en=JObject.Parse(File.ReadAllText(Path.Combine(folder,"en.json")));
        var kr=JObject.Parse(File.ReadAllText(Path.Combine(folder,"kr.json")));
        var bi=JObject.Parse(File.ReadAllText(Path.Combine(folder,"kr-en.json")));
        int names=0,headers=0,quests=0;
        foreach(var p in en.Properties())
        {
            var key=p.Name;var source=(string)p.Value;var korean=(string)kr[key];var bilingual=(string)bi[key];
            if(key.EndsWith(" Name") && bilingual==korean+"\n("+source+")")
            {
                Equal(bilingual,LocaleDisplayRules.Select(source,korean,null,"item_name",null,"kr-en"),"actual parent item name "+key);names++;
            }
            if(key.EndsWith(" Description"))
            {
                var name=(string)en[key.Substring(0,key.Length-12)+" Name"];
                var header="["+name+"]\n";
                if(!string.IsNullOrEmpty(name) && korean==bilingual && korean.StartsWith(header,StringComparison.Ordinal))
                {
                    foreach(var culture in new[]{"kr","kr-en"})
                        Equal(korean,LocaleDisplayRules.Select(source,korean.Substring(header.Length),null,"item_description",null,culture,name),"actual parent description "+key);
                    headers++;
                }
            }
            if(key.EndsWith(" name") && !string.IsNullOrEmpty(source) && korean==bilingual && korean.EndsWith(" ("+source+")",StringComparison.Ordinal))
            {
                var body=korean.Substring(0,korean.Length-source.Length-3);
                foreach(var culture in new[]{"kr","kr-en"})
                    Equal(korean,LocaleDisplayRules.Select(source,body,null,"quest_title",null,culture),"actual parent quest title "+key);
                quests++;
            }
        }
        Equal(true,names>3900 && headers>4000 && quests>550,"parent payload coverage");
        foreach(var key in new[]{"54cb50c76803fa8b248b4571 Nickname","5936d90786f7742b1420ba5b description","5936d90786f7742b1420ba5b successMessageText"})
        {
            Equal((string)kr[key],(string)bi[key],"parent unannotated surface "+key);
            Equal((string)bi[key],LocaleText.Select((string)en[key],(string)kr[key],null,"kr-en"),"parent unannotated output "+key);
        }
        Console.WriteLine("Parent golden values: names="+names+", descriptions="+headers+", shared quest titles="+quests);
    }

    private static void ConfigContracts(string addon, string work)
    {
        var copy=Path.Combine(work,"config-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(copy,"config-ui"));
        var files=Directory.GetFiles(Path.Combine(addon,"config-ui"),"*.json").Where(p=>!Path.GetFileName(p).StartsWith("_")).ToArray();
        foreach(var p in files) File.Copy(p,Path.Combine(copy,"config-ui",Path.GetFileName(p)));
        var profiles=new ConfigManagerProfiles(copy);
        Equal(files.Length,profiles.ProfileCount,"all config profiles load");
        LocaleMode.Initialize(Assembly.GetExecutingAssembly());
        foreach(var culture in new[]{"kr-en","kr","en","kr-en"})
        {
            KoreanPatchFix.ClientLocaleRuntime.Culture=culture;LocaleMode.Refresh();
            foreach(var file in files)
            {
                var doc=JsonConvert.DeserializeObject<ConfigUiProfile>(File.ReadAllText(file));
                foreach(var row in doc.entries)
                {
                    var source=new ConfigUiSource {plugin_guid=doc.plugin_guid,section=row.section,key=row.key,source_category=row.section,source_display_name=row.source_display_name,source_description=row.source_description};
                    var result=profiles.Resolve(source);
                    if(!string.IsNullOrEmpty(row.translation_display_name) && (culture!="en" || !string.IsNullOrEmpty(row.source_display_name)))
                        Equal(culture=="en"?row.source_display_name:row.translation_display_name,result?.display_name,"F12 display "+doc.plugin_guid+"/"+row.key);
                    if(!string.IsNullOrEmpty(row.translation_description) && (culture!="en" || !string.IsNullOrEmpty(row.source_description)))
                        Equal(culture=="en"?row.source_description:row.translation_description,result?.description,"F12 description "+doc.plugin_guid+"/"+row.key);
                    foreach(var value in row.values)
                        if(!string.IsNullOrEmpty(value.translation)) Equal(culture=="en"?value.source:value.translation,profiles.ResolveValue(source,value.source,value.source),"F12 enum "+row.key);
                }
            }
        }
    }

    private static void HeaderLookupContracts(string work)
    {
        var root=Path.Combine(work,"headers-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"translations"));
        var referenced=Row("clothes Description","Description","의류 설명");referenced.name_key="custom-name";
        File.WriteAllText(Path.Combine(root,"translations","Header.json"),JsonConvert.SerializeObject(Profile("Header Mod",Row("ammo Description","Description","탄약 설명"),referenced)));
        var overlay=new ClientLocaleModOverlay(root,"4.1.6");
        foreach(var culture in new[]{"kr","kr-en"})
        {
            var raw=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) { ["ammo Description"]="Description",["ammo Name"]=".22 LR <color=#808080>[40/30]</color>",["clothes Description"]="Description",["custom-name"]="English clothing" };
            var result=new Dictionary<string,string>(raw,StringComparer.OrdinalIgnoreCase);
            overlay.Apply(culture,raw,result,0,new Dictionary<string,string>());
            Equal("[.22 LR]\n탄약 설명\n(Header Mod)",result["ammo Description"],"raw untranslated ammo name fallback");
            Equal("[English clothing]\n의류 설명\n(Header Mod)",result["clothes Description"],"explicit name key fallback");
            raw["ammo Name"]="다른 모드의 한국어";overlay.Apply(culture,raw,result,0,new Dictionary<string,string>());
            Equal("탄약 설명\n(Header Mod)",result["ammo Description"],"never pretend a Korean runtime name is English");
        }
    }

    private static void AssemblyContracts(string addon, string parentPath)
    {
        var compiled=Assembly.LoadFrom(Path.Combine(addon,"bin/Release/net48/SPT_Mod_Korean_Addon.dll"));
        Equal(new Version(1,7,21,0),compiled.GetName().Version,"built release version");
        Equal(null,compiled.GetType("Program"),"test harness excluded from client DLL");
        var selector=compiled.GetType("SPT.ModKoreanAddon.LocaleDisplayRules",true).GetMethod("Select",BindingFlags.Static|BindingFlags.NonPublic);
        Equal("한국어 (English)",(string)selector.Invoke(null,new object[]{"English","한국어",null,"quest_title","Mod","kr-en",null}),"built DLL title behavior");
        Equal("한국어\n(English)",(string)selector.Invoke(null,new object[]{"English","한국어",null,"quest_objective","Mod","kr",null}),"built DLL objective behavior");
        Equal("[Item]\n한국어\n(Mod)",(string)selector.Invoke(null,new object[]{"English","한국어",null,"item_description","Mod","kr-en","Item"}),"built DLL description behavior");
        // Read metadata only: don't execute downloaded parent code or alter its trust/zone flags.
        using(var parent=Mono.Cecil.AssemblyDefinition.ReadAssembly(parentPath))
        {
            var bundle=parent.MainModule.GetType("KoreanPatchFix.ClientLocaleBundle");
            const string dictionary="System.Collections.Generic.Dictionary`2<System.String,System.String>";
            foreach(var field in new[]{"korean","bilingual","baseline"})
                Equal(dictionary,bundle.Fields.Single(f=>f.Name==field).FieldType.FullName,"actual parent field "+field);
            var merge=bundle.Methods.Single(m=>m.Name=="MergeGlobal");
            Equal(dictionary,merge.ReturnType.FullName,"actual parent MergeGlobal return");
            Equal("System.String",merge.Parameters[0].ParameterType.FullName,"actual parent culture parameter");
            Equal("System.Collections.Generic.IDictionary`2<System.String,System.String>",merge.Parameters[1].ParameterType.FullName,"actual parent source parameter");
            Equal("System.String",parent.MainModule.GetType("KoreanPatchFix.ClientLocaleRuntime").Methods.Single(m=>m.Name=="CurrentCulture").ReturnType.FullName,"actual parent culture contract");
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            ClientLocaleModOverlay.Diagnostic=(path,report)=>Reports[Path.GetFullPath(path)]=report;
            var warningMessages=new List<string>(); MinimalLog.Reset();MinimalLog.WarningSink=warningMessages.Add;
            int messageBuilds=0;
            for(int i=0;i<100;i++) MinimalLog.WarnOnce("same",()=>{messageBuilds++;return "failure";});
            Equal(1,warningMessages.Count,"repeat warning once");Equal(1,messageBuilds,"repeat warning does not format text");
            MinimalLog.WarnOnce("different",()=>"other");Equal(2,warningMessages.Count,"different failures retained");
            MinimalLog.Reset();
            UnitContracts();
            var work=Path.GetFullPath(args[0]); Directory.CreateDirectory(work);
            OverlayContracts(work);
            AdditionContracts(work);
            CloneSuffixContracts(work);
            CorrectedPolicyContracts();
            if(args.Length>4) ParentPayloadContracts(args[4]);
            HeaderLookupContracts(work);
            if(args.Length>1) { ConfigContracts(args[1],work); CasinoContracts(args[1]); }
            if(args.Length>2) DataContracts(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),work);
            if(args.Length>3) AssemblyContracts(Path.GetFullPath(args[1]),Path.GetFullPath(args[3]));
            if(args.Length>5) RuntimeSnapshotContracts(Path.GetFullPath(args[1]),Path.GetFullPath(args[5]),work);
            if(args.Length>6) MergeContracts(Path.GetFullPath(args[1]),Path.GetFullPath(args[6]));
            Console.WriteLine("PASS: "+checks+" assertions using production C# sources"); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void CasinoContracts(string addon)
    {
        const string id="com.mybutthasarash.sptcasino";
        var profiles=new EditableProfiles(addon);
        var examples=new[]{
            new[]{"small_ui","CASINO","카지노"},
            new[]{"horse_racing_ui","HORSE RACING","경마"},
            new[]{"horse_racing_ui","CLOSE","닫기"},
            new[]{"horse_racing_ui","RUN THE RACE","경주 시작"},
            new[]{"horse_racing_ui","GRAY GHOST","그레이 고스트"},
            new[]{"horse_racing_ui","THE DASH  5 furlongs","단거리  5펄롱"},
            new[]{"horse_racing_ui","WIN 1 GRAY GHOST","우승 1 그레이 고스트"}
        };
        foreach(var e in examples)
        {
            foreach(var culture in new[]{"kr","kr-en"})
                Equal(e[2],profiles.Translate(id,e[0],e[1],culture),"Casino screenshot "+culture+" "+e[1]);
            Equal(e[1],profiles.Translate(id,e[0],e[1],"en"),"Casino English "+e[1]);
        }
        using(var module=Mono.Cecil.ModuleDefinition.ReadModule(Path.Combine(addon,"bin/Release/net48/SPT_Mod_Korean_Addon.dll")))
        {
            var hooks=module.Types.Single(t=>t.FullName=="SPT.ModKoreanAddon.CasinoUiHooks");
            Equal(true,hooks.Methods.Any(m=>m.Name=="WriteText"),"write-time translator present");
            Equal(true,hooks.Methods.Any(m=>m.Name=="Transpile"),"audited call-site transpiler present");
            var plugin=module.Types.Single(t=>t.FullName=="SPT.ModKoreanAddon.Plugin");
            Equal(true,plugin.Methods.Single(m=>m.Name=="Start").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference mr && mr.DeclaringType.FullName==hooks.FullName && mr.Name=="Enable"),"startup enables Casino hook");
            Equal(false,plugin.Methods.Single(m=>m.Name=="Update").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference mr && mr.DeclaringType.FullName==hooks.FullName),"no Casino timer repair");
            Equal(false,module.Types.Single(t=>t.FullName=="SPT.ModKoreanAddon.SmallUiLiteralHooks").Methods.Any(m=>m.Name=="RefreshCasinoRenderedText"),"old Casino refresh removed");
        }
        Console.WriteLine("Casino screenshot translations and shipped DLL write-time wiring passed");
    }

    private static void MergeContracts(string addon, string work)
    {
        var oldRoot=Path.Combine(work,"before");
        var oldProfiles=new EditableProfiles(oldRoot);
        var current=new EditableProfiles(addon);
        var map=JObject.Parse(File.ReadAllText(Path.Combine(work,"merge-report.json")))["profile_ids"].ToObject<Dictionary<string,string>>();
        Equal(map.Count,oldProfiles.Documents.Count,"all pre-merge profiles loaded");
        Equal(map.Values.Distinct().Count(),current.Documents.Count,"all merged profiles loaded");
        foreach(var doc in oldProfiles.Documents)
        foreach(var channel in doc.channels)
        {
            Equal(oldProfiles.HasRules(doc.mod_id,channel.Key),current.HasRules(map[doc.mod_id],channel.Key),"merged channel connection "+doc.mod_id+"/"+channel.Key);
            if(!oldProfiles.HasRules(doc.mod_id,channel.Key)) continue;
            foreach(var row in channel.Value)
            foreach(var culture in new[]{"kr","kr-en","en"})
                Equal(oldProfiles.Translate(doc.mod_id,channel.Key,row.source,culture),current.Translate(map[doc.mod_id],channel.Key,row.source,culture),"merged UI behavior "+doc.mod_id+"/"+channel.Key+"/"+row.id);
        }
        var raw=JObject.Parse(File.ReadAllText(Path.Combine(work,"runtime-kr.json")))["data"].ToObject<Dictionary<string,string>>();
        var en=JObject.Parse(File.ReadAllText(Path.Combine(work,"runtime-en.json")))["data"].ToObject<Dictionary<string,string>>();
        var allowed=new HashSet<string>(JObject.Parse(File.ReadAllText(Path.Combine(work,"description-classification.json")))["changes"].Select(r=>(string)r["key"]),StringComparer.OrdinalIgnoreCase);
        var beforeOverlay=new ClientLocaleModOverlay(oldRoot,"4.1.6");
        // Use a temporary copy so tests never write reports into the source addon.
        var newRoot=Path.Combine(work,"after"); Directory.CreateDirectory(Path.Combine(newRoot,"translations"));
        foreach(var p in Directory.GetFiles(Path.Combine(addon,"translations"),"*.json")) File.Copy(p,Path.Combine(newRoot,"translations",Path.GetFileName(p)),true);
        var afterOverlay=new ClientLocaleModOverlay(newRoot,"4.1.6");
        foreach(var culture in new[]{"kr","kr-en","en"})
        {
            var input=culture=="en"?en:raw;
            var before=new Dictionary<string,string>(input,StringComparer.OrdinalIgnoreCase);
            var after=new Dictionary<string,string>(input,StringComparer.OrdinalIgnoreCase);
            beforeOverlay.Apply(culture,input,before,0,en); afterOverlay.Apply(culture,input,after,0,en);
            Equal(before.Count,after.Count,"merged locale key count "+culture);
            foreach(var kv in before)
                if(culture=="en" || !allowed.Contains(kv.Key)) Equal(kv.Value,after[kv.Key],"merge preserves existing output "+culture+" "+kv.Key);
        }
        Console.WriteLine("Merged profile channels and full real locale output preservation passed");
    }

    private static void RuntimeSnapshotContracts(string addon, string snapshots, string work)
    {
        var root=Path.Combine(work,"runtime-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"translations"));
        foreach(var p in Directory.GetFiles(Path.Combine(addon,"translations"),"*.json"))
            File.Copy(p,Path.Combine(root,"translations",Path.GetFileName(p)));
        var raw=JObject.Parse(File.ReadAllText(Path.Combine(snapshots,"runtime-kr.json")))["data"].ToObject<Dictionary<string,string>>();
        var en=JObject.Parse(File.ReadAllText(Path.Combine(snapshots,"runtime-en.json")))["data"].ToObject<Dictionary<string,string>>();
        var fixtures=JArray.Parse(File.ReadAllText(Path.Combine(snapshots,"runtime-expected.json")));
        var parentObjectives=JArray.Parse(File.ReadAllText(Path.Combine(snapshots,"parent-objectives.json")));
        Equal(1598,parentObjectives.Count,"parent audited objective sample count");
        foreach(var row in parentObjectives)
        {
            var source=((string)row["en"]).Trim();
            var expected=((string)row["kr"]).TrimEnd();
            var suffix="\n("+source+")";
            var body=expected.Substring(0,expected.Length-suffix.Length);
            foreach(var culture in new[]{"kr","kr-en"})
                Equal(((string)row[culture]).TrimEnd(),LocaleDisplayRules.Select(source,body,null,"quest_objective","Ignored",culture),"actual parent objective "+row["key"]+" "+culture);
        }
        var overlay=new ClientLocaleModOverlay(root,"4.1.6");
        foreach(var culture in new[]{"kr","kr-en","en","kr"})
        {
            var input=culture=="en"?en:raw;
            var result=new Dictionary<string,string>(input,StringComparer.OrdinalIgnoreCase);
            overlay.Apply(culture,input,result,0,en);
            foreach(var fixture in fixtures)
            {
                var key=(string)fixture["key"];
                Equal(culture=="en"?en[key]:(string)fixture[culture],result[key],"real server snapshot "+culture+" "+key);
            }
            // Preserve another mod's already Korean text instead of forcing stale English-source rows.
            Equal(input["5c1fd66286f7743c7b261f7b"],result["5c1fd66286f7743c7b261f7b"],"other Korean provider retained "+culture);
            if(culture!="en")
            {
                var first=new Dictionary<string,string>(result);
                overlay.Apply(culture,input,result,0,en);
                foreach(var f in fixtures) Equal(first[(string)f["key"]],result[(string)f["key"]],"repeat real snapshot "+(string)f["key"]);
                File.WriteAllText(Path.Combine(snapshots,"replay-"+culture+".json"),ReadReport(Path.Combine(root,"mod-locales/reports",culture+".json")).ToString());
            }
        }
        Console.WriteLine("Real runtime fixtures: "+fixtures.Count+" keys across kr/kr-en/en switches");
    }
}
