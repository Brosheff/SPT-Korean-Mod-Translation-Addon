using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    internal static class CasinoTextStyle
    {
        private sealed class Saved
        {
            internal TMP_FontAsset Font, AppliedFont;
            internal Material Material, AppliedMaterial;
            internal string Text;
            internal bool HasOriginal, Retrying;
        }
        private static ConditionalWeakTable<TMP_Text,Saved> saved = new ConditionalWeakTable<TMP_Text,Saved>();
        private static readonly HashSet<TMP_Text> pending = new HashSet<TMP_Text>();
        private static readonly List<WeakReference> tracked = new List<WeakReference>();
        private static readonly List<WeakReference> retry = new List<WeakReference>();
        private static readonly Dictionary<TMP_FontAsset,Material> materials = new Dictionary<TMP_FontAsset,Material>();
        private static TMP_FontAsset[] fonts;
        private static int searchedFrame=-1000;
        internal static void Enable() { Canvas.preWillRenderCanvases += Flush; }
        internal static void Queue(TMP_Text label) { if(label!=null) pending.Add(label); }
        internal static void Disable()
        {
            Canvas.preWillRenderCanvases -= Flush;
            // Restore surviving labels before destroying materials owned by this addon.
            foreach(var reference in tracked)
            {
                var label=reference.Target as TMP_Text;
                if(label!=null && saved.TryGetValue(label,out var state) && state.HasOriginal &&
                    label.font==state.AppliedFont && label.fontSharedMaterial==state.AppliedMaterial)
                { label.font=state.Font; label.fontSharedMaterial=state.Material; }
            }
            pending.Clear(); tracked.Clear(); retry.Clear(); saved=new ConditionalWeakTable<TMP_Text,Saved>();
            foreach(var material in materials.Values) UnityEngine.Object.Destroy(material);
            materials.Clear(); fonts=null; searchedFrame=-1000;
        }
        private static bool Hangul(string text)
        {
            if(text==null) return false;
            foreach(char c in text)
                if((c>='\uac00' && c<='\ud7a3') || (c>='\u3130' && c<='\u318f') || (c>='\u1100' && c<='\u11ff')) return true;
            return false;
        }
        private static bool Supports(TMP_FontAsset font,string text)
        {
            if(font==null || font.material==null) return false;
            foreach(char c in text) if(!char.IsControl(c) && !font.HasCharacter(c)) return false;
            return true;
        }
        private static void Flush()
        {
            // Only unresolved fonts retry, with weak references and a bounded frequency.
            if(retry.Count>0 && Time.frameCount-searchedFrame>=300)
            {
                foreach(var reference in retry)
                {
                    var label=reference.Target as TMP_Text;
                    if(label==null) continue;
                    if(saved.TryGetValue(label,out var state)) state.Retrying=false;
                    pending.Add(label);
                }
                retry.Clear();
            }
            foreach(var label in pending)
            {
                if(label==null) continue;
                try { Apply(label); }
                catch(Exception ex) { SPT.EditableTranslations.MinimalLog.WarnOnce("casino-font",()=>"Font adjustment skipped: "+ex.Message); }
            }
            pending.Clear();
        }
        private static void Apply(TMP_Text label)
        {
            if(!saved.TryGetValue(label,out var state))
            {
                state=new Saved(); saved.Add(label,state); tracked.Add(new WeakReference(label));
                if(tracked.Count%128==0) tracked.RemoveAll(w=>w.Target as TMP_Text==null);
            }
            var text=label.text;
            // Factories may reset fonts AFTER text assignment. Compare actual state here,
            // at pre-render, never skip Queue merely because the source text is unchanged.
            if(state.HasOriginal && state.AppliedFont!=null && state.AppliedMaterial!=null &&
                text==state.Text && label.font==state.AppliedFont && label.fontSharedMaterial==state.AppliedMaterial) return;
            if(!Hangul(text))
            {
                if(state.HasOriginal)
                { label.font=state.Font; label.fontSharedMaterial=state.Material; state.HasOriginal=false; }
                state.Text=null; state.AppliedFont=null; state.AppliedMaterial=null;
                return;
            }
            var font=state.AppliedFont;
            if(text!=state.Text || font==null || state.AppliedMaterial==null)
            {
                if(!Supports(font,text))
                {
                    font=null;
                    if(fonts!=null) foreach(var candidate in fonts)
                        if(Supports(candidate,text)) { font=candidate; break; }
                    if(font==null && (fonts==null || Time.frameCount-searchedFrame>=300))
                    {
                        fonts=Resources.FindObjectsOfTypeAll<TMP_FontAsset>(); searchedFrame=Time.frameCount;
                        foreach(var candidate in fonts)
                            if(candidate!=null && candidate.HasCharacter('가') && Supports(candidate,text)) { font=candidate; break; }
                    }
                }
            }
            if(font==null)
            {
                if(!state.Retrying) { state.Retrying=true; retry.Add(new WeakReference(label)); }
                SPT.EditableTranslations.MinimalLog.WarnOnce("casino-font-unavailable",()=>"Korean font not ready; original appearance retained while waiting for a compatible font.");
                return;
            }
            if(!state.HasOriginal)
            { state.Font=label.font; state.Material=label.fontSharedMaterial; state.HasOriginal=true; }
            if(!materials.TryGetValue(font,out var material) || material==null)
            {
                material=new Material(font.material); material.name="Casino Korean readable ("+font.name+")";
                if(material.HasProperty("_FaceColor")) material.SetColor("_FaceColor",Color.white);
                if(material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth",0f);
                materials[font]=material;
            }
            if(label.font!=font) label.font=font;
            if(label.fontSharedMaterial!=material) label.fontSharedMaterial=material;
            state.Text=text; state.AppliedFont=font; state.AppliedMaterial=material;
        }
    }
}
