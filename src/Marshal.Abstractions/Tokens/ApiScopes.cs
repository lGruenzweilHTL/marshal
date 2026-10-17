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

    /// <summary>Running AI tasks (<c>POST /api/v1/ai-tasks</c>), e.g. from Home Assistant automations.</summary>
    public const string AiTasksWrite = "aitasks:write";

    /// <summary>Prefix of the per-source webhook scopes (<c>hooks:&lt;source&gt;</c>).</summary>
    public const string HooksPrefix = "hooks:";

    /// <summary>Posting Uptime Kuma webhooks.</summary>
    public const string HooksUptimeKuma = HooksPrefix + "uptime-kuma";

    /// <summary>Posting Proxmox VE webhooks.</summary>
    public const string HooksProxmox = HooksPrefix + "proxmox";

    /// <summary>Posting Seerr webhooks.</summary>
    public const string HooksSeerr = HooksPrefix + "seerr";

    /// <summary>Every known scope.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { ConversationWrite, TasksRead, TasksWrite, AiTasksWrite, Admin, HooksUptimeKuma, HooksProxmox, HooksSeerr };

    /// <summary>The scope that allows posting webhooks of <paramref name="source"/>.</summary>
    public static string Hooks(string source) => HooksPrefix + source;

    /// <summary>Whether <paramref name="scope"/> is a webhook scope; only the <c>admin</c> role grants those.</summary>
    public static bool IsHook(string scope) => scope.StartsWith(HooksPrefix, StringComparison.Ordinal);
}
