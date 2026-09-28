namespace Marshal.Core.Mcp;

/// <summary>
/// Exponential backoff between connection attempts: the delay doubles per failed attempt up to a maximum.
/// </summary>
public sealed record McpReconnectPolicy(TimeSpan InitialDelay, TimeSpan MaxDelay)
{
    public static McpReconnectPolicy Default { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));

    /// <summary>Delay before the next attempt after <paramref name="attempt"/> consecutive failures (0-based).</summary>
    public TimeSpan Delay(int attempt)
    {
        double factor = Math.Pow(2, Math.Min(attempt, 30));
        return TimeSpan.FromTicks((long)Math.Min(InitialDelay.Ticks * factor, MaxDelay.Ticks));
    }
}
