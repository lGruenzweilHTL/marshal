using System.Security.Cryptography;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Tokens;

/// <summary>
/// Machine tokens: <c>mrs_</c> + 32 random bytes (base64url). Only the SHA-256 of the token is stored.
/// </summary>
public sealed class ApiTokenService(MarshalDbContext db, IExecutionContextAccessor executionContext, TimeProvider timeProvider) : IApiTokenService
{
    private const string Prefix = "mrs_";

    private static readonly Error _invalid = new("token.invalid", "The token is unknown, revoked or expired.", ErrorType.Forbidden);

    public async Task<Result<CreatedApiToken>> CreateAsync(string name, IReadOnlyList<string> scopes, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new Error("token.invalid_name", "A token needs a name.", ErrorType.Validation);
        if (scopes.Count == 0)
            return new Error("token.invalid_scope", "A token needs at least one scope.", ErrorType.Validation);

        string? unknown = scopes.FirstOrDefault(scope => !ApiScopes.All.Contains(scope));
        if (unknown is not null)
            return new Error("token.invalid_scope", $"Unknown scope '{unknown}'. Known scopes: {string.Join(", ", ApiScopes.All)}.", ErrorType.Validation);

        MarshalExecutionContext context = executionContext.Current
            ?? throw new InvalidOperationException("Creating a token requires an execution context.");
        string plaintext = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var row = new ApiToken
        {
            Id = ApiTokenId.New(),
            OwnerId = context.OwnerId,
            Name = name.Trim(),
            TokenHash = Hash(plaintext),
            Scopes = [.. scopes.Distinct(StringComparer.Ordinal)],
            CreatedAt = timeProvider.GetUtcNow(),
            ExpiresAt = expiresAt,
        };
        db.ApiTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedApiToken(ToInfo(row), plaintext);
    }

    public async Task<IReadOnlyList<ApiTokenInfo>> ListAsync(CancellationToken cancellationToken)
    {
        List<ApiToken> rows = await db.ApiTokens.AsNoTracking().OrderBy(token => token.CreatedAt).ToListAsync(cancellationToken);
        return [.. rows.Select(ToInfo)];
    }

    public async Task<Result> RevokeAsync(ApiTokenId id, CancellationToken cancellationToken)
    {
        ApiToken? row = await db.ApiTokens.SingleOrDefaultAsync(token => token.Id == id, cancellationToken);
        if (row is null)
            return new Error("token.not_found", "The token does not exist.", ErrorType.NotFound);

        row.RevokedAt ??= timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ApiTokenInfo>> ValidateAsync(string plaintext, CancellationToken cancellationToken)
    {
        if (!plaintext.StartsWith(Prefix, StringComparison.Ordinal))
            return _invalid;

        string hash = Hash(plaintext);
        // The one sanctioned cross-owner query: authentication happens before an owner is known. The hash is the
        // lookup key, so only the presented token's row can match.
        ApiToken? row = await db.ApiTokens.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (row is null || row.RevokedAt is not null || (row.ExpiresAt is { } expires && expires <= now))
            return _invalid;

        return ToInfo(row);
    }

    private static string Hash(string plaintext) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ApiTokenInfo ToInfo(ApiToken row) =>
        new(row.Id, row.OwnerId, row.Name, row.Scopes, row.Kind, row.CreatedAt, row.ExpiresAt, row.RevokedAt);
}
