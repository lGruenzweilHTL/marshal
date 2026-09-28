namespace Marshal.Api.Auth;

/// <summary>
/// Post-login redirect targets. Only app-relative paths are accepted, so login cannot become an open redirect.
/// </summary>
internal static class ReturnUrls
{
    public const string Default = "/";

    public static bool IsLocal(string url) =>
        url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);
}
