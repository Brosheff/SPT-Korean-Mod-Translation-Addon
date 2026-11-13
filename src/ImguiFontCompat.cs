using System;
using System.Reflection;
using UnityEngine;

namespace SPT.ModKoreanAddon
{
    // UnityEngine.Font lives in UnityEngine.TextRenderingModule at runtime, but
    // that module is deliberately not part of the addon's compile-only refs.
    // Keep the optional Korean IMGUI fallback without a compile-time Font type.
    internal static class ImguiFontCompat
    {
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        private static Type RuntimeFontType()
        {
            try { return typeof(GUIStyle).GetProperty("font", PublicInstance)?.PropertyType; }
            catch (Exception) { return null; }
        }

        internal static object GetStyleFont(GUIStyle style)
        {
            if (style == null) return null;
            return typeof(GUIStyle).GetProperty("font", PublicInstance)?.GetValue(style, null);
        }

        internal static void SetStyleFont(GUIStyle style, object font)
        {
            if (style == null) return;
            var property = typeof(GUIStyle).GetProperty("font", PublicInstance);
            if (property == null || !property.CanWrite ||
                (font != null && !property.PropertyType.IsInstanceOfType(font))) return;
            property.SetValue(style, font, null);
        }

        internal static bool HasKoreanGlyphs(object font)
        {
            if (font == null) return false;
            try
            {
                var method = font.GetType().GetMethod("HasCharacter", PublicInstance, null,
                    new[] { typeof(char) }, null);
                return method != null &&
                    Equals(method.Invoke(font, new object[] { '한' }), true) &&
                    Equals(method.Invoke(font, new object[] { '글' }), true);
            }
            catch (Exception) { return false; }
        }

        internal static string[] InstalledFontNames()
        {
            try
            {
                var method = RuntimeFontType()?.GetMethod("GetOSInstalledFontNames", PublicStatic,
                    null, Type.EmptyTypes, null);
                return method?.Invoke(null, null) as string[] ?? Array.Empty<string>();
            }
            catch (Exception) { return Array.Empty<string>(); }
        }

        internal static object CreateOSFont(string family, int size, bool persist)
        {
            try
            {
                var type = RuntimeFontType();
                var factory = type?.GetMethod("CreateDynamicFontFromOSFont", PublicStatic,
                    null, new[] { typeof(string), typeof(int) }, null);
                var font = factory?.Invoke(null, new object[] { family, size });
                if (font == null) return null;
                if (!HasKoreanGlyphs(font))
                {
                    // A dynamically created Font with missing glyphs must not
                    // leak into the source mod's GUI or replace its existing font.
                    if (font is UnityEngine.Object invalid) UnityEngine.Object.Destroy(invalid);
                    return null;
                }
                if (persist && font is UnityEngine.Object asset)
                    asset.hideFlags = HideFlags.HideAndDontSave;
                return font;
            }
            catch (Exception) { return null; }
        }
    }
}
