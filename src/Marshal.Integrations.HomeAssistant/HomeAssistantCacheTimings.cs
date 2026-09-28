namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Reconnect backoff, command timeout and the periodic registry refresh (HA has no event for exposure changes).
/// </summary>
public sealed record HomeAssistantCacheTimings
{
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RegistryRefresh { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Delay before the next attempt after <paramref name="attempt"/> consecutive failures (0-based).</summary>
    public TimeSpan Backoff(int attempt) =>
        TimeSpan.FromTicks((long)Math.Min(ReconnectDelay.Ticks * Math.Pow(2, Math.Min(attempt, 30)), MaxReconnectDelay.Ticks));
}
