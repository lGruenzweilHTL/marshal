using Marshal.Abstractions;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Tools;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.InputRequests;

/// <summary>
/// Input requests in <c>pending_input_requests</c>, scoped to the current owner.
/// </summary>
public sealed class EfInputRequestStore(IDbContextFactory<MarshalDbContext> contexts, TimeProvider time) : IInputRequestStore
{
    public async Task SaveAsync(InputRequestRecord request, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        db.PendingInputRequests.Add(new PendingInputRequest
        {
            Id = request.Id,
            OwnerId = request.OwnerId,
            SessionId = request.SessionId,
            SatelliteId = request.SatelliteId,
            TaskId = request.TaskId,
            RunId = request.RunId,
            ServerId = request.ServerId,
            Tool = request.Tool.ToString(),
            Args = request.ArgumentsJson,
            PreviousAnswers = request.PreviousAnswersJson,
            Mode = request.Mode,
            Message = request.Message,
            RequestedSchema = request.RequestedSchemaJson,
            Url = request.Url,
            ServerTaskId = request.ServerTask?.TaskId,
            ServerTaskInputKey = request.ServerTask?.InputKey,
            Nonce = request.Nonce,
            Language = request.Language,
            CreatedAt = request.CreatedAt,
            ExpiresAt = request.ExpiresAt,
            Status = request.Status,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<InputRequestRecord?> GetAsync(InputRequestId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        PendingInputRequest? row = await db.PendingInputRequests.AsNoTracking().SingleOrDefaultAsync(request => request.Id == id, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<InputRequestRecord>> ListPendingAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<PendingInputRequest> rows = await db.PendingInputRequests.AsNoTracking()
            .Where(request => request.Status == InputRequestStatus.Pending)
            .OrderBy(request => request.CreatedAt)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToRecord)];
    }

    public async Task<Result> DecideAsync(InputRequestId id, InputRequestStatus status, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        PendingInputRequest? row = await db.PendingInputRequests.SingleOrDefaultAsync(request => request.Id == id, cancellationToken);
        if (row is null)
            return new Error("input_request.not_found", "There is no such input request.", ErrorType.NotFound);
        if (row.Status != InputRequestStatus.Pending)
            return new Error("input_request.not_pending", "The input request was already answered or has expired.", ErrorType.Conflict);

        row.Status = status;
        row.DecidedAt = time.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new Error("input_request.not_pending", "The input request was already answered or has expired.", ErrorType.Conflict);
        }

        return Result.Success();
    }

    private static InputRequestRecord ToRecord(PendingInputRequest row) => new()
    {
        Id = row.Id,
        OwnerId = row.OwnerId,
        SessionId = row.SessionId,
        SatelliteId = row.SatelliteId,
        TaskId = row.TaskId,
        RunId = row.RunId,
        ServerId = row.ServerId,
        Tool = ToolId.Parse(row.Tool).Value,
        ArgumentsJson = row.Args,
        PreviousAnswersJson = row.PreviousAnswers,
        Mode = row.Mode,
        Message = row.Message,
        RequestedSchemaJson = row.RequestedSchema,
        Url = row.Url,
        ServerTask = row.ServerTaskId is { } serverTask ? new McpServerTaskInput(serverTask, row.ServerTaskInputKey ?? "") : null,
        Nonce = row.Nonce,
        Language = row.Language,
        CreatedAt = row.CreatedAt,
        ExpiresAt = row.ExpiresAt,
        Status = row.Status,
    };
}
