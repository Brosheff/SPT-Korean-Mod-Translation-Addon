using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using SPT.ModKoreanAddon;
using TMPro;
using UnityEngine;

namespace KoreanPatchFix
{
    public static class ClientLocaleRuntime
    {
        public static string Culture="kr"; public static int Queries;
        public static string CurrentCulture() { Queries++; return Culture; }
    }
}
namespace SPT.ModKoreanAddon
{
    internal static class ClientLocaleModOverlay { internal static Action<string> Log=Console.WriteLine; }
}
internal static class Program
{
    static int checks;
    static void Eq<T>(T expected,T actual,string label)
    { checks++; if(!Equals(expected,actual)) throw new Exception(label+": "+actual+" != "+expected); }
    static int Main(string[] args)
    {
        var patcher=new Harmony("casino.write.contract");
        try
        {
            LocaleMode.Initialize(typeof(Program).Assembly);
            var latin=new TMP_FontAsset { name="Latin" };
            var korean=new TMP_FontAsset { name="Korean",Korean=true };
            Resources.Assets=new object[]{latin,korean};
            Eq(0,CasinoUiHooks.Enable(patcher,new Assembly[0],new EditableProfiles(args[0])),"Casino absent");
            int absentQueries=KoreanPatchFix.ClientLocaleRuntime.Queries;
            Canvas.Render();Canvas.Render();
            Eq(absentQueries,KoreanPatchFix.ClientLocaleRuntime.Queries,"absent Casino does not query language on canvas");
            // Simulate AssemblyLoad through the same handler, then reset for full test.
            typeof(CasinoUiHooks).GetMethod("OnAssemblyLoad",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{null,new AssemblyLoadEventArgs(typeof(Casino.Client.CasinoTab).Assembly)});
            Canvas.Render();
            Eq(true,KoreanPatchFix.ClientLocaleRuntime.Queries>absentQueries,"late Casino attaches rendering");
            CasinoUiHooks.Disable();patcher.UnpatchSelf();
            var prebuilt=new TMP_Text(); prebuilt.text="HORSE RACING";
            HorseRacing.Client.RacePanel._root=new GameObject(); HorseRacing.Client.RacePanel._root.Children.Add(prebuilt);
            Eq(5,CasinoUiHooks.Enable(patcher,new[]{typeof(Casino.Client.CasinoTab).Assembly},new EditableProfiles(args[0])),"real Harmony fixture hooks");
            Casino.Client.CasinoTab.Relabel(null,null,"CASINO");
            Eq("카지노",Casino.Client.CasinoTab.Label.text,"tab immediately translated");
            Eq(false,Casino.Client.CasinoTab.Label.Writes.Contains("CASINO"),"no English setter before translation");
            var title=HorseRacing.Client.RacePanel.Text("title",null,"HORSE RACING",12,default(Color),TextAlignmentOptions.Left);
            Eq("경마",title.text,"title first assignment");
            Eq("HORSE RACING",HorseRacing.Client.RacePanel.Input.text,"input field untouched");
            Eq("HORSE RACING",HorseRacing.Client.RacePanel.ProtocolText,"game payload untouched");
            Eq(false,title.Writes.Contains("HORSE RACING"),"no transient English title");
            for(int i=0;i<20;i++)
            {
                HorseRacing.Client.RacePanel.SetStatus("WIN 1 GRAY GHOST");
                Eq("우승 1 그레이 고스트",HorseRacing.Client.RacePanel.Status.text,"dynamic immediate "+i);
            }
            Eq(false,HorseRacing.Client.RacePanel.Status.Writes.Any(x=>x.Contains("GRAY GHOST")),"no transient English on dynamic rewrites");
            HorseRacing.Client.RacePanel.SetBlurb(); Eq("단거리  5펄롱",HorseRacing.Client.RacePanel.Blurb.text,"composite original translated");
            title.font=latin; title.fontSharedMaterial=latin.material; // factory assigns after text
            Canvas.Render();
            Eq(korean,title.font,"Korean direct font selected after factory");
            Eq(true,title.fontSharedMaterial.White,"glyph face filled white before vertex tint");
            Eq(0f,title.fontSharedMaterial.Outline,"outline does not obscure Hangul");
            Eq(false,korean.material.White,"shared game font material untouched");
            Eq(1f,korean.material.Outline,"shared outline untouched");
            Eq("경마",prebuilt.text,"startup adoption before first render");
            int searches=Resources.Searches, checksBefore=korean.Checks, fontWrites=title.FontWrites, materialWrites=title.MaterialWrites;
            for(int i=0;i<400;i++) { CasinoTextStyle.Queue(title);Canvas.Render(); }
            Eq(searches,Resources.Searches,"stable font no periodic resource scan");
            Eq(checksBefore,korean.Checks,"identical text and actual font skip glyph checks");
            Eq(fontWrites,title.FontWrites,"identical font is not reassigned");
            Eq(materialWrites,title.MaterialWrites,"identical material is not reassigned");
            title.font=latin;title.fontSharedMaterial=latin.material;CasinoTextStyle.Queue(title);Canvas.Render();
            Eq(korean,title.font,"same text with factory font reset is repaired before render");
            Eq(false,ReferenceEquals(latin.material,title.fontSharedMaterial),"factory material reset repaired");
            int writes=title.Writes.Count+HorseRacing.Client.RacePanel.Status.Writes.Count;
            for(int i=0;i<120;i++) Canvas.Render();
            Eq(writes,title.Writes.Count+HorseRacing.Client.RacePanel.Status.Writes.Count,"stable frames perform no text repair");
            KoreanPatchFix.ClientLocaleRuntime.Culture="en";Canvas.Render();
            Eq("HORSE RACING",title.text,"English restored from stored original");
            Canvas.Render();
            Eq(latin,title.font,"English restores original font");
            Eq(latin.material,title.fontSharedMaterial,"English restores original material");
            Eq("WIN 1 GRAY GHOST",HorseRacing.Client.RacePanel.Status.text,"dynamic original restored exactly");
            Eq("THE DASH  5 furlongs",HorseRacing.Client.RacePanel.Blurb.text,"segment original restored exactly");
            KoreanPatchFix.ClientLocaleRuntime.Culture="kr-en";Canvas.Render();
            Eq("경마",title.text,"bilingual Korean UI without annotation");
            Eq("우승 1 그레이 고스트",HorseRacing.Client.RacePanel.Status.text,"dynamic Korean restored");
            Eq("CASINO",OtherPlugin.Screen.Build().text,"other plugin setter not patched");
            CasinoUiHooks.Disable();patcher.UnpatchSelf();
            Casino.Client.CasinoTab.Relabel(null,null,"CASINO");Eq("CASINO",Casino.Client.CasinoTab.Label.text,"disable restores original path");
            // Missing fonts may arrive after the label was constructed, without another text write.
            CasinoTextStyle.Enable(); Resources.Assets=new object[0];
            var delayed=new TMP_Text { text="경마",font=latin,fontSharedMaterial=latin.material };
            CasinoTextStyle.Queue(delayed);Canvas.Render();Eq(latin,delayed.font,"no font preserves original");
            Resources.Assets=new object[]{korean};
            for(int i=0;i<301;i++) Canvas.Render();
            Eq(korean,delayed.font,"late font retry handles static label");
            korean.Missing="새";
            var second=new TMP_FontAsset {name="More Korean",Korean=true};Resources.Assets=new object[]{korean,second};
            delayed.text="새";CasinoTextStyle.Queue(delayed);Canvas.Render();
            for(int i=0;i<301;i++) Canvas.Render();
            Eq(second,delayed.font,"new glyph searches another compatible font");
            CasinoTextStyle.Disable();Eq(latin,delayed.font,"disable restores before destroying material");
            AuditRealDll(args[1],args[2]);
            Console.WriteLine("PASS: "+checks+" Casino write-time assertions; fake render sink records every actual setter call.");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { CasinoUiHooks.Disable();patcher.UnpatchSelf(); }
    }
    static IEnumerable<Mono.Cecil.TypeDefinition> Types(IEnumerable<Mono.Cecil.TypeDefinition> types)
    { foreach(var type in types) { yield return type; foreach(var child in Types(type.NestedTypes)) yield return child; } }
    static void AuditRealDll(string casinoPath,string addonPath)
    {
        using(var real=Mono.Cecil.ModuleDefinition.ReadModule(casinoPath))
        {
            var targets=(Array)typeof(CasinoUiHooks).GetField("Targets",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            foreach(var target in targets)
            {
                var t=target.GetType(); Func<string,object> get=n=>t.GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
                string name=(string)get("Type"),method=(string)get("Method");int parameters=(int)get("Parameters"),expected=(int)get("Writes");
                var owner=Types(real.Types).Single(x=>x.FullName.Replace('/','+')==name);
                var body=owner.Methods.Single(m=>m.Name==method && m.Parameters.Count==parameters);
                Eq(expected,body.Body.Instructions.Count(i=>i.Operand is Mono.Cecil.MethodReference m && m.FullName=="System.Void TMPro.TMP_Text::set_text(System.String)"),"installed DLL exact write sites "+name+"."+method);
            }
        }
        using(var addon=Mono.Cecil.ModuleDefinition.ReadModule(addonPath))
        {
            var small=addon.Types.Single(t=>t.Name=="SmallUiLiteralHooks");
            Eq(false,small.Methods.Any(m=>m.Name=="RefreshCasinoRenderedText"),"old timer repair removed from binary");
            var update=addon.Types.Single(t=>t.Name=="Plugin").Methods.Single(m=>m.Name=="Update");
            Eq(false,update.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name=="CasinoUiHooks"),"Update has no Casino polling");
        }
    }
}
