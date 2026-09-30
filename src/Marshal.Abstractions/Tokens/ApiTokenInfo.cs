using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Tokens;

/// <summary>
/// A token without its secret. <see cref="TaskId"/> is set for <see cref="ApiTokenKind.ExecutorCallback"/> tokens.
/// </summary>
public sealed record ApiTokenInfo(
    ApiTokenId Id,
    OwnerId OwnerId,
    string Name,
    IReadOnlyList<string> Scopes,
    ApiTokenKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    TaskId? TaskId = null);
