using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tokens;
using Marshal.Data;
using Marshal.Data.Tasks;
using Marshal.Tasks.Running;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Executors;

/// <summary>
/// Starts executor sessions and applies their callbacks. A session belongs to an "on done" task that waits without a
/// run time; the session's callback token is bound to that task and to the session. Completion creates a
/// <c>code</c> proposal (branch and diff, merged only in the web UI), revokes the token, publishes
/// <c>executor.completed</c> / <c>failed</c> / <c>cancelled</c> on the event bus and resumes the task with the outcome.
/// Everything an executor reports is untrusted.
/// </summary>
public sealed partial class ExecutorService(
    IEnumerable<IExecutor> executors,
    IExecutorCallbackTokens tokens,
    ITaskStore tasks,
    ITaskScheduler scheduler,
    IEventPublisher events,
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IOptions<ExecutorOptions> options,
    TimeProvider time,
    ILogger<ExecutorService> logger) : IExecutorCallbacks
{
    /// <summary>Top-level key of the trigger payload the "on done" task resumes with.</summary>
    public const string PayloadKey = "executor";

    /// <summary>Event source of session outcomes; the entity is the session id.</summary>
    public const string EventSource = "executor";

    /// <summary>Callback path below <see cref="ExecutorOptions.CallbackBaseUrl"/> (the v1 callback endpoint).</summary>
    public const string CallbackPath = "api/v1/executor/callbacks/";

    private const int MaxTitleLength = 120;
    private const int MaxTextLength = 4000;

    private readonly IReadOnlyList<IExecutor> _executors = [.. executors];

    /// <summary>The configured executors.</summary>
    public IReadOnlyList<IExecutor> Executors => _executors;

    /// <summary>
    /// Creates <paramref name="onDone"/> (waiting for the outcome), a session with its callback token, and starts it.
    /// </summary>
    public async Task<Result<StartedSession>> StartAsync(string executorName, JsonElement spec, NewTask onDone, CancellationToken cancellationToken)
    {
        IExecutor? executor = _executors.FirstOrDefault(candidate => string.Equals(candidate.Name, executorName, StringComparison.OrdinalIgnoreCase));
        if (executor is null)
            return new Error("executor.unknown", $"There is no executor '{executorName}'. Known: {string.Join(", ", _executors.Select(known => known.Name))}.", ErrorType.Validation);
        Result valid = executor.ValidateSpec(spec);
        if (valid.IsFailure)
            return valid.Error;
        if (options.Value.CallbackBaseUrl is not { } callbackBase)
            return new Error("executor.not_configured", "executors.callbackBaseUrl is not set.", ErrorType.Unavailable);

        Result<TaskRecord> created = await tasks.CreateAsync(onDone with { AwaitsResume = true }, cancellationToken);
        if (created.IsFailure)
            return created.Error;
        TaskRecord task = created.Value;

        var sessionId = Guid.NewGuid();
        CreatedApiToken token = await tokens.IssueAsync(task.Id, sessionId, cancellationToken);
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            db.ExecutorSessions.Add(new ExecutorSession
            {
                Id = sessionId,
                OwnerId = CurrentOwner(),
                TaskId = task.Id,
                Executor = executor.Name,
                Handle = "",
                CallbackTokenId = token.Info.Id,
                StartedAt = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var callbackUrl = new Uri(callbackBase, CallbackPath + sessionId.ToString());
        Result<ExecutorStarted> started = await executor.StartAsync(new ExecutorStart(sessionId, spec, callbackUrl, token.Plaintext), cancellationToken);
        if (started.IsFailure)
        {
            LogStartFailed(sessionId, executor.Name, started.Error.Message);
            await tokens.RevokeAsync(token.Info.Id, CancellationToken.None);
            await UpdateSessionAsync(sessionId, session => (session.Status, session.FinishedAt) = (ExecutorSessionStatus.Failed, time.GetUtcNow()));
            await tasks.UpdateAsync(task with { Status = AgentTaskStatus.Failed, StatusReason = "executor_start_failed", NextRunAt = null }, CancellationToken.None);
            return started.Error;
        }

        await UpdateSessionAsync(sessionId, session =>
        {
            session.Handle = started.Value.Handle;
            session.Target = started.Value.Target;
            session.Status = ExecutorSessionStatus.Running;
        });
        LogStarted(sessionId, executor.Name, task.Id);
        return new StartedSession(sessionId, task.Id);
    }

    public async Task<Result> HandleAsync(Guid sessionId, ApiTokenId token, ExecutorReport report, CancellationToken cancellationToken)
    {
        ExecutorSession? session;
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
            session = await db.ExecutorSessions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == sessionId, cancellationToken);

        // The token authenticated its owner; it is valid only for the session it was issued for.
        if (session is null || session.CallbackTokenId != token)
            return new Error("executor.wrong_session", "This callback token belongs to another session.", ErrorType.Forbidden);
        if (session.Status is not (ExecutorSessionStatus.Starting or ExecutorSessionStatus.Running))
            return new Error("executor.session_ended", "The session has ended.", ErrorType.Conflict);

        ExecutorSessionStatus? outcome = report.Type switch
        {
            "progress" => null,
            "completed" => ExecutorSessionStatus.Done,
            "failed" => ExecutorSessionStatus.Failed,
            "cancelled" => ExecutorSessionStatus.Cancelled,
            _ => ExecutorSessionStatus.Failed,
        };
        if (outcome is not { } status)
            return Result.Success();

        Guid? proposal = status == ExecutorSessionStatus.Done && !string.IsNullOrEmpty(report.Branch) && !string.IsNullOrEmpty(report.Diff)
            ? await CreateProposalAsync(session, report, cancellationToken)
            : null;
        await UpdateSessionAsync(session.Id, row => (row.Status, row.FinishedAt) = (status, time.GetUtcNow()));
        await tokens.RevokeAsync(token, CancellationToken.None);

        JsonObject outcomeJson = Outcome(session, status, report, proposal);
        events.Publish(new MarshalEvent(EventSource, EventType(status), session.Id.ToString(),
            JsonSerializer.SerializeToElement(Summary(outcomeJson)), session.OwnerId, time.GetUtcNow()));
        if (!await TaskResume.ResumeAsync(tasks, scheduler, time, session.TaskId, new JsonObject { [PayloadKey] = outcomeJson }.ToJsonString(), CancellationToken.None))
            LogResumeFailed(session.TaskId);

        LogFinished(session.Id, EventType(status));
        return Result.Success();
    }

    private async Task<Guid> CreateProposalAsync(ExecutorSession session, ExecutorReport report, CancellationToken cancellationToken)
    {
        var proposal = new Proposal
        {
            Id = Guid.NewGuid(),
            OwnerId = session.OwnerId,
            TaskId = session.TaskId,
            ExecutorSessionId = session.Id,
            Kind = ProposalKind.Code,
            Target = session.Target ?? session.Executor,
            Branch = report.Branch!,
            Title = Title(report.Summary, session.Target),
            Diff = report.Diff!,
            CreatedAt = time.GetUtcNow(),
        };
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        db.Proposals.Add(proposal);
        await db.SaveChangesAsync(cancellationToken);
        return proposal.Id;
    }

    private static JsonObject Outcome(ExecutorSession session, ExecutorSessionStatus status, ExecutorReport report, Guid? proposal)
    {
        var outcome = new JsonObject
        {
            ["session"] = session.Id.ToString(),
            ["executor"] = session.Executor,
            ["status"] = EventType(status),
            ["target"] = session.Target,
        };
        if (report.Branch is not null && status == ExecutorSessionStatus.Done)
            outcome["branch"] = report.Branch;
        if (proposal is { } id)
        {
            outcome["proposal"] = id.ToString();
            outcome["review"] = "The change waits for the user's review in the web UI. It can only be merged there; say that it is ready for review.";
        }

        if (Truncate(report.Summary) is { } summary)
            outcome["summary"] = summary;
        if (Truncate(report.Message) is { } message)
            outcome["message"] = message;
        return outcome;
    }

    // The event carries ids and the outcome, not the executor's text.
    private static JsonObject Summary(JsonObject outcome)
    {
        var summary = new JsonObject();
        foreach (string key in (string[])["session", "executor", "status", "target", "branch", "proposal"])
        {
            if (outcome[key] is { } value)
                summary[key] = value.DeepClone();
        }

        return summary;
    }

    private static string EventType(ExecutorSessionStatus status) => status switch
    {
        ExecutorSessionStatus.Done => "completed",
        ExecutorSessionStatus.Cancelled => "cancelled",
        _ => "failed",
    };

    private static string Title(string? summary, string? target)
    {
        string? first = summary?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(first))
            return target is null ? "Code change" : $"Code change in {target}";
        return first.Length <= MaxTitleLength ? first : first[..MaxTitleLength];
    }

    private static string? Truncate(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= MaxTextLength ? text : text[..MaxTextLength];

    private async Task UpdateSessionAsync(Guid id, Action<ExecutorSession> change)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(CancellationToken.None);
        ExecutorSession session = await db.ExecutorSessions.SingleAsync(row => row.Id == id, CancellationToken.None);
        change(session);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private OwnerId CurrentOwner() =>
        executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Executor sessions require an execution context.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Executor session {SessionId} ({Executor}) started for task {TaskId}")]
    private partial void LogStarted(Guid sessionId, string executor, TaskId taskId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Executor session {SessionId} ({Executor}) could not start: {Reason}")]
    private partial void LogStartFailed(Guid sessionId, string executor, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Executor session {SessionId} finished: {Outcome}")]
    private partial void LogFinished(Guid sessionId, string outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {TaskId} was not resumed with its executor outcome (it no longer waits or could not be saved)")]
    private partial void LogResumeFailed(TaskId taskId);
}

/// <summary>A started executor session and the task that receives its outcome.</summary>
public sealed record StartedSession(Guid SessionId, TaskId TaskId);
