using System.Collections.Concurrent;
using Marshal.Abstractions.InputRequests;

namespace Marshal.Abstractions.Execution;

/// <summary>
/// Requests a tool makes to the agent loop that runs it, acted on once the current tool round ends.
/// </summary>
public sealed class RunControl
{
    private readonly ConcurrentQueue<InputRequestId> _inputRequests = new();
    private volatile string? _backgroundMessage;

    /// <summary>The status message of a requested move to the background, or null.</summary>
    public string? BackgroundMessage => _backgroundMessage;

    /// <summary>Asks the loop to end the turn with <paramref name="statusMessage"/> and continue in the background.</summary>
    public void RequestBackground(string statusMessage) => _backgroundMessage = statusMessage;

    /// <summary>Input requests a tool created in this run; the web chat shows them, a background run waits for them.</summary>
    public IReadOnlyList<InputRequestId> InputRequests => [.. _inputRequests];

    /// <summary>Records that a tool call waits for the user's input in the web UI.</summary>
    public void RequestInput(InputRequestId id) => _inputRequests.Enqueue(id);
}
