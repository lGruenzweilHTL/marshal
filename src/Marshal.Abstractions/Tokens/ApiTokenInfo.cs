using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Tokens;

/// <summary>
/// A token without its secret.
/// </summary>
public sealed record ApiTokenInfo(
    ApiTokenId Id,
    OwnerId OwnerId,
    string Name,
    IReadOnlyList<string> Scopes,
    ApiTokenKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);
