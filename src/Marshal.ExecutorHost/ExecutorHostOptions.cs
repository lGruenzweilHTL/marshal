namespace Marshal.ExecutorHost;

/// <summary>
/// The <c>executorHost</c> configuration section. No secrets: the host token and the sandbox environment
/// (e.g. <c>ANTHROPIC_API_KEY</c>) are files, normally under <c>/run/secrets</c>.
/// </summary>
public sealed class ExecutorHostOptions
{
    /// <summary>File holding the token Marshal authenticates with.</summary>
    public string TokenFile { get; set; } = "/run/secrets/executor_host_token";

    /// <summary>Directory whose subdirectories are the repositories sessions may work on.</summary>
    public string RepositoriesRoot { get; set; } = "/repos";

    /// <summary>Directory for the per-session git worktrees.</summary>
    public string WorktreesRoot { get; set; } = "/worktrees";

    /// <summary>Marshal's base URL as seen from this host; callbacks go only below it.</summary>
    public Uri? MarshalUrl { get; set; }

    /// <summary>Sandbox image with Claude Code.</summary>
    public string Image { get; set; } = "marshal-claude-sandbox";

    /// <summary>Docker network of the sandbox (its egress goes through the allowlist proxy).</summary>
    public string Network { get; set; } = "marshal-sandbox";

    /// <summary>Environment file for the sandbox (the model API key); passed to Docker, never read here.</summary>
    public string EnvFile { get; set; } = "/run/secrets/sandbox.env";

    public int MaxTurns { get; set; } = 50;

    public decimal MaxBudgetUsd { get; set; } = 5m;
}
