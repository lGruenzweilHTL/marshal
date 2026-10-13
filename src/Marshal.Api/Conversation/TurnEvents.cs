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

/// <summary>The last event of a turn (<c>final</c> event).</summary>
/// <param name="Speech">Full reply text.</param>
/// <param name="ContinueConversation">True when Marshal asked something and the client should keep listening.</param>
/// <param name="ConversationId">The client's conversation id for token clients, Marshal's session id otherwise.</param>
public sealed record TurnFinal(string Speech, bool ContinueConversation, string ConversationId);
