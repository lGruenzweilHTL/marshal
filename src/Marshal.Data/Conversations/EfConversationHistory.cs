using System.Text.Json;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Marshal.Data.Conversations;

/// <summary>
/// Rebuilds the user / assistant exchange of a session from the context and reply steps of its runs.
/// </summary>
public sealed class EfConversationHistory(IDbContextFactory<MarshalDbContext> contexts) : IConversationHistory
{
    public async Task<IReadOnlyList<ChatMessage>> LoadAsync(SessionId sessionId, int maxTurns, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<RunId> runs = await db.Runs.AsNoTracking()
            .Where(run => run.SessionId == sessionId)
            .OrderByDescending(run => run.StartedAt)
            .Take(maxTurns)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);

        var steps = await db.RunSteps.AsNoTracking()
            .Where(step => runs.Contains(step.RunId) && (step.Kind == RunStepKind.Context || step.Kind == RunStepKind.Reply))
            .Select(step => new { step.RunId, step.Kind, step.Payload })
            .ToListAsync(cancellationToken);

        var messages = new List<ChatMessage>();
        foreach (RunId run in Enumerable.Reverse(runs))
        {
            string? input = Read(steps.FirstOrDefault(step => step.RunId == run && step.Kind == RunStepKind.Context)?.Payload)?.Input;
            string? reply = Read(steps.FirstOrDefault(step => step.RunId == run && step.Kind == RunStepKind.Reply)?.Payload)?.Content;
            if (input is not null)
                messages.Add(new ChatMessage(ChatRole.User, input));
            if (reply is not null)
                messages.Add(new ChatMessage(ChatRole.Assistant, reply));
        }

        return messages;
    }

    private static DbRunRecorder.StepPayload? Read(string? payload) =>
        payload is null ? null : JsonSerializer.Deserialize<DbRunRecorder.StepPayload>(payload, DbRunRecorder.StepPayload.Json);
}
