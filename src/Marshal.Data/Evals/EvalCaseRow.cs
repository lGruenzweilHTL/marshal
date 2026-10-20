using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;

namespace Marshal.Data.Evals;

/// <summary>
/// An eval case (<c>eval_cases</c>): a snapshot of one run's turn. No foreign key to the run, so the retention purge
/// leaves cases alone.
/// </summary>
public sealed class EvalCaseRow : IOwned
{
    public required EvalCaseId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required RunId SourceRunId { get; init; }

    public required EvalExpectation Expectation { get; set; }

    public string? Note { get; set; }

    public required string Input { get; init; }

    public required InputMode InputMode { get; init; }

    public required string Language { get; init; }

    public string? SatelliteId { get; init; }

    public string? AreaId { get; init; }

    /// <summary>JSON array of <see cref="EvalMessage"/>.</summary>
    public required string History { get; init; }

    public string? TurnContext { get; init; }

    /// <summary>JSON array of <see cref="EvalToolCall"/>.</summary>
    public required string ToolCalls { get; init; }

    public string? Reply { get; init; }

    public bool Incomplete { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
