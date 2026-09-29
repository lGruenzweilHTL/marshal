using System.Threading.Channels;
using Marshal.Abstractions;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Core.Agent;
using Marshal.Core.Guard;
using Marshal.Core.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Conversation;

/// <summary>
/// Handles one interactive turn: session and owner, reply language, a voice follow-up it answers, the session's
/// pending action (keyword / judge), then the agent loop under the latency cap.
/// </summary>
public sealed class ConversationService(
    SessionResolver sessions,
    IConfirmationFlow confirmation,
    IAgentRunner runner,
    IConversationHistory history,
    IUserProfileProvider profiles,
    IExecutionContextAccessor executionContext,
    IOptions<AgentOptions> options,
    IFollowUpReplies? followUps = null)
{
    private const int HistoryTurns = 10;
    private static readonly string[] _supportedLanguages = ["de", "en"];

    /// <summary>Prepares and runs a turn.</summary>
    public async Task<Result<TurnOutcome>> HandleAsync(ConversationTurn turn, ChannelWriter<TurnEvent>? events, CancellationToken cancellationToken)
    {
        Result<PreparedTurn> prepared = await PrepareAsync(turn, cancellationToken);
        return prepared.IsFailure ? prepared.Error : await RunAsync(prepared.Value, events, cancellationToken);
    }

    /// <summary>Resolves the session, owner and reply language. Fails before anything runs.</summary>
    public async Task<Result<PreparedTurn>> PrepareAsync(ConversationTurn turn, CancellationToken cancellationToken)
    {
        Result<SessionRecord> session = await ResolveSessionAsync(turn, cancellationToken);
        if (session.IsFailure)
            return session.Error;

        string language = await LanguageAsync(turn.Language, session.Value.OwnerId, cancellationToken);
        var context = new MarshalExecutionContext
        {
            OwnerId = session.Value.OwnerId,
            Source = turn.Source,
            InputMode = turn.InputMode,
            Language = language,
            DeviceId = turn.Evidence.DeviceId,
            SatelliteId = turn.Evidence.SatelliteId,
            AreaId = turn.Evidence.AreaId,
            SessionId = session.Value.Id,
        };
        return new PreparedTurn(turn, session.Value, context);
    }

    /// <summary>Decides the session's pending action, then runs the loop under the latency cap.</summary>
    public async Task<TurnOutcome> RunAsync(PreparedTurn prepared, ChannelWriter<TurnEvent>? events, CancellationToken cancellationToken)
    {
        (ConversationTurn turn, SessionRecord session, MarshalExecutionContext context) = prepared;
        using IDisposable scope = executionContext.Use(context);
        IReadOnlyList<ChatMessage> earlier = await history.LoadAsync(session.Id, HistoryTurns, cancellationToken);
        FollowUpReply? followUp = followUps is null ? null : await followUps.HandleAsync(turn, context, cancellationToken);
        AgentRunRequest request = new()
        {
            Context = context,
            History = [.. earlier, .. followUp?.Seed ?? []],
            UserText = turn.Text,
            Events = events,
            LatencyCap = turn.InputMode == InputMode.Voice ? options.Value.VoiceLatencyCap : options.Value.TextLatencyCap,
        };

        // A reply that decided an approval or resumed a waiting task is answered without a model call.
        if (followUp?.FixedReply is { } fixedReply)
        {
            AgentRunResult answered = await runner.RunAsync(request with { Trigger = "followup", FixedReply = fixedReply }, cancellationToken);
            return new TurnOutcome(answered.Reply, answered.ContinueConversation, ConversationId(turn, session));
        }

        ConfirmationOutcome decided = await confirmation.HandleUserTurnAsync(session.Id, turn.Text, context, cancellationToken);
        request = decided.Kind switch
        {
            ConfirmationOutcomeKind.Executed => request with { Trigger = "confirmation", Confirmation = Step(decided) },
            ConfirmationOutcomeKind.Cancelled => request with { Trigger = "confirmation", Confirmation = Step(decided), FixedReply = FixedReplies.Cancelled(context.Language) },
            ConfirmationOutcomeKind.Expired => request with { Trigger = "confirmation", Confirmation = Step(decided), FixedReply = FixedReplies.Expired(context.Language) },
            ConfirmationOutcomeKind.Dropped => request with { Confirmation = Step(decided) },
            _ => request,
        };

        AgentRunResult result = await runner.RunAsync(request, cancellationToken);
        return new TurnOutcome(result.Reply, result.ContinueConversation, ConversationId(turn, session));
    }

    private static string ConversationId(ConversationTurn turn, SessionRecord session) =>
        turn.ApiTokenId is null ? session.Id.ToString() : turn.ConversationId!;

    private async Task<Result<SessionRecord>> ResolveSessionAsync(ConversationTurn turn, CancellationToken cancellationToken)
    {
        if (turn.ApiTokenId is { } token)
        {
            if (string.IsNullOrWhiteSpace(turn.ConversationId))
                return new Error("conversation.id_required", "conversationId is required for token clients.", ErrorType.Validation);

            return await sessions.ResolveTokenSessionAsync(token, turn.PrincipalOwner, turn.ConversationId, turn.Evidence, turn.Source, cancellationToken);
        }

        SessionId? id = null;
        if (turn.ConversationId is not null)
        {
            if (!SessionId.TryParse(turn.ConversationId, null, out SessionId parsed))
                return new Error("session.not_found", "The conversation does not exist or has ended.", ErrorType.NotFound);

            id = parsed;
        }

        return await sessions.ResolveOwnSessionAsync(id, turn.PrincipalOwner, turn.Source, cancellationToken);
    }

    private async Task<string> LanguageAsync(string? requested, OwnerId owner, CancellationToken cancellationToken)
    {
        string? normalized = requested is { Length: >= 2 } ? requested[..2].ToLowerInvariant() : null;
        if (normalized is not null && _supportedLanguages.Contains(normalized))
            return normalized;

        Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
        return profile.IsSuccess && _supportedLanguages.Contains(profile.Value.Language) ? profile.Value.Language : "en";
    }

    private static ConfirmationStep? Step(ConfirmationOutcome decided) =>
        decided.Action is null || decided.Decision is null
            ? null
            : new ConfirmationStep(decided.Action, decided.Tool, decided.Decision.Value, decided.ToolResult);
}
