using System.Text.Json.Serialization;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// Conversations of the current owner for the chat history: sessions with their turns and the background results
/// that belong to them. Task sessions are not conversations.
/// </summary>
public interface IConversationReader
{
    /// <summary>Conversations whose last turn is before <paramref name="before"/> (all when null), newest first.</summary>
    Task<IReadOnlyList<ConversationSummary>> ListAsync(DateTimeOffset? before, int limit, CancellationToken cancellationToken);

    /// <summary>One conversation with its entries in time order; null when it does not exist for this owner.</summary>
    Task<ConversationDetail?> GetAsync(SessionId id, CancellationToken cancellationToken);
}

/// <summary>
/// One conversation in the history list.
/// </summary>
/// <param name="Id">The session id.</param>
/// <param name="Source">Where the conversation came from (web, voice or api).</param>
/// <param name="CreatedAt">When the session started.</param>
/// <param name="LastTurnAt">When the last turn arrived; the list is ordered by it.</param>
/// <param name="Turns">Number of turns.</param>
/// <param name="Title">The first user input, cut to 80 characters; null when no turn has an input.</param>
/// <param name="CanContinue">Whether the web chat can add turns (open web sessions only).</param>
/// <param name="SatelliteId">Satellite of the first turn (voice sessions).</param>
/// <param name="AreaId">Area of the first turn (voice sessions).</param>
public sealed record ConversationSummary(
    SessionId Id,
    SessionSource Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastTurnAt,
    int Turns,
    string? Title,
    bool CanContinue,
    string? SatelliteId,
    string? AreaId);

/// <summary>
/// A conversation with all its entries in time order.
/// </summary>
/// <param name="Conversation">The list entry of the conversation.</param>
/// <param name="Entries">Turns and background results, oldest first.</param>
public sealed record ConversationDetail(ConversationSummary Conversation, IReadOnlyList<ConversationEntry> Entries);

/// <summary>
/// A turn or a background result of a conversation.
/// </summary>
/// <param name="At">When the turn started, or when the background result was ready.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TurnEntry), "turn")]
[JsonDerivedType(typeof(BackgroundResultEntry), "backgroundResult")]
public abstract record ConversationEntry(DateTimeOffset At);

/// <summary>
/// One user turn and Marshal's reply.
/// </summary>
/// <param name="RunId">The run of the turn.</param>
/// <param name="At">When the turn started.</param>
/// <param name="InputMode">voice or text.</param>
/// <param name="SatelliteId">The satellite the turn came from (voice).</param>
/// <param name="AreaId">The area of the satellite (voice).</param>
/// <param name="Input">What the user said or typed; null for confirmations without text.</param>
/// <param name="Reply">The full reply; null when the run recorded none (error, cancellation).</param>
/// <param name="ContinueConversation">Whether the reply asked something back.</param>
/// <param name="Artifacts">Artifacts the turn showed.</param>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Reasoning">The reasoning of the turn's model calls, in order; null when none was recorded.</param>
/// <param name="ReasoningWithheld">Some reasoning followed a redacted tool result and was not kept.</param>
/// <param name="Tools">The turn's tool calls (not those of subagents), in order.</param>
public sealed record TurnEntry(
    RunId RunId,
    DateTimeOffset At,
    InputMode? InputMode,
    string? SatelliteId,
    string? AreaId,
    string? Input,
    string? Reply,
    bool? ContinueConversation,
    IReadOnlyList<ArtifactRef> Artifacts,
    RunOutcome Outcome,
    string? Reasoning,
    bool ReasoningWithheld,
    IReadOnlyList<TurnToolStep> Tools) : ConversationEntry(At)
{
    /// <summary>Built-in widgets the turn showed, with their data at the time of the call.</summary>
    public IReadOnlyList<Widgets.WidgetRef> Widgets { get; init; } = [];

    /// <summary>MCP apps the turn showed; the chat loads each by its app id.</summary>
    public IReadOnlyList<Apps.AppRef> Apps { get; init; } = [];

    /// <summary>The owner's rating of the turn's run; null when unrated.</summary>
    public Evals.RunRating? Rating { get; init; }
}

/// <summary>
/// One tool call of a turn, summarized like the live <c>tool</c> events.
/// </summary>
/// <param name="Tool">Tool id, e.g. <c>printer.status</c>.</param>
/// <param name="Arguments">One-line argument summary.</param>
/// <param name="State">How the call ended.</param>
/// <param name="DurationMs">How long the call took.</param>
/// <param name="Result">One-line result summary or error code; null for redacted tools.</param>
public sealed record TurnToolStep(string Tool, string Arguments, ToolEventState State, int DurationMs, string? Result);

/// <summary>
/// The result of a background task that a turn of this conversation handed off.
/// </summary>
/// <param name="TaskId">The task that produced the result.</param>
/// <param name="At">When the result was ready.</param>
/// <param name="Body">The result text.</param>
public sealed record BackgroundResultEntry(TaskId TaskId, DateTimeOffset At, string Body) : ConversationEntry(At);
