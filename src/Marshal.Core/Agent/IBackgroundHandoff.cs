using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tasks;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Agent;

/// <summary>
/// Continues an interactive run as a background task (latency cap, <c>agent.move_to_background</c>): the task is
/// owned by the same user, keeps the turn's satellite as its origin (and so its policy), delivers its result where
/// the turn came from, and its first run continues the handed-over transcript.
/// </summary>
public interface IBackgroundHandoff
{
    /// <summary>Creates the continuation task; fails when background work is not possible now (e.g. the kill switch).</summary>
    Task<Result<TaskId>> HandOffAsync(BackgroundHandoff handoff, CancellationToken cancellationToken);
}

/// <summary>An interactive run moving to the background.</summary>
/// <param name="Context">The turn's execution context (owner, input mode, satellite, run, taint).</param>
/// <param name="Request">What the user asked in this turn.</param>
/// <param name="Transcript">The run's messages after the system prompt, up to the last completed tool round.</param>
/// <param name="Reason"><c>latency_cap</c> or <c>move_to_background</c>.</param>
public sealed record BackgroundHandoff(MarshalExecutionContext Context, string Request, IReadOnlyList<ChatMessage> Transcript, string Reason);
