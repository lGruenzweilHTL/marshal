using Marshal.Abstractions.Identity;

namespace Marshal.Data.Secrets;

/// <summary>
/// A UI-managed secret, encrypted with Data Protection. A null owner means instance-scoped.
/// </summary>
public sealed class Secret
{
    public required Guid Id { get; init; }

    public OwnerId? OwnerId { get; init; }

    public required string Name { get; init; }

    public required byte[] Ciphertext { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
