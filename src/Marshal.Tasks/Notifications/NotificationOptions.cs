namespace Marshal.Tasks.Notifications;

/// <summary>
/// Notification routing (<c>notifications</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>What happens to <c>normal</c> notifications during quiet hours.</summary>
    public QuietHoursMode QuietHoursMode { get; set; } = QuietHoursMode.Hold;

    /// <summary>Window in which <c>normal</c> notifications are merged into one message (zero: never).</summary>
    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>At most one voice notification per source within this interval; later ones are pushed.</summary>
    public TimeSpan SourceVoiceInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>At most this many voice notifications per hour; later ones are pushed.</summary>
    public int VoicePerHour { get; set; } = 6;

    /// <summary>How long a spoken question waits for its reply.</summary>
    public TimeSpan FollowUpTtl { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A voice turn without a follow-up reference from the satellite that asked, within this time after the question,
    /// is taken as the reply (for satellites or HA versions where the reference does not arrive). Zero: off.
    /// </summary>
    public TimeSpan FollowUpReplyWindow { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Marshal's public URL, for push links into the web UI; without it pushes carry no link.</summary>
    public Uri? PublicUrl { get; set; }
}

/// <summary>Quiet-hours handling of <c>normal</c> notifications.</summary>
public enum QuietHoursMode
{
    /// <summary>Held and delivered when quiet hours end.</summary>
    Hold,

    /// <summary>Delivered at once as a silent push.</summary>
    SilentPush,
}
