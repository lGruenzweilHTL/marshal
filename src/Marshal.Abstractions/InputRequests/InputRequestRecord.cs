using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.InputRequests;

/// <summary>How an MCP server asks the user for input.</summary>
public enum InputRequestMode
{
    /// <summary>A form generated from the requested schema; the answer goes back to the server.</summary>
    Form,

    /// <summary>A page the user opens (e.g. a login); the user says when done, nothing else goes back.</summary>
    Url,
}

/// <summary>Lifecycle of a pending input request.</summary>
public enum InputRequestStatus
{
    /// <summary>Waiting for the user.</summary>
    Pending,

    /// <summary>Answered; the frozen call was repeated with the answer.</summary>
    Answered,

    /// <summary>Declined by the user; nothing was sent to the server.</summary>
    Declined,

    /// <summary>Not answered before the expiry; nothing was sent to the server.</summary>
    Expired,

    /// <summary>Dropped without an answer (its task ended).</summary>
    Dropped,
}

/// <summary>
/// An MCP server's request for user input, with the tool call it interrupted frozen. The request's content
/// (message, schema, URL) is server text: shown only to the user in the web UI, never to the model.
/// </summary>
public sealed record InputRequestRecord
{
    public required InputRequestId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public SessionId? SessionId { get; init; }

    public string? SatelliteId { get; init; }

    /// <summary>The task resumed with the outcome: the asking background task, or one created for the request.</summary>
    public TaskId? TaskId { get; init; }

    public RunId? RunId { get; init; }

    /// <summary>MCP server id.</summary>
    public required string ServerId { get; init; }

    /// <summary>The interrupted tool.</summary>
    public required ToolId Tool { get; init; }

    /// <summary>Frozen arguments object, as JSON text.</summary>
    public required string ArgumentsJson { get; init; }

    /// <summary>
    /// Answers to earlier requests of the same call (a chain), as a JSON array; the repeated call gives them to the
    /// server again before this request's answer.
    /// </summary>
    public string PreviousAnswersJson { get; init; } = "[]";

    public required InputRequestMode Mode { get; init; }

    /// <summary>What the server asks (server text).</summary>
    public required string Message { get; init; }

    /// <summary>Form mode: the requested schema (object with primitive properties), as JSON text.</summary>
    public string? RequestedSchemaJson { get; init; }

    /// <summary>URL mode: the page to open (server text).</summary>
    public string? Url { get; init; }

    /// <summary>
    /// Set when a running server task asks (tasks extension): the answer goes to it with <c>tasks/update</c> instead
    /// of repeating the call, and the task keeps waiting for the server task's outcome.
    /// </summary>
    public McpServerTaskInput? ServerTask { get; init; }

    public required string Nonce { get; init; }

    public required string Language { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public InputRequestStatus Status { get; init; } = InputRequestStatus.Pending;
}

/// <summary>Which input of which server task a request answers.</summary>
/// <param name="TaskId">The server's task id.</param>
/// <param name="InputKey">Key of the request in the task's <c>inputRequests</c>.</param>
public sealed record McpServerTaskInput(string TaskId, string InputKey);

/// <summary>
/// What a tool source asks for. The owner, session, satellite, run, task and language come from the execution context.
/// </summary>
/// <param name="ServerId">MCP server id.</param>
/// <param name="Tool">The interrupted tool.</param>
/// <param name="ArgumentsJson">Frozen arguments.</param>
/// <param name="Mode">Form or URL.</param>
/// <param name="Message">What the server asks.</param>
/// <param name="RequestedSchemaJson">Form mode: the requested schema.</param>
/// <param name="Url">URL mode: the page to open.</param>
/// <param name="PreviousAnswersJson">Answers to earlier requests of the same call, as a JSON array.</param>
/// <param name="ServerTask">Set when a running server task asks; the context's task is the one waiting for it.</param>
public sealed record NewInputRequest(
    string ServerId,
    ToolId Tool,
    string ArgumentsJson,
    InputRequestMode Mode,
    string Message,
    string? RequestedSchemaJson,
    string? Url,
    string PreviousAnswersJson = "[]",
    McpServerTaskInput? ServerTask = null);
