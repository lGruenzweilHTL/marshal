using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Agent;

/// <summary>
/// One tool call through the guard: deny, freeze (session or approval queue) or execute, then record the step and
/// mark taint. The agent runner and Marshal's MCP server share it, so every caller gets the same rules.
/// </summary>
public interface IToolCallPipeline
{
    /// <summary>Evaluates and handles a call; the run is <see cref="ToolCallRequest.Context"/>'s <c>RunId</c>.</summary>
    Task<ToolCallOutcome> CallAsync(ToolCallRequest request, CancellationToken cancellationToken);

    /// <summary>Records a frozen action that was executed after the user confirmed it, and marks taint like a direct call.</summary>
    Task<ToolCallOutcome> CompleteConfirmedAsync(ConfirmedToolCall call, CancellationToken cancellationToken);
}

/// <param name="ModelName">The name the caller used (<c>namespace_name</c>).</param>
/// <param name="CallId">The caller's call id.</param>
/// <param name="Arguments">The call's arguments (a JSON object).</param>
/// <param name="Context">The run's execution context; its <c>RunId</c> receives the step.</param>
/// <param name="Offered">Model names the caller was offered; anything else is denied as unknown.</param>
public sealed record ToolCallRequest(string ModelName, string CallId, JsonElement Arguments, MarshalExecutionContext Context, IReadOnlySet<string> Offered)
{
    /// <summary>Context notes recorded with the step, read when it is recorded (for example tool namespaces this call loaded).</summary>
    public Func<IReadOnlyList<ContextNote>>? Notes { get; init; }

    /// <summary>Runs once the guard decided, before anything else happens (the runner's tool events and fillers).</summary>
    public Func<GuardVerdict, CancellationToken, Task>? OnDecided { get; init; }

    /// <summary>
    /// When set, a call the guard puts at confirm tier is denied with this error instead of frozen, and no pending
    /// action is created (calls requested by an MCP app: a click in server-authored HTML is no confirmation).
    /// </summary>
    public Error? ConfirmationDenial { get; init; }
}

/// <summary>A confirmed frozen action and the result of executing it.</summary>
public sealed record ConfirmedToolCall(RegisteredTool Tool, string ModelName, JsonElement Arguments, GuardDecision Decision, Result<ToolOutput> Result, MarshalExecutionContext Context);

/// <param name="Text">What the caller gives the model as the tool result.</param>
public sealed record ToolCallOutcome(string Text)
{
    /// <summary>The guard's verdict; null for a confirmed action.</summary>
    public GuardVerdict? Verdict { get; init; }

    /// <summary>The tool's result when it executed.</summary>
    public Result<ToolOutput>? Result { get; init; }

    /// <summary>The frozen action when the call needs confirmation.</summary>
    public PendingActionRecord? Frozen { get; init; }

    /// <summary>The frozen action waits in the approval queue and nobody in this run can confirm it.</summary>
    public bool Queued { get; init; }

    /// <summary>How long the tool ran.</summary>
    public int DurationMs { get; init; }

    /// <summary>The result is recorded as a reference only; later reasoning may quote it.</summary>
    public bool Redacted { get; init; }
}
