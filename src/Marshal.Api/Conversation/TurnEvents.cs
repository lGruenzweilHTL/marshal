using System.Text.Json.Serialization;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Conversation;

namespace Marshal.Api.Conversation;

/// <summary>A reply text chunk (<c>delta</c> event).</summary>
public sealed record TurnDelta(string Text);

/// <summary>A progress message (<c>status</c> event).</summary>
public sealed record TurnStatus(string Text);

/// <summary>An artifact the turn shows (<c>artifact</c> event).</summary>
/// <param name="Id">Artifact id.</param>
/// <param name="Name">File name.</param>
/// <param name="Kind">Kind; picks inline image or card.</param>
/// <param name="Url">Content URL.</param>
public sealed record TurnArtifact(string Id, string Name, ArtifactKind Kind, string Url);

/// <summary>A chunk of the model's reasoning (<c>reasoning</c> event, cookie clients only).</summary>
public sealed record TurnReasoning(string Text);

/// <summary>A tool call started or ended (<c>tool</c> event, cookie clients only).</summary>
/// <param name="Id">Call id; the ending event repeats it.</param>
/// <param name="Tool">Tool id, e.g. <c>printer.status</c>.</param>
/// <param name="Arguments">One-line argument summary.</param>
/// <param name="State"><c>running</c>, <c>ok</c>, <c>error</c>, <c>denied</c> or <c>pending</c>.</param>
/// <param name="DurationMs">How long the call took (ending events).</param>
/// <param name="Result">One-line result summary or error code; absent for sensitive tools.</param>
public sealed record TurnTool(
    string Id,
    string Tool,
    string Arguments,
    ToolEventState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DurationMs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Result);

/// <summary>
/// An MCP server asked the user for input (<c>input_request</c> event, cookie clients only); the chat loads the request
/// from <c>/api/v1/approvals/inputs/{id}</c> and shows its form or link.
/// </summary>
/// <param name="Id">Input request id.</param>
public sealed record TurnInputRequest(string Id);

/// <summary>A built-in widget for a tool call's result (<c>widget</c> event, cookie clients only).</summary>
/// <param name="Kind">Widget kind: <c>printer</c> or <c>music</c>.</param>
/// <param name="ToolCallId">The tool call that produced it.</param>
/// <param name="Data">The widget's data at the time of the call.</param>
public sealed record TurnWidget(string Kind, string ToolCallId, System.Text.Json.JsonElement Data);

/// <summary>An MCP app for a tool call's result (<c>app</c> event, cookie clients only).</summary>
/// <param name="AppId">Load it from <c>/api/v1/apps/{appId}</c>; its HTML is at <c>/mcp-apps/{appId}</c>.</param>
/// <param name="Server">The MCP server id.</param>
/// <param name="Tool">The tool name as the server reports it.</param>
/// <param name="ToolCallId">The tool call that produced it.</param>
public sealed record TurnApp(string AppId, string Server, string Tool, string ToolCallId);

/// <summary>The last event of a turn (<c>final</c> event).</summary>
/// <param name="Speech">Full reply text.</param>
/// <param name="ContinueConversation">True when Marshal asked something and the client should keep listening.</param>
/// <param name="ConversationId">The client's conversation id for token clients, Marshal's session id otherwise.</param>
public sealed record TurnFinal(string Speech, bool ContinueConversation, string ConversationId);
