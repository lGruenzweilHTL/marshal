namespace Marshal.Abstractions.Suggestions;

/// <summary>What happens to a candidate.</summary>
public enum SuggestionDelivery
{
    /// <summary>Not sent: the owner does not want this detector's suggestions often enough.</summary>
    Drop,

    /// <summary>Sent to the inbox with priority <c>low</c>.</summary>
    Inbox,

    /// <summary>Sent as push with priority <c>normal</c>.</summary>
    Push,
}

/// <summary>What the owner did with a detector's suggestions since the counts were last reset.</summary>
/// <param name="Detector">Detector id.</param>
/// <param name="Sent">Suggestions sent.</param>
/// <param name="Accepted">Accepted ones.</param>
/// <param name="Dismissed">Dismissed ones.</param>
/// <param name="MutedUntil">Muted until then; <see cref="DateTimeOffset.MaxValue"/> for indefinitely; null when not muted.</param>
public sealed record SuggestionDetectorStats(string Detector, int Sent, int Accepted, int Dismissed, DateTimeOffset? MutedUntil = null)
{
    /// <summary>Beta(1,1) posterior mean of the acceptance: (accepted + 1) / (accepted + dismissed + 2).</summary>
    public double Estimate => (Accepted + 1.0) / (Accepted + Dismissed + 2.0);

    public bool IsMuted(DateTimeOffset now) => MutedUntil is { } until && until > now;
}

/// <summary>Where a sent suggestion stands.</summary>
public enum SuggestionStatus
{
    Open,
    Accepted,
    Dismissed,

    /// <summary>Unanswered until its expiry, or its condition no longer held; counts neither way.</summary>
    Expired,
}
