using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Suggestions;

namespace Marshal.Tasks.Suggestions;

/// <summary>Sent suggestions, the owner's answers per detector and mutes; scoped to the current owner.</summary>
public interface ISuggestionStore
{
    /// <summary>The detector's counts and mute; zeros when it never sent anything.</summary>
    Task<SuggestionDetectorStats> GetStatsAsync(string detector, CancellationToken cancellationToken);

    /// <summary>Every detector the owner has counts or a mute for.</summary>
    Task<IReadOnlyList<SuggestionDetectorStats>> ListStatsAsync(CancellationToken cancellationToken);

    /// <summary>Whether the detector sent a suggestion with this key at or after <paramref name="since"/>.</summary>
    Task<bool> SentSinceAsync(string detector, string dedupeKey, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Stores a sent suggestion and counts it as sent.</summary>
    Task AddAsync(SuggestionRecord suggestion, CancellationToken cancellationToken);

    /// <summary>Links the notification that carried it.</summary>
    Task SetNotificationAsync(Guid id, Guid notificationId, CancellationToken cancellationToken);

    Task<SuggestionRecord?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The owner's open suggestions, newest first.</summary>
    Task<IReadOnlyList<SuggestionRecord>> ListOpenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves an open suggestion to <paramref name="status"/>; accepted and dismissed ones count for the detector,
    /// expired ones do not. Fails when it is unknown or no longer open.
    /// </summary>
    Task<Result<SuggestionRecord>> DecideAsync(Guid id, SuggestionStatus status, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Mutes the detector until then (<see cref="DateTimeOffset.MaxValue"/>: indefinitely), or unmutes with null.</summary>
    Task MuteAsync(string detector, DateTimeOffset? until, CancellationToken cancellationToken);

    /// <summary>Sets the detector's counts back to zero; a mute stays.</summary>
    Task ResetAsync(string detector, CancellationToken cancellationToken);

    /// <summary>The owner of the open suggestion with this push nonce, across owners; null when unknown.</summary>
    Task<(Guid Id, OwnerId Owner)?> FindByNonceAsync(string nonce, CancellationToken cancellationToken);

    /// <summary>Open suggestions of every owner, oldest first (for expiry).</summary>
    Task<IReadOnlyList<(Guid Id, OwnerId Owner, string Detector, string DedupeKey, DateTimeOffset ExpiresAt)>> ListAllOpenAsync(CancellationToken cancellationToken);
}

/// <summary>A sent suggestion.</summary>
public sealed record SuggestionRecord(
    Guid Id,
    OwnerId OwnerId,
    string Detector,
    string DedupeKey,
    string Text,
    IReadOnlyDictionary<string, string> Facts,
    SuggestionDelivery Delivery,
    SuggestionStatus Status,
    string Nonce,
    PendingActionId? PendingActionId,
    string? ActionSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt = null,
    string? Snippet = null);
