using System.Text.Json;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Data.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Evals;

/// <summary>
/// Builds an eval case from a recorded run: input and turn context from the context step, tool calls (depth 0) with
/// canonical arguments and stored results, the reply, and the session history before the turn.
/// </summary>
internal static class EvalCaseSnapshots
{
    /// <summary>Turns of history kept, like the conversation service gives the agent loop.</summary>
    private const int HistoryTurns = 10;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static EvalExpectation ExpectationOf(RunRating? rating) => rating switch
    {
        RunRating.Up => EvalExpectation.Good,
        RunRating.Down => EvalExpectation.Bad,
        _ => EvalExpectation.Unrated,
    };

    public static async Task<EvalCaseRow> CreateAsync(MarshalDbContext db, Run run, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<RunStep> steps = await db.RunSteps.AsNoTracking()
            .Where(step => step.RunId == run.Id && step.Depth == 0 && (step.Kind == RunStepKind.Context || step.Kind == RunStepKind.ToolCall || step.Kind == RunStepKind.Reply))
            .OrderBy(step => step.Sequence)
            .ToListAsync(cancellationToken);
        HashSet<int> tainting = run.TaintSources is null
            ? []
            : [.. (JsonSerializer.Deserialize<List<TaintSource>>(run.TaintSources, Json) ?? []).Select(source => source.FirstStep)];

        DbRunRecorder.StepPayload? context = Read(steps.FirstOrDefault(step => step.Kind == RunStepKind.Context)?.Payload);
        DbRunRecorder.StepPayload? reply = Read(steps.LastOrDefault(step => step.Kind == RunStepKind.Reply)?.Payload);
        bool incomplete = false;
        var calls = new List<EvalToolCall>();
        foreach (RunStep step in steps.Where(step => step.Kind == RunStepKind.ToolCall))
        {
            DbRunRecorder.StepPayload? payload = Read(step.Payload);
            // A sensitive result is kept as a reference only: the replay cannot give the model what it saw.
            if (payload?.Content is null && payload?.Reference is not null)
                incomplete = true;
            if (payload?.Content?.EndsWith(DbRunRecorder.TruncationMarker, StringComparison.Ordinal) == true)
                incomplete = true;

            calls.Add(new EvalToolCall(step.Tool ?? "", CanonicalJson.Of(payload?.Arguments ?? "{}"), payload?.Content, step.Error, tainting.Contains(step.Sequence)));
        }

        // Runs recorded before turn snapshots have no turn context to replay (a turn without turn sections stores "").
        incomplete |= context?.TurnContext is null;
        return new EvalCaseRow
        {
            Id = EvalCaseId.New(),
            OwnerId = run.OwnerId,
            SourceRunId = run.Id,
            Expectation = ExpectationOf(run.Rating),
            Note = run.RatingNote,
            Input = context?.Input ?? "",
            InputMode = run.InputMode ?? InputMode.Text,
            Language = run.Language ?? "en",
            SatelliteId = run.SatelliteId,
            AreaId = run.AreaId,
            History = JsonSerializer.Serialize(await HistoryAsync(db, run, cancellationToken), Json),
            TurnContext = context?.TurnContext,
            ToolCalls = JsonSerializer.Serialize(calls, Json),
            Reply = reply?.Content,
            Incomplete = incomplete,
            RecordedAt = run.StartedAt,
            CreatedAt = now,
        };
    }

    public static EvalCase ToCase(EvalCaseRow row) => new()
    {
        Id = row.Id,
        SourceRunId = row.SourceRunId,
        Expectation = row.Expectation,
        Note = row.Note,
        Input = row.Input,
        InputMode = row.InputMode,
        Language = row.Language,
        SatelliteId = row.SatelliteId,
        AreaId = row.AreaId,
        History = JsonSerializer.Deserialize<List<EvalMessage>>(row.History, Json) ?? [],
        TurnContext = row.TurnContext,
        ToolCalls = JsonSerializer.Deserialize<List<EvalToolCall>>(row.ToolCalls, Json) ?? [],
        Reply = row.Reply,
        Incomplete = row.Incomplete,
        RecordedAt = row.RecordedAt,
        CreatedAt = row.CreatedAt,
    };

    // The user and assistant text of the session's turns before this one, as the agent loop gets them.
    private static async Task<List<EvalMessage>> HistoryAsync(MarshalDbContext db, Run run, CancellationToken cancellationToken)
    {
        if (run.SessionId is not { } session)
            return [];

        List<ConversationEntry> entries = await ConversationEntries.LoadAsync(db, session, after: null, maxTurns: null, cancellationToken);
        List<ConversationEntry> before = [.. entries.Where(entry => entry.At < run.StartedAt && !(entry is TurnEntry turn && turn.RunId == run.Id))];
        int turns = 0;
        int start = before.Count;
        while (start > 0 && turns < HistoryTurns)
        {
            start--;
            if (before[start] is TurnEntry)
                turns++;
        }

        var messages = new List<EvalMessage>();
        foreach (ConversationEntry entry in before.Skip(start))
        {
            switch (entry)
            {
                case TurnEntry turn:
                    if (turn.Input is not null)
                        messages.Add(new EvalMessage("user", turn.Input));
                    if (turn.Reply is not null)
                        messages.Add(new EvalMessage("assistant", turn.Reply));
                    break;
                case BackgroundResultEntry result:
                    messages.Add(new EvalMessage("assistant", result.Body));
                    break;
            }
        }

        return messages;
    }

    private static DbRunRecorder.StepPayload? Read(string? payload) =>
        payload is null ? null : JsonSerializer.Deserialize<DbRunRecorder.StepPayload>(payload, DbRunRecorder.StepPayload.Json);
}
