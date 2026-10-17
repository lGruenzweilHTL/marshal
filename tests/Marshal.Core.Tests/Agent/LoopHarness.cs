using System.Threading.Channels;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.AiTasks;
using Marshal.Core.Context;
using Marshal.Core.Guard;
using Marshal.Core.Models;
using Marshal.Core.Templates;
using Marshal.Core.Tools;
using Marshal.Testing;
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

    /// <summary>Model table for the cost calculator (keys are tier names, e.g. <c>standard</c>).</summary>
    public ModelRoutingOptions Routing { get; } = new();

    public SingleModelRouter Router { get; private set; } = null!;

    public ToolRegistry Registry { get; private set; } = null!;

    /// <summary>The source behind <see cref="Registry"/>; changes show after the next refresh.</summary>
    public FakeToolSource Source { get; private set; } = null!;

    public AgentRunner Runner { get; private set; } = null!;

    public FakeTemplateCatalog Templates { get; } = new();

    /// <summary><c>agent.delegate</c> bound to <see cref="Runner"/>; offered only when passed as a built-in.</summary>
    public DelegateTool DelegateTool { get; private set; } = null!;

    /// <summary>Everything the runner logged.</summary>
    public ListLogger<AgentRunner> Logger { get; } = new();

    public Channel<TurnEvent> Events { get; } = Channel.CreateUnbounded<TurnEvent>();

    /// <summary>Wraps <see cref="Runner"/> and records the requests it receives.</summary>
    public RunnerSpy RunnerSpy { get; private set; } = null!;

    /// <summary>History source counting loads (returns nothing).</summary>
    public CountingHistory History { get; } = new();

    /// <summary>Background handoffs of the runner; refuses until <see cref="RecordingHandoff.Accepts"/> is set.</summary>
    public RecordingHandoff Handoff { get; } = new();

    /// <summary>Tool retrieval settings; with the defaults, the harness's few tools fall under <c>minTools</c> and retrieval is skipped.</summary>
    public ToolsOptions ToolsOptions { get; } = new();

    public VoiceOptions VoiceOptions { get; } = new();

    public FakeEmbeddingGenerator Embeddings { get; } = new(dimensions: 4);

    /// <summary>The turn embedding tool retrieval sees; none unless set.</summary>
    public FixedTurnEmbedding TurnEmbedding { get; } = new();

    /// <summary>Namespace hints; add to make a turn load a namespace deterministically.</summary>
    public List<IToolNamespaceHints> Hints { get; } = [];

    public InMemorySessionToolNamespaces SessionTools { get; } = new();

    public FixedToolLatency Latency { get; } = new();

    public NamespaceToolSelector Selector { get; private set; } = null!;

    public ToolVisibilityFilter Visibility { get; private set; } = null!;

    /// <summary>Approval queue of non-interactive runs; freezes into <see cref="Store"/> and records what it queued.</summary>
    public QueueingApprovals Approvals { get; private set; } = null!;

    public static async Task<LoopHarness> CreateAsync(string rules = "risk: []", SatelliteProfile? kitchen = null, bool withAdvisor = true, Func<LoopHarness, ITool[]>? builtIns = null, IContextContributor[]? contributors = null, params ITool[] tools)
    {
        var harness = new LoopHarness();
        harness.Store = new InMemoryPendingActionStore(harness.Time);
        harness.Approvals = new QueueingApprovals(harness.Store);
        harness.Router = new SingleModelRouter(harness.Model, withAdvisor ? harness.Advisor : null);
        harness.DelegateTool = new DelegateTool(() => harness.Runner, harness.Templates, Microsoft.Extensions.Options.Options.Create(harness.Options));
        harness.Selector = new NamespaceToolSelector(
            Microsoft.Extensions.Options.Options.Create(harness.ToolsOptions),
            harness.Hints,
            new CachedNamespaceEmbeddings(harness.Embeddings),
            harness.Embeddings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NamespaceToolSelector>.Instance,
            harness.TurnEmbedding);
        ITool[] all = [.. tools, .. builtIns?.Invoke(harness) ?? []];
        harness.Source = new FakeToolSource("test", true, all);
        harness.Registry = new ToolRegistry([harness.Source]);
        await harness.Registry.RefreshAsync(TestContext.Current.CancellationToken);
        RuleBasedRiskEngine engine = GuardTestKit.Engine(rules);
        var satellites = new FixedSatellites("assist_satellite.kitchen", kitchen ?? SatelliteProfile.Default);
        var visibility = new ToolVisibilityFilter(harness.Registry, engine, satellites, harness.Templates);
        harness.Visibility = visibility;
        var guard = new ToolCallGuard(harness.Registry, visibility, engine, satellites);
        var costs = new PriceTableCostCalculator(Microsoft.Extensions.Options.Options.Create(harness.Routing));
        harness.Runner = new AgentRunner(
            harness.Router,
            harness.Registry,
            harness.Templates,
            new ContextBuilder([new BasePromptContributor(), new OutputModeContributor(), new TemplatePromptContributor(harness.Templates), new DelegationIndexContributor(harness.Templates, Microsoft.Extensions.Options.Options.Create(harness.Options)), .. contributors ?? []]),
            visibility,
            guard,
            harness.Store,
            harness.Recorder,
            costs,
            harness.Accessor,
            Microsoft.Extensions.Options.Options.Create(harness.Options),
            Microsoft.Extensions.Options.Options.Create(new ConfirmationOptions()),
            harness.Time,
            harness.Logger,
            approvals: harness.Approvals,
            handoff: harness.Handoff,
            selector: harness.Selector,
            sessionTools: harness.SessionTools,
            latency: harness.Latency,
            voiceOptions: Microsoft.Extensions.Options.Options.Create(harness.VoiceOptions),
            shaper: new ModelStructuredOutputShaper(harness.Recorder, costs, harness.Time));
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

internal sealed class QueueingApprovals(InMemoryPendingActionStore store) : IApprovalQueue
{
    public List<PendingActionRecord> Queued { get; } = [];

    public async Task<PendingActionRecord> EnqueueAsync(FreezeRequest request, CancellationToken cancellationToken)
    {
        PendingActionRecord frozen = await store.FreezeAsync(request with { Queued = true }, cancellationToken);
        Queued.Add(frozen);
        return frozen;
    }

    public Task<IReadOnlyList<PendingActionRecord>> ListPendingAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PendingActionRecord>>(Queued);

    public Task<Marshal.Abstractions.Result<PendingActionRecord>> DecideAsync(PendingActionId id, bool approve, DecidedBy decidedBy, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FixedTurnEmbedding : ITurnEmbedding
{
    public float[]? Vector { get; set; }

    public Task<ReadOnlyMemory<float>?> GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<ReadOnlyMemory<float>?>(Vector);
}

internal sealed class FixedHints(params string[] names) : IToolNamespaceHints
{
    public Task<IReadOnlyList<string>> GetHintsAsync(string turnText, Marshal.Abstractions.Execution.MarshalExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(names);
}

internal sealed class InMemorySessionToolNamespaces : Marshal.Abstractions.Sessions.ISessionToolNamespaces
{
    public Dictionary<Marshal.Abstractions.Sessions.SessionId, IReadOnlyList<string>> Saved { get; } = [];

    public Task<IReadOnlyList<string>> GetAsync(Marshal.Abstractions.Sessions.SessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.GetValueOrDefault(sessionId) ?? []);

    public Task SaveAsync(Marshal.Abstractions.Sessions.SessionId sessionId, IReadOnlyList<string> namespaces, CancellationToken cancellationToken)
    {
        Saved[sessionId] = namespaces;
        return Task.CompletedTask;
    }
}

internal sealed class FixedToolLatency : Marshal.Abstractions.Runs.IToolLatencyStats
{
    public Dictionary<ToolId, TimeSpan?> Expected { get; } = [];

    public Task<TimeSpan?> ExpectedDurationAsync(ToolId tool, CancellationToken cancellationToken) =>
        Task.FromResult(Expected.GetValueOrDefault(tool));
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

internal sealed class RecordingHandoff : IBackgroundHandoff
{
    public bool Accepts { get; set; }

    public List<BackgroundHandoff> HandOffs { get; } = [];

    public Task<Marshal.Abstractions.Result<Marshal.Abstractions.Tasks.TaskId>> HandOffAsync(BackgroundHandoff handoff, CancellationToken cancellationToken)
    {
        if (!Accepts)
            return Task.FromResult<Marshal.Abstractions.Result<Marshal.Abstractions.Tasks.TaskId>>(new Marshal.Abstractions.Error("tasks.paused", "Background work is paused.", Marshal.Abstractions.ErrorType.Conflict));

        HandOffs.Add(handoff);
        return Task.FromResult<Marshal.Abstractions.Result<Marshal.Abstractions.Tasks.TaskId>>(Marshal.Abstractions.Tasks.TaskId.New());
    }
}

internal sealed class CountingHistory : Marshal.Abstractions.Sessions.IConversationHistory
{
    public int Loads { get; private set; }

    public Task<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>> LoadAsync(Marshal.Abstractions.Sessions.SessionId sessionId, int maxTurns, DateTimeOffset? after, CancellationToken cancellationToken)
    {
        Loads++;
        return Task.FromResult<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>>([]);
    }
}
