using Marshal.Abstractions.Identity;

namespace Marshal.Data.Auth;

/// <summary>
/// A server-side cookie authentication ticket. The browser only holds the key; the ticket (including IdP tokens)
/// stays here, encrypted with Data Protection.
/// </summary>
public sealed class AuthTicket : IOwned
{
    /// <summary>SHA-256 hex of the key in the cookie.</summary>
    public required string Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    /// <summary>The serialized, protected ticket.</summary>
    public required byte[] Value { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; set; }
}
