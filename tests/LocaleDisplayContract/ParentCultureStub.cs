// Contract test only. The client project excludes tests/**; production uses the actual parent.
namespace KoreanPatchFix
{
    internal static class ClientLocaleRuntime
    {
        internal static string Culture = "kr";
        internal static string CurrentCulture() => Culture;
    }
}
