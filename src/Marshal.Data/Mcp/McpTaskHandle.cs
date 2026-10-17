using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;

namespace Marshal.Data.Mcp;

/// <summary>
/// A server task an MCP tool call returned, polled until it ends (<c>mcp_task_handles</c>). Deleted once the
/// waiting task has the outcome.
/// </summary>
public sealed class McpTaskHandle : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required string ServerId { get; init; }

    /// <summary>The server's task id.</summary>
    public required string ServerTaskId { get; init; }

    /// <summary>Internal id of the called tool.</summary>
    public required string Tool { get; init; }

    /// <summary>The waiting task that receives the outcome.</summary>
    public required TaskId TaskId { get; init; }

    /// <summary>Language of the calling turn, for input requests the server task makes.</summary>
    public required string Language { get; init; }

    /// <summary>Taint kind of the tool's output; null when the tool is trusted.</summary>
    public string? TaintKind { get; init; }

    /// <summary>The server's latest suggested poll interval in milliseconds.</summary>
    public long? PollIntervalMs { get; set; }

    /// <summary>Keys of the server task's input requests already asked (JSON array); each is asked once.</summary>
    public string AskedInputs { get; set; } = "[]";

    /// <summary>Polls so far; the backoff grows with it.</summary>
    public int Polls { get; set; }

    public required DateTimeOffset NextPollAt { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
