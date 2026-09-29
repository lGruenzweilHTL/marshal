namespace Marshal.Abstractions.Notifications;

/// <summary>
/// What the router did with a notification.
/// </summary>
public enum NotificationOutcome
{
    /// <summary>Not decided yet.</summary>
    Pending,

    /// <summary>Delivered on its channel.</summary>
    Delivered,

    /// <summary>Merged into another notification.</summary>
    Coalesced,

    /// <summary>Held until quiet hours end.</summary>
    Held,

    /// <summary>Stored in the inbox.</summary>
    Inboxed,

    /// <summary>Its delivery window passed.</summary>
    Expired,

    /// <summary>Delivered on the fallback channel.</summary>
    Fallback,
}
