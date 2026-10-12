namespace Marshal.Abstractions.Sessions;

/// <summary>
/// How far the memory summarizer has read a session, so a web session that rests several times is not summarized
/// again from its start.
/// </summary>
public interface ISummaryWatermark
{
    /// <summary>Time of the newest turn or background result already summarized; null when none was.</summary>
    Task<DateTimeOffset?> GetAsync(SessionId session, CancellationToken cancellationToken);

    Task SetAsync(SessionId session, DateTimeOffset summarizedUntil, CancellationToken cancellationToken);
}
