namespace Marshal.Integrations.Moonraker;

/// <summary>Reconnect backoff and request timeout of the Moonraker websocket (shortened in tests).</summary>
public sealed record MoonrakerTimings
{
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Delay before the next attempt after <paramref name="attempt"/> consecutive failures (0-based).</summary>
    public TimeSpan Backoff(int attempt) =>
        TimeSpan.FromTicks((long)Math.Min(InitialBackoff.Ticks * Math.Pow(2, Math.Min(attempt, 30)), MaxBackoff.Ticks));
}
