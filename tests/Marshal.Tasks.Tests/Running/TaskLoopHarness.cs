using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Context;
using Marshal.Core.Execution;
using Marshal.Core.Guard;
using Marshal.Core.Models;
using Marshal.Core.Tools;
using Marshal.Tasks.Running;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Marshal.Tasks.Tests.Running;

/// <summary>
/// A real agent loop, guard and visibility filter around a scripted fake model, for task runner tests.
/// </summary>
internal sealed class TaskLoopHarness
{
    public const string KitchenSatellite = "assist_satellite.kitchen";

    private TaskLoopHarness()
    {
    }

    public FakeChatClient Model { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));

    public InMemoryRunRecorder Recorder { get; } = new();

    public InMemoryPendingActionStore PendingActions { get; private set; } = null!;

    /// <summary>Background confirmations the loop queued.</summary>
    public RecordingApprovals Approvals { get; private set; } = null!;

    public FakeTemplateCatalog Templates { get; } = new();

    public AsyncLocalExecutionContextAccessor Accessor { get; } = new();

    public ActiveTaskRuns ActiveRuns { get; } = new();

    public AgentTaskRunner Runner { get; private set; } = null!;

    /// <summary>The agent loop behind <see cref="Runner"/>, for interactive turns.</summary>
    public AgentRunner Agent { get; private set; } = null!;

    /// <summary>The context of the last run, as the agent loop saw it.</summary>
    public MarshalExecutionContext LastContext => Recorder.Runs[^1].Start.Context;

    public static async Task<TaskLoopHarness> CreateAsync(SatelliteProfile? kitchen = null, params ITool[] tools)
    {
        var harness = new TaskLoopHarness();
        harness.PendingActions = new InMemoryPendingActionStore(harness.Time);
        harness.Approvals = new RecordingApprovals(harness.PendingActions);
        var registry = new ToolRegistry([new FakeToolSource("test", true, tools)]);
        await registry.RefreshAsync(TestContext.Current.CancellationToken);
        var engine = new RuleBasedRiskEngine(new FixedMonitor<RiskRulesFile>(Rules("risk: []")), []);
        var satellites = new KitchenSatellites(kitchen ?? SatelliteProfile.Default);
        var visibility = new ToolVisibilityFilter(registry, engine, satellites, harness.Templates);
        var agent = new AgentRunner(
            new OneModelRouter(harness.Model),
            registry,
            harness.Templates,
            new ContextBuilder([new BasePromptContributor(), new OutputModeContributor(), new TaskPromptContributor()]),
            visibility,
            new ToolCallGuard(registry, visibility, engine, satellites),
            harness.PendingActions,
            harness.Recorder,
            new PriceTableCostCalculator(Options.Create(new ModelRoutingOptions())),
            harness.Accessor,
            Options.Create(new AgentOptions()),
            Options.Create(new ConfirmationOptions()),
            harness.Time,
            NullLogger<AgentRunner>.Instance,
            harness.Approvals);
        harness.Agent = agent;
        harness.Runner = new AgentTaskRunner(agent, new Profiles(), harness.ActiveRuns);
        return harness;
    }

    private static RiskRulesFile Rules(string yaml) =>
        new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build().Deserialize<RiskRulesFile>(yaml);

    /// <summary>Freezes queued approvals in the in-memory store and records them.</summary>
    internal sealed class RecordingApprovals(InMemoryPendingActionStore store) : IApprovalQueue
    {
        public List<PendingActionRecord> Queued { get; } = [];

        public async Task<PendingActionRecord> EnqueueAsync(FreezeRequest request, CancellationToken cancellationToken)
        {
            PendingActionRecord frozen = await store.FreezeAsync(request, cancellationToken);
            Queued.Add(frozen);
            return frozen;
        }

        public Task<IReadOnlyList<PendingActionRecord>> ListPendingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<PendingActionRecord>> DecideAsync(PendingActionId id, bool approve, DecidedBy decidedBy, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class KitchenSatellites(SatelliteProfile kitchen) : ISatelliteContextProvider
    {
        public SatelliteProfile GetProfile(string? id) => id == KitchenSatellite ? kitchen : SatelliteProfile.Default;

        public Task<SatelliteContext> GetContextAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new SatelliteContext(context.SatelliteId, GetProfile(context.SatelliteId), context.AreaId, null));
    }

    private sealed class OneModelRouter(IChatClient client) : IModelRouter
    {
        public Result<RoutedModel> Route(ModelRequest request) =>
            request.Tier == ModelTier.Advisor ? new Error("model.not_available", "no advisor") : new RoutedModel("fake", request.Tier, client);
    }

    private sealed class Profiles : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Alice", "de", "Europe/Vienna"));
    }
}
