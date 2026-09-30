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

    /// <summary>
    /// Merges <paramref name="branch"/> into the branch it was created from (a merge commit; nothing is pushed) and
    /// deletes it. A conflict, or a base branch that moved meanwhile, changes nothing.
    /// </summary>
    Task<MergeOutcome> MergeAsync(string repository, string branch, string message, CancellationToken cancellationToken);

    /// <summary>Deletes <paramref name="branch"/>; false when it does not exist.</summary>
    Task<bool> DeleteBranchAsync(string repository, string branch, CancellationToken cancellationToken);
}

/// <summary>What a merge did.</summary>
public enum MergeOutcome
{
    Merged,

    /// <summary>The branch does not exist (never created, discarded or already merged).</summary>
    NotFound,

    /// <summary>The branch does not merge cleanly into its base, or the base moved during the merge.</summary>
    Conflict,
}

/// <summary><see cref="IWorktrees"/> through the <c>git</c> CLI (arguments as a list, never a shell).</summary>
public sealed class GitWorktrees : IWorktrees
{
    private const int MaxDiffLength = 1_000_000;

    // The branch a session started from, kept in the repository's config so a merge after a restart finds it
    // (`git branch -D` removes the section with the branch).
    private const string BaseKey = "marshalbase";

    public async Task<string> CreateAsync(string repository, string path, string branch, string? baseBranch, CancellationToken cancellationToken)
    {
        string baseRef = baseBranch ?? (await RunAsync(repository, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD")).Trim();
        string commit = (await RunAsync(repository, cancellationToken, "rev-parse", "--verify", $"{baseRef}^{{commit}}")).Trim();
        await RunAsync(repository, cancellationToken, "worktree", "add", "-q", "-b", branch, path, commit);
        if (baseRef != "HEAD")
            await RunAsync(repository, cancellationToken, "config", $"branch.{branch}.{BaseKey}", baseRef);
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

    public async Task<MergeOutcome> MergeAsync(string repository, string branch, string message, CancellationToken cancellationToken)
    {
        if (await RevisionAsync(repository, $"refs/heads/{branch}", cancellationToken) is not { } tip)
            return MergeOutcome.NotFound;
        (int configured, string baseName) = await TryRunAsync(repository, cancellationToken, "config", "--get", $"branch.{branch}.{BaseKey}");
        if (configured != 0 || await RevisionAsync(repository, $"refs/heads/{baseName.Trim()}", cancellationToken) is not { } old)
            return MergeOutcome.Conflict;
        string baseRef = $"refs/heads/{baseName.Trim()}";

        // Merge without a working tree, then move the base branch only if it still points where the merge started.
        (int merged, string tree) = await TryRunAsync(repository, cancellationToken, "merge-tree", "--write-tree", old, tip);
        if (merged == 1)
            return MergeOutcome.Conflict;
        if (merged != 0)
            throw new InvalidOperationException("git merge-tree failed.");

        string commit = (await RunAsync(repository, cancellationToken, "-c", "user.name=Marshal", "-c", "user.email=marshal@localhost",
            "commit-tree", FirstLine(tree), "-p", old, "-p", tip, "-m", message)).Trim();
        if ((await TryRunAsync(repository, cancellationToken, "update-ref", "-m", message, baseRef, commit, old)).ExitCode != 0)
            return MergeOutcome.Conflict;

        // When the base branch is checked out in the repository, bring its index and files along.
        (int symbolic, string head) = await TryRunAsync(repository, cancellationToken, "symbolic-ref", "-q", "HEAD");
        if (symbolic == 0 && head.Trim() == baseRef)
            await TryRunAsync(repository, cancellationToken, "read-tree", "-m", "-u", old, commit);

        await RunAsync(repository, cancellationToken, "branch", "-D", branch);
        return MergeOutcome.Merged;
    }

    public async Task<bool> DeleteBranchAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        if (await RevisionAsync(repository, $"refs/heads/{branch}", cancellationToken) is null)
            return false;

        await RunAsync(repository, cancellationToken, "branch", "-D", branch);
        return true;
    }

    private static async Task<string?> RevisionAsync(string repository, string reference, CancellationToken cancellationToken)
    {
        (int exitCode, string output) = await TryRunAsync(repository, cancellationToken, "rev-parse", "--verify", "--quiet", reference);
        return exitCode == 0 ? output.Trim() : null;
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    private static async Task<string> RunAsync(string directory, CancellationToken cancellationToken, params string[] arguments)
    {
        (int exitCode, string output, string error) = await ExecuteAsync(directory, arguments, cancellationToken);
        return exitCode == 0 ? output : throw new InvalidOperationException($"git {arguments[0]} failed: {error.Trim()}");
    }

    private static async Task<(int ExitCode, string Output)> TryRunAsync(string directory, CancellationToken cancellationToken, params string[] arguments)
    {
        (int exitCode, string output, _) = await ExecuteAsync(directory, arguments, cancellationToken);
        return (exitCode, output);
    }

    private static async Task<(int ExitCode, string Output, string Error)> ExecuteAsync(string directory, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output, await error);
    }
}
