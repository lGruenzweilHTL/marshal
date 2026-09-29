namespace Marshal.Abstractions.Execution;

/// <summary>
/// Requests a tool makes to the agent loop that runs it, acted on once the current tool round ends.
/// </summary>
public sealed class RunControl
{
    private volatile string? _backgroundMessage;

    /// <summary>The status message of a requested move to the background, or null.</summary>
    public string? BackgroundMessage => _backgroundMessage;

    /// <summary>Asks the loop to end the turn with <paramref name="statusMessage"/> and continue in the background.</summary>
    public void RequestBackground(string statusMessage) => _backgroundMessage = statusMessage;
}
