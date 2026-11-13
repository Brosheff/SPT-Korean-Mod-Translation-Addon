using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // Hideout Interiors 0.1.0 — R002 fixed, R003 dynamic, R004 names, R005 search display.
    // Translate only the mod's final TMP/UI notification presentation. Do not alter
    // object names, stored keys, original filter comparisons, timers, or user input.
    internal static class HideoutInteriorsUiHooks
    {
        private const string ProfileId = "jrs.hideoutinteriors";
        private const string Channel = "small_ui";
        private const string DecorNames = "decor_names";
        // Hideout Last Stand installs its own decorations into Hideout Interiors.
        // Only the final TMP name/tab is translated; keys and registered categories stay English.
        private const string SiegeProfileId = "jrs.hideoutsiege";
        private const string AssemblyName = "HideoutInteriors";
        private const string TypeName = "HideoutInteriors.Interiors";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private sealed class LabelState
        {
            internal string Source, Applied;
            internal TMP_FontAsset OriginalFont, AppliedFont;
            internal bool IsCatalogCell;
        }
        private static ConditionalWeakTable<TextMeshProUGUI, LabelState> states =
            new ConditionalWeakTable<TextMeshProUGUI, LabelState>();
        private static readonly List<WeakReference> labels = new List<WeakReference>();
        private static Harmony harmony;
        private static EditableProfiles profiles;
        private static bool enabled;
        private static TMP_FontAsset[] loadedFonts;
        private static int fontCheckFrame = -1000;
        private static FieldInfo tabButtonsField;
        private static FieldInfo statusTextField, tipTextField, resetLabelField, decorButtonField;
        // R003: Read-only upstream collection counts distinguish a multi-item status
        // from an actual object whose displayed name happens to contain "THINGS".
        private static FieldInfo selectionField, carryingField;
        // R005: These are the audited, private UI data structures. We never rewrite
        // query, Entry.Name, classification, saved keys, or the upstream comparisons.
        private static FieldInfo queryField, tabField, catalogField, itemLayoutField;
        private static FieldInfo cellsField, hiddenCellsField, contentField, searchTextField;
        private static MethodInfo cellMethod, prettyMethod, fillTabMethod, searchLookMethod;
        private static float searchPanelWidth, searchCellWidth, searchCellHeight, searchGap;
        private static bool lastSearchKoreanMode;
        private static object tooltipAttached, tooltipFailed;
        // Exactly verified Pretty(Transform.name) display names only. Populated from the
        // mod-specific exact-rule channel, not from a word dictionary or scene scan.
        private static readonly HashSet<string> verifiedNames = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> siegeDecorNames = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> uppercaseNames = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> assemblies, EditableProfiles data)
        {
            if (patcher == null || data == null || !data.HasRules(ProfileId, Channel)) return 0;
            lock (Gate)
            {
                if (enabled) return 0;
                harmony = patcher; profiles = data; LoadVerifiedNames(data); enabled = true;
                lastSearchKoreanMode = LocaleMode.IsKoreanMode();
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            int patched = 0;
            foreach (var assembly in assemblies ?? new Assembly[0]) patched += PatchAssembly(assembly);
            return patched;
        }

        internal static void Disable()
        {
            if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            enabled = false;
            foreach (var reference in labels)
            {
                var label = reference.Target as TextMeshProUGUI;
                if (label == null || !states.TryGetValue(label, out var state)) continue;
                if (string.Equals(label.text, state.Applied, StringComparison.Ordinal))
                    label.text = state.Source;
                if (state.AppliedFont != null && label.font == state.AppliedFont && state.OriginalFont != null)
                    label.font = state.OriginalFont;
            }
            labels.Clear(); states = new ConditionalWeakTable<TextMeshProUGUI, LabelState>();
            Patched.Clear(); harmony = null; profiles = null;
            tabButtonsField = statusTextField = tipTextField = resetLabelField = decorButtonField = null;
            selectionField = carryingField = null;
            queryField = tabField = catalogField = itemLayoutField = null;
            cellsField = hiddenCellsField = contentField = searchTextField = null;
            cellMethod = prettyMethod = fillTabMethod = searchLookMethod = null;
            searchPanelWidth = searchCellWidth = searchCellHeight = searchGap = 0;
            lastSearchKoreanMode = false;
            tooltipAttached = tooltipFailed = null; loadedFonts = null; fontCheckFrame = -1000;
            verifiedNames.Clear(); siegeDecorNames.Clear(); uppercaseNames.Clear();
        }

        // Called by the addon's existing locale-change dispatch; no per-frame Canvas scan.
        internal static void RefreshLanguage()
        {
            if (!enabled) return;
            // A language switch must update the *result set* as well as its labels.
            // Rebuild through the mod's own FillTab (no direct change to query).
            bool koreanNow = LocaleMode.IsKoreanMode();
            if (koreanNow != lastSearchKoreanMode)
            {
                lastSearchKoreanMode = koreanNow;
                try
                {
                    var currentQuery = queryField?.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(currentQuery) && ContainsHangul(currentQuery) &&
                        contentField?.GetValue(null) != null) fillTabMethod?.Invoke(null, null);
                    searchLookMethod?.Invoke(null, null);
                }
                catch (Exception ex) { Warn("r005-refresh", ex); }
            }
            for (var i = labels.Count - 1; i >= 0; i--)
            {
                var label = labels[i].Target as TextMeshProUGUI;
                if (label == null) { labels.RemoveAt(i); continue; }
                if (!states.TryGetValue(label, out var state)) continue;
                // A later upstream writer owns the label when its value no longer equals
                // the source or our last translation. Do not rewrite that new content.
                if (!string.Equals(label.text, state.Applied, StringComparison.Ordinal) &&
                    !string.Equals(label.text, state.Source, StringComparison.Ordinal)) continue;
                Apply(label, state, state.Source);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try { PatchAssembly(args.LoadedAssembly); }
            catch (Exception ex) { Warn("late-load", ex); }
        }

        private static int PatchAssembly(Assembly assembly)
        {
            if (assembly == null || !string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.Ordinal)) return 0;
            lock (Gate)
            {
                if (!enabled || Patched.Contains(assembly)) return 0;
                var owner = assembly.GetType(TypeName, false);
                if (owner == null) { Warn("owner", new TypeLoadException(TypeName)); return 0; }
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                var text = owner.GetMethod("Text", flags, null, new[] {
                    typeof(string), typeof(Transform), typeof(string), typeof(float),
                    typeof(Vector2), typeof(Vector2), typeof(TextAlignmentOptions),
                    typeof(Color), typeof(bool)}, null);
                var fillTab = owner.GetMethod("FillTab", flags, null, Type.EmptyTypes, null);
                var tabs = owner.GetField("tabButtons", flags);
                var cell = owner.GetMethod("Cell", flags, null, new[] {
                    typeof(int), typeof(int), typeof(string), typeof(Texture2D), typeof(UnityEngine.UI.RawImage).MakeByRefType()}, null);
                if (text == null || text.ReturnType != typeof(TextMeshProUGUI) ||
                    fillTab == null || fillTab.ReturnType != typeof(void) || tabs == null)
                {
                    Warn("signature", new MissingMethodException("The audited Hideout Interiors Text/FillTab signatures were not found"));
                    return 0;
                }
                // Preserve R002 even if an R003-only method is unavailable. Two
                // independent patch groups: each group rolls back on failure.
                var baseMethods = new[] { text, fillTab };
                var baseHooks = new[] { nameof(AfterText), nameof(AfterFillTab) };
                var baseInstalled = new List<MethodInfo>();
                try
                {
                    for (int i = 0; i < baseMethods.Length; i++)
                    {
                        harmony.Patch(baseMethods[i], postfix: new HarmonyMethod(typeof(HideoutInteriorsUiHooks), baseHooks[i]));
                        baseInstalled.Add(baseMethods[i]);
                    }
                }
                catch (Exception ex)
                {
                    foreach (var method in baseInstalled)
                        try { harmony.Unpatch(method, HarmonyPatchType.Postfix, harmony.Id); } catch { }
                    Warn("r002-base-patch", ex);
                    return 0;
                }
                tabButtonsField = tabs;
                Patched.Add(assembly);
                // Cell returns a newly created, named TextMeshProUGUI child. The
                // Postfix touches that child only; Entry.Name/Key and search stay raw.
                bool cellInstalled = false;
                if (cell != null && cell.ReturnType == typeof(RectTransform))
                {
                    try { harmony.Patch(cell, postfix: new HarmonyMethod(typeof(HideoutInteriorsUiHooks), nameof(AfterCell))); cellInstalled = true; }
                    catch (Exception ex) { Warn("r004-cell-patch", ex); }
                }
                else Warn("r004-cell-signature", new MissingMethodException("Cell(int,int,string,Texture2D,out RawImage)"));

                // The input string is never intercepted. SearchLook owns just the
                // exact empty search placeholder. Search result extension is attached
                // to the already-installed FillTab Postfix and requires the exact
                // upstream UI collections and Cell signature.
                var searchLook = owner.GetMethod("SearchLook", flags, null, Type.EmptyTypes, null);
                var searchText = owner.GetField("searchText", flags);
                if (searchLook != null && searchLook.ReturnType == typeof(void) &&
                    searchText?.FieldType == typeof(TextMeshProUGUI))
                {
                    try
                    {
                        harmony.Patch(searchLook, postfix: new HarmonyMethod(typeof(HideoutInteriorsUiHooks), nameof(AfterSearchLook)));
                        searchLookMethod = searchLook;
                        searchTextField = searchText;
                    }
                    catch (Exception ex) { Warn("r005-search-look", ex); }
                }
                else Warn("r005-search-look-signature", new MissingMethodException("SearchLook() or searchText"));
                if (cellInstalled) ConfigureSearch(owner, flags, cell, fillTab);

                var status = owner.GetMethod("Status", flags, null, Type.EmptyTypes, null);
                var tip = owner.GetMethod("DecorTip", flags, null, new[] { typeof(Transform) }, null);
                var resetClicked = owner.GetMethod("ResetClicked", flags, null, Type.EmptyTypes, null);
                var decorTick = owner.GetMethod("DecorTick", flags, null, Type.EmptyTypes, null);
                var refreshHidden = owner.GetMethod("RefreshHiddenTab", flags, null, Type.EmptyTypes, null);
                var decorButton = owner.GetMethod("DecorButton", flags, null, Type.EmptyTypes, null);
                var statusField = owner.GetField("statusText", flags);
                var tipField = owner.GetField("tipText", flags);
                var resetField = owner.GetField("resetLabel", flags);
                var buttonField = owner.GetField("decorButton", flags);
                var selectedField = owner.GetField("selection", flags);
                var carriedField = owner.GetField("carrying", flags);
                var methods = new[] { status, tip, resetClicked, decorTick, refreshHidden, decorButton };
                if (methods.Any(m => m == null) ||
                    methods.Any(m => m != null && m.ReturnType != typeof(void)) ||
                    statusField?.FieldType != typeof(TextMeshProUGUI) ||
                    tipField?.FieldType != typeof(TextMeshProUGUI) ||
                    resetField?.FieldType != typeof(TextMeshProUGUI) || buttonField == null ||
                    selectedField == null || !typeof(ICollection).IsAssignableFrom(selectedField.FieldType) ||
                    carriedField == null || !typeof(ICollection).IsAssignableFrom(carriedField.FieldType))
                {
                    Warn("r003-signature", new MissingMethodException("R003 dynamic UI methods/fields were not found; R002 stays enabled"));
                    return baseInstalled.Count + (cellInstalled ? 1 : 0);
                }
                var hooks = new[] {
                    nameof(AfterStatus), nameof(AfterDecorTip), nameof(AfterResetClicked),
                    nameof(AfterDecorTick), nameof(AfterRefreshHiddenTab), nameof(AfterDecorButton) };
                var installed = new List<MethodInfo>();
                try
                {
                    for (int i = 0; i < methods.Length; i++)
                    {
                        harmony.Patch(methods[i], postfix: new HarmonyMethod(typeof(HideoutInteriorsUiHooks), hooks[i]));
                        installed.Add(methods[i]);
                    }
                    statusTextField = statusField; tipTextField = tipField;
                    resetLabelField = resetField; decorButtonField = buttonField;
                    selectionField = selectedField; carryingField = carriedField;
                    return baseInstalled.Count + installed.Count + (cellInstalled ? 1 : 0);
                }
                catch (Exception ex)
                {
                    foreach (var method in installed)
                        try { harmony.Unpatch(method, HarmonyPatchType.Postfix, harmony.Id); } catch { }
                    Warn("r003-patch", ex);
                    return baseInstalled.Count + (cellInstalled ? 1 : 0);
                }
            }
        }

        private static void Warn(string target, Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("hideout-interiors-r002-" + target,
                () => "Hideout Interiors R002 display localization skipped at " + target + ": " + ex.Message);
        }

        // An exact full-name list is populated only from independently confirmed
        // scene object names. Empty channel means no names are guessed/transformed.
        private static void LoadVerifiedNames(EditableProfiles data)
        {
            verifiedNames.Clear(); siegeDecorNames.Clear(); uppercaseNames.Clear();
            foreach (var doc in data.Documents)
            {
                if (doc.channels == null ||
                    (doc.mod_id != ProfileId && doc.mod_id != SiegeProfileId) ||
                    !doc.channels.TryGetValue(DecorNames, out var rows) || rows == null) continue;
                foreach (var row in rows)
                {
                    if (!row.enabled || string.IsNullOrEmpty(row.source) ||
                        string.IsNullOrEmpty(row.translation) || row.match_mode != "exact") continue;
                    if (doc.mod_id == SiegeProfileId) siegeDecorNames.Add(row.source);
                    else verifiedNames.Add(row.source);
                }
            }
            // Uppercase strings emitted by Status/DecorTip must resolve to one
            // unambiguous canonical name, never to a fuzzy/partial word match.
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in verifiedNames.Concat(siegeDecorNames))
            {
                string upper = source.ToUpperInvariant();
                if (uppercaseNames.TryGetValue(upper, out var previous) && previous != source)
                { uppercaseNames.Remove(upper); ambiguous.Add(upper); }
                else if (!ambiguous.Contains(upper)) uppercaseNames[upper] = source;
            }
        }

        private static bool SiegeSourceLoaded()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var plugin = assembly.GetType("HideoutSiege.Plugin", false);
                if (plugin == null) continue;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                var id = plugin.GetField("Id", flags);
                var version = plugin.GetField("Version", flags);
                return id?.IsLiteral == true && version?.IsLiteral == true &&
                    string.Equals(id.GetRawConstantValue() as string, SiegeProfileId, StringComparison.Ordinal) &&
                    HideoutLastStandVersionGuard.IsSupported(version.GetRawConstantValue() as string);
            }
            return false;
        }

        private static bool IsVerifiedName(string name) => verifiedNames.Contains(name) ||
            (siegeDecorNames.Contains(name) && SiegeSourceLoaded());

        private static string ExactDisplayName(string english, bool uppercase = false)
        {
            if (!LocaleMode.IsKoreanMode() || string.IsNullOrEmpty(english)) return english;
            string canonical = english;
            if (!IsVerifiedName(canonical) &&
                (!uppercase || !uppercaseNames.TryGetValue(english, out canonical) ||
                 !IsVerifiedName(canonical))) return english;
            if (verifiedNames.Contains(canonical)) return profiles.Translate(ProfileId, DecorNames, canonical);
            return profiles.Translate(SiegeProfileId, DecorNames, canonical);
        }

        private static string TranslateOwnOrSiege(string english)
        {
            var translated = profiles.Translate(ProfileId, Channel, english);
            if (!string.Equals(translated, english, StringComparison.Ordinal)) return translated;
            if (SiegeSourceLoaded() && (string.Equals(english, "Defences", StringComparison.Ordinal) ||
                                        english.StartsWith("Defences (", StringComparison.Ordinal)))
                return profiles.Translate(SiegeProfileId, Channel, english);
            return english;
        }

        // Hideout Interiors writes a list/hidden-list cell's final label here;
        // do not patch Pretty(), BuildCatalog() or the search's query comparison.
        private static void AfterCell(RectTransform __result, string __2)
        {
            if (!enabled || __result == null || __result.name != "Cell" || string.IsNullOrEmpty(__2)) return;
            try
            {
                const string hintSuffix = "\n<size=11>click to show</size>";
                if (!__2.EndsWith(hintSuffix, StringComparison.Ordinal) &&
                    !IsVerifiedName(__2)) return;
                var child = __result.Find("Name");
                var label = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
                if (label == null || !string.Equals(label.text, __2, StringComparison.Ordinal)) return;
                Capture(label, __2, true);
            }
            catch (Exception ex) { Warn("r004-cell", ex); }
        }

        private static string TranslateCell(string english)
        {
            const string suffix = "\n<size=11>click to show</size>";
            bool hidden = english.EndsWith(suffix, StringComparison.Ordinal);
            string name = hidden ? english.Substring(0, english.Length - suffix.Length) : english;
            string translated = ExactDisplayName(name);
            if (!hidden) return translated;
            string hint = profiles.Translate(ProfileId, Channel, "click to show");
            return translated + "\n<size=11>" + hint + "</size>";
        }

        // The upstream Status() uses exactly the same {what} slot for Pretty(name)
        // and "count + THINGS".  Only convert the aggregate when the original
        // (read-only) carrying/selection collection proves it is a multi-item label.
        // Do not touch Transform.name, Pretty(), saved keys or any UI input fields.
        private static string TranslateAggregateStatusHead(string source, bool moving)
        {
            var field = moving ? carryingField : selectionField;
            var collection = field?.GetValue(null) as ICollection;
            if (collection == null || collection.Count == 1) return source;
            var countText = collection.Count.ToString();
            return string.Equals(source, countText + " THINGS", StringComparison.Ordinal)
                ? "물건 " + countText + "개" : source;
        }

        // Apply only to the independently verified full name inside the original
        // object's display slot. Area headers and counts are not object names.
        private static string TranslateDynamicItemName(TextMeshProUGUI label, string english, string translated)
        {
            if (!LocaleMode.IsKoreanMode()) return translated;
            bool tip = ReferenceEquals(label, tipTextField?.GetValue(null));
            bool status = ReferenceEquals(label, statusTextField?.GetValue(null));
            if (!tip && !status) return translated;
            const string start = "<b>";
            const string end = "</b>";
            if (!english.StartsWith(start, StringComparison.Ordinal)) return translated;
            int close = english.IndexOf(end, start.Length, StringComparison.Ordinal);
            if (close < 0) return translated;
            string originalHead = english.Substring(start.Length, close - start.Length);
            string tail = english.Substring(close + end.Length);
            // An item itself may be named "MOVING TABLE"; only the upstream
            // carrying branch has the matching "belongs to" status suffix.
            bool moving = status && originalHead.StartsWith("MOVING ", StringComparison.Ordinal) &&
                          tail.StartsWith(" · belongs to ", StringComparison.Ordinal);
            bool aggregateContext = moving || (status && (
                tail.StartsWith(" · turning about ", StringComparison.Ordinal) ||
                tail.StartsWith(" · at ", StringComparison.Ordinal) ||
                tail.StartsWith(" · along ", StringComparison.Ordinal)));
            // Area icon header is followed by a newline, not an object slot.
            bool objectHead = moving || tail.StartsWith(" · ", StringComparison.Ordinal);
            if (objectHead)
            {
                string objectName = moving ? originalHead.Substring("MOVING ".Length) : originalHead;
                // This branch has an upstream "count + THINGS" supplier only on
                // Status()'s moving and axis paths.  A single item can genuinely be
                // named "3 THINGS"; its name must remain untouched.
                string korean = aggregateContext ? TranslateAggregateStatusHead(objectName, moving) : objectName;
                if (string.Equals(korean, objectName, StringComparison.Ordinal))
                    korean = ExactDisplayName(objectName, true);
                if (!string.Equals(objectName, korean, StringComparison.Ordinal))
                {
                    string slot = moving ? start + objectName + " 이동 중" : start + objectName + end;
                    string replacement = moving ? start + korean + " 이동 중" : start + korean + end;
                    if (translated.StartsWith(slot, StringComparison.Ordinal))
                        translated = replacement + translated.Substring(slot.Length);
                }
            }
            // One selected item in the area-icon assignment sentence is an
            // independent name slot. Do not translate the area name / target.
            const string clickStart = "\nClick: the ";
            const string clickEnd = " belongs to ";
            if (status)
            {
                int from = english.IndexOf(clickStart, StringComparison.Ordinal);
                if (from >= 0)
                {
                    from += clickStart.Length;
                    int to = english.IndexOf(clickEnd, from, StringComparison.Ordinal);
                    if (to > from)
                    {
                        string item = english.Substring(from, to - from);
                        string korean = ExactDisplayName(item);
                        string target = "클릭: 이제 " + item + "의 소속 구역";
                        if (korean != item && translated.Contains(target))
                            translated = translated.Replace(target, "클릭: 이제 " + korean + "의 소속 구역");
                    }
                }
            }
            return translated;
        }

        private static void AfterText(TextMeshProUGUI __result, string __0, string __2, bool __8)
        {
            if (!enabled || __result == null || string.IsNullOrEmpty(__2)) return;
            // 'Name' is an object name, and 'Text' is the search/status widget.
            // R002 owns only fixed Title/Info/Label literals.
            if (__0 != "Title" && __0 != "Info" && __0 != "Label") return;
            try
            {
                if (!profiles.CanTranslate(ProfileId, Channel, __2)) return;
                Capture(__result, __8 ? "<b>" + __2 + "</b>" : __2);
            }
            catch (Exception ex) { Warn("Text", ex); }
        }

        // Match the exact game-produced empty placeholder, not arbitrary user text.
        private static void AfterSearchLook()
        {
            if (!enabled || searchTextField == null) return;
            try
            {
                var label = searchTextField.GetValue(null) as TextMeshProUGUI;
                const string original = "<color=#8a877f>Search...</color>";
                if (label == null) return;
                if (LocaleMode.IsKoreanMode() &&
                    string.Equals(label.text, original, StringComparison.Ordinal) &&
                    profiles.CanTranslate(ProfileId, Channel, "Search..."))
                {
                    // The placeholder is the only source text changed here.
                    Capture(label, original);
                }
                else if (LocaleMode.IsKoreanMode() && ContainsHangul(label.text))
                {
                    // Preserve the input and caret supplied by upstream Typing().
                    // Only its rendered glyph font may need replacing.
                    if (!states.TryGetValue(label, out var state))
                    {
                        state = new LabelState();
                        states.Add(label, state);
                        labels.Add(new WeakReference(label));
                    }
                    state.Source = state.Applied = label.text;
                    ApplyFont(label, state, label.text);
                }
                else if (states.TryGetValue(label, out var previous))
                {
                    // After a Hangul query, upstream may write an ASCII query or
                    // an empty active field. Restore only the font WE installed;
                    // otherwise it leaks into later English searches and modes.
                    if (previous.AppliedFont != null && label.font == previous.AppliedFont &&
                        previous.OriginalFont != null)
                        label.font = previous.OriginalFont;
                    previous.OriginalFont = previous.AppliedFont = null;
                    previous.Source = previous.Applied = label.text;
                }
            }
            catch (Exception ex) { Warn("r005-search-look", ex); }
        }

        private static bool ContainsHangul(string value)
        {
            foreach (char c in value)
                if ((c >= '\uac00' && c <= '\ud7a3') || (c >= '\u3130' && c <= '\u318f') ||
                    (c >= '\u1100' && c <= '\u11ff')) return true;
            return false;
        }

        // Validate every reflected member before enabling the optional search path.
        // Failure never disables the existing R002/R003/R004 display hooks.
        private static void ConfigureSearch(Type owner, BindingFlags flags, MethodInfo cell, MethodInfo fillTab)
        {
            try
            {
                var query = owner.GetField("query", flags);
                var tab = owner.GetField("tab", flags);
                var catalog = owner.GetField("catalog", flags);
                var layout = owner.GetField("itemLayout", flags);
                var cells = owner.GetField("cells", flags);
                var hidden = owner.GetField("hiddenCells", flags);
                var content = owner.GetField("content", flags);
                var pretty = owner.GetMethod("Pretty", flags, null, new[] { typeof(string) }, null);
                var panelWidth = owner.GetField("PanelWidth", flags);
                var cellWidth = owner.GetField("CellWidth", flags);
                var cellHeight = owner.GetField("CellHeight", flags);
                var gap = owner.GetField("Gap", flags);
                if (query?.FieldType != typeof(string) || tab?.FieldType != typeof(string) ||
                    catalog == null || layout == null || cells == null || hidden == null ||
                    content?.FieldType != typeof(RectTransform) ||
                    pretty == null || pretty.ReturnType != typeof(string) ||
                    !typeof(IList).IsAssignableFrom(cells.FieldType) ||
                    !typeof(IList).IsAssignableFrom(hidden.FieldType) ||
                    !typeof(IDictionary).IsAssignableFrom(layout.FieldType) ||
                    panelWidth == null || cellWidth == null || cellHeight == null || gap == null ||
                    !panelWidth.IsLiteral || !cellWidth.IsLiteral || !cellHeight.IsLiteral || !gap.IsLiteral)
                {
                    Warn("r005-search-signature", new MissingMemberException("Search backing members changed"));
                    return;
                }
                queryField = query; tabField = tab; catalogField = catalog; itemLayoutField = layout;
                cellsField = cells; hiddenCellsField = hidden; contentField = content;
                prettyMethod = pretty; cellMethod = cell; fillTabMethod = fillTab;
                searchPanelWidth = (float)panelWidth.GetRawConstantValue();
                searchCellWidth = (float)cellWidth.GetRawConstantValue();
                searchCellHeight = (float)cellHeight.GetRawConstantValue();
                searchGap = (float)gap.GetRawConstantValue();
            }
            catch (Exception ex) { Warn("r005-search-config", ex); }
        }

        private static object TuplePart(object tuple, string field)
        {
            return tuple?.GetType().GetField(field)?.GetValue(tuple);
        }

        private static object MakeTuple(IList list, object first, object second)
        {
            return Activator.CreateInstance(list.GetType().GetGenericArguments()[0], first, second);
        }

        // Return to the mod's own renderer and preserve every original English
        // match. Only rows with independently verified, exact Korean display names
        // can be ADDED. The original IndexOf comparisons stay in place.
        private static void AppendKoreanSearchMatches()
        {
            if (!enabled || !LocaleMode.IsKoreanMode() || verifiedNames.Count == 0 ||
                queryField == null || cellMethod == null) return;
            string search = queryField.GetValue(null) as string;
            if (string.IsNullOrEmpty(search) || !ContainsHangul(search)) return;
            var view = contentField.GetValue(null) as RectTransform;
            if (view == null) return;
            string activeTab = tabField.GetValue(null) as string;
            bool hidden = string.Equals(activeTab, "Hidden", StringComparison.Ordinal);
            var list = (hidden ? hiddenCellsField : cellsField).GetValue(null) as IList;
            if (list == null) return;
            var saved = new List<object>();
            foreach (var tuple in list) saved.Add(tuple);
            var originalSize = view.sizeDelta;
            var originalPosition = view.anchoredPosition;
            var originalCardPositions = new Dictionary<RectTransform, Vector2>();
            foreach (var tuple in saved)
            {
                var rect = TuplePart(tuple, "Item1") as RectTransform;
                if (rect != null) originalCardPositions[rect] = rect.anchoredPosition;
            }
            var newRects = new List<RectTransform>();
            var changedEntries = new List<Tuple<object, FieldInfo, object, FieldInfo, object>>();
            int columns = Mathf.Max(1, Mathf.FloorToInt((searchPanelWidth - 24 + searchGap) / (searchCellWidth + searchGap)));
            try
            {
                if (hidden)
                {
                    var layout = itemLayoutField.GetValue(null) as IDictionary;
                    if (layout == null) return;
                    var included = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var tuple in saved)
                    {
                        var key = TuplePart(tuple, "Item2") as string;
                        if (key != null) included[key] = tuple;
                    }
                    foreach (DictionaryEntry pair in layout)
                    {
                        var key = pair.Key as string;
                        var hiddenField = pair.Value?.GetType().GetField("Hidden");
                        if (key == null || hiddenField == null || !(bool)hiddenField.GetValue(pair.Value)) continue;
                        int slash = key.LastIndexOf('/');
                        var name = prettyMethod.Invoke(null, new object[] { key.Substring(slash + 1) }) as string;
                        if (string.IsNullOrEmpty(name) || !verifiedNames.Contains(name)) continue;
                        string translated = ExactDisplayName(name);
                        if (string.Equals(name, translated, StringComparison.Ordinal) ||
                            translated.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 || included.ContainsKey(key)) continue;
                        var args = new object[] { list.Count, columns, name + "\n<size=11>click to show</size>", null, null };
                        var rect = cellMethod.Invoke(null, args) as RectTransform;
                        if (rect == null) throw new InvalidOperationException("Hideout Cell returned null");
                        newRects.Add(rect);
                        var tuple = MakeTuple(list, rect, key);
                        list.Add(tuple); included[key] = tuple;
                    }
                    if (newRects.Count == 0) return;
                    list.Clear();
                    foreach (DictionaryEntry pair in layout)
                        if (pair.Key is string key && included.TryGetValue(key, out var tuple)) list.Add(tuple);
                }
                else
                {
                    var catalog = catalogField.GetValue(null) as IEnumerable;
                    if (catalog == null) return;
                    var included = new Dictionary<object, object>();
                    foreach (var tuple in saved)
                    {
                        var entry = TuplePart(tuple, "Item2");
                        if (entry != null) included[entry] = tuple;
                    }
                    var ordered = new List<object>();
                    foreach (var entry in catalog)
                    {
                        if (entry == null) continue;
                        var t = entry.GetType();
                        var nameField = t.GetField("Name");
                        var kindField = t.GetField("Kind");
                        var sourceField = t.GetField("Source");
                        var cardField = t.GetField("Cell");
                        var imageField = t.GetField("Image");
                        if (nameField == null || kindField == null || sourceField == null ||
                            cardField == null || imageField == null) throw new MissingFieldException("Entry display fields changed");
                        var name = nameField.GetValue(entry) as string;
                        var kind = kindField.GetValue(entry) as string;
                        if (!string.Equals(activeTab, "All", StringComparison.Ordinal) &&
                            !string.Equals(activeTab, kind, StringComparison.Ordinal)) continue;
                        if (sourceField.GetValue(entry) == null) continue;
                        if (!included.ContainsKey(entry) && name != null && IsVerifiedName(name))
                        {
                            string translated = ExactDisplayName(name);
                            if (!string.Equals(translated, name, StringComparison.Ordinal) &&
                                translated.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var picture = t.GetField("Picture")?.GetValue(entry);
                                var args = new object[] { list.Count, columns, name, picture, null };
                                var rect = cellMethod.Invoke(null, args) as RectTransform;
                                if (rect == null) throw new InvalidOperationException("Hideout Cell returned null");
                                newRects.Add(rect);
                                var tuple = MakeTuple(list, rect, entry);
                                list.Add(tuple); included[entry] = tuple;
                                changedEntries.Add(Tuple.Create(entry, cardField, cardField.GetValue(entry),
                                    imageField, imageField.GetValue(entry)));
                                cardField.SetValue(entry, rect);
                                imageField.SetValue(entry, args[4]);
                            }
                        }
                        if (included.TryGetValue(entry, out var existing)) ordered.Add(existing);
                    }
                    if (newRects.Count == 0) return;
                    list.Clear();
                    foreach (var tuple in ordered) list.Add(tuple);
                }
                for (int n = 0; n < list.Count; n++)
                {
                    var rect = TuplePart(list[n], "Item1") as RectTransform;
                    if (rect == null) continue;
                    rect.anchoredPosition = new Vector2((n % columns) * (searchCellWidth + searchGap),
                        -(n / columns) * (searchCellHeight + searchGap));
                }
                int rows = (list.Count + columns - 1) / columns;
                view.sizeDelta = new Vector2(0, rows * (searchCellHeight + searchGap));
                view.anchoredPosition = Vector2.zero;
            }
            catch (Exception ex)
            {
                // A failed optional search extension must not invalidate the original
                // search results or leave an added, untracked card clickable.
                list.Clear();
                foreach (var tuple in saved) list.Add(tuple);
                foreach (var original in changedEntries)
                {
                    original.Item2.SetValue(original.Item1, original.Item3);
                    original.Item4.SetValue(original.Item1, original.Item5);
                }
                foreach (var rect in newRects)
                    if (rect != null) UnityEngine.Object.Destroy(rect.gameObject);
                foreach (var oldCard in originalCardPositions)
                    if (oldCard.Key != null) oldCard.Key.anchoredPosition = oldCard.Value;
                view.sizeDelta = originalSize;
                view.anchoredPosition = originalPosition;
                Warn("r005-append", ex);
            }
        }

        private static void AfterFillTab()
        {
            if (!enabled || tabButtonsField == null) return;
            // Only the mod's own 10 category buttons. Their tuple.Item4 keeps
            // the English classification key, while Item2 is the UI label.
            try
            {
                var buttons = tabButtonsField.GetValue(null) as IEnumerable;
                if (buttons == null) return;
                foreach (var tuple in buttons)
                {
                    if (tuple == null) continue;
                    var type = tuple.GetType();
                    var label = type.GetField("Item2")?.GetValue(tuple) as TextMeshProUGUI;
                    var kind = type.GetField("Item4")?.GetValue(tuple) as string;
                    if (label == null || string.IsNullOrEmpty(kind)) continue;
                    if (!profiles.CanTranslate(ProfileId, Channel, kind) &&
                        !(string.Equals(kind, "Defences", StringComparison.Ordinal) && SiegeSourceLoaded())) continue;
                    // FillTab overwrote text in English; keep the source including
                    // the hidden count, without touching tab, Kind or Kinds.
                    Capture(label, label.text);
                }
            }
            catch (Exception ex) { Warn("tabs", ex); }
            // Runs after the original filter and preserves its English results.
            // The extra cards are built by the original mod's Cell method.
            AppendKoreanSearchMatches();
        }

        // Status/tooltip are rewritten by their upstream methods on different branches.
        // Postfix sees the final text, including live coordinates and original item names.
        // These values are captures in anchored JSON templates, never mutated in the game.
        private static void AfterStatus()
        {
            if (!enabled) return;
            try { CaptureDynamic(statusTextField?.GetValue(null) as TextMeshProUGUI); }
            catch (Exception ex) { Warn("status", ex); }
        }

        private static void AfterDecorTip()
        {
            if (!enabled) return;
            try { CaptureDynamic(tipTextField?.GetValue(null) as TextMeshProUGUI); }
            catch (Exception ex) { Warn("tip", ex); }
        }

        private static void CaptureDynamic(TextMeshProUGUI label)
        {
            if (label == null || string.IsNullOrEmpty(label.text)) return;
            // An unmodified label is a candidate source. A label still holding our own
            // translation (e.g. Status' six-frame early return) must not be recaptured.
            if (states.TryGetValue(label, out var previous) &&
                string.Equals(label.text, previous.Applied, StringComparison.Ordinal) &&
                !string.Equals(previous.Applied, previous.Source, StringComparison.Ordinal)) return;
            var value = label.text;
            if (!profiles.CanTranslate(ProfileId, Channel, value)) return;
            Capture(label, value);
        }

        private static void AfterResetClicked() { CaptureResetLabel(); }
        private static void AfterDecorTick() { CaptureResetLabel(); }

        private static void CaptureResetLabel()
        {
            if (!enabled) return;
            try
            {
                var label = resetLabelField?.GetValue(null) as TextMeshProUGUI;
                if (label == null) return;
                // Direct upstream writes, including the timeout restoring RESET ALL.
                if (label.text == "<b>CLICK AGAIN</b>" || label.text == "<b>RESET ALL</b>")
                    Capture(label, label.text);
            }
            catch (Exception ex) { Warn("reset", ex); }
        }

        private static void AfterRefreshHiddenTab()
        {
            if (!enabled || tabButtonsField == null) return;
            try
            {
                var buttons = tabButtonsField.GetValue(null) as IEnumerable;
                if (buttons == null) return;
                foreach (var entry in buttons)
                {
                    if (entry == null) continue;
                    var type = entry.GetType();
                    if (!string.Equals(type.GetField("Item4")?.GetValue(entry) as string, "Hidden", StringComparison.Ordinal)) continue;
                    var label = type.GetField("Item2")?.GetValue(entry) as TextMeshProUGUI;
                    // FillTab() has its own Postfix. Translate only the actual fresh
                    // English from RefreshHiddenTab's non-Hidden branch.
                    if (label != null && label.text.StartsWith("Hidden (", StringComparison.Ordinal))
                        Capture(label, label.text);
                }
            }
            catch (Exception ex) { Warn("hidden-tab", ex); }
        }

        // The top-level overview button does not use the mod's Text() helper.
        // Its tooltip is supplied as Func<string> callbacks. Replace just those
        // callbacks after original creation; preserve button identity and click action.
        private static void AfterDecorButton()
        {
            if (!enabled || decorButtonField == null) return;
            try
            {
                var button = decorButtonField.GetValue(null);
                if (button == null || ReferenceEquals(button, tooltipAttached) || ReferenceEquals(button, tooltipFailed)) return;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var method = button.GetType().GetMethods(flags).FirstOrDefault(m =>
                {
                    if (m.Name != "SetTooltipMessages") return false;
                    var pars = m.GetParameters();
                    return pars.Length >= 2 && pars[0].ParameterType == typeof(Func<string>) &&
                           pars[1].ParameterType == typeof(Func<string>) &&
                           pars.Skip(2).All(x => x.IsOptional);
                });
                if (method == null) { tooltipFailed = button; Warn("tooltip-signature", new MissingMethodException("SetTooltipMessages(Func<string>, Func<string>)")); return; }
                Func<string> text = () => enabled && profiles != null
                    ? profiles.Translate(ProfileId, Channel, "Decorate the hideout") : "Decorate the hideout";
                var pars2 = method.GetParameters();
                var args = new object[pars2.Length];
                args[0] = text; args[1] = text;
                for (int i = 2; i < args.Length; i++) args[i] = Type.Missing;
                method.Invoke(button, args);
                tooltipAttached = button;
            }
            catch (Exception ex) { tooltipFailed = decorButtonField.GetValue(null); Warn("tooltip", ex); }
        }

        private static void Capture(TextMeshProUGUI label, string original, bool isCatalogCell = false)
        {
            if (label == null || string.IsNullOrEmpty(original)) return;
            if (!states.TryGetValue(label, out var state))
            {
                state = new LabelState(); states.Add(label, state); labels.Add(new WeakReference(label));
            }
            state.Source = original;
            state.IsCatalogCell = isCatalogCell;
            Apply(label, state, original);
        }

        private static void Apply(TextMeshProUGUI label, LabelState state, string english)
        {
            if (label == null || state == null) return;
            var bold = english.Length >= 7 && english.StartsWith("<b>", StringComparison.Ordinal) &&
                       english.EndsWith("</b>", StringComparison.Ordinal);
            var raw = bold ? english.Substring(3, english.Length - 7) : english;
            string translated;
            try
            {
                const string searchPlaceholder = "<color=#8a877f>Search...</color>";
                translated = state.IsCatalogCell ? TranslateCell(english) :
                    string.Equals(english, searchPlaceholder, StringComparison.Ordinal)
                        ? "<color=#8a877f>" + profiles.Translate(ProfileId, Channel, "Search...") + "</color>"
                        : TranslateOwnOrSiege(raw);
                if (!state.IsCatalogCell) translated = TranslateDynamicItemName(label, english, translated);
            }
            catch (Exception ex) { Warn("translation", ex); translated = english; }
            var chosen = state.IsCatalogCell ? translated : bold ? "<b>" + translated + "</b>" : translated;
            state.Applied = chosen;
            if (!string.Equals(label.text, chosen, StringComparison.Ordinal)) label.text = chosen;
            if (LocaleMode.IsKoreanMode())
            {
                try { ApplyFont(label, state, translated); }
                catch (Exception ex) { Warn("font", ex); }
            }
            else if (state.AppliedFont != null && label.font == state.AppliedFont && state.OriginalFont != null)
            {
                label.font = state.OriginalFont;
                state.OriginalFont = null; state.AppliedFont = null;
            }
        }

        private static bool SupportsHangul(TMP_FontAsset font, string text)
        {
            if (font == null) return false;
            foreach (var c in text)
            {
                if (!((c >= '\uac00' && c <= '\ud7a3') || (c >= '\u3130' && c <= '\u318f') ||
                      (c >= '\u1100' && c <= '\u11ff'))) continue;
                if (!font.HasCharacter(c)) return false;
            }
            return true;
        }

        private static void ApplyFont(TextMeshProUGUI label, LabelState state, string text)
        {
            if (SupportsHangul(label.font, text)) return;
            if (loadedFonts == null || Time.frameCount - fontCheckFrame >= 300)
            {
                loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                fontCheckFrame = Time.frameCount;
            }
            foreach (var font in loadedFonts ?? new TMP_FontAsset[0])
            {
                if (font == null || !SupportsHangul(font, text)) continue;
                if (state.OriginalFont == null) state.OriginalFont = label.font;
                if (font != label.font) { label.font = font; state.AppliedFont = font; }
                return;
            }
            SPT.EditableTranslations.MinimalLog.WarnOnce("hideout-interiors-r002-font",
                () => "Hideout Interiors UI: a font containing the required Hangul glyphs was not found; Korean text may not render.");
        }
    }
}
