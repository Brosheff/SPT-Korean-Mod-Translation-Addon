using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

// Fake render sink: records every setter value, not only the eventual displayed text.
// No Unity renderer is emulated; production Harmony patching and translation code are used.
namespace UnityEngine
{
    public class Transform { }
    public class RectTransform : Transform { }
    public struct Color { public static Color white => new Color(); }
    public class Object { public static void Destroy(object value) { } }
    public class Material
    {
        public string name; public float Outline=1f; public bool White;
        public Material() { } public Material(Material source) { Outline=source.Outline; White=source.White; }
        public bool HasProperty(string name)=>true;
        public void SetColor(string name,Color value) { White=true; }
        public void SetFloat(string name,float value) { Outline=value; }
    }
    public static class Resources
    {
        public static object[] Assets=new object[0]; public static int Searches;
        public static T[] FindObjectsOfTypeAll<T>() { Searches++; return Assets.OfType<T>().ToArray(); }
    }
    public class GameObject
    {
        public readonly List<object> Children=new List<object>();
        public T[] GetComponentsInChildren<T>(bool inactive) => Children.OfType<T>().ToArray();
    }
    public static class Time { public static int frameCount; }
    public static class Canvas
    {
        public static event Action willRenderCanvases;
        public static event Action preWillRenderCanvases;
        public static void Render() { Time.frameCount++; preWillRenderCanvases?.Invoke(); willRenderCanvases?.Invoke(); }
    }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Left }
    public class TMP_FontAsset
    {
        public string name; public bool Korean; public int Checks; public string Missing="";
        public UnityEngine.Material material=new UnityEngine.Material();
        public bool HasCharacter(char c) { Checks++; return (c<128 || Korean) && !Missing.Contains(c); }
        public bool HasCharacters(string text)=>text.All(HasCharacter);
    }
    public class TMP_Text
    {
        private TMP_FontAsset fontValue; private UnityEngine.Material materialValue;
        public int FontWrites, MaterialWrites;
        public TMP_FontAsset font { get=>fontValue; set {fontValue=value;FontWrites++;} }
        public UnityEngine.Material fontSharedMaterial { get=>materialValue; set {materialValue=value;MaterialWrites++;} }
        public readonly List<string> Writes=new List<string>();
        private string value;
        public virtual string text { [MethodImpl(MethodImplOptions.NoInlining)] get=>value;
            [MethodImpl(MethodImplOptions.NoInlining)] set { this.value=value; Writes.Add(value); } }
    }
    public class TMP_InputField { public string text { get; set; } }
}
namespace Casino.Client
{
    public static class CasinoLobby
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TMPro.TMP_Text NewText(string name,UnityEngine.Transform parent,string text,float size)
        { var label=new TMPro.TMP_Text(); label.text=text; return label; }
    }
    public static class CasinoTab
    {
        public static UnityEngine.GameObject _tab;
        public static readonly TMPro.TMP_Text Label=new TMPro.TMP_Text();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Relabel(UnityEngine.GameObject clone,UnityEngine.Transform parent,string text) { Label.text=text; }
    }
}
namespace HorseRacing.Client
{
    public static class RacePanel
    {
        public static UnityEngine.GameObject _root;
        public static readonly TMPro.TMP_Text Status=new TMPro.TMP_Text();
        public static readonly TMPro.TMP_Text Blurb=new TMPro.TMP_Text();
        public static readonly TMPro.TMP_InputField Input=new TMPro.TMP_InputField();
        public static string ProtocolText;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TMPro.TMP_Text Text(string name,UnityEngine.Transform parent,string content,float size,UnityEngine.Color color,TMPro.TextAlignmentOptions alignment)
        { var text=new TMPro.TMP_Text(); ProtocolText=content; Input.text=content; text.text=content; return text; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SetStatus(string text) { ProtocolText=text; Status.text=text; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SetBlurb() { Blurb.text="THE DASH  5 furlongs"; }
    }
}
namespace OtherPlugin
{
    public static class Screen
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TMPro.TMP_Text Build() { var label=new TMPro.TMP_Text();label.text="CASINO";return label; }
    }
}
