using System.Collections.Concurrent;
using SPTarkov.Server.Core.Models.Common;

namespace SPT_Mod_Korean_Server;

internal static class CultureRegistry
{
    internal const string Korean = "kr";
    internal const string Bilingual = "kr-en";
    internal const string Source = "source";

    private static readonly ConcurrentDictionary<string, string> BySession = new(StringComparer.Ordinal);

    internal static string Normalize(string? culture)
    {
        if (string.Equals(culture, Korean, StringComparison.OrdinalIgnoreCase)) return Korean;
        if (string.Equals(culture, Bilingual, StringComparison.OrdinalIgnoreCase)) return Bilingual;
        return Source;
    }

    internal static void Set(MongoId sessionId, string? culture)
        => BySession[sessionId.ToString()] = Normalize(culture);

    internal static string Get(MongoId sessionId)
        => BySession.TryGetValue(sessionId.ToString(), out var culture) ? culture : Source;
}
