namespace Marshal.Abstractions.Tokens;

/// <summary>
/// Scopes a machine token can carry. Cookie principals carry the scopes their role grants.
/// </summary>
public static class ApiScopes
{
    public const string ConversationWrite = "conversation:write";
    public const string TasksRead = "tasks:read";
    public const string TasksWrite = "tasks:write";
    public const string Admin = "admin";

    /// <summary>Every known scope.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { ConversationWrite, TasksRead, TasksWrite, Admin };
}
