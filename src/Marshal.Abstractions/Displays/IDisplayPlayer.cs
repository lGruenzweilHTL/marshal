namespace Marshal.Abstractions.Displays;

/// <summary>Plays a URL on a display.</summary>
public interface IDisplayPlayer
{
    /// <summary>Whether the player's integration is configured; an unconfigured player has no displays.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Asks the display to fetch and play <paramref name="url"/>; fails with <c>display.failed</c> naming the display
    /// when it refuses, and with the integration's unavailable error when it cannot be reached.
    /// </summary>
    Task<Result> PlayAsync(DisplaySurface display, string url, DisplayMediaType type, CancellationToken cancellationToken);
}
