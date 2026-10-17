#pragma warning disable CA1711 // "Queue" is the domain term (the review queue), not a collection type.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.AiTasks;
using Marshal.Core.Mcp;
using Marshal.Data;
using Marshal.Tasks.Running;
using Marshal.Tasks.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.InputRequests;

/// <summary>
/// <see cref="IInputRequestQueue"/>: an MCP server's input request is frozen with the approval TTL, a task waits for
/// its outcome (the asking background task, or one created for the turn's conversation), and the owner gets a push
/// with a link and no actions. Only an answer in the web UI repeats the frozen call, once, with the answer; the task
/// resumes with what came of it. Declining and expiry send nothing to the server. A running server task's request is
/// answered, declined or expired with <c>tasks/update</c> instead; the server task's outcome resumes the task.
/// </summary>
public sealed partial class InputRequestQueue(
    IInputRequestStore store,
    ITaskStore tasks,
    INotificationSender notifications,
    IToolRegistry registry,
    IRiskEngine riskEngine,
    IExecutionContextAccessor executionContext,
    IDbContextFactory<MarshalDbContext> contexts,
    ITaskScheduler scheduler,
    ActiveTaskRuns activeRuns,
    IMcpTaskClient serverTasks,
    IOptions<TaskOptions> options,
    TimeProvider time,
    ILogger<InputRequestQueue> logger) : IInputRequestQueue
{
    /// <summary>Top-level key of the trigger payload an outcome resumes a task with.</summary>
    public const string PayloadKey = "input";

    /// <summary>Key of the payload's taint kind, present when the repeated call returned untrusted content.</summary>
    public const string TaintKey = "taint";

    public async Task<InputRequestRecord> CreateAsync(NewInputRequest request, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        var id = InputRequestId.New();
        TaskId? taskId = context.TaskId ?? await CreateWaitingTaskAsync(request, context, cancellationToken);
        var record = new InputRequestRecord
        {
            Id = id,
            OwnerId = context.OwnerId,
            SessionId = context.SessionId,
            SatelliteId = context.SatelliteId,
            TaskId = taskId,
            RunId = context.RunId,
            ServerId = request.ServerId,
            Tool = request.Tool,
            ArgumentsJson = request.ArgumentsJson,
            PreviousAnswersJson = request.PreviousAnswersJson,
            Mode = request.Mode,
            Message = request.Message,
            RequestedSchemaJson = request.RequestedSchemaJson,
            Url = request.Url,
            ServerTask = request.ServerTask,
            Nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            Language = context.Language,
            CreatedAt = now,
            ExpiresAt = now + options.Value.ApprovalTtl,
        };
        await store.SaveAsync(record, cancellationToken);

        // The asking background run ends as waiting; the outcome resumes it.
        if (context.TaskId is { } running)
            activeRuns.Find(running)?.RequestWait(null, $"input requested: {request.Tool} ({id})");

        await NotifyAsync(record, cancellationToken);
        LogCreated(id, request.ServerId, request.Mode);
        return record;
    }

    public Task<InputRequestRecord?> GetAsync(InputRequestId id, CancellationToken cancellationToken) => store.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<InputRequestRecord>> ListPendingAsync(CancellationToken cancellationToken) => store.ListPendingAsync(cancellationToken);

    public async Task<Result<InputRequestRecord>> AnswerAsync(InputRequestId id, JsonElement? content, CancellationToken cancellationToken)
    {
        Result<InputRequestRecord> pending = await PendingAsync(id, cancellationToken);
        if (pending.IsFailure)
            return pending.Error;

        InputRequestRecord request = pending.Value;
        Result<JsonElement?> answer = Validate(request, content);
        if (answer.IsFailure)
            return answer.Error;

        Result decided = await store.DecideAsync(id, InputRequestStatus.Answered, cancellationToken);
        if (decided.IsFailure)
            return InputRequestErrors.AlreadyDecided();

        LogDecided(id, "answered");

        // A running server task takes the answer itself; its outcome resumes the task later.
        if (request.ServerTask is { } serverTask)
        {
            await AnswerServerTaskAsync(request, serverTask, accept: true, answer.Value);
            return request with { Status = InputRequestStatus.Answered };
        }

        List<McpInputAnswer> answers = [.. McpInputAnswer.ParseList(request.PreviousAnswersJson), new McpInputAnswer(Signature(request), request.Mode, answer.Value)];
        var replay = new McpInputReplay(answers);
        (RegisteredTool? tool, Result<ToolOutput> result) = await RepeatAsync(request, replay, cancellationToken);

        // The server asked again: the new request carries the answers so far and the task keeps waiting for it.
        if (replay.Created is not null)
            return request with { Status = InputRequestStatus.Answered };

        JsonObject payload = Payload(request, "answered");
        string? taint = null;
        if (result.IsFailure)
        {
            payload["error"] = result.Error.Message;
        }
        else
        {
            payload["result"] = result.Value.Content;
            taint = tool is { Descriptor.UntrustedOutput: true } ? tool.Descriptor.TaintKind ?? tool.Id.Namespace : null;
        }

        await ResumeAsync(request, payload, taint, cancellationToken);
        return request with { Status = InputRequestStatus.Answered };
    }

    public async Task<Result<InputRequestRecord>> DeclineAsync(InputRequestId id, CancellationToken cancellationToken)
    {
        Result<InputRequestRecord> pending = await PendingAsync(id, cancellationToken);
        if (pending.IsFailure)
            return pending.Error;

        Result decided = await store.DecideAsync(id, InputRequestStatus.Declined, cancellationToken);
        if (decided.IsFailure)
            return InputRequestErrors.AlreadyDecided();

        LogDecided(id, "declined");
        if (pending.Value.ServerTask is { } serverTask)
            await AnswerServerTaskAsync(pending.Value, serverTask, accept: false, null);
        else
            await ResumeAsync(pending.Value, Payload(pending.Value, "declined"), null, cancellationToken);
        return pending.Value with { Status = InputRequestStatus.Declined };
    }

    /// <summary>Expires the overdue input requests of every owner and resumes their tasks with a rejection.</summary>
    public async Task ExpireDueAsync(CancellationToken cancellationToken)
    {
        List<(InputRequestId Id, OwnerId Owner)> overdue;
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            DateTimeOffset now = time.GetUtcNow();
            overdue = [.. (await db.PendingInputRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.Status == InputRequestStatus.Pending && row.ExpiresAt <= now)
                .Select(row => new { row.Id, row.OwnerId })
                .ToListAsync(cancellationToken)).Select(row => (row.Id, row.OwnerId))];
        }

        foreach ((InputRequestId id, OwnerId owner) in overdue)
        {
            using (executionContext.Use(BackgroundTaskScheduler.SystemContext(owner)))
            {
                InputRequestRecord? request = await store.GetAsync(id, cancellationToken);
                if (request is null)
                    continue;

                // The asking run is still finishing; the next pass expires it.
                if (request.TaskId is { } taskId && await tasks.GetAsync(taskId, cancellationToken) is { IsSuccess: true } task && task.Value.Status == AgentTaskStatus.Running)
                    continue;

                await ExpireAsync(request, cancellationToken);
            }
        }
    }

    private async Task<Result<InputRequestRecord>> PendingAsync(InputRequestId id, CancellationToken cancellationToken)
    {
        InputRequestRecord? request = await store.GetAsync(id, cancellationToken);
        if (request is null)
            return InputRequestErrors.NotFound();
        if (request.Status != InputRequestStatus.Pending)
            return InputRequestErrors.AlreadyDecided();
        if (time.GetUtcNow() >= request.ExpiresAt)
        {
            await ExpireAsync(request, cancellationToken);
            return InputRequestErrors.Expired();
        }

        if (request.TaskId is { } taskId && await tasks.GetAsync(taskId, cancellationToken) is { IsSuccess: true } task && task.Value.Status == AgentTaskStatus.Running)
            return InputRequestErrors.TaskBusy();

        return request;
    }

    private async Task ExpireAsync(InputRequestRecord request, CancellationToken cancellationToken)
    {
        if ((await store.DecideAsync(request.Id, InputRequestStatus.Expired, cancellationToken)).IsFailure)
            return;

        LogDecided(request.Id, "expired");
        if (request.ServerTask is { } serverTask)
        {
            await AnswerServerTaskAsync(request, serverTask, accept: false, null);
            return;
        }

        JsonObject payload = Payload(request, "rejected");
        payload["reason"] = "expired";
        await ResumeAsync(request, payload, null, cancellationToken);
    }

    // A server task would otherwise wait for the answer until it times out, so declining and expiry are sent too.
    private async Task AnswerServerTaskAsync(InputRequestRecord request, McpServerTaskInput serverTask, bool accept, JsonElement? content)
    {
        Result sent = await serverTasks.AnswerInputAsync(request.ServerId, serverTask.TaskId, serverTask.InputKey, accept, content, CancellationToken.None);
        if (sent.IsFailure)
            LogServerTaskAnswerFailed(request.Id, sent.Error.Message);
    }

    // Form answers must fit the requested schema; a URL request is only marked done.
    private static Result<JsonElement?> Validate(InputRequestRecord request, JsonElement? content)
    {
        if (request.Mode == InputRequestMode.Url)
        {
            return content is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }
                ? Result.Success<JsonElement?>(null)
                : InputRequestErrors.InvalidAnswer("A URL request takes no content; send it once the page is done.");
        }

        if (content is not { ValueKind: JsonValueKind.Object } answer)
            return InputRequestErrors.InvalidAnswer("The answer must be an object with the requested fields.");

        using var schema = JsonDocument.Parse(request.RequestedSchemaJson ?? """{"type":"object"}""");
        IReadOnlyList<string> errors = JsonSchemaSubsetValidator.Validate(schema.RootElement, answer);
        return errors.Count == 0
            ? Result.Success<JsonElement?>(answer.Clone())
            : InputRequestErrors.InvalidAnswer(string.Join(" ", errors));
    }

    // Same signature as the server's request had, so the repeated call's request takes this answer.
    private static string Signature(InputRequestRecord request)
    {
        var parameters = new ModelContextProtocol.Protocol.ElicitRequestParams
        {
            Mode = request.Mode == InputRequestMode.Url ? "url" : "form",
            Message = request.Message,
            Url = request.Url,
            RequestedSchema = request.RequestedSchemaJson is { } schema
                ? JsonSerializer.Deserialize<ModelContextProtocol.Protocol.ElicitRequestParams.RequestSchema>(schema, ModelContextProtocol.McpJsonUtilities.DefaultOptions)
                : null,
        };
        return McpInputAnswer.SignatureOf(parameters);
    }

    // Exactly the frozen tool and arguments, as the owner in a background context, with the stored answers in scope.
    private async Task<(RegisteredTool? Tool, Result<ToolOutput> Result)> RepeatAsync(InputRequestRecord request, McpInputReplay replay, CancellationToken cancellationToken)
    {
        TaskRecord? task = request.TaskId is { } taskId && await tasks.GetAsync(taskId, cancellationToken) is { IsSuccess: true } found ? found.Value : null;
        var context = new MarshalExecutionContext
        {
            OwnerId = request.OwnerId,
            Source = SessionSource.Task,
            InputMode = InputMode.Text,
            Language = request.Language,
            SatelliteId = request.SatelliteId ?? task?.OriginSatelliteId,
            IsInteractive = false,
            Template = task?.Template ?? "default",
            TaskId = request.TaskId,
            ProjectId = task?.ProjectId,
            RunId = request.RunId,
        };

        RegisteredTool? tool = registry.Current.FindById(request.Tool);
        if (tool is null)
            return (null, new Error("tool.unknown", $"The tool '{request.Tool}' is no longer available.", ErrorType.NotFound));
        if (tool.Tool.PinState != ToolPinState.Approved)
            return (tool, new Error("tool.pin_pending", "The tool changed and waits for an admin's approval; it was not called again.", ErrorType.Forbidden));

        using var document = JsonDocument.Parse(request.ArgumentsJson);
        JsonElement arguments = document.RootElement.Clone();
        RiskAssessment assessment = await riskEngine.AssessAsync(tool, arguments, cancellationToken);
        if (assessment.Tier == RiskTier.Forbidden)
            return (tool, new Error("tool.forbidden", "This action is no longer allowed.", ErrorType.Forbidden));

        using (executionContext.Use(context))
        using (replay.Use())
            return (tool, await tool.Tool.InvokeAsync(new ToolInvocation(arguments, context), cancellationToken));
    }

    // An interactive turn or an AI task has no task to resume: one waits for the outcome, joins the turn's
    // conversation and delivers where the turn came from.
    private async Task<TaskId?> CreateWaitingTaskAsync(NewInputRequest request, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        Result<TaskRecord> created = await tasks.CreateAsync(new NewTask
        {
            Goal = $"The tool {request.Tool} waited for the user's input in the web UI. Tell the user briefly what came of it.",
            Trigger = TaskTrigger.Now(),
            CreatedBy = TaskCreator.User,
            Template = context.Template,
            ProjectId = context.ProjectId,
            OriginSatelliteId = context.SatelliteId,
            OriginSessionId = context.SessionId,
            Notes = $"Waits for an input request of the MCP server {request.ServerId} (run {context.RunId}).",
            Deliveries = [OriginDelivery.For(context)],
            AwaitsResume = true,
        }, cancellationToken);
        if (created.IsSuccess)
            return created.Value.Id;

        LogTaskFailed(created.Error.Message);
        return null;
    }

    // A link and no actions: only the web UI answers. Marshal's text only; the server's message stays in the web UI.
    private async Task NotifyAsync(InputRequestRecord request, CancellationToken cancellationToken)
    {
        bool german = request.Language == "de";
        Result<Guid> sent = await notifications.SendAsync(new NotificationRequest
        {
            Owner = request.OwnerId,
            Source = request.TaskId is { } task ? $"task:{task}" : "input",
            Priority = NotificationPriority.Normal,
            Channel = DeliveryChannel.Push,
            Text = german
                ? $"Der MCP-Server {request.ServerId} braucht eine Angabe von dir für {request.Tool}. Öffne Marshal, um zu antworten."
                : $"The MCP server {request.ServerId} needs your input for {request.Tool}. Open Marshal to answer.",
            TaskId = request.TaskId,
            Link = $"inbox/inputs/{request.Id}",
            Tag = $"input-{request.Id}",
        }, cancellationToken);
        if (sent.IsFailure)
            LogNotifyFailed(request.Id, sent.Error.Message);
    }

    private static JsonObject Payload(InputRequestRecord request, string decision) => new()
    {
        ["id"] = request.Id.ToString(),
        ["tool"] = request.Tool.ToString(),
        ["server"] = request.ServerId,
        ["decision"] = decision,
    };

    private async Task ResumeAsync(InputRequestRecord request, JsonObject outcome, string? taint, CancellationToken cancellationToken)
    {
        if (request.TaskId is not { } taskId)
            return;

        var payload = new JsonObject { [PayloadKey] = outcome };
        if (taint is not null)
            payload[TaintKey] = taint;

        if (!await TaskResume.ResumeAsync(tasks, scheduler, time, taskId, payload.ToJsonString(), cancellationToken))
            LogResumeFailed(taskId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Input request {InputRequestId} of MCP server {Server} ({Mode}) created")]
    private partial void LogCreated(InputRequestId inputRequestId, string server, InputRequestMode mode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Input request {InputRequestId}: {Decision}")]
    private partial void LogDecided(InputRequestId inputRequestId, string decision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input request {InputRequestId} was created, but its push failed: {Reason}")]
    private partial void LogNotifyFailed(InputRequestId inputRequestId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input request {InputRequestId}: the server task did not take the answer: {Reason}")]
    private partial void LogServerTaskAnswerFailed(InputRequestId inputRequestId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No task could be created to wait for an input request: {Reason}")]
    private partial void LogTaskFailed(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {TaskId} was not resumed with its input request outcome (it no longer waits or could not be saved)")]
    private partial void LogResumeFailed(TaskId taskId);
}
