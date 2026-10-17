using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;

namespace Marshal.Data.Conversations;

/// <summary>
/// A conversation session. The owner is pinned on the first turn.
/// </summary>
public sealed class Session : IOwned
{
    public required SessionId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required SessionSource Source { get; init; }

    public ApiTokenId? ApiTokenId { get; init; }

    /// <summary>Client-supplied conversation id (HA conversation_id).</summary>
    public string? ExternalId { get; init; }

    /// <summary>JSON: ha_user_id, device_id, satellite_id, area_id.</summary>
    public required string IdentityEvidence { get; init; }

    public required string ResolvedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset LastTurnAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>When an idle web session rested (pending actions dropped, summarized); cleared by its next turn.</summary>
    public DateTimeOffset? RestedAt { get; set; }

    /// <summary>Newest turn or background result the memory summarizer has read; later ones are summarized next.</summary>
    public DateTimeOffset? SummarizedUntil { get; set; }

    /// <summary>JSON array of tool namespaces the session loaded beyond the core ones, in load order; null = core only.</summary>
    public string? LoadedToolNamespaces { get; set; }
}
