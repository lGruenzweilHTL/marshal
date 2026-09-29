namespace Marshal.Abstractions.Notifications;

/// <summary>
/// Priority of a notification. Only system-defined sources may send <see cref="Critical"/>.
/// </summary>
public enum NotificationPriority
{
    /// <summary>Always voice and push; ignores quiet hours, DND and caps.</summary>
    Critical,

    /// <summary>The default.</summary>
    Normal,

    /// <summary>Never voice; goes to the inbox.</summary>
    Low,
}
