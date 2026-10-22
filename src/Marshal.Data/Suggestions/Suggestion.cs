using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Suggestions;

namespace Marshal.Data.Suggestions;

/// <summary>A suggestion sent to its owner, with its proposed action (frozen as a pending action) and outcome.</summary>
public sealed class Suggestion : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required string Detector { get; init; }

    /// <summary>Repeats of the key within the detector's cooldown are dropped.</summary>
    public required string DedupeKey { get; init; }

    /// <summary>The worded text.</summary>
    public required string Text { get; init; }

    /// <summary>JSON object of the detector's facts.</summary>
    public required string FactsJson { get; init; }

    public required SuggestionDelivery Delivery { get; init; }

    public SuggestionStatus Status { get; set; } = SuggestionStatus.Open;

    /// <summary>Push actions carry it; unique.</summary>
    public required string Nonce { get; init; }

    public PendingActionId? PendingActionId { get; init; }

    /// <summary>What the proposed action does; null without one.</summary>
    public string? ActionSummary { get; init; }

    /// <summary>Text shown verbatim below the suggestion (a YAML snippet); null for none.</summary>
    public string? Snippet { get; init; }

    public Guid? NotificationId { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>An owner's answers to one detector since the last reset, and its mute.</summary>
public sealed class SuggestionDetectorStat : IOwned
{
    public required OwnerId OwnerId { get; init; }

    public required string Detector { get; init; }

    public int Sent { get; set; }

    public int Accepted { get; set; }

    public int Dismissed { get; set; }

    /// <summary>Muted until then; <see cref="DateTimeOffset.MaxValue"/> for indefinitely.</summary>
    public DateTimeOffset? MutedUntil { get; set; }
}
