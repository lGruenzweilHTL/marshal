using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Tokens;

/// <summary>
/// Single-purpose <c>executor_callback</c> tokens: no scopes, bound to one task, valid only for the callbacks of the
/// session they were issued for, revoked when it ends. Act for the current owner.
/// </summary>
public interface IExecutorCallbackTokens
{
    Task<CreatedApiToken> IssueAsync(TaskId task, Guid session, CancellationToken cancellationToken);

    Task RevokeAsync(ApiTokenId token, CancellationToken cancellationToken);
}
