using System.Diagnostics;
using System.Text;

namespace Marshal.ExecutorHost.Git;

/// <summary>
/// One git worktree and branch per session.
/// </summary>
public interface IWorktrees
{
    /// <summary>Adds a worktree at <paramref name="path"/> on a new <paramref name="branch"/> from <paramref name="baseBranch"/> (default: the current branch).</summary>
    Task<string> CreateAsync(string repository, string path, string branch, string? baseBranch, CancellationToken cancellationToken);

    /// <summary>Commits everything in the worktree and returns the diff of its branch against <paramref name="baseRef"/>.</summary>
    Task<string> CommitAndDiffAsync(string path, string baseRef, string message, CancellationToken cancellationToken);

    /// <summary>Removes the worktree, and its branch when <paramref name="deleteBranch"/> is set.</summary>
    Task RemoveAsync(string repository, string path, string branch, bool deleteBranch, CancellationToken cancellationToken);
}

/// <summary><see cref="IWorktrees"/> through the <c>git</c> CLI (arguments as a list, never a shell).</summary>
public sealed class GitWorktrees : IWorktrees
{
    private const int MaxDiffLength = 1_000_000;

    public async Task<string> CreateAsync(string repository, string path, string branch, string? baseBranch, CancellationToken cancellationToken)
    {
        string baseRef = baseBranch ?? (await RunAsync(repository, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD")).Trim();
        string commit = (await RunAsync(repository, cancellationToken, "rev-parse", "--verify", $"{baseRef}^{{commit}}")).Trim();
        await RunAsync(repository, cancellationToken, "worktree", "add", "-q", "-b", branch, path, commit);
        return commit;
    }

    public async Task<string> CommitAndDiffAsync(string path, string baseRef, string message, CancellationToken cancellationToken)
    {
        await RunAsync(path, cancellationToken, "add", "-A");
        if ((await RunAsync(path, cancellationToken, "status", "--porcelain")).Length > 0)
            await RunAsync(path, cancellationToken, "-c", "user.name=Marshal", "-c", "user.email=marshal@localhost", "commit", "-q", "-m", message);

        string diff = await RunAsync(path, cancellationToken, "diff", $"{baseRef}..HEAD");
        return diff.Length <= MaxDiffLength ? diff : diff[..MaxDiffLength] + "\n[diff truncated]\n";
    }

    public async Task RemoveAsync(string repository, string path, string branch, bool deleteBranch, CancellationToken cancellationToken)
    {
        await RunAsync(repository, cancellationToken, "worktree", "remove", "--force", path);
        if (deleteBranch)
            await RunAsync(repository, cancellationToken, "branch", "-D", branch);
    }

    private static async Task<string> RunAsync(string directory, CancellationToken cancellationToken, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0
            ? await output
            : throw new InvalidOperationException($"git {arguments[0]} failed: {(await error).Trim()}");
    }
}
