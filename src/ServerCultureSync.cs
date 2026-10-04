using System;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace SPT.ModKoreanAddon
{
    /// <summary>
    /// Sends the already-authoritative Korean Patch Fix culture to the server-side
    /// direct-message translator. This is not a second language setting: the value
    /// is derived only from KoreanPatchFix.ClientLocaleRuntime.CurrentCulture().
    /// </summary>
    internal static class ServerCultureSync
    {
        internal const string Route = "/sptmodkorean/culture";

        internal static bool Push(string culture)
        {
            var normalized = string.Equals(culture, LocaleMode.Korean, StringComparison.OrdinalIgnoreCase)
                ? LocaleMode.Korean
                : string.Equals(culture, LocaleMode.Bilingual, StringComparison.OrdinalIgnoreCase)
                    ? LocaleMode.Bilingual
                    : "source";

            var response = RequestHandler.PostJson(Route, JsonConvert.SerializeObject(new CultureRequest { culture = normalized }));
            return response != null && response.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0;
        }

        private sealed class CultureRequest
        {
            public string culture { get; set; }
        }
    }
}
