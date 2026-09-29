using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Agent;

/// <summary>
/// <c>agent.ask_advisor(question, context)</c> (safe): one advisor-tier call without tools that sees only the
/// question and the agent-written context summary. Hidden in voice turns, without an advisor model, after the
/// per-run limit and once the owner's daily advisor cost cap is reached.
/// </summary>
public sealed class AskAdvisorTool(
    IModelRouter router,
    IRunRecorder recorder,
    ICostCalculator costs,
    IAdvisorUsage usage,
    IUserProfileProvider profiles,
    ITemplateCatalog templates,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider) : ITool
{
    public const string Id = "agent.ask_advisor";

    private const string SystemPrompt = """
        You are an advisor to a home assistant agent. Give concise, practical advice for the question, using only the
        context provided. You cannot call tools. Answer in plain text.
        """;

    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("agent", "ask_advisor"),
        Description = "Ask a stronger model for advice. Use only for multi-step planning or when stuck after a failed attempt. "
            + "The advisor sees nothing but your question and your context summary, so include every relevant fact in the summary.",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{"question":{"type":"string","description":"The question for the advisor."},"context":{"type":"string","description":"Short summary of the relevant context."}},"required":["question","context"]}
            """).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    public async ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.InputMode == InputMode.Voice)
            return false;
        if (context.Counters.Get(Id) >= (templates.For(context).MaxAdvisorCalls ?? options.Value.Advisor.MaxCallsPerRun))
            return false;
        if (router.Route(new ModelRequest(ModelTier.Advisor)).IsFailure)
            return false;

        DateTimeOffset since = await StartOfLocalDayAsync(context.OwnerId, cancellationToken);
        return await usage.GetAdvisorCostSinceAsync(since, cancellationToken) < options.Value.Advisor.DailyCostCap;
    }

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!TryGetString(invocation.Arguments, "question", out string question) || question.Length == 0)
            return new Error("tool.invalid_arguments", "'question' is required.", ErrorType.Validation);

        TryGetString(invocation.Arguments, "context", out string summary);
        Result<RoutedModel> model = router.Route(new ModelRequest(ModelTier.Advisor));
        if (model.IsFailure)
            return model.Error;

        invocation.Context.Counters.Increment(Id);
        long started = timeProvider.GetTimestamp();
        ChatResponse response = await model.Value.Client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, $"Question: {question}\n\nContext: {summary}")],
            new ChatOptions(),
            cancellationToken);

        decimal cost = costs.Calculate(model.Value.Key, response.Usage);
        invocation.Context.Usage.AddModelCall(cost, (response.Usage?.InputTokenCount ?? 0) + (response.Usage?.OutputTokenCount ?? 0));
        if (invocation.Context.RunId is { } runId)
        {
            await recorder.RecordStepAsync(runId, new RunStepRecord
            {
                Depth = invocation.Context.Depth,
                Kind = RunStepKind.ModelCall,
                Model = model.Value.Key,
                Tier = "advisor",
                TokensIn = response.Usage?.InputTokenCount,
                TokensOut = response.Usage?.OutputTokenCount,
                TokensCached = response.Usage?.CachedInputTokenCount,
                DurationMs = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds,
                Cost = cost,
                Content = response.Text,
            }, cancellationToken);
        }

        return new ToolOutput(response.Text);
    }

    private async Task<DateTimeOffset> StartOfLocalDayAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
        TimeZoneInfo zone = profile.IsSuccess && TimeZoneInfo.TryFindSystemTimeZoneById(profile.Value.TimeZone, out TimeZoneInfo? found) ? found : TimeZoneInfo.Utc;
        DateTimeOffset local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        DateTime midnight = local.Date;
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static bool TryGetString(JsonElement arguments, string name, out string value)
    {
        value = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
        return value.Length > 0;
    }
}
