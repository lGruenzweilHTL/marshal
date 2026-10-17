using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Data;
using Marshal.Data.Mcp;
using Marshal.Tasks.Running;
using Marshal.Tasks.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Mcp;

/// <summary>
/// <see cref="IMcpTaskTracker"/>: a server task gets a waiting agent task that delivers where the call came from, and
/// a handle in <c>mcp_task_handles</c> that is polled with <c>tasks/get</c> (never sooner than the server asks, at
/// least 5 seconds apart, backing off to 5 minutes). When the server task ends, <c>mcp.task_finished</c> is published
/// and the waiting task resumes with the outcome. A server task that waits for input asks each of its requests once as
/// an input request of the waiting task. Cancelling the waiting task cancels the server task.
/// </summary>
public sealed partial class PollingMcpTaskTracker(
    IDbContextFactory<MarshalDbContext> contexts,
    ITaskStore tasks,
    ITaskScheduler scheduler,
    IMcpTaskClient client,
    IInputRequestQueue inputs,
    IEventPublisher events,
    IExecutionContextAccessor executionContext,
    TimeProvider time,
    ILogger<PollingMcpTaskTracker> logger) : BackgroundService, IMcpTaskTracker
{
    /// <summary>Top-level key of the trigger payload the waiting task resumes with.</summary>
    public const string PayloadKey = "mcpTask";

    /// <summary>Key of the payload's taint kind, present when the tool's output is untrusted.</summary>
    public const string TaintKey = "taint";

    /// <summary>Event source of server task outcomes; the type is <see cref="FinishedEvent"/>.</summary>
    public const string EventSource = "mcp";

    /// <summary>Event type published when a server task ends.</summary>
    public const string FinishedEvent = "task_finished";

    /// <summary>Server tasks one owner may follow at a time.</summary>
    public const int MaxHandlesPerOwner = 50;

    /// <summary>Shortest time between two polls of one task.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>Longest time between two polls of one task.</summary>
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromMinutes(5);

    private const int PollBatch = 20;

    public async Task<Result<TaskId>> TrackAsync(McpTaskStart start, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        using IDisposable scope = executionContext.Use(context);
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            if (await db.McpTaskHandles.CountAsync(cancellationToken) >= MaxHandlesPerOwner)
                return new Error("mcp.task_limit", $"Marshal already follows {MaxHandlesPerOwner} background tasks of MCP servers; the server task was cancelled. Try again once some have finished.", ErrorType.Conflict);
        }

        Result<TaskRecord> created = await tasks.CreateAsync(new NewTask
        {
            Goal = $"The MCP server {start.ServerId} ran the tool {start.Tool} as a background task. Tell the user briefly what came of it.",
            Trigger = TaskTrigger.Now(),
            CreatedBy = TaskCreator.User,
            Template = context.Template,
            ProjectId = context.ProjectId,
            OriginSatelliteId = context.SatelliteId,
            OriginSessionId = context.SessionId,
            Notes = $"Waits for task {start.TaskId} of the MCP server {start.ServerId} (run {context.RunId}).",
            Deliveries = [OriginDelivery.For(context)],
            AwaitsResume = true,
        }, cancellationToken);
        if (created.IsFailure)
            return created.Error;

        DateTimeOffset now = time.GetUtcNow();
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            db.McpTaskHandles.Add(new McpTaskHandle
            {
                Id = Guid.CreateVersion7(),
                OwnerId = context.OwnerId,
                ServerId = start.ServerId,
                ServerTaskId = start.TaskId,
                Tool = start.Tool.ToString(),
                TaskId = created.Value.Id,
                Language = context.Language,
                TaintKind = start.TaintKind,
                PollIntervalMs = (long?)start.PollInterval?.TotalMilliseconds,
                NextPollAt = now + Delay(0, start.PollInterval),
                CreatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        LogTracking(start.ServerId, start.TaskId, created.Value.Id);
        return created.Value.Id;
    }

    /// <summary>Polls every handle that is due, across owners.</summary>
    public async Task PollDueAsync(CancellationToken cancellationToken)
    {
        List<(Guid Id, OwnerId Owner)> due;
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            DateTimeOffset now = time.GetUtcNow();
            due = [.. (await db.McpTaskHandles.IgnoreQueryFilters().AsNoTracking()
                .Where(handle => handle.NextPollAt <= now)
                .OrderBy(handle => handle.NextPollAt)
                .Take(PollBatch)
                .Select(handle => new { handle.Id, handle.OwnerId })
                .ToListAsync(cancellationToken)).Select(handle => (handle.Id, handle.OwnerId))];
        }

        foreach ((Guid id, OwnerId owner) in due)
        {
            using (executionContext.Use(BackgroundTaskScheduler.SystemContext(owner)))
            {
                try
                {
                    await PollAsync(id, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    LogPollFailed(id, exception);
                }
            }
        }
    }

    /// <summary>
    /// The wait before poll <paramref name="polls"/> + 1: doubling from <see cref="MinimumInterval"/>, never shorter
    /// than the server's suggestion and never longer than <see cref="MaximumInterval"/>.
    /// </summary>
    public static TimeSpan Delay(int polls, TimeSpan? serverInterval)
    {
        TimeSpan backoff = polls >= 10 ? MaximumInterval : MinimumInterval * Math.Pow(2, polls);
        TimeSpan delay = serverInterval is { } suggested && suggested > backoff ? suggested : backoff;
        return delay < MinimumInterval ? MinimumInterval : delay > MaximumInterval ? MaximumInterval : delay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(MinimumInterval, time, stoppingToken);
                try
                {
                    await PollDueAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogLoopFailed(exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task PollAsync(Guid id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        McpTaskHandle? handle = await db.McpTaskHandles.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (handle is null)
            return;

        // The waiting task was cancelled (or ended otherwise): nobody wants the outcome any more.
        Result<TaskRecord> task = await tasks.GetAsync(handle.TaskId, cancellationToken);
        if (task.IsFailure || task.Value.Status != AgentTaskStatus.Waiting)
        {
            if (task.IsSuccess && task.Value.Status == AgentTaskStatus.Cancelled)
                await client.CancelTaskAsync(handle.ServerId, handle.ServerTaskId, cancellationToken);

            db.McpTaskHandles.Remove(handle);
            await db.SaveChangesAsync(cancellationToken);
            LogDropped(handle.ServerId, handle.ServerTaskId, handle.TaskId);
            return;
        }

        Result<McpTaskSnapshot> polled = await client.GetTaskAsync(handle.ServerId, handle.ServerTaskId, cancellationToken);
        McpTaskSnapshot? snapshot = polled switch
        {
            { IsSuccess: true } => polled.Value,
            { Error.Type: ErrorType.NotFound } => new McpTaskSnapshot(McpTaskStatus.Failed, Error: "The MCP server no longer knows this task."),
            _ => null,
        };

        // Still running, or the server is unreachable for now: poll again later.
        if (snapshot is null or { IsTerminal: false })
        {
            if (snapshot is { Status: McpTaskStatus.InputRequired })
                await AskAsync(handle, task.Value, snapshot.Inputs, cancellationToken);

            handle.Polls++;
            if (snapshot?.PollInterval is { } interval)
                handle.PollIntervalMs = (long)interval.TotalMilliseconds;
            handle.NextPollAt = time.GetUtcNow() + Delay(handle.Polls, handle.PollIntervalMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await FinishAsync(handle, snapshot, cancellationToken);
        db.McpTaskHandles.Remove(handle);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Each of the server task's requests becomes one input request for the waiting task; the answer goes back with
    // tasks/update and the task keeps waiting for the outcome.
    private async Task AskAsync(McpTaskHandle handle, TaskRecord task, IReadOnlyList<McpTaskInput> requested, CancellationToken cancellationToken)
    {
        List<string> asked = JsonSerializer.Deserialize<List<string>>(handle.AskedInputs) ?? [];
        MarshalExecutionContext context = BackgroundTaskScheduler.SystemContext(handle.OwnerId) with
        {
            TaskId = task.Id,
            SessionId = task.OriginSessionId,
            SatelliteId = task.OriginSatelliteId,
            Template = task.Template ?? "default",
            ProjectId = task.ProjectId,
            Language = handle.Language,
        };
        foreach (McpTaskInput input in requested.Where(input => !asked.Contains(input.Key)))
        {
            await inputs.CreateAsync(new NewInputRequest(
                handle.ServerId,
                ToolId.Parse(handle.Tool).Value,
                "{}",
                input.Mode,
                input.Message,
                input.RequestedSchemaJson,
                input.Url,
                ServerTask: new McpServerTaskInput(handle.ServerTaskId, input.Key)), context, cancellationToken);
            asked.Add(input.Key);
        }

        handle.AskedInputs = JsonSerializer.Serialize(asked);
    }

    private async Task FinishAsync(McpTaskHandle handle, McpTaskSnapshot snapshot, CancellationToken cancellationToken)
    {
        string status = snapshot.Status switch
        {
            McpTaskStatus.Completed when snapshot.ResultIsError => "failed",
            McpTaskStatus.Completed => "completed",
            McpTaskStatus.Cancelled => "cancelled",
            _ => "failed",
        };

        // The event carries ids and the status, not the server's text.
        var summary = new JsonObject { ["server"] = handle.ServerId, ["tool"] = handle.Tool, ["task"] = handle.ServerTaskId, ["status"] = status };
        events.Publish(new MarshalEvent(EventSource, FinishedEvent, $"{handle.ServerId}/{handle.ServerTaskId}",
            JsonSerializer.SerializeToElement(summary), handle.OwnerId, time.GetUtcNow()));

        var outcome = (JsonObject)summary.DeepClone();
        if (snapshot.Status == McpTaskStatus.Completed)
            outcome[snapshot.ResultIsError ? "error" : "result"] = snapshot.Result ?? "";
        else if (snapshot.Error is { } error)
            outcome["error"] = error;

        var payload = new JsonObject { [PayloadKey] = outcome };
        if (handle.TaintKind is { } taint)
            payload[TaintKey] = taint;

        if (!await TaskResume.ResumeAsync(tasks, scheduler, time, handle.TaskId, payload.ToJsonString(), cancellationToken))
            LogResumeFailed(handle.TaskId);

        LogFinished(handle.ServerId, handle.ServerTaskId, status);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {ServerTask} of MCP server {Server} is followed by task {TaskId}")]
    private partial void LogTracking(string server, string serverTask, TaskId taskId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {ServerTask} of MCP server {Server} finished: {Status}")]
    private partial void LogFinished(string server, string serverTask, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {ServerTask} of MCP server {Server} is no longer followed: task {TaskId} does not wait for it")]
    private partial void LogDropped(string server, string serverTask, TaskId taskId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling MCP task handle {HandleId} failed")]
    private partial void LogPollFailed(Guid handleId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Polling MCP server tasks failed")]
    private partial void LogLoopFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {TaskId} was not resumed with its MCP server task outcome (it no longer waits or could not be saved)")]
    private partial void LogResumeFailed(TaskId taskId);
}
