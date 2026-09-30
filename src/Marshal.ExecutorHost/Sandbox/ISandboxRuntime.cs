namespace Marshal.ExecutorHost.Sandbox;

/// <summary>
/// Runs Claude Code headless in an isolated container on this host.
/// </summary>
public interface ISandboxRuntime
{
    /// <summary>Runs <paramref name="run"/>, handing each stdout line to <paramref name="onLine"/>; returns the exit code. Cancelling stops the container.</summary>
    Task<int> RunAsync(SandboxRun run, Func<string, CancellationToken, Task> onLine, CancellationToken cancellationToken);
}

/// <summary>One sandbox run.</summary>
/// <param name="Name">Container name.</param>
/// <param name="WorkDirectory">The session's worktree (mounted at the same path).</param>
/// <param name="GitDirectory">The repository's <c>.git</c> directory the worktree points to (mounted at the same path).</param>
/// <param name="Prompt">The task for Claude Code.</param>
public sealed record SandboxRun(string Name, string WorkDirectory, string GitDirectory, string Prompt);
