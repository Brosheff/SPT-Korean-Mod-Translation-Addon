using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Microsoft.AspNetCore.Mvc;

namespace SPT_Mod_Korean_Server;

// ISB Special Forces 2.0.2: edit ONLY its embedded, authenticated /isb page assets.
// No catch-all controller, client-wide text hook, API/catalog rewriting or file patch.
internal static class IsbWebUiHooks
{
    private const string AssemblyName = "ISBSpecialForces";
    private const string ControllerName = "ISBSpecialForces.Web.IsbWebController";
    private const string IndexSha256 = "0cc23f8f894f2dd5edac64b3e18e69c79b77b6baf9485a85f1111d1301d22677";
    private const string JsSha256 = "8797c2e1379ec2345ca001177ba761333f37333cb10cce355dc66b6dbdf6f97d";
    private static readonly object Sync = new();
    private static readonly Harmony Harmony = new("local.spt41.modkorean.isb-web-2.0.2");
    private static MessageTranslator? translator;
    private static bool homePatched;
    private static bool assetsPatched;

    internal static void Enable(MessageTranslator? current)
    {
        if (current == null) return;
        translator = current;
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase))
                Patch(assembly);
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (string.Equals(args.LoadedAssembly?.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase))
            Patch(args.LoadedAssembly);
    }

    private static void Patch(Assembly assembly)
    {
        var controller = assembly.GetType(ControllerName, false);
        if (controller == null) return;
        const BindingFlags methods = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        lock (Sync)
        {
            try
            {
                var home = controller.GetMethod("Home", methods, null, Type.EmptyTypes, null);
                if (!homePatched && home?.ReturnType == typeof(IActionResult))
                {
                    Harmony.Patch(home, postfix: new HarmonyMethod(typeof(IsbWebUiHooks).GetMethod(
                        nameof(AfterHome), BindingFlags.Static | BindingFlags.NonPublic)));
                    homePatched = true;
                }
                var assets = controller.GetMethod("Assets", methods, null, new[] { typeof(string) }, null);
                if (!assetsPatched && assets?.ReturnType == typeof(IActionResult))
                {
                    Harmony.Patch(assets, postfix: new HarmonyMethod(typeof(IsbWebUiHooks).GetMethod(
                        nameof(AfterAssets), BindingFlags.Static | BindingFlags.NonPublic)));
                    assetsPatched = true;
                }
            }
            catch (Exception ex)
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("isb-web-hook", () =>
                    "ISB embedded web display hook skipped: " + ex.Message);
            }
        }
    }

    private static void AfterHome(object __instance, ref IActionResult __result)
        => TranslateResponse(__instance, __result, false);

    private static void AfterAssets(object __instance, string __0, ref IActionResult __result)
    {
        if (string.Equals(__0, "app.js", StringComparison.Ordinal))
            TranslateResponse(__instance, __result, true);
    }

    private static void TranslateResponse(object instance, IActionResult response, bool javascript)
    {
        if (translator == null || instance is not Controller controller || response is not FileContentResult result) return;
        try
        {
            // The browser UI has no connection to the EFT session culture. Only an
            // explicit Korean Accept-Language browser request activates translation.
            var accept = controller.HttpContext?.Request.Headers.AcceptLanguage.ToString();
            if (!IsKoreanBrowser(accept)) return;
            var original = result.FileContents;
            if (original == null || original.Length == 0) return;
            var digest = Convert.ToHexString(SHA256.HashData(original));
            var expected = javascript ? JsSha256 : IndexSha256;
            if (!string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase))
            {
                SPT.EditableTranslations.MinimalLog.WarnOnce("isb-web-version", () =>
                    "ISB web resource differs from reviewed 2.0.2 source; original page retained.");
                return;
            }
            var source = Encoding.UTF8.GetString(original);
            var translated = translator.TranslateIsbWebResource(source, javascript);
            if (!string.Equals(source, translated, StringComparison.Ordinal))
                result.FileContents = Encoding.UTF8.GetBytes(translated);
        }
        catch (Exception ex)
        {
            SPT.EditableTranslations.MinimalLog.WarnOnce("isb-web-response", () =>
                "ISB web display translation skipped; response retained: " + ex.Message);
        }
    }

    private static bool IsKoreanBrowser(string? languages)
    {
        if (string.IsNullOrWhiteSpace(languages)) return false;
        foreach (var part in languages.Split(','))
        {
            var code = part.Split(';')[0].Trim();
            if (string.Equals(code, "ko", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("ko-", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "kr", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return false;
    }
}
