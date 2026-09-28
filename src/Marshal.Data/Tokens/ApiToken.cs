using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;

namespace Marshal.Data.Tokens;

/// <summary>
/// A machine API token. Only the hash of the token is stored.
/// </summary>
public sealed class ApiToken : IOwned
{
    public required ApiTokenId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required string Name { get; set; }

    /// <summary>Lowercase hex SHA-256 of the token.</summary>
    public required string TokenHash { get; init; }

    public required List<string> Scopes { get; init; }

    public ApiTokenKind Kind { get; init; } = ApiTokenKind.Standard;

    /// <summary>Owning task for executor callback tokens (background-work).</summary>
    public Guid? TaskId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }
}
