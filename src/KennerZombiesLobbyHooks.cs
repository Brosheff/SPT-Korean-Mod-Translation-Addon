using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Dr K's SPZombies 0.1.1 R002 — client-facing lobby, social, invitation,
    // notices and host status only. No party/network state or callback is modified.
    internal static class KennerZombiesLobbyHooks
    {
        private const string Id = "com.kennerz.zmcore";
        private const string AssemblyId = "Kennerz_ZM_Core";
        private const string PluginType = "KennerZombies.ZombiesPlugin";
        private static readonly HashSet<Assembly> Installed = new HashSet<Assembly>();
        private static readonly object Gate = new object();
        private static Harmony owner;
        private static bool enabled;
        private static FieldInfo lobby, modal, banner, toast, cover, modalKind;
        private static FieldInfo socialContent, memberSlot, memberStatus, memberName, coverRoot;
        private static TMP_Text coverTitle;
        private static FieldInfo[] dynamicLabels, dynamicButtons;
        private static PropertyInfo buttonLabel;

        internal static int Enable(Harmony harmony, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (harmony == null || data == null || !data.HasRules(Id, "small_ui")) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                enabled = true;
                owner = harmony;
                AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
            }
            int installed = 0;
            foreach (var a in assemblies ?? Enumerable.Empty<Assembly>()) installed += PatchAssembly(a);
            return installed;
        }

        internal static void Disable()
        {
            if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
            enabled = false;
            Installed.Clear();
            owner = null;
            lobby = modal = banner = toast = cover = modalKind = null;
            socialContent = memberSlot = memberStatus = memberName = coverRoot = null;
            coverTitle = null;
            dynamicLabels = dynamicButtons = null;
            buttonLabel = null;
            // The R001 shared label owner handles text/font restoration on teardown.
        }

        private static void AssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            try { PatchAssembly(args.LoadedAssembly); }
            catch (Exception ex) { Warn("late", ex); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly?.GetName().Name != AssemblyId) return 0;
            var plugin = assembly.GetType(PluginType, false);
            var guid = plugin?.GetField("PluginGuid", BindingFlags.Public | BindingFlags.Static);
            var version = plugin?.GetField("PluginVersion", BindingFlags.Public | BindingFlags.Static);
            if (guid == null || version == null || !guid.IsLiteral || !version.IsLiteral ||
                (string)guid.GetRawConstantValue() != Id ||
                (string)version.GetRawConstantValue() != "0.1.1") return 0;

            var frontend = assembly.GetType("KennerZombies.ZombiesFrontEnd", false);
            var social = frontend?.GetNestedType("SocialPanel", BindingFlags.NonPublic);
            var member = frontend?.GetNestedType("MemberRow", BindingFlags.NonPublic);
            var zbutton = assembly.GetType("KennerZombies.ZButton", false);
            if (frontend == null || social == null || member == null || zbutton == null) return 0;
            var targets = new (Type type, string name, Type[] arguments, string hook)[]
            {
                (frontend, "BuildLobby", Type.EmptyTypes, nameof(AfterBuildLobby)),
                (frontend, "UpdateLobby", Type.EmptyTypes, nameof(AfterUpdateLobby)),
                (social, "Header", new[] { typeof(string), typeof(int) }, nameof(AfterSocialElement)),
                (social, "Empty", new[] { typeof(string) }, nameof(AfterSocialElement)),
                (social, "Row", null, nameof(AfterSocialElement)),
                (member, "Set", null, nameof(AfterMember)),
                (frontend, "Confirm", null, nameof(AfterModal)),
                (frontend, "Options", null, nameof(AfterModal)),
                (frontend, "UpdateInvite", new[] { typeof(bool), typeof(bool) }, nameof(AfterInvite)),
                (frontend, "UpdateRequests", Type.EmptyTypes, nameof(AfterRequest)),
                (frontend, "UpdateToast", Type.EmptyTypes, nameof(AfterToast)),
                (frontend, "UpdateBanner", new[] { typeof(bool) }, nameof(AfterBanner)),
                (frontend, "UpdateCover", Type.EmptyTypes, nameof(AfterCover)),
                (frontend, "BuildOverlays", Type.EmptyTypes, nameof(AfterBuildOverlays)),
            };
            var methods = new List<(MethodInfo method, string hook)>();
            foreach (var t in targets)
            {
                var method = t.arguments == null ? AccessTools.Method(t.type, t.name) : AccessTools.Method(t.type, t.name, t.arguments);
                if (method == null || !method.IsPrivate && !method.IsPublic || method.IsStatic ||
                    method.ReturnType != typeof(void)) return 0;
                methods.Add((method, t.hook));
            }
            var l = AccessTools.Field(frontend, "_lobby");
            var m = AccessTools.Field(frontend, "_modal");
            var b = AccessTools.Field(frontend, "_bannerText");
            var tField = AccessTools.Field(frontend, "_toastText");
            var c = AccessTools.Field(frontend, "_coverSub");
            var k = AccessTools.Field(frontend, "_modalKind");
            var content = AccessTools.Field(social, "_content");
            var label = AccessTools.Property(zbutton, "Label");
            var ms = AccessTools.Field(member, "_slot");
            var mst = AccessTools.Field(member, "_status");
            var mn = AccessTools.Field(member, "_name");
            var cr = AccessTools.Field(frontend, "_cover");
            var textFields = new[] { "_typeText", "_fikaText", "_countdown", "_footer" }
                .Select(name => AccessTools.Field(frontend, name)).ToArray();
            var buttonFields = new[] { "_startButton", "_typeButton", "_fikaButton" }
                .Select(name => AccessTools.Field(frontend, name)).ToArray();
            if (l == null || m == null || b == null || tField == null || c == null ||
                k == null || content == null || ms == null || mst == null || mn == null || cr == null ||
                textFields.Any(x => x == null) || buttonFields.Any(x => x == null) ||
                label == null || label.PropertyType != typeof(TextMeshProUGUI)) return 0;

            lock (Gate)
            {
                if (!enabled || owner == null || !Installed.Add(assembly)) return 0;
            }
            try
            {
                lobby = l; modal = m; banner = b; toast = tField; cover = c;
                modalKind = k; socialContent = content; buttonLabel = label;
                memberSlot = ms; memberStatus = mst; memberName = mn; coverRoot = cr;
                dynamicLabels = textFields; dynamicButtons = buttonFields;
                foreach (var item in methods)
                    owner.Patch(item.method, postfix: new HarmonyMethod(typeof(KennerZombiesLobbyHooks), item.hook));
                return methods.Count;
            }
            catch (Exception ex)
            {
                Installed.Remove(assembly);
                Warn("install", ex);
                throw;
            }
        }

        private static void AfterBuildLobby(object __instance) => Try(() =>
            KennerZombiesMenuHooks.CaptureTree(lobby?.GetValue(__instance) as Component), "lobby-build");

        private static void AfterUpdateLobby(object __instance) => Try(() =>
        {
            foreach (var field in dynamicLabels)
                KennerZombiesMenuHooks.Capture(field.GetValue(__instance) as TMP_Text);
            foreach (var field in dynamicButtons)
            {
                var button = field.GetValue(__instance);
                var label = button == null ? null : buttonLabel?.GetValue(button, null) as TMP_Text;
                // The ready-toggle action is different from a member's NOT READY status.
                KennerZombiesMenuHooks.Capture(label, semanticSource:
                    field.Name == "_startButton" && label != null && label.text == "NOT READY" ? "Not ready" : null);
            }
        }, "lobby-update");

        private static void AfterSocialElement(object __instance) => Try(() =>
        {
            var parent = socialContent?.GetValue(__instance) as Transform;
            if (parent != null && parent.childCount > 0)
                KennerZombiesMenuHooks.CaptureTree(parent.GetChild(parent.childCount - 1));
        }, "social-element");

        private static void AfterMember(object __instance) => Try(() =>
        {
            KennerZombiesMenuHooks.Capture(memberSlot?.GetValue(__instance) as TMP_Text);
            KennerZombiesMenuHooks.Capture(memberStatus?.GetValue(__instance) as TMP_Text);
            var name = memberName?.GetValue(__instance) as TMP_Text;
            if (name != null && (name.text == "Open slot" || name.text.Contains("(YOU)")))
                KennerZombiesMenuHooks.Capture(name, true);
        }, "member");

        private static void AfterModal(object __instance) => Try(() => CaptureModal(__instance), "modal");
        private static void AfterInvite(object __instance) => Try(() =>
        {
            CaptureField(__instance, "_bannerText");
            if ((string)modalKind?.GetValue(__instance) == "invite") CaptureModal(__instance);
        }, "invite");
        private static void AfterRequest(object __instance) => Try(() =>
        {
            if ((string)modalKind?.GetValue(__instance) == "request") CaptureModal(__instance);
        }, "request");
        private static void AfterToast(object __instance) => Try(() =>
            KennerZombiesMenuHooks.Capture(toast?.GetValue(__instance) as TMP_Text), "toast");
        private static void AfterBanner(object __instance) => Try(() =>
            KennerZombiesMenuHooks.Capture(banner?.GetValue(__instance) as TMP_Text), "banner");
        private static void AfterBuildOverlays(object __instance) => Try(() =>
        {
            var root = coverRoot?.GetValue(__instance) as Component;
            coverTitle = root?.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(label => label != null && label.text == "STARTING MATCH");
            KennerZombiesMenuHooks.Capture(coverTitle);
        }, "cover-build");
        private static void AfterCover(object __instance) => Try(() =>
            KennerZombiesMenuHooks.Capture(cover?.GetValue(__instance) as TMP_Text), "cover-update");

        private static void CaptureModal(object instance) =>
            KennerZombiesMenuHooks.CaptureTree(modal?.GetValue(instance) as Component);
        private static void CaptureField(object instance, string name) =>
            KennerZombiesMenuHooks.Capture(AccessTools.Field(instance.GetType(), name)?.GetValue(instance) as TMP_Text);

        private static void Try(Action action, string place)
        {
            if (!enabled) return;
            try { action(); }
            catch (Exception ex) { Warn(place, ex); }
        }
        private static void Warn(string place, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("kenner-zombies-r002-" + place,
                () => "SPZombies R002 lobby translation skipped (" + place + "): " + ex.Message);
        }
    }
}
