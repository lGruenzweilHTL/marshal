using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Data.Conversations;

/// <summary>
/// Writes runs and steps to the domain tables and passes the same (redacted) data to observers such as the
/// OpenTelemetry exporter. Results of sensitive tools are stored as references only.
/// </summary>
public sealed class DbRunRecorder(IDbContextFactory<MarshalDbContext> contexts, IEnumerable<IRunObserver> observers, IOptions<RecordingOptions> options, TimeProvider timeProvider) : IRunRecorder
{
    private const string TruncationMarker = " [truncated]";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Payloads are stored, never rendered as HTML; keep umlauts and symbols readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly IRunObserver[] _observers = [.. observers];

    public async Task<RunId> StartRunAsync(RunStart start, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        var run = new Run
        {
            Id = RunId.New(),
            OwnerId = start.Context.OwnerId,
            SessionId = start.Context.SessionId,
            TaskId = start.Context.TaskId,
            Source = start.Context.Source,
            InputMode = start.Context.InputMode,
            SatelliteId = start.Context.SatelliteId,
            AreaId = start.Context.AreaId,
            Language = start.Context.Language,
            Trigger = start.Trigger,
            StartedAt = now,
            Outcome = RunOutcome.Ok,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        foreach (IRunObserver observer in _observers)
            observer.RunStarted(run.Id, start, now);

        return run.Id;
    }

    public async Task<int> RecordStepAsync(RunId runId, RunStepRecord step, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Run run = await db.Runs.SingleAsync(candidate => candidate.Id == runId, cancellationToken);
        int sequence = await db.RunSteps.CountAsync(candidate => candidate.RunId == runId, cancellationToken);
        RunStepRecord redacted = Redact(step);
        db.RunSteps.Add(new RunStep
        {
            Id = Guid.CreateVersion7(),
            OwnerId = run.OwnerId,
            RunId = runId,
            Sequence = sequence,
            Kind = redacted.Kind,
            Model = redacted.Model,
            Tier = redacted.Tier,
            TokensIn = ToInt(redacted.TokensIn),
            TokensOut = ToInt(redacted.TokensOut),
            TokensCached = ToInt(redacted.TokensCached),
            FirstTokenMs = redacted.FirstTokenMs,
            Tool = redacted.Tool?.ToString(),
            RiskTier = redacted.RiskTier,
            GuardDecision = redacted.GuardDecision,
            Payload = Payload(redacted),
            DurationMs = redacted.DurationMs,
            Cost = redacted.Cost,
            Error = redacted.Error,
            Depth = redacted.Depth,
        });
        await db.SaveChangesAsync(cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (IRunObserver observer in _observers)
            observer.StepRecorded(runId, sequence, redacted, now);

        return sequence;
    }

    public async Task CompleteRunAsync(RunId runId, RunCompletion completion, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Run run = await db.Runs.SingleAsync(candidate => candidate.Id == runId, cancellationToken);
        List<decimal?> costs = await db.RunSteps.Where(step => step.RunId == runId).Select(step => step.Cost).ToListAsync(cancellationToken);
        run.Cost = costs.Sum(cost => cost ?? 0m);
        run.Outcome = completion.Outcome;
        run.OutcomeReason = completion.Reason;
        run.Tainted = completion.TaintSources.Count > 0;
        run.TaintSources = run.Tainted ? JsonSerializer.Serialize(completion.TaintSources, _json) : null;
        run.EndedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        foreach (IRunObserver observer in _observers)
            observer.RunCompleted(runId, completion, run.Cost, run.EndedAt.Value);
    }

    private RunStepRecord Redact(RunStepRecord step)
    {
        bool sensitive = step.Tool is { } tool
            && (step.Sensitive || options.Value.RedactTools.Any(pattern => ToolGlob.Matches(pattern, tool)))
            && !options.Value.KeepContentTools.Any(pattern => ToolGlob.Matches(pattern, tool));
        // The reply is the model's answer the user already saw in full; chat history shows it again, so it is kept whole.
        if (step.Kind == RunStepKind.Reply)
            return step;

        return sensitive ? step with { Content = null } : step with { Content = Truncate(step.Content) };
    }

    private string? Truncate(string? content)
    {
        int max = options.Value.MaxContentLength;
        return content is null || content.Length <= max ? content : content[..max] + TruncationMarker;
    }

    private static string? Payload(RunStepRecord step)
    {
        var payload = new StepPayload(
            step.Input,
            step.Content,
            step.Arguments,
            step.Reference,
            step.Reasons.Count == 0 ? null : step.Reasons,
            step.ContinueConversation,
            step.Notes.Count == 0 ? null : step.Notes,
            step.Artifacts);
        return payload == StepPayload.Empty ? null : JsonSerializer.Serialize(payload, _json);
    }

    private static int? ToInt(long? value) => value is null ? null : (int)Math.Min(value.Value, int.MaxValue);

    internal sealed record StepPayload(
        string? Input,
        string? Content,
        string? Arguments,
        string? Reference,
        IReadOnlyList<Abstractions.Guard.ConfirmationReason>? Reasons,
        bool? ContinueConversation,
        IReadOnlyList<Abstractions.Context.ContextNote>? Notes = null,
        IReadOnlyList<Abstractions.Artifacts.ArtifactRef>? Artifacts = null)
    {
        public static StepPayload Empty { get; } = new(null, null, null, null, null, null);

        public static JsonSerializerOptions Json => _json;
    }
}
