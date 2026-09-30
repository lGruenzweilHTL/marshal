namespace Marshal.Tasks.Executors;

/// <summary>
/// The <c>executors</c> section of <c>marshal.yaml</c>. No executor is available until one is configured.
/// </summary>
public sealed class ExecutorOptions
{
    /// <summary>Marshal's base URL as seen from the executors; callbacks go to <c>api/v1/executor/callbacks/&lt;session&gt;</c> below it.</summary>
    public Uri? CallbackBaseUrl { get; set; }

    public ClaudeCodeOptions ClaudeCode { get; set; } = new();
}

/// <summary>
/// <c>executors.claudeCode</c>: the executor host that runs Claude Code sessions.
/// </summary>
public sealed class ClaudeCodeOptions
{
    /// <summary>Base URL of <c>Marshal.ExecutorHost</c>; unset = the executor is not available.</summary>
    public Uri? Url { get; set; }

    /// <summary>Name of the secret holding the executor host's token.</summary>
    public string? TokenSecret { get; set; }

    /// <summary>Repositories sessions may work on (directory names on the executor host).</summary>
    public List<ExecutorRepository> Repositories { get; set; } = [];
}

/// <summary>A repository on the executor host.</summary>
public sealed class ExecutorRepository
{
    public string Name { get; set; } = "";

    /// <summary>What the repository is, for the model.</summary>
    public string? Description { get; set; }
}
