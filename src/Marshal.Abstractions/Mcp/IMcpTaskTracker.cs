using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Mcp;

/// <summary>
/// Follows server tasks that MCP tool calls returned: a waiting agent task receives the outcome and delivers it
/// where the call came from.
/// </summary>
public interface IMcpTaskTracker
{
    /// <summary>
    /// Creates the waiting task for the context's owner and starts polling the handle. Fails with
    /// <c>mcp.task_limit</c> when the owner already follows the maximum number of server tasks.
    /// </summary>
    Task<Result<TaskId>> TrackAsync(McpTaskStart start, MarshalExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// A task handle a tool call returned.
/// </summary>
/// <param name="ServerId">MCP server id.</param>
/// <param name="Tool">Internal id of the called tool.</param>
/// <param name="TaskId">The server's task id.</param>
/// <param name="PollInterval">The server's suggested poll interval, if it sent one.</param>
/// <param name="TaintKind">Taint kind of the tool's output when it is untrusted; null when trusted.</param>
public sealed record McpTaskStart(string ServerId, ToolId Tool, string TaskId, TimeSpan? PollInterval, string? TaintKind);
