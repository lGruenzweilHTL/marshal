using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;

namespace Marshal.Abstractions.Evals;

/// <summary>What a replay of a case should show.</summary>
public enum EvalExpectation
{
    /// <summary>Rated up: the recorded behavior is right.</summary>
    Good,

    /// <summary>Rated down: the recorded behavior is wrong; the note says what was expected.</summary>
    Bad,

    /// <summary>Saved by an admin without a rating; compared like a good case.</summary>
    Unrated,
}

/// <summary>A message of the session history before the case's turn (user and assistant text only).</summary>
/// <param name="Role"><c>user</c> or <c>assistant</c>.</param>
/// <param name="Text">The message text.</param>
public sealed record EvalMessage(string Role, string Text);

/// <summary>A tool call as recorded in the case's run.</summary>
/// <param name="Tool">Internal tool id, e.g. <c>ha.HassTurnOff</c>.</param>
/// <param name="Arguments">Canonical arguments (<see cref="CanonicalJson"/>).</param>
/// <param name="Result">The result as stored; null when it failed or only a reference was kept.</param>
/// <param name="Error">The error, when the call failed or was denied.</param>
/// <param name="Untrusted">The result was third-party content (it tainted the run).</param>
public sealed record EvalToolCall(string Tool, string Arguments, string? Result, string? Error, bool Untrusted);

/// <summary>An eval case: one turn of a run, snapshotted so it can be replayed against another configuration.</summary>
public sealed record EvalCase
{
    public required EvalCaseId Id { get; init; }

    /// <summary>The run it was made from (it may be purged since).</summary>
    public required RunId SourceRunId { get; init; }

    public required EvalExpectation Expectation { get; init; }

    /// <summary>What should have happened, from the rating note or edited later.</summary>
    public string? Note { get; init; }

    public required string Input { get; init; }

    public InputMode InputMode { get; init; } = InputMode.Text;

    public string Language { get; init; } = "en";

    public string? SatelliteId { get; init; }

    public string? AreaId { get; init; }

    /// <summary>The session history before the turn, oldest first.</summary>
    public IReadOnlyList<EvalMessage> History { get; init; } = [];

    /// <summary>The turn-level context text (retrieved memory, area, time, task); null for runs recorded before snapshots.</summary>
    public string? TurnContext { get; init; }

    public IReadOnlyList<EvalToolCall> ToolCalls { get; init; } = [];

    public string? Reply { get; init; }

    /// <summary>Some recorded data is missing (a sensitive result kept as a reference, no turn context).</summary>
    public bool Incomplete { get; init; }

    /// <summary>When the run took place; replays use it as the clock.</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
