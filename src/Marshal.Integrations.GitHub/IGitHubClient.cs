using Marshal.Abstractions;

namespace Marshal.Integrations.GitHub;

/// <summary>
/// Read-only GitHub REST API for allowlisted repositories: only <c>GET</c> requests exist. Failures use
/// <c>repos.not_found</c> and <c>repos.unavailable</c>.
/// </summary>
public interface IGitHubClient
{
    Task<Result<IReadOnlyList<GitHubPullSummary>>> ListPullsAsync(GitHubRepo repo, string state, int limit, CancellationToken cancellationToken);

    Task<Result<GitHubPull>> GetPullAsync(GitHubRepo repo, int number, CancellationToken cancellationToken);

    /// <summary>Issues only (pull requests, which GitHub lists as issues too, are left out), newest first.</summary>
    Task<Result<IReadOnlyList<GitHubIssueSummary>>> ListIssuesAsync(GitHubRepo repo, string state, CancellationToken cancellationToken);

    Task<Result<GitHubIssue>> GetIssueAsync(GitHubRepo repo, int number, CancellationToken cancellationToken);

    /// <summary>The newest <paramref name="limit"/> comments of an issue with <paramref name="total"/> comments, oldest first.</summary>
    Task<Result<IReadOnlyList<GitHubComment>>> ListCommentsAsync(GitHubRepo repo, int number, int total, int limit, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<GitHubRun>>> ListRunsAsync(GitHubRepo repo, string? branch, int limit, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<GitHubJob>>> ListJobsAsync(GitHubRepo repo, long runId, CancellationToken cancellationToken);

    /// <summary>Streams a pull request's diff or a job's log to <paramref name="read"/>.</summary>
    Task<Result> DownloadAsync(GitHubRepo repo, GitHubDownload download, long id, Func<Stream, CancellationToken, Task> read, CancellationToken cancellationToken);
}

/// <summary>Large text the client streams instead of parsing.</summary>
public enum GitHubDownload
{
    /// <summary>A pull request's unified diff (id = PR number).</summary>
    PullDiff,

    /// <summary>A workflow job's log (id = job id).</summary>
    JobLog,
}

public sealed record GitHubPullSummary(int Number, string Title, string Author, bool Draft, string Head, string Base, DateTimeOffset Updated);

public sealed record GitHubPull(
    int Number, string Title, string Author, string State, bool Draft, bool Merged, string Head, string HeadSha, string Base, string? Body, int ChangedFiles, int Additions, int Deletions);

public sealed record GitHubIssueSummary(int Number, string Title, string Author, IReadOnlyList<string> Labels, int Comments, DateTimeOffset Updated);

public sealed record GitHubIssue(int Number, string Title, string Author, string State, IReadOnlyList<string> Labels, string? Body, int Comments);

public sealed record GitHubComment(string Author, DateTimeOffset Created, string Body);

public sealed record GitHubRun(long Id, string Workflow, string Title, string Branch, string Event, string Status, string? Conclusion, string Sha, DateTimeOffset Created);

public sealed record GitHubJob(long Id, string Name, string Status, string? Conclusion, IReadOnlyList<GitHubStep> Steps);

public sealed record GitHubStep(string Name, string? Conclusion);
