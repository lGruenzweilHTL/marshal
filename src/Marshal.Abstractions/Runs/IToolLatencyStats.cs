using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// How long a tool usually takes, from recorded runs. Decides whether a voice turn speaks a filler line first.
/// </summary>
public interface IToolLatencyStats
{
    /// <summary>Expected duration of a call; null when too few calls are recorded to say.</summary>
    Task<TimeSpan?> ExpectedDurationAsync(ToolId tool, CancellationToken cancellationToken);
}
