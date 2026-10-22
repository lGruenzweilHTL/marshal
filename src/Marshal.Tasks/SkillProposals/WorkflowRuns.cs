using System.Runtime.InteropServices;
using System.Text.Json;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Runs;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.SkillProposals;

/// <summary>
/// A run as the skill proposal job sees it: the user's input, the tool ids in call order and each call's argument
/// keys. Never tool results or argument values.
/// </summary>
/// <param name="Id">The run.</param>
/// <param name="Input">What the user asked.</param>
/// <param name="Tools">Tool ids of the top-level calls, in order.</param>
/// <param name="ArgumentKeys">The argument names of each call, in the same order.</param>
/// <param name="Embedding">The cached input embedding; null until computed.</param>
public sealed record WorkflowRun(RunId Id, string Input, IReadOnlyList<string> Tools, IReadOnlyList<IReadOnlyList<string>> ArgumentKeys, float[]? Embedding);

/// <summary>Reads the current owner's candidate runs and caches their input embeddings.</summary>
public interface IWorkflowRunReader
{
    /// <summary>Runs started at or after <paramref name="since"/> with outcome ok, at least <paramref name="minToolCalls"/> tool calls and no down rating.</summary>
    Task<IReadOnlyList<WorkflowRun>> ListAsync(DateTimeOffset since, int minToolCalls, CancellationToken cancellationToken);

    Task SaveEmbeddingAsync(RunId run, float[] embedding, CancellationToken cancellationToken);
}

/// <summary>
/// Runs from the database, scoped to the current owner by the query filter. The input comes from the context step,
/// tool ids and argument keys from the tool call steps; the steps' contents (results) are never read.
/// </summary>
public sealed class EfWorkflowRunReader(IDbContextFactory<MarshalDbContext> contexts) : IWorkflowRunReader
{
    public async Task<IReadOnlyList<WorkflowRun>> ListAsync(DateTimeOffset since, int minToolCalls, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var runs = await db.Runs.AsNoTracking()
            .Where(run => run.StartedAt >= since && run.Outcome == RunOutcome.Ok && (run.Rating == null || run.Rating != RunRating.Down))
            .Where(run => db.RunSteps.Count(step => step.RunId == run.Id && step.Kind == RunStepKind.ToolCall && step.Depth == 0) >= minToolCalls)
            .OrderBy(run => run.StartedAt)
            .Select(run => new { run.Id, run.InputEmbedding })
            .ToListAsync(cancellationToken);
        if (runs.Count == 0)
            return [];

        RunId[] ids = [.. runs.Select(run => run.Id)];
        var steps = await db.RunSteps.AsNoTracking()
            .Where(step => ids.Contains(step.RunId) && step.Depth == 0 && (step.Kind == RunStepKind.ToolCall || step.Kind == RunStepKind.Context))
            .Select(step => new { step.RunId, step.Sequence, step.Kind, step.Tool, step.Payload })
            .ToListAsync(cancellationToken);

        var result = new List<WorkflowRun>();
        foreach (var run in runs)
        {
            var own = steps.Where(step => step.RunId == run.Id).OrderBy(step => step.Sequence).ToList();
            string? input = own.Where(step => step.Kind == RunStepKind.Context).Select(step => Field(step.Payload, "input")).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            if (input is null)
                continue;

            var calls = own.Where(step => step.Kind == RunStepKind.ToolCall && step.Tool is not null).ToList();
            result.Add(new WorkflowRun(
                run.Id,
                input,
                [.. calls.Select(call => call.Tool!)],
                [.. calls.Select(call => ArgumentKeys(Field(call.Payload, "arguments")))],
                run.InputEmbedding is { Length: > 0 } bytes ? MemoryMarshal.Cast<byte, float>(bytes).ToArray() : null));
        }

        return result;
    }

    public async Task SaveEmbeddingAsync(RunId run, float[] embedding, CancellationToken cancellationToken)
    {
        byte[] bytes = MemoryMarshal.AsBytes(embedding.AsSpan()).ToArray();
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Runs.Where(row => row.Id == run).ExecuteUpdateAsync(update => update.SetProperty(row => row.InputEmbedding, bytes), cancellationToken);
    }

    // One top-level string field of the stored step payload (camelCase JSON).
    private static string? Field(string? payload, string name)
    {
        if (payload is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Only the names: argument values can carry content the run read.
    private static IReadOnlyList<string> ArgumentKeys(string? arguments)
    {
        if (arguments is null)
            return [];

        try
        {
            using var document = JsonDocument.Parse(arguments);
            return document.RootElement.ValueKind == JsonValueKind.Object ? [.. document.RootElement.EnumerateObject().Select(property => property.Name)] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
