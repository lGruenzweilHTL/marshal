namespace Marshal.Abstractions.Mcp;

/// <summary>
/// The tasks extension's <c>tasks/get</c> and <c>tasks/cancel</c> on the configured MCP servers.
/// </summary>
public interface IMcpTaskClient
{
    /// <summary>
    /// The current state of a server task. Fails with <see cref="ErrorType.Unavailable"/> while the server is down
    /// (poll again later) and with <see cref="ErrorType.NotFound"/> when the server does not know the task.
    /// </summary>
    Task<Result<McpTaskSnapshot>> GetTaskAsync(string serverId, string taskId, CancellationToken cancellationToken);

    /// <summary>
    /// Answers one of a task's input requests with <c>tasks/update</c>: accepted with <paramref name="content"/>
    /// (form) or without (URL mode, done), or declined.
    /// </summary>
    Task<Result> AnswerInputAsync(string serverId, string taskId, string inputKey, bool accept, System.Text.Json.JsonElement? content, CancellationToken cancellationToken);

    /// <summary>Asks the server to cancel a task; best effort.</summary>
    Task<Result> CancelTaskAsync(string serverId, string taskId, CancellationToken cancellationToken);
}

/// <summary>Status of a server task (tasks extension).</summary>
public enum McpTaskStatus
{
    /// <summary>The server is still working on it.</summary>
    Working,

    /// <summary>The server waits for input from the client.</summary>
    InputRequired,

    /// <summary>Finished with a result.</summary>
    Completed,

    /// <summary>Finished with an error.</summary>
    Failed,

    /// <summary>Cancelled by the server or the client.</summary>
    Cancelled,
}

/// <summary>
/// One <c>tasks/get</c> answer.
/// </summary>
/// <param name="Status">The task's status.</param>
/// <param name="PollInterval">The server's suggested poll interval, if it sent one.</param>
/// <param name="Result">Text of the final tool result (completed only); server content.</param>
/// <param name="ResultIsError">Whether the final tool result is a tool error.</param>
/// <param name="Error">Message of the task's error (failed only); server text.</param>
public sealed record McpTaskSnapshot(McpTaskStatus Status, TimeSpan? PollInterval = null, string? Result = null, bool ResultIsError = false, string? Error = null)
{
    /// <summary>Input the task waits for (input required only), by key; elicitation requests only.</summary>
    public IReadOnlyList<McpTaskInput> Inputs { get; init; } = [];

    /// <summary>Whether the task has ended.</summary>
    public bool IsTerminal => Status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled;
}

/// <summary>
/// One elicitation request of a server task waiting for input. Server text: shown only in the web UI.
/// </summary>
/// <param name="Key">Key in the task's <c>inputRequests</c>.</param>
/// <param name="Mode">Form or URL.</param>
/// <param name="Message">What the server asks.</param>
/// <param name="RequestedSchemaJson">Form mode: the requested schema.</param>
/// <param name="Url">URL mode: the page to open.</param>
public sealed record McpTaskInput(string Key, InputRequests.InputRequestMode Mode, string Message, string? RequestedSchemaJson, string? Url);
