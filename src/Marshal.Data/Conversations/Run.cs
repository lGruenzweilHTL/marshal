using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;

namespace Marshal.Data.Conversations;

/// <summary>
/// One agent loop execution (a turn or a task run).
/// </summary>
public sealed class Run : IOwned
{
    public required RunId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public SessionId? SessionId { get; init; }

    public Guid? TaskId { get; init; }

    public required SessionSource Source { get; init; }

    public InputMode? InputMode { get; init; }

    public string? SatelliteId { get; init; }

    public string? AreaId { get; init; }

    public string? Language { get; init; }

    public required string Trigger { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; set; }

    public RunOutcome Outcome { get; set; }

    /// <summary>Why the run ended the way it did (e.g. <c>latency_cap</c>).</summary>
    public string? OutcomeReason { get; set; }

    public bool Tainted { get; set; }

    /// <summary>JSON array of taint sources (kind, first tainting step).</summary>
    public string? TaintSources { get; set; }

    public decimal Cost { get; set; }
}
