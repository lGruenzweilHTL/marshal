using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions;
using Marshal.Core.Agent;
using Marshal.Core.Conversation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.McpServer;

/// <summary>
/// <see cref="IAskMarshalRunner"/>: a non-interactive API turn with the default template under the text latency cap
/// and what is left of the daily cost cap. Nobody can confirm inside it: confirm-tier calls go to the approval queue,
/// and the turn neither decides the session's pending action nor answers follow-ups, so a "yes" from the external
/// agent confirms nothing.
/// </summary>
public sealed class AskMarshalRunner(
    ConversationService conversations,
    IAgentRunner runner,
    IConversationHistory history,
    IDailyCostCap cap,
    IExecutionContextAccessor executionContext,
    IOptions<AgentOptions> options) : IAskMarshalRunner
{
    public const string Trigger = "mcp:ask_marshal";

    private const int HistoryTurns = 10;

    public async Task<Result<AskResult>> AskAsync(AskRequest request, CancellationToken cancellationToken)
    {
        decimal left = await cap.LeftTodayAsync(request.Owner, cancellationToken);
        if (left <= 0)
            return AskErrors.DailyCap();

        string conversationId = string.IsNullOrWhiteSpace(request.ConversationId) ? Guid.CreateVersion7().ToString() : request.ConversationId;
        Result<PreparedTurn> prepared = await conversations.PrepareAsync(new ConversationTurn
        {
            PrincipalOwner = request.Owner,
            ApiTokenId = request.Token,
            Source = SessionSource.Api,
            ConversationId = conversationId,
            Text = request.Text,
            Language = request.Language,
            Evidence = new IdentityEvidence(null, null, null, null),
        }, cancellationToken);
        if (prepared.IsFailure)
            return prepared.Error;

        MarshalExecutionContext context = prepared.Value.Context with { IsInteractive = false };
        using IDisposable scope = executionContext.Use(context);
        IReadOnlyList<ChatMessage> earlier = await history.LoadAsync(prepared.Value.Session.Id, HistoryTurns, after: null, cancellationToken);
        AgentRunResult result = await runner.RunAsync(new AgentRunRequest
        {
            Context = context,
            Trigger = Trigger,
            History = earlier,
            UserText = request.Text,
            LatencyCap = options.Value.TextLatencyCap,
            Limits = new RunLimits(MaxCost: left),
        }, cancellationToken);

        return result.OutcomeReason == "budget_exceeded"
            ? AskErrors.DailyCap()
            : new AskResult(result.RunId, result.Reply, conversationId);
    }
}
