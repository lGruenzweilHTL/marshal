namespace Marshal.Integrations.Mail.Imap;

/// <summary>
/// Timings of the new-mail watch. Tests shorten them.
/// </summary>
public sealed record MailWatchTimings
{
    /// <summary>Use IMAP IDLE when the server offers it; otherwise poll.</summary>
    public bool UseIdle { get; init; } = true;

    /// <summary>How long one IDLE lasts before it is renewed (servers drop IDLE after 30 minutes, RFC 2177).</summary>
    public TimeSpan IdleRenewal { get; init; } = TimeSpan.FromMinutes(9);

    /// <summary>Polling interval without IDLE.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the account list is re-read (added and removed accounts).</summary>
    public TimeSpan AccountRefresh { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan Backoff(int attempt) =>
        TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, InitialBackoff.Ticks * (1L << Math.Min(attempt, 20))));
}
