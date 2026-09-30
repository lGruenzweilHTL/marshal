using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marshal.ExecutorHost.Callbacks;
using Marshal.ExecutorHost.Git;
using Marshal.ExecutorHost.Sandbox;
using Microsoft.Extensions.Options;

namespace Marshal.ExecutorHost.Sessions;

/// <summary>
/// Coding sessions: each gets its own worktree on branch <c>marshal/&lt;id&gt;</c>, runs Claude Code in the sandbox and
/// reports to Marshal's callback URL (only below <see cref="ExecutorHostOptions.MarshalUrl"/>) with the session's
/// token: <c>progress</c> for the model's text, then <c>completed</c> (branch, diff, summary; the branch stays for
/// review), <c>failed</c> or <c>cancelled</c> (branch deleted). The worktree is removed at the end.
/// </summary>
public sealed partial class SessionManager(
    ISandboxRuntime sandbox,
    IWorktrees worktrees,
    ICallbackSender callbacks,
    IOptions<ExecutorHostOptions> options,
    ILogger<SessionManager> logger) : IAsyncDisposable
{
    private const int MaxProgressLength = 500;

    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<string, Task> _runs = new();

    public async Task<(StartedSession? Started, string? Error)> StartAsync(SessionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.CallbackToken))
            return (null, "prompt and callbackToken are required.");
        if (Repository(request.Repository) is not { } repository)
            return (null, "Unknown repository.");
        string gitDirectory = Path.Combine(repository, ".git");
        if (request.BaseBranch is { } baseBranch && (!BranchName().IsMatch(baseBranch) || baseBranch.Contains("..", StringComparison.Ordinal)))
            return (null, "Invalid base branch.");
        if (!Uri.TryCreate(request.CallbackUrl, UriKind.Absolute, out Uri? callback) || !IsBelowMarshal(callback))
            return (null, "The callback URL must be below Marshal's URL.");

        string id = Guid.NewGuid().ToString("N");
        string branch = $"marshal/{id}";
        string path = Path.Combine(options.Value.WorktreesRoot, id);
        string baseCommit = await worktrees.CreateAsync(repository, path, branch, request.BaseBranch, cancellationToken);

        var cancel = new CancellationTokenSource();
        _running[id] = cancel;
        var session = new Session(id, repository, path, branch, baseCommit, callback, request.CallbackToken!);
        var run = Task.Run(() => RunAsync(session, new SandboxRun($"marshal-exec-{id}", path, gitDirectory, request.Prompt!), cancel), CancellationToken.None);
        _runs[id] = run;
        _ = run.ContinueWith(_ => _runs.TryRemove(id, out Task? _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        LogStarted(id, request.Repository!);
        return (new StartedSession(id, branch), null);
    }

    /// <summary>Stops a running session; false when it is unknown or already finished.</summary>
    public bool Cancel(string id)
    {
        if (!_running.TryGetValue(id, out CancellationTokenSource? cancel))
            return false;

        cancel.Cancel();
        return true;
    }

    /// <summary>
    /// Merges a completed session's branch into the branch it started from and deletes it (see
    /// <see cref="IWorktrees.MergeAsync"/>). Refused while the session runs.
    /// </summary>
    public async Task<BranchResult> MergeAsync(string id, string? repositoryName, CancellationToken cancellationToken)
    {
        if (!SessionId().IsMatch(id))
            return BranchResult.NotFound;
        if (Repository(repositoryName) is not { } repository)
            return BranchResult.InvalidRepository;
        if (_running.ContainsKey(id))
            return BranchResult.Running;

        MergeOutcome outcome = await worktrees.MergeAsync(repository, $"marshal/{id}", $"Merge Marshal session {id}", cancellationToken);
        LogMerged(id, outcome);
        return outcome switch
        {
            MergeOutcome.Merged => BranchResult.Done,
            MergeOutcome.NotFound => BranchResult.NotFound,
            _ => BranchResult.Conflict,
        };
    }

    /// <summary>Deletes a completed session's branch (its proposal was rejected). Refused while the session runs.</summary>
    public async Task<BranchResult> DiscardAsync(string id, string? repositoryName, CancellationToken cancellationToken)
    {
        if (!SessionId().IsMatch(id))
            return BranchResult.NotFound;
        if (Repository(repositoryName) is not { } repository)
            return BranchResult.InvalidRepository;
        if (_running.ContainsKey(id))
            return BranchResult.Running;

        return await worktrees.DeleteBranchAsync(repository, $"marshal/{id}", cancellationToken) ? BranchResult.Done : BranchResult.NotFound;
    }

    /// <summary>Cancels running sessions and waits for their cleanup (worktree removal, the cancelled callback).</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (CancellationTokenSource cancel in _running.Values)
            cancel.Cancel();
        await Task.WhenAll(_runs.Values).WaitAsync(ShutdownWait).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task RunAsync(Session session, SandboxRun run, CancellationTokenSource cancel)
    {
        var result = new StreamResult();
        try
        {
            int exitCode = await sandbox.RunAsync(run, (line, ct) => OnLineAsync(session, line, result, ct), cancel.Token);
            if (exitCode != 0 || result.IsError)
            {
                await FinishAsync(session, new ExecutorCallback("failed", result.Summary ?? $"Claude Code exited with code {exitCode}.", CostUsd: result.CostUsd), keepBranch: false);
                return;
            }

            string diff = await worktrees.CommitAndDiffAsync(session.Path, session.BaseCommit, $"Marshal session {session.Id}", CancellationToken.None);
            await FinishAsync(session, new ExecutorCallback("completed", Branch: session.Branch, Diff: diff, Summary: result.Summary, CostUsd: result.CostUsd), keepBranch: true);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            await FinishAsync(session, new ExecutorCallback("cancelled"), keepBranch: false);
        }
        catch (Exception exception)
        {
            LogFailed(session.Id, exception);
            await FinishAsync(session, new ExecutorCallback("failed", "The session failed on the executor host."), keepBranch: false);
        }
        finally
        {
            _running.TryRemove(session.Id, out _);
            cancel.Dispose();
        }
    }

    private async Task OnLineAsync(Session session, string line, StreamResult result, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type))
                return;

            switch (type.GetString())
            {
                case "assistant":
                    foreach (string text in Texts(root))
                        await PostAsync(session, new ExecutorCallback("progress", text.Length <= MaxProgressLength ? text : text[..MaxProgressLength]), cancellationToken);
                    break;
                case "result":
                    result.Summary = root.TryGetProperty("result", out JsonElement summary) && summary.ValueKind == JsonValueKind.String ? summary.GetString() : null;
                    result.IsError = root.TryGetProperty("is_error", out JsonElement error) && error.ValueKind == JsonValueKind.True;
                    result.CostUsd = root.TryGetProperty("total_cost_usd", out JsonElement cost) && cost.ValueKind == JsonValueKind.Number ? cost.GetDecimal() : null;
                    break;
            }
        }
    }

    private static IEnumerable<string> Texts(JsonElement assistant)
    {
        if (!assistant.TryGetProperty("message", out JsonElement message) || !message.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out JsonElement kind) && kind.GetString() == "text"
                && block.TryGetProperty("text", out JsonElement text) && text.GetString() is { Length: > 0 } value)
            {
                yield return value;
            }
        }
    }

    private async Task FinishAsync(Session session, ExecutorCallback callback, bool keepBranch)
    {
        try
        {
            await worktrees.RemoveAsync(session.Repository, session.Path, session.Branch, deleteBranch: !keepBranch, CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            LogCleanupFailed(session.Id, exception);
        }

        // Finished before Marshal hears of it, so a merge right after the callback is not refused as running.
        _running.TryRemove(session.Id, out _);
        await PostAsync(session, callback, CancellationToken.None);
        LogFinished(session.Id, callback.Type);
    }

    private async Task PostAsync(Session session, ExecutorCallback callback, CancellationToken cancellationToken)
    {
        try
        {
            await callbacks.SendAsync(session.Callback, session.CallbackToken, callback, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            LogCallbackFailed(session.Id, callback.Type, exception);
        }
    }

    // A directory below the repositories root with a .git directory; null for anything else.
    private string? Repository(string? name)
    {
        if (name is null || !RepositoryName().IsMatch(name) || name.Contains("..", StringComparison.Ordinal))
            return null;
        string repository = Path.Combine(options.Value.RepositoriesRoot, name);
        return Directory.Exists(Path.Combine(repository, ".git")) ? repository : null;
    }

    private bool IsBelowMarshal(Uri callback) =>
        options.Value.MarshalUrl is { } marshal
        && Uri.Compare(callback, marshal, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0
        && callback.AbsolutePath.StartsWith(marshal.AbsolutePath, StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex RepositoryName();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$")]
    private static partial Regex BranchName();

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex SessionId();

    [LoggerMessage(Level = LogLevel.Information, Message = "Merging the branch of session {SessionId}: {Outcome}")]
    private partial void LogMerged(string sessionId, MergeOutcome outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} started on {Repository}")]
    private partial void LogStarted(string sessionId, string repository);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} finished: {Outcome}")]
    private partial void LogFinished(string sessionId, string outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "Session {SessionId} failed")]
    private partial void LogFailed(string sessionId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Removing the worktree of session {SessionId} failed")]
    private partial void LogCleanupFailed(string sessionId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting the {Type} callback of session {SessionId} failed")]
    private partial void LogCallbackFailed(string sessionId, string type, Exception exception);

    private sealed record Session(string Id, string Repository, string Path, string Branch, string BaseCommit, Uri Callback, string CallbackToken);

    private sealed class StreamResult
    {
        public string? Summary { get; set; }

        public bool IsError { get; set; }

        public decimal? CostUsd { get; set; }
    }
}

/// <summary>A session to start.</summary>
/// <param name="Repository">Repository name below the repositories root.</param>
/// <param name="Prompt">The task for Claude Code.</param>
/// <param name="BaseBranch">Branch to start from (default: the repository's current branch).</param>
/// <param name="CallbackUrl">Where to post progress and completion (below Marshal's URL).</param>
/// <param name="CallbackToken">The session's callback token.</param>
public sealed record SessionRequest(string? Repository, string? Prompt, string? BaseBranch, string? CallbackUrl, string? CallbackToken);

/// <summary>What a merge or discard request did.</summary>
public enum BranchResult
{
    Done,
    NotFound,
    InvalidRepository,
    Running,
    Conflict,
}

/// <summary>A merge or discard request: the repository the session worked on.</summary>
public sealed record BranchRequest(string? Repository);

/// <summary>A started session.</summary>
/// <param name="Id">Session id (the handle Marshal stores).</param>
/// <param name="Branch">The session's branch.</param>
public sealed record StartedSession(string Id, string Branch);
