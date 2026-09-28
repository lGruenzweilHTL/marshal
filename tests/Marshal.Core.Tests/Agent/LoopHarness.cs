using System.Threading.Channels;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Context;
using Marshal.Core.Guard;
using Marshal.Core.Models;
using Marshal.Core.Templates;
using Marshal.Core.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Core.Tests.Agent;

/// <summary>
/// Wires a real agent runner, guard, registry and risk engine around a scripted fake model.
/// </summary>
internal sealed class LoopHarness
{
    private LoopHarness()
    {
    }

    public FakeChatClient Model { get; } = new();

    public FakeChatClient Advisor { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    public InMemoryPendingActionStore Store { get; private set; } = null!;

    public InMemoryRunRecorder Recorder { get; } = new();

    public FakeExecutionContextAccessor Accessor { get; } = new();

    public AgentOptions Options { get; } = new();

    public SingleModelRouter Router { get; private set; } = null!;

    public ToolRegistry Registry { get; private set; } = null!;

    /// <summary>The source behind <see cref="Registry"/>; changes show after the next refresh.</summary>
    public FakeToolSource Source { get; private set; } = null!;

    public AgentRunner Runner { get; private set; } = null!;

    public FakeTemplateCatalog Templates { get; } = new();

    /// <summary><c>agent.delegate</c> bound to <see cref="Runner"/>; offered only when passed as a built-in.</summary>
    public DelegateTool DelegateTool { get; private set; } = null!;

    public Channel<TurnEvent> Events { get; } = Channel.CreateUnbounded<TurnEvent>();

    /// <summary>Wraps <see cref="Runner"/> and records the requests it receives.</summary>
    public RunnerSpy RunnerSpy { get; private set; } = null!;

    /// <summary>History source counting loads (returns nothing).</summary>
    public CountingHistory History { get; } = new();

    public static async Task<LoopHarness> CreateAsync(string rules = "risk: []", SatelliteProfile? kitchen = null, bool withAdvisor = true, Func<LoopHarness, ITool[]>? builtIns = null, params ITool[] tools)
    {
        var harness = new LoopHarness();
        harness.Store = new InMemoryPendingActionStore(harness.Time);
        harness.Router = new SingleModelRouter(harness.Model, withAdvisor ? harness.Advisor : null);
        harness.DelegateTool = new DelegateTool(() => harness.Runner, harness.Templates, Microsoft.Extensions.Options.Options.Create(harness.Options));
        ITool[] all = [.. tools, .. builtIns?.Invoke(harness) ?? []];
        harness.Source = new FakeToolSource("test", true, all);
        harness.Registry = new ToolRegistry([harness.Source]);
        await harness.Registry.RefreshAsync(TestContext.Current.CancellationToken);
        RuleBasedRiskEngine engine = GuardTestKit.Engine(rules);
        var satellites = new FixedSatellites("assist_satellite.kitchen", kitchen ?? SatelliteProfile.Default);
        var visibility = new ToolVisibilityFilter(harness.Registry, engine, satellites, harness.Templates);
        var guard = new ToolCallGuard(harness.Registry, visibility, engine, satellites);
        harness.Runner = new AgentRunner(
            harness.Router,
            harness.Registry,
            harness.Templates,
            new ContextBuilder([new BasePromptContributor(), new OutputModeContributor(), new TemplatePromptContributor(harness.Templates), new DelegationIndexContributor(harness.Templates, Microsoft.Extensions.Options.Options.Create(harness.Options))]),
            visibility,
            guard,
            harness.Store,
            harness.Recorder,
            new PriceTableCostCalculator(Microsoft.Extensions.Options.Options.Create(new ModelRoutingOptions())),
            harness.Accessor,
            Microsoft.Extensions.Options.Options.Create(harness.Options),
            Microsoft.Extensions.Options.Options.Create(new ConfirmationOptions()),
            harness.Time,
            NullLogger<AgentRunner>.Instance);
        harness.RunnerSpy = new RunnerSpy(harness.Runner);
        return harness;
    }

    public async Task<List<TurnEvent>> DrainEventsAsync()
    {
        Events.Writer.TryComplete();
        var events = new List<TurnEvent>();
        await foreach (TurnEvent turnEvent in Events.Reader.ReadAllAsync())
            events.Add(turnEvent);

        return events;
    }
}

internal sealed class RunnerSpy(IAgentRunner inner) : IAgentRunner
{
    public List<TimeSpan?> LatencyCaps { get; } = [];

    public Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        LatencyCaps.Add(request.LatencyCap);
        return inner.RunAsync(request, cancellationToken);
    }
}

internal sealed class CountingHistory : Marshal.Abstractions.Sessions.IConversationHistory
{
    public int Loads { get; private set; }

    public Task<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>> LoadAsync(Marshal.Abstractions.Sessions.SessionId sessionId, int maxTurns, CancellationToken cancellationToken)
    {
        Loads++;
        return Task.FromResult<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>>([]);
    }
}
