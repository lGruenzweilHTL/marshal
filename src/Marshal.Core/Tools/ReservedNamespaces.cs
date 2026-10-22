namespace Marshal.Core.Tools;

/// <summary>
/// Namespaces only built-in tools may use. They name capabilities, never providers.
/// </summary>
public static class ReservedNamespaces
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "agent", "memory", "tasks", "notifications", "timers", "mail", "rss", "printer", "calendar", "notes", "places", "energy", "artifacts", "infra", "repos", "media", "music", "system", "maintenance", "web",
    };
}
