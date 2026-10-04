using System;
using System.Collections.Generic;

namespace SPT.EditableTranslations
{
    // Stable issue keys, bounded memory, no files and no repeated stack traces.
    internal static class MinimalLog
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
        internal static Action<string> WarningSink = message => Console.WriteLine(message);
        internal static bool HasWarnings { get { lock(Gate) return Seen.Count != 0; } }
        internal static void Reset() { lock(Gate) Seen.Clear(); }
        internal static void WarnOnce(string key, Func<string> message)
        {
            lock(Gate)
            {
                if(Seen.Count >= 512 || !Seen.Add(key)) return;
            }
            var text = (message() ?? "").Replace('\r',' ').Replace('\n',' ');
            if(text.Length > 350) text = text.Substring(0,350) + "…";
            WarningSink("[Mod Korean: " + key + "] " + text);
        }
    }
}
