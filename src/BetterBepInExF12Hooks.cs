using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // BetterBepInEx 1.0.4 copies the stock settings into its own SettingsModel.
    // Translate only that model's GUIContent and search aliases after Build; never edit
    // SettingEntryBase, ConfigEntryBase, definitions, dropdown values or the source mod.
    internal static class BetterBepInExF12Hooks
    {
        private const string ModelTypeName = "BetterBepInEx.SettingsModel";
        private const string PluginTypeName = "BetterBepInEx.BetterBepInExPlugin";
        private const string SupportedVersion = "1.0.4";
        private const string SupportedGuid = "com.mybutthasarash.betterbepinex";
        private static readonly object Gate = new object();
        private static readonly HashSet<Assembly> Patched = new HashSet<Assembly>();
        private static Harmony harmony;
        private static ConfigManagerProfiles profiles;
        private static bool enabled;
        private static WeakReference pluginReference;
        private static Type patchedPluginType;
        // The upstream Build() resets undo/redo history and invokes OnModelRebuilt(),
        // which also closes popups and cancels keyboard capture. A language change
        // must never invoke that workflow. Retain only presentation baselines for
        // the current model and restore/retranslate them in place.
        private sealed class RowOriginal
        {
            internal object Row;
            internal GUIContent Name;
            internal GUIContent NamePlain;
            internal string Description;
            internal GUIContent DescriptionContent;
            internal string Haystack;
            internal GUIContent[] OptionLabels;
        }

        private sealed class SectionOriginal
        {
            internal object Section;
            internal GUIContent Label;
        }

        private sealed class ModelOriginals
        {
            internal readonly List<RowOriginal> Rows = new List<RowOriginal>();
            internal readonly List<SectionOriginal> Sections = new List<SectionOriginal>();
        }

        private static ConditionalWeakTable<object, ModelOriginals> modelOriginals =
            new ConditionalWeakTable<object, ModelOriginals>();
        private static WeakReference modelReference;

        private static GUIContent Copy(GUIContent content) => content == null ? null : new GUIContent(content);

        private static GUIContent[] Copy(GUIContent[] labels)
        {
            if (labels == null) return null;
            var result = new GUIContent[labels.Length];
            for (int i = 0; i < labels.Length; i++) result[i] = Copy(labels[i]);
            return result;
        }

        private static ModelOriginals CaptureModel(object model)
        {
            var original = new ModelOriginals();
            if (!(Read(model, "Pages") is IEnumerable pages)) return original;
            foreach (var page in pages)
            {
                if (!(Read(page, "Sections") is IEnumerable sections)) continue;
                foreach (var section in sections)
                {
                    original.Sections.Add(new SectionOriginal
                    {
                        Section = section,
                        Label = Copy(Read(section, "Label") as GUIContent),
                    });
                    if (!(Read(section, "Rows") is IEnumerable rows)) continue;
                    foreach (var row in rows)
                    {
                        original.Rows.Add(new RowOriginal
                        {
                            Row = row,
                            Name = Copy(Read(row, "Name") as GUIContent),
                            NamePlain = Copy(Read(row, "NamePlain") as GUIContent),
                            Description = Read(row, "Description") as string,
                            DescriptionContent = Copy(Read(row, "DescriptionContent") as GUIContent),
                            Haystack = Read(row, "Haystack") as string,
                            OptionLabels = Copy(Read(row, "OptionLabels") as GUIContent[]),
                        });
                    }
                }
            }
            return original;
        }

        private static void RestoreModel(ModelOriginals original)
        {
            foreach (var section in original.Sections)
                Set(section.Section, "Label", Copy(section.Label));
            foreach (var row in original.Rows)
            {
                Set(row.Row, "Name", Copy(row.Name));
                Set(row.Row, "NamePlain", Copy(row.NamePlain));
                Set(row.Row, "Description", row.Description);
                Set(row.Row, "DescriptionContent", Copy(row.DescriptionContent));
                Set(row.Row, "Haystack", row.Haystack);
                Set(row.Row, "OptionLabels", Copy(row.OptionLabels));
            }
        }

        private static void ApplyLanguage(object model, ModelOriginals original)
        {
            // Restoring first also prevents Korean search aliases from accumulating
            // on each language cycle or being left behind in English mode.
            RestoreModel(original);
            if (profiles == null || !LocaleMode.IsKoreanMode()) return;
            TranslateModel(model);
        }

        internal static int Enable(Harmony patcher, IEnumerable<Assembly> assemblies, ConfigManagerProfiles data)
        {
            if (patcher == null) throw new ArgumentNullException(nameof(patcher));
            if (data == null) throw new ArgumentNullException(nameof(data));
            lock (Gate)
            {
                if (enabled) return 0;
                harmony = patcher;
                profiles = data;
                enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            var count = 0;
            foreach (var assembly in assemblies ?? Enumerable.Empty<Assembly>()) count += TryPatch(assembly);
            if (count > 0) RefreshLanguage(); // BetterBepInEx may have built its model before our Start.
            return count;
        }

        internal static void Disable()
        {
            lock (Gate)
            {
                if (enabled) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                enabled = false;
                harmony = null;
                profiles = null;
                Patched.Clear();
                var model = modelReference?.Target;
                if (model != null && modelOriginals.TryGetValue(model, out var original))
                {
                    try { RestoreModel(original); } catch { /* Optional UI was already torn down. */ }
                }
                modelReference = null;
                modelOriginals = new ConditionalWeakTable<object, ModelOriginals>();
                pluginReference = null;
                patchedPluginType = null;
            }
        }

        internal static void RefreshLanguage()
        {
            // Do NOT call BetterBepInExPlugin.RebuildModel here: SettingsModel.Build
            // clears History, and MenuWindow.OnModelRebuilt cancels active UI actions.
            // The existing model's config objects, rows, selection and undo chain stay intact.
            var model = modelReference?.Target;
            if (model == null)
            {
                var plugin = pluginReference?.Target ?? ReadStatic(patchedPluginType, "Instance");
                model = Read(plugin, "_model");
            }
            if (model == null || !modelOriginals.TryGetValue(model, out var original)) return;
            try { ApplyLanguage(model, original); }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("betterbepinex-f12-refresh", () =>
                    "[Config UI] BetterBepInEx display refresh skipped: " + ex.Message);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                // AssemblyLoad can arrive off the Unity thread. Only install here; the
                // BetterBepInEx Start path will build its model on Unity's thread.
                TryPatch(args.LoadedAssembly);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("betterbepinex-f12-lateload", () =>
                    "[Config UI] BetterBepInEx late-load hook skipped: " + ex.Message);
            }
        }

        private static int TryPatch(Assembly assembly)
        {
            if (assembly == null) return 0;
            Type model;
            Type pluginType;
            try
            {
                model = assembly.GetType(ModelTypeName, false);
                if (model == null) return 0;
                pluginType = assembly.GetType(PluginTypeName, false);
            }
            catch { return 0; }

            var versionField = pluginType?.GetField("PluginVersion", BindingFlags.Static | BindingFlags.Public);
            var guidField = pluginType?.GetField("PluginGuid", BindingFlags.Static | BindingFlags.Public);
            if (versionField == null || guidField == null || !versionField.IsLiteral || !guidField.IsLiteral ||
                !string.Equals(versionField.GetRawConstantValue() as string, SupportedVersion, StringComparison.Ordinal) ||
                !string.Equals(guidField.GetRawConstantValue() as string, SupportedGuid, StringComparison.Ordinal))
                return 0; // Never guess compatible private model fields on another upstream release.

            var build = model.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "Build" && m.ReturnType == typeof(void) && m.GetParameters().Length == 3);
            if (build == null) return 0;
            lock (Gate)
            {
                if (!enabled || harmony == null || !Patched.Add(assembly)) return 0;
            }
            try
            {
                harmony.Patch(build, postfix: new HarmonyMethod(typeof(BetterBepInExF12Hooks).GetMethod(
                    nameof(AfterModelBuild), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last });
                var instance = ReadStatic(pluginType, "Instance");
                patchedPluginType = pluginType;
                if (instance != null) pluginReference = new WeakReference(instance);
                return 1;
            }
            catch
            {
                lock (Gate) Patched.Remove(assembly);
                throw;
            }
        }

        private static void AfterModelBuild(object __instance)
        {
            if (__instance == null || profiles == null) return;
            try
            {
                // Build creates new rows on the same SettingsModel instance. Capture
                // their untouched display values before any translation is applied.
                var original = CaptureModel(__instance);
                modelOriginals.Remove(__instance);
                modelOriginals.Add(__instance, original);
                modelReference = new WeakReference(__instance);
                ApplyLanguage(__instance, original);
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("betterbepinex-f12-model", () =>
                    "[Config UI] BetterBepInEx display translation skipped: " + ex.Message);
            }
        }

        private static void TranslateModel(object model)
        {
                if (!(Read(model, "Pages") is IEnumerable pages)) return;
                foreach (var page in pages)
                {
                    var guid = Read(page, "Guid") as string;
                    if (string.IsNullOrWhiteSpace(guid) || !(Read(page, "Sections") is IEnumerable sections)) continue;
                    foreach (var section in sections)
                    {
                        if (Read(section, "IsHiddenGroup") is bool hidden && hidden) continue;
                        var sectionName = Read(section, "Name") as string ?? string.Empty;
                        if (!(Read(section, "Rows") is IEnumerable rows)) continue;
                        string translatedCategory = null;
                        foreach (var row in rows)
                        {
                            var setting = Read(row, "Setting");
                            if (setting == null) continue;
                            var entry = Read(row, "Entry");
                            var definition = Read(entry, "Definition");
                            var name = (Read(setting, "DispName") as string ?? string.Empty).TrimStart('!');
                            var source = new ConfigUiSource
                            {
                                plugin_guid = guid,
                                section = Read(definition, "Section") as string ?? sectionName,
                                key = Read(definition, "Key") as string ?? name,
                                source_category = Read(setting, "Category") as string ?? sectionName,
                                source_display_name = Read(setting, "DispName") as string ?? name,
                                source_description = Read(setting, "Description") as string,
                            };
                            var translation = profiles.Resolve(source);
                            if (translation != null)
                            {
                                if (!string.IsNullOrEmpty(translation.category)) translatedCategory = translation.category;
                                var display = translation.display_name;
                                var description = translation.description;
                                if (!string.IsNullOrEmpty(display) || !string.IsNullOrEmpty(description))
                                {
                                    // The GUIContent copies retain existing images and other metadata.
                                    var normal = Read(row, "Name") as GUIContent;
                                    var plain = Read(row, "NamePlain") as GUIContent;
                                    if (normal != null)
                                    {
                                        var content = new GUIContent(normal);
                                        if (!string.IsNullOrEmpty(display)) content.text = display;
                                        if (!string.IsNullOrEmpty(description)) content.tooltip = description;
                                        Set(row, "Name", content);
                                    }
                                    if (plain != null && !string.IsNullOrEmpty(display))
                                    {
                                        var content = new GUIContent(plain) { text = display };
                                        Set(row, "NamePlain", content);
                                    }
                                    if (!string.IsNullOrEmpty(description))
                                    {
                                        Set(row, "Description", description);
                                        var old = Read(row, "DescriptionContent") as GUIContent;
                                        Set(row, "DescriptionContent", old == null
                                            ? new GUIContent(description) : new GUIContent(old) { text = description });
                                    }
                                }
                            }

                            var aliases = new List<string>();
                            if (translation != null)
                            {
                                if (!string.IsNullOrEmpty(translation.category)) aliases.Add(translation.category);
                                if (!string.IsNullOrEmpty(translation.display_name)) aliases.Add(translation.display_name);
                                if (!string.IsNullOrEmpty(translation.description)) aliases.Add(translation.description);
                            }
                            // Replace only labels, not their associated option objects or indices.
                            var options = Read(row, "Options") as Array;
                            var labels = Read(row, "OptionLabels") as GUIContent[];
                            if (options != null && labels != null && options.Length == labels.Length)
                            {
                                for (int i = 0; i < labels.Length; i++)
                                {
                                    var label = labels[i];
                                    if (label == null) continue;
                                    var translated = profiles.ResolveValue(source, options.GetValue(i), label.text);
                                    if (string.IsNullOrEmpty(translated)) continue;
                                    labels[i] = new GUIContent(label) { text = translated };
                                    aliases.Add(translated);
                                }
                            }
                            if (aliases.Count > 0)
                            {
                                var originalHaystack = Read(row, "Haystack") as string ?? string.Empty;
                                Set(row, "Haystack", originalHaystack + "\n" + string.Join("\n", aliases).ToLowerInvariant());
                            }
                        }
                        if (!string.IsNullOrEmpty(translatedCategory))
                        {
                            var oldLabel = Read(section, "Label") as GUIContent;
                            if (oldLabel != null)
                                Set(section, "Label", new GUIContent(oldLabel) { text = translatedCategory.ToUpperInvariant() });
                        }
                    }
                }
        }

        // The screenshot/default-value helpers call SettingsModel.OptionText directly
        // instead of consuming Row.OptionLabels. Route only list/enum presentation
        // through the same per-plugin F12 value profile; raw ConfigEntry values stay intact.
        internal static string TranslateRenderedValue(object row, string rendered)
        {
            if (row == null || string.IsNullOrEmpty(rendered) || profiles == null || !LocaleMode.IsKoreanMode())
                return rendered;
            var kind = Read(row, "Kind")?.ToString();
            if (kind != "List" && kind != "Enum") return rendered;
            var setting = Read(row, "Setting");
            var page = Read(row, "Page");
            if (setting == null || page == null) return rendered;
            var entry = Read(row, "Entry");
            var definition = Read(entry, "Definition");
            var display = Read(setting, "DispName") as string ?? string.Empty;
            var source = new ConfigUiSource
            {
                plugin_guid = Read(page, "Guid") as string,
                section = Read(definition, "Section") as string ?? Read(setting, "Category") as string ?? string.Empty,
                key = Read(definition, "Key") as string ?? display.TrimStart('!'),
                source_display_name = display,
                source_category = Read(setting, "Category") as string,
                source_description = Read(setting, "Description") as string,
            };
            var translated = profiles.ResolveValue(source, null, rendered);
            return string.IsNullOrEmpty(translated) ? rendered : translated;
        }

        private static object ReadStatic(Type type, string name)
        {
            return type?.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        }

        private static object Read(object instance, string name)
        {
            if (instance == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = instance.GetType();
            var field = type.GetField(name, flags);
            if (field != null) return field.GetValue(instance);
            var property = type.GetProperty(name, flags);
            return property != null && property.GetIndexParameters().Length == 0 ? property.GetValue(instance, null) : null;
        }

        private static void Set(object instance, string name, object value)
        {
            instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(instance, value);
        }
    }
}
