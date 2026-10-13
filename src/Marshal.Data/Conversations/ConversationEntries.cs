using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Conversations;

/// <summary>
/// The one query behind the chat history, the model's conversation history and the summarizer: turns are the
/// session's conversation runs (context and reply step at depth 0), background results are tasks handed off from the
/// session that have a result.
/// </summary>
internal static class ConversationEntries
{
    /// <summary>Run triggers that are user turns; summarizer and task runs are not.</summary>
    public static readonly string[] TurnTriggers = ["turn", "followup", "confirmation"];

    /// <summary>
    /// Entries of <paramref name="session"/> after <paramref name="after"/>, oldest first. With
    /// <paramref name="maxTurns"/> only the newest turns are kept, plus the background results since the first of them.
    /// With <paramref name="details"/> turns also carry their reasoning and tool steps (chat history view only).
    /// </summary>
    public static async Task<List<ConversationEntry>> LoadAsync(
        MarshalDbContext db, SessionId session, DateTimeOffset? after, int? maxTurns, CancellationToken cancellationToken, IToolCallSummarizer? details = null)
    {
        IQueryable<Run> query = db.Runs.AsNoTracking().Where(run => run.SessionId == session && TurnTriggers.Contains(run.Trigger));
        if (after is { } since)
            query = query.Where(run => run.StartedAt > since);

        query = query.OrderByDescending(run => run.StartedAt);
        if (maxTurns is { } limit)
            query = query.Take(limit);

        List<Run> runs = await query.ToListAsync(cancellationToken);
        runs.Reverse();
        List<RunId> ids = [.. runs.Select(run => run.Id)];
        bool loop = details is not null;
        List<StepRow> steps = await db.RunSteps.AsNoTracking()
            // Subagent steps (depth 1 and more) are the loop's internals, not the exchange with the user.
            .Where(step => ids.Contains(step.RunId) && step.Depth == 0
                && (step.Kind == RunStepKind.Context || step.Kind == RunStepKind.Reply || (loop && (step.Kind == RunStepKind.ModelCall || step.Kind == RunStepKind.ToolCall))))
            .OrderBy(step => step.Sequence)
            .Select(step => new StepRow(step.RunId, step.Kind, step.Payload, step.Tool, step.GuardDecision, step.DurationMs, step.Error))
            .ToListAsync(cancellationToken);

        var entries = new List<ConversationEntry>();
        foreach (Run run in runs)
        {
            DbRunRecorder.StepPayload? context = Read(steps.FirstOrDefault(step => step.RunId == run.Id && step.Kind == RunStepKind.Context)?.Payload);
            DbRunRecorder.StepPayload? reply = Read(steps.FirstOrDefault(step => step.RunId == run.Id && step.Kind == RunStepKind.Reply)?.Payload);
            List<StepRow> own = [.. steps.Where(step => step.RunId == run.Id)];
            List<DbRunRecorder.StepPayload?> calls = [.. own.Where(step => step.Kind == RunStepKind.ModelCall).Select(step => Read(step.Payload))];
            string[] reasoning = [.. calls.Select(call => call?.Reasoning).OfType<string>()];
            entries.Add(new TurnEntry(
                run.Id,
                run.StartedAt,
                run.InputMode,
                run.SatelliteId,
                run.AreaId,
                context?.Input,
                reply?.Content,
                reply?.ContinueConversation,
                reply?.Artifacts ?? [],
                run.Outcome,
                reasoning.Length == 0 ? null : string.Join("\n\n", reasoning),
                calls.Any(call => call?.ReasoningWithheld == true),
                details is null ? [] : [.. own.Where(step => step.Kind == RunStepKind.ToolCall).Select(step => ToolStep(step, details))]));
        }

        IQueryable<Tasks.AgentTask> tasks = db.AgentTasks.AsNoTracking().Where(task => task.OriginSessionId == session && task.ResultAt != null && task.ResultBody != null);
        if (after is { } resultsAfter)
            tasks = tasks.Where(task => task.ResultAt > resultsAfter);
        if (maxTurns is not null && runs.Count > 0)
        {
            DateTimeOffset first = runs[0].StartedAt;
            tasks = tasks.Where(task => task.ResultAt >= first);
        }

        var results = await tasks.Select(task => new { task.Id, task.ResultAt, task.ResultBody }).ToListAsync(cancellationToken);
        entries.AddRange(results.Select(result => new BackgroundResultEntry(result.Id, result.ResultAt!.Value, result.ResultBody!)));

        // Stable sort: a turn stays before a result with the same time.
        return [.. entries.OrderBy(entry => entry.At)];
    }

    /// <summary>The input of the first context step of each run (titles of the history list).</summary>
    public static async Task<Dictionary<RunId, string?>> InputsAsync(MarshalDbContext db, IReadOnlyCollection<RunId> runs, CancellationToken cancellationToken)
    {
        var steps = await db.RunSteps.AsNoTracking()
            .Where(step => runs.Contains(step.RunId) && step.Depth == 0 && step.Kind == RunStepKind.Context)
            .OrderBy(step => step.Sequence)
            .Select(step => new { step.RunId, step.Payload })
            .ToListAsync(cancellationToken);
        return steps.GroupBy(step => step.RunId).ToDictionary(group => group.Key, group => Read(group.First().Payload)?.Input);
    }

    private static TurnToolStep ToolStep(StepRow step, IToolCallSummarizer summarizer)
    {
        DbRunRecorder.StepPayload? payload = Read(step.Payload);
        // Recorded errors are "code: message"; the summary shows the code like the live event.
        string? errorCode = step.Error?.Split(": ", 2)[0];
        ToolEventState state = step.GuardDecision switch
        {
            GuardDecision.Pending or GuardDecision.EscalatedByTaint => ToolEventState.Pending,
            GuardDecision.Denied or GuardDecision.JudgeNeutral or GuardDecision.JudgeNegative or GuardDecision.Expired => ToolEventState.Denied,
            _ when errorCode is not null => ToolEventState.Error,
            _ => ToolEventState.Ok,
        };
        Result<ToolId> parsed = ToolId.Parse(step.Tool);
        ToolId? tool = parsed.IsSuccess ? parsed.Value : null;
        string arguments = payload?.Arguments is { } raw ? summarizer.Arguments(JsonDocument.Parse(raw).RootElement) : "";
        string? result = state == ToolEventState.Pending ? null : summarizer.Result(tool, payload?.Sensitive == true, payload?.Content, errorCode);
        return new TurnToolStep(step.Tool ?? "", arguments, state, step.DurationMs, result);
    }

    private sealed record StepRow(RunId RunId, RunStepKind Kind, string? Payload, string? Tool, GuardDecision? GuardDecision, int DurationMs, string? Error);

    private static DbRunRecorder.StepPayload? Read(string? payload) =>
        payload is null ? null : JsonSerializer.Deserialize<DbRunRecorder.StepPayload>(payload, DbRunRecorder.StepPayload.Json);
}
