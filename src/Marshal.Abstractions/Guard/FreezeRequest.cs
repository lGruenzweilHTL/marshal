using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// What to freeze. The owner comes from the execution context.
/// </summary>
/// <param name="SessionId">Session of an interactive turn.</param>
/// <param name="SatelliteId">Satellite of the turn.</param>
/// <param name="Tool">Tool to execute on confirmation.</param>
/// <param name="ArgumentsJson">Frozen arguments object, as JSON text.</param>
/// <param name="Summary">What the action does, for the question.</param>
/// <param name="Reasons">Why it needs confirmation, the spoken one first.</param>
/// <param name="Language">Language of the reasons.</param>
/// <param name="ExpiresAt">When the confirmation window ends.</param>
/// <param name="TaskId">Background runs: the task that waits for the decision.</param>
/// <param name="RunId">The run that asked (the review queue links to its steps).</param>
/// <param name="Taint">Untrusted sources of the run, with the step that brought each in.</param>
public sealed record FreezeRequest(
    SessionId? SessionId,
    string? SatelliteId,
    ToolId Tool,
    string ArgumentsJson,
    string Summary,
    IReadOnlyList<ConfirmationReason> Reasons,
    string Language,
    DateTimeOffset ExpiresAt,
    TaskId? TaskId = null,
    RunId? RunId = null,
    IReadOnlyList<TaintSource>? Taint = null);
