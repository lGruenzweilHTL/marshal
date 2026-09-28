namespace Marshal.Abstractions.Tokens;

/// <summary>
/// Creates, lists, revokes and validates machine tokens. Create, list and revoke act for the current owner.
/// </summary>
public interface IApiTokenService
{
    /// <summary>Creates a token; fails with <c>token.invalid_scope</c> for unknown scopes.</summary>
    Task<Result<CreatedApiToken>> CreateAsync(string name, IReadOnlyList<string> scopes, DateTimeOffset? expiresAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiTokenInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Revokes a token of the current owner.</summary>
    Task<Result> RevokeAsync(ApiTokenId id, CancellationToken cancellationToken);

    /// <summary>Checks a presented token; fails with <c>token.invalid</c> when unknown, revoked or expired.</summary>
    Task<Result<ApiTokenInfo>> ValidateAsync(string plaintext, CancellationToken cancellationToken);
}
