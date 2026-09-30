using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Tokens;

/// <summary>
/// <see cref="IExecutorCallbackTokens"/> on the <c>api_tokens</c> table (kind <c>executor_callback</c>, no scopes,
/// the task set). Same token format and hashing as <see cref="ApiTokenService"/>.
/// </summary>
public sealed class ExecutorCallbackTokens(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext, TimeProvider timeProvider) : IExecutorCallbackTokens
{
    public async Task<CreatedApiToken> IssueAsync(TaskId task, Guid session, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = executionContext.Current
            ?? throw new InvalidOperationException("Issuing a callback token requires an execution context.");
        string plaintext = ApiTokenService.NewPlaintext();
        var row = new ApiToken
        {
            Id = ApiTokenId.New(),
            OwnerId = context.OwnerId,
            Name = $"executor session {session}",
            TokenHash = ApiTokenService.Hash(plaintext),
            Scopes = [],
            Kind = ApiTokenKind.ExecutorCallback,
            TaskId = task,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ApiTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedApiToken(ApiTokenService.ToInfo(row), plaintext);
    }

    public async Task RevokeAsync(ApiTokenId token, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.ApiTokens
            .Where(row => row.Id == token && row.Kind == ApiTokenKind.ExecutorCallback && row.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, now), cancellationToken);
    }
}
