using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Only audited Casino.Client text-write call sites are patched. TMP's setter itself,
    // input fields, game state, network payloads and other plugins are never patched.
    internal static class CasinoUiHooks
    {
        internal const string ProfileId = "com.mybutthasarash.sptcasino";
        private sealed class Target
        {
            internal readonly string Type, Method, Channel;
            internal readonly int Parameters, Writes;
            internal Target(string type, string method, int parameters, int writes, string channel)
            { Type=type; Method=method; Parameters=parameters; Writes=writes; Channel=channel; }
        }
        // Generated from the inspected Casino.Client 1.3.3 IL, then restricted to UI owners.
        private static readonly Target[] Targets = {
            new Target("Blackjack.Client.BlackjackPanel", "AskBetEverything", 0, 2, "small_ui"),
            new Target("Blackjack.Client.BlackjackPanel", "RenderRound", 1, 2, "small_ui"),
            new Target("Blackjack.Client.BlackjackPanel", "Say", 2, 1, "small_ui"),
            new Target("Blackjack.Client.BlackjackPanel", "UpdateHeld", 0, 4, "small_ui"),
            new Target("Blackjack.Client.BlackjackPanel", "Populate", 1, 4, "small_ui"),
            new Target("Blackjack.Client.BlackjackPanel", "Label", 5, 1, "small_ui"),
            new Target("Blackjack.Client.BlackjackPanel+<>c__DisplayClass69_0", "<RenderRound>b__0", 0, 1, "small_ui"),
            new Target("War.Client.WarPanel", "DrawRound", 2, 2, "casino_war_ui"),
            new Target("War.Client.WarPanel", "ShowHeadline", 1, 2, "casino_war_ui"),
            new Target("War.Client.WarPanel", "Headline", 2, 1, "casino_war_ui"),
            new Target("War.Client.WarPanel", "Say", 2, 1, "casino_war_ui"),
            new Target("War.Client.WarPanel", "RefreshBalances", 0, 1, "casino_war_ui"),
            new Target("War.Client.WarPanel", "UpdateHeld", 0, 4, "casino_war_ui"),
            new Target("War.Client.WarPanel", "Populate", 1, 3, "casino_war_ui"),
            new Target("War.Client.WarPanel", "Label", 5, 1, "casino_war_ui"),
            new Target("HorseRacing.Client.RacePanel", "MarkSettlements", 1, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.RacePanel", "RefreshBalance", 0, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.RacePanel", "SetStatus", 1, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.RacePanel", "SetResult", 2, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.RacePanel", "SetBlurb", 0, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.RacePanel", "RenderSlip", 0, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.RacePanel", "Text", 6, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.RacePanel", "SetWalletLabel", 0, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.TrackView", "BuildFurlongMarkers", 3, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.TrackView", "BuildLane", 5, 3, "horse_racing_ui"),
            new Target("HorseRacing.Client.TrackView", "MakeHorse", 4, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.TrackView", "Rewind", 0, 1, "horse_racing_ui"),
            new Target("HorseRacing.Client.TrackView+<Gallop>d__34", "MoveNext", 0, 1, "horse_racing_ui"),
            new Target("SlotMachine.Client.SlotPanel", "SetBalanceLabel", 0, 1, "small_ui"),
            new Target("SlotMachine.Client.SlotPanel", "SetSpinEnabled", 1, 1, "small_ui"),
            new Target("SlotMachine.Client.SlotPanel", "SetAutoRunning", 1, 1, "small_ui"),
            new Target("SlotMachine.Client.SlotPanel", "RenderSpeed", 0, 1, "small_ui"),
            new Target("SlotMachine.Client.SlotPanel", "Populate", 1, 4, "small_ui"),
            new Target("SlotMachine.Client.SlotPanel", "SetStake", 0, 1, "small_ui"),
            new Target("SlotMachine.Client.SlotPanel", "SetPaid", 2, 2, "small_ui"),
            new Target("SlotMachine.Client.SlotPanel", "SetStatus", 1, 1, "small_ui"),
            new Target("SlotMachine.Client.SlotPanel", "NewText", 4, 1, "small_ui"),
            new Target("Poker.Client.PokerPanel", "SetStatus", 1, 1, "small_ui"),
            new Target("Poker.Client.PokerPanel", "NewText", 5, 1, "small_ui"),
            new Target("Roulette.Client.RoulettePanel", "SetBalance", 1, 1, "small_ui"),
            new Target("Roulette.Client.RoulettePanel", "SetStatus", 1, 1, "small_ui"),
            new Target("Roulette.Client.RoulettePanel", "SetResult", 2, 1, "small_ui"),
            new Target("Roulette.Client.RoulettePanel", "NewText", 5, 1, "small_ui"),
            new Target("Roulette.Client.ClothView", "NewText", 3, 1, "small_ui"),
            new Target("Casino.Client.CasinoLobby", "NewText", 4, 1, "small_ui"),
            new Target("Casino.Client.CasinoTab", "Relabel", 3, 1, "small_ui"),
        };
        private sealed class Original
        {
            internal string Source, Channel;
        }
        private static readonly Dictionary<MethodBase,Target> Patched = new Dictionary<MethodBase,Target>();
        private static ConditionalWeakTable<TMP_Text,Original> Originals = new ConditionalWeakTable<TMP_Text,Original>();
        private static readonly List<WeakReference> Labels = new List<WeakReference>();
        private static readonly object Gate = new object();
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static Assembly casino;
        private static bool enabled, adoptExisting, renderingAttached;
        private static string renderedCulture;
        private static int lastRenderFrame = -1;

        internal static int Enable(Harmony patcher, Assembly[] assemblies, EditableProfiles data)
        {
            harmony=patcher; profiles=data; enabled=true;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            var assembly=assemblies.FirstOrDefault(a=>a.GetName().Name=="Casino.Client");
            return assembly == null ? 0 : Patch(assembly);
        }

        internal static void Disable()
        {
            enabled=false;
            if(renderingAttached) CasinoTextStyle.Disable();
            renderingAttached=false;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            Canvas.preWillRenderCanvases -= BeforeCanvasRender;
            lock(Gate) { Patched.Clear(); casino=null; adoptExisting=false; }
            Labels.Clear(); Originals=new ConditionalWeakTable<TMP_Text,Original>();
            profiles=null; harmony=null; renderedCulture=null; lastRenderFrame=-1;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (!enabled || args.LoadedAssembly.GetName().Name!="Casino.Client") return;
            try { Patch(args.LoadedAssembly); }
            catch(Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("CasinoUiHooks:114", () => "[Casino write-time] late-load skipped: "+ex.Message); }
        }

        private static int Patch(Assembly assembly)
        {
            int count=0;
            lock(Gate)
            {
                if (!enabled || harmony==null) return 0;
                if(!renderingAttached)
                {
                    // Register in this order: refresh text/culture, then finalize font before render.
                    Canvas.preWillRenderCanvases += BeforeCanvasRender;
                    CasinoTextStyle.Enable();
                    renderingAttached=true;
                }
                casino=assembly; adoptExisting=true;
                foreach(var target in Targets)
                {
                    if (!profiles.HasRules(ProfileId,target.Channel)) continue;
                    var type=assembly.GetType(target.Type,false);
                    var method=type?.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
                        .SingleOrDefault(m=>m.Name==target.Method && m.GetParameters().Length==target.Parameters);
                    if(method==null) { SPT.EditableTranslations.MinimalLog.WarnOnce("CasinoUiHooks:130", () => "[Casino write-time] missing audited method: "+target.Type+"."+target.Method); continue; }
                    if(Patched.ContainsKey(method)) continue;
                    Patched.Add(method,target);
                    try
                    {
                        harmony.Patch(method,transpiler:new HarmonyMethod(typeof(CasinoUiHooks),nameof(Transpile)));
                        count++;
                    }
                    catch(Exception ex)
                    {
                        Patched.Remove(method);
                        SPT.EditableTranslations.MinimalLog.WarnOnce("CasinoUiHooks:141", () => "[Casino write-time] skipped "+target.Type+"."+target.Method+": "+ex.Message);
                    }
                }
            }
            return count;
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var target=Patched[__originalMethod];
            var result=new List<CodeInstruction>(); int writes=0;
            foreach(var instruction in instructions)
            {
                var setter=instruction.operand as MethodInfo;
                if ((instruction.opcode==OpCodes.Callvirt || instruction.opcode==OpCodes.Call) && setter!=null &&
                    setter.Name=="set_text" && setter.ReturnType==typeof(void) &&
                    typeof(TMP_Text).IsAssignableFrom(setter.DeclaringType) &&
                    setter.GetParameters().Length==1 && setter.GetParameters()[0].ParameterType==typeof(string))
                {
                    // Original stack: TMP_Text, string. Add channel and call our typed writer.
                    // No raw English setter call occurs before the translated value is ready.
                    var load=new CodeInstruction(OpCodes.Ldstr,target.Channel);
                    var call=new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CasinoUiHooks),nameof(WriteText)));
                    load.labels.AddRange(instruction.labels);
                    foreach(var block in instruction.blocks)
                        (block.blockType==ExceptionBlockType.EndExceptionBlock ? call.blocks : load.blocks).Add(block);
                    result.Add(load); result.Add(call); writes++;
                }
                else result.Add(instruction);
            }
            if(writes!=target.Writes) throw new InvalidOperationException("Text-write signature changed: expected "+target.Writes+", got "+writes);
            return result;
        }

        private static void WriteText(TMP_Text target, string source, string channel)
        {
            if (!enabled || profiles==null) { target.text=source; return; }
            if(target==null) return;
            if(!Originals.TryGetValue(target,out var original))
            {
                original=new Original(); Originals.Add(target,original); Labels.Add(new WeakReference(target));
                if(Labels.Count%128==0) Labels.RemoveAll(w=>w.Target as TMP_Text == null);
            }
            original.Source=source; original.Channel=channel;
            target.text=profiles.Translate(ProfileId,channel,source);
            CasinoTextStyle.Queue(target);
        }

        private static void BeforeCanvasRender()
        {
            if(!enabled || profiles==null || lastRenderFrame==Time.frameCount) return;
            lastRenderFrame=Time.frameCount;
            // Resolve a real language transition before rendering. When unchanged, no labels
            // are enumerated or rewritten. This is not a timer repair after English is shown.
            var culture=LocaleMode.Refresh();
            bool adopt;
            lock(Gate) { adopt=adoptExisting; adoptExisting=false; }
            if(adopt) AdoptExisting(); // One-time support for UI built before addon initialization.
            if(string.Equals(culture,renderedCulture,StringComparison.OrdinalIgnoreCase)) return;
            renderedCulture=culture;
            for(int i=Labels.Count-1;i>=0;i--)
            {
                var label=Labels[i].Target as TMP_Text;
                if(label==null) { Labels.RemoveAt(i); continue; }
                if(Originals.TryGetValue(label,out var original))
                    { label.text=profiles.Translate(ProfileId,original.Channel,original.Source,culture); CasinoTextStyle.Queue(label); }
            }
        }

        private static void AdoptExisting()
        {
            if(casino==null) return;
            var roots=new[]{
                "Casino.Client.CasinoTab|_tab|small_ui", "Casino.Client.CasinoLobby|_root|small_ui",
                "Casino.Client.CasinoIntro|_root|small_ui", "Casino.Client.CasinoGift|_root|small_ui",
                "Blackjack.Client.BlackjackPanel|_root|small_ui", "Poker.Client.PokerPanel|_root|small_ui",
                "Roulette.Client.RoulettePanel|_root|small_ui", "SlotMachine.Client.SlotPanel|_root|small_ui",
                "HorseRacing.Client.RacePanel|_root|horse_racing_ui", "War.Client.WarPanel|_root|casino_war_ui"
            };
            foreach(var binding in roots)
            {
                var parts=binding.Split('|');
                var root=casino.GetType(parts[0],false)?.GetField(parts[1],BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(null) as GameObject;
                if(root==null) continue;
                foreach(var label in root.GetComponentsInChildren<TMP_Text>(true))
                    if(label!=null && !Originals.TryGetValue(label,out var _)) WriteText(label,label.text,parts[2]);
            }
        }
    }
}
