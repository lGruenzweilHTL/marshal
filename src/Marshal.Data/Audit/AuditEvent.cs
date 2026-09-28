using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;

namespace Marshal.Data.Audit;

/// <summary>
/// An action performed outside a run (UI, approvals, tokens, secrets, CLI).
/// </summary>
public sealed class AuditEvent : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required AuditActorKind ActorKind { get; init; }

    public string? ActorId { get; init; }

    public RunId? RunId { get; init; }

    public required string Action { get; init; }

    public string? Target { get; init; }

    public string? Details { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
