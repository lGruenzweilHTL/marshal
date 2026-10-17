using System.Threading.Channels;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Agent;

/// <summary>
/// One run of the agent loop. Works without an HTTP request.
/// </summary>
public sealed record AgentRunRequest
{
    public required MarshalExecutionContext Context { get; init; }

    public string Trigger { get; init; } = "turn";

    /// <summary>Earlier user and assistant messages of the session.</summary>
    public IReadOnlyList<ChatMessage> History { get; init; } = [];

    public string? UserText { get; init; }

    /// <summary>Decision on the session's pending action made at the start of the turn, if any.</summary>
    public ConfirmationStep? Confirmation { get; init; }

    /// <summary>When set, the run replies with this text and makes no model call (e.g. "cancelled").</summary>
    public string? FixedReply { get; init; }

    /// <summary>Receives deltas and status events, if streaming.</summary>
    public ChannelWriter<TurnEvent>? Events { get; init; }

    /// <summary>Hard latency cap; null = none.</summary>
    public TimeSpan? LatencyCap { get; init; }

    /// <summary>
    /// <see cref="TimeProvider.GetTimestamp"/> when the request arrived, for the time to first delta; null uses the
    /// run's start.
    /// </summary>
    public long? RequestStartedAt { get; init; }

    /// <summary>Budget limits; a run that reaches one ends with reason <c>budget_exceeded</c>.</summary>
    public RunLimits? Limits { get; init; }
}
