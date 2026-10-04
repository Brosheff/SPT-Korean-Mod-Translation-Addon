using SPT_Mod_Korean_Server;
using System.Text.Json;

int checks=0;
void Equal<T>(T expected,T actual,string label)
{
    checks++;
    if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception(label+": expected "+expected+", got "+actual);
}
foreach(var mode in new[]{"exact","segment","template","segment_template","regex_segment"})
{
    bool dynamic=mode.Contains("template")||mode=="regex_segment";
    string source=mode=="regex_segment"?@"Count (?<count>\d+)":dynamic?"Count {count}":"Count";
    string ko=dynamic?"개수 {count}":"개수";
    var rules=new ServerTextRules(new[]{new TextRule{id="mail",source=source,translation=ko,translation_bilingual=ko+" (Count"+(dynamic?" {count}":"")+")",match_mode=mode}});
    string input=dynamic?"Count 12":"Count";
    foreach(var culture in new[]{"kr","kr-en"}) Equal(dynamic?"개수 12":"개수",rules.Translate(input,culture),mode+"/"+culture);
    foreach(var culture in new[]{"en","source",""}) Equal(input,rules.Translate(input,culture),mode+"/"+culture);
}
if(args.Length>0)
{
    foreach(var path in Directory.GetFiles(Path.Combine(args[0],"translations"),"*.json"))
    {
        var doc=JsonSerializer.Deserialize<ModTexts>(File.ReadAllText(path))!;
        foreach(var channel in new[]{"npc_messages","server_messages"})
        {
            if(!doc.channels.TryGetValue(channel,out var rows))continue;
            foreach(var row in rows.Where(r=>r.enabled))
            {
                var rules=new ServerTextRules(new[]{row});
                if(row.match_mode=="regex_segment")continue; // exercised with a rendered capture above
                Equal(row.translation,rules.Translate(row.source!,"kr"),path+"/"+row.id);
                Equal(row.translation,rules.Translate(row.source!,"kr-en"),path+"/"+row.id);
                Equal(row.source,rules.Translate(row.source!,"en"),path+"/"+row.id);
            }
        }
    }
}
if(args.Length>0)
{
    var warnings=new List<string>();
    var translator=MessageTranslator.FromProfiles(Path.Combine(args[0],"translations"),(file,message)=>warnings.Add(message),null!);
    Equal(0,warnings.Count,"actual server profiles load without rejection");
    Equal("Unmatched mail",translator.Translate("test-trader","Unmatched mail","kr"),"unmatched mail preserved");
    Equal(false,typeof(MessageTranslator).GetMethods(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Any(m=>m.Name=="RecordUntranslated"),"no untranslated-mail recorder");
}
Console.WriteLine("PASS server: "+checks+" assertions");
