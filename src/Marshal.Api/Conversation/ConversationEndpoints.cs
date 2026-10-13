using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Channels;
using Marshal.Abstractions;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Core.Conversation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Conversation;

/// <summary>
/// <c>POST /conversation/turns</c> on the versioned API group: one interactive turn streamed as SSE.
/// </summary>
internal static partial class ConversationEndpoints
{
    public static RouteGroupBuilder MapConversationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/conversation/turns", CreateTurnAsync)
            .RequireAuthorization(Policies.ConversationWrite)
            .WithName("CreateConversationTurn")
            .WithSummary("Runs one conversation turn and streams the reply as server-sent events (delta, status, artifact, final; web sessions also reasoning and tool).");
        return group;
    }

    private static async Task<Results<ServerSentEventsResult<object>, ProblemHttpResult>> CreateTurnAsync(
        ConversationTurnRequest request, ClaimsPrincipal user, ConversationService conversations, ILoggerFactory loggers, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return new Error("conversation.text_required", "text is required.", ErrorType.Validation).ToProblem();

        var owner = OwnerId.Parse(user.FindFirstValue(MarshalClaims.Owner)!, null);
        ApiTokenId? token = ApiTokenId.TryParse(user.FindFirstValue(MarshalClaims.TokenId), null, out ApiTokenId parsed) ? parsed : null;
        var turn = new ConversationTurn
        {
            PrincipalOwner = owner,
            ApiTokenId = token,
            Source = token is null ? SessionSource.Web : request.SatelliteId is null && request.DeviceId is null && request.HaUserId is null ? SessionSource.Api : SessionSource.Voice,
            ConversationId = request.ConversationId,
            Text = request.Text,
            Language = request.Language,
            Evidence = new IdentityEvidence(request.HaUserId, request.DeviceId, request.SatelliteId, request.AreaId),
            InputMode = request.InputMode ?? InputMode.Text,
            FollowupRef = request.FollowupRef,
        };

        Result<PreparedTurn> prepared = await conversations.PrepareAsync(turn, ct);
        if (prepared.IsFailure)
            return prepared.Error.ToProblem();

        ILogger logger = loggers.CreateLogger("Marshal.Conversation");
        LogTurn(logger, prepared.Value.Context.SessionId?.Value, turn.InputMode);
        return TypedResults.ServerSentEvents(StreamAsync(conversations, prepared.Value, webClient: token is null, ct));
    }

    private static async IAsyncEnumerable<SseItem<object>> StreamAsync(
        ConversationService conversations, PreparedTurn turn, bool webClient, [EnumeratorCancellation] CancellationToken ct)
    {
        var events = Channel.CreateUnbounded<TurnEvent>(new UnboundedChannelOptions { SingleReader = true });
        Task<TurnOutcome> running = RunAsync(conversations, turn, events.Writer, ct);
        await foreach (TurnEvent turnEvent in events.Reader.ReadAllAsync(ct))
        {
            // Reasoning and tool steps are for the web UI; token clients keep the v1 stream unchanged.
            if (!webClient && turnEvent is ReasoningEvent or ToolEvent)
                continue;

            yield return turnEvent switch
            {
                DeltaEvent delta => new SseItem<object>(new TurnDelta(delta.Text), "delta"),
                StatusEvent status => new SseItem<object>(new TurnStatus(status.Text), "status"),
                ArtifactEvent shown => new SseItem<object>(new TurnArtifact(shown.Artifact.Id.ToString(), shown.Artifact.Name, shown.Artifact.Kind, shown.Artifact.Url), "artifact"),
                ReasoningEvent reasoning => new SseItem<object>(new TurnReasoning(reasoning.Text), "reasoning"),
                ToolEvent tool => new SseItem<object>(new TurnTool(tool.CallId, tool.Tool, tool.Arguments, tool.State, tool.DurationMs, tool.Result), "tool"),
                _ => throw new InvalidOperationException($"Unknown turn event {turnEvent.GetType().Name}."),
            };
        }

        TurnOutcome outcome = await running;
        yield return new SseItem<object>(new TurnFinal(outcome.Speech, outcome.ContinueConversation, outcome.ConversationId), "final");
    }

    private static async Task<TurnOutcome> RunAsync(ConversationService conversations, PreparedTurn turn, ChannelWriter<TurnEvent> events, CancellationToken ct)
    {
        try
        {
            return await conversations.RunAsync(turn, events, ct);
        }
        finally
        {
            events.TryComplete();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Conversation turn in session {SessionId} ({InputMode})")]
    private static partial void LogTurn(ILogger logger, Guid? sessionId, InputMode inputMode);
}
