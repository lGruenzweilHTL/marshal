using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Configuration;
using Marshal.Core.Conversation;
using Marshal.Core.Guard;
using Marshal.Core.Templates;
using Marshal.Data;
using Marshal.Data.Conversations;
using Marshal.Data.Identity;
using Marshal.Data.Notifications;
using Marshal.Tasks.Deliveries;
using Marshal.Tasks.FollowUps;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Notifications;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Tasks.Tests.Running;
using Marshal.Tasks.Tools;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using YamlDotNet.RepresentationModel;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Briefing;

/// <summary>
/// The daily briefing is only configuration: the example task definition, created through <c>tasks.create</c>, runs
/// on its schedule with the example template, waits for the kitchen, is spoken there with a follow-up and falls back
/// to a push when nobody comes. Everything but the model and the house is real.
/// </summary>
public sealed class BriefingEndToEndTests : IAsyncLifetime
{
    private const string Kitchen = TaskLoopHarness.KitchenSatellite;
    private const string Occupancy = "binary_sensor.kitchen_occupancy";
    private const string Speech = "Guten Morgen! Um neun hast du den Zahnarzt, Anna wartet auf deine Antwort, und in den Feeds geht es um den Bahnstreik und die neue Klipper-Version.";
    private const string Details = "## Feeds\n1. Bahnstreik ab Donnerstag (ORF)\n2. Klipper 0.13 mit Input Shaping (Voron News)";

    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);
    private static readonly string _examples = Path.Combine(RepositoryRoot(), "deploy", "config.example");

    private readonly FakeConditions _house = new();
    private readonly FakeUserPreferencesStore _preferences = new();
    private readonly RecordingChannel _push = new(NotificationChannelKind.Push);
    private readonly RecordingChannel _announce = new(NotificationChannelKind.VoiceAnnounce);
    private readonly TaskSignals _signals = new();
    private TaskLoopHarness _harness = null!;
    private TestDatabase _database = null!;
    private ConversationChannel _conversation = null!;
    private DeliveryDispatcher _dispatcher = null!;
    private BackgroundTaskScheduler _scheduler = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _harness = await TaskLoopHarness.CreateAsync();
        _database = await TestDatabase.CreateFileAsync(_harness.Accessor);
        using var templates = new FileTemplateCatalog(Path.Combine(_examples, "templates"), null, "templates/*.md", new ConfigSecretScanner(), NullLogger<FileTemplateCatalog>.Instance);
        _harness.Templates.Items.Add(templates.Find("briefing") ?? throw new InvalidOperationException("Example briefing template missing."));
        _preferences.Stored[OwnerA] = new UserPreferencesRecord("de", "Europe/Vienna", [], new NotificationTargets("person.alice", ["notify.mobile_app_pixel"]), null);
        _house.States[StateCondition.Person] = "home";
        _house.States[Occupancy] = "off";

        _conversation = new ConversationChannel(new EfFollowUpStore(_database.Factory, _harness.Accessor, Options.Create(new NotificationOptions()), _harness.Time));
        var router = new NotificationRouter(
            _database.Factory,
            _harness.Accessor,
            _preferences,
            new InTheKitchen(),
            [_announce, _push, _conversation],
            new FakeLiveEventPublisher(),
            Options.Create(new NotificationOptions { CoalesceWindow = TimeSpan.Zero, PublicUrl = new Uri("https://marshal.example.com/") }),
            _harness.Time);
        _dispatcher = new DeliveryDispatcher(Store(), _house, router, _harness.Accessor, new ViennaProfiles(), _harness.Time, NullLogger<DeliveryDispatcher>.Instance);
        _scheduler = new BackgroundTaskScheduler(
            Store(), _signals, [_harness.Runner], _dispatcher, Budgets(), new TaskPauses(_database.Factory), _harness.Accessor, new ViennaProfiles(),
            Options.Create(new TaskOptions()), _harness.Time, NullLogger<BackgroundTaskScheduler>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.StopAsync(CancellationToken.None);
        _scheduler.Dispose();
        _signals.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task PreparedAtSix_SpokenWhenTheOwnerEntersTheKitchen_TellMeMoreRunsInTheSeededSession()
    {
        TaskRecord briefing = await CreateFromExampleAsync();
        Assert.Equal(("briefing", "briefing", TaskTrigger.Cron("0 6 * * *")), (briefing.Name, briefing.Template, briefing.Trigger));
        // 06:00 in Vienna is 04:00 UTC the next day.
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero), briefing.NextRunAt);

        await PrepareAtSixAsync();
        Assert.Empty(_conversation.Messages);
        Assert.Equal([(NotificationOutcome.Inboxed, Speech)], await HistoryAsync());

        // 07:30: the owner walks into the kitchen.
        _harness.Time.SetUtcNow(new DateTimeOffset(2026, 9, 30, 5, 30, 0, TimeSpan.Zero));
        _house.States[Occupancy] = "on";
        await _dispatcher.ReviewPendingAsync(Occupancy, Ct);

        (ChannelMessage spoken, FollowUpRecord listening) = Assert.Single(_conversation.Messages);
        Assert.Equal((Speech, (string?)Kitchen, (TaskId?)briefing.Id), (spoken.Text, spoken.Satellite, spoken.TaskId));
        Assert.Empty(_announce.Messages);
        Assert.Empty(_push.Messages);

        _harness.Model.ThenText("Klipper 0.13 bringt Input Shaping für alle Drucker.");
        MarshalExecutionContext context = await VoiceSessionAsync();
        AgentRunResult answer = await TellMeMoreAsync(listening, context);

        Assert.Equal("Klipper 0.13 bringt Input Shaping für alle Drucker.", answer.Reply);
        IReadOnlyList<ChatMessage> seen = _harness.Model.Requests[^1].Messages;
        Assert.Contains(seen, message => message.Role == ChatRole.System && message.Text.Contains("Klipper 0.13 mit Input Shaping", StringComparison.Ordinal));
        Assert.Contains(seen, message => message.Role == ChatRole.Assistant && message.Text == Speech);
        // The briefing itself ran once, at six; the reply did not run it again.
        Assert.Single(_harness.Recorder.Runs, run => run.Start.Context.Source == SessionSource.Task);
        Assert.Equal(2, _harness.Model.Requests.Count);
    }

    [Fact]
    public async Task NobodyInTheKitchenBy11_PushedInstead()
    {
        await CreateFromExampleAsync();
        await PrepareAtSixAsync();

        _harness.Time.SetUtcNow(new DateTimeOffset(2026, 9, 30, 8, 59, 0, TimeSpan.Zero));
        await _dispatcher.ReviewPendingAsync(null, Ct);
        Assert.Empty(_push.Messages);

        // 11:00 in Vienna.
        _harness.Time.SetUtcNow(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero));
        await _dispatcher.ReviewPendingAsync(null, Ct);

        ChannelMessage pushed = Assert.Single(_push.Messages);
        Assert.Equal(Speech, pushed.Text);
        Assert.Empty(_conversation.Messages);
        Assert.Empty(_announce.Messages);
        Assert.Equal([(NotificationOutcome.Inboxed, Speech), (NotificationOutcome.Fallback, Speech)], await HistoryAsync());
    }

    private async Task<TaskRecord> CreateFromExampleAsync()
    {
        JsonNode arguments = YamlToJson(Path.Combine(_examples, "tasks", "briefing.yaml"));
        MarshalExecutionContext context = Text(OwnerA, "de");
        ITool create = (await Tools().GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == "create");
        using (_harness.Accessor.Use(context))
        {
            Result<ToolOutput> created = await create.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), context), Ct);
            Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Message : null);
            return (await Store().FindAsync("briefing", Ct)).Value;
        }
    }

    private async Task PrepareAtSixAsync()
    {
        _harness.Model.ThenText($"{Speech}\n\n{Details}");
        await _scheduler.StartAsync(Ct);
        await WaitIdleAsync();
        _harness.Time.SetUtcNow(new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero));
        await WaitForAsync(async () => (await HistoryAsync()).Count > 0);
        await WaitIdleAsync();
    }

    private async Task<MarshalExecutionContext> VoiceSessionAsync()
    {
        MarshalExecutionContext voice = Voice(OwnerA, "de", Kitchen);
        using (_harness.Accessor.Use(voice))
        {
            SessionRecord session = await new EfSessionStore(_database.Factory, _harness.Accessor, _harness.Time)
                .CreateAsync(new NewSession(SessionSource.Voice, null, null, new IdentityEvidence(null, null, Kitchen, null), "test"), Ct);
            return voice with { SessionId = session.Id };
        }
    }

    // What the conversation service does with a reply turn: follow-up first, then the loop with its seed.
    private async Task<AgentRunResult> TellMeMoreAsync(FollowUpRecord listening, MarshalExecutionContext context)
    {
        var turn = new ConversationTurn
        {
            PrincipalOwner = OwnerA,
            Source = SessionSource.Voice,
            Text = "Erzähl mir mehr über den zweiten Artikel.",
            Evidence = new IdentityEvidence(null, null, Kitchen, null),
            InputMode = InputMode.Voice,
            FollowupRef = listening.Nonce,
        };
        using (_harness.Accessor.Use(context))
        {
            FollowUpReply? reply = await Replies().HandleAsync(turn, context, Ct);
            Assert.NotNull(reply);
            Assert.Null(reply.FixedReply);
            return await _harness.Agent.RunAsync(new AgentRunRequest { Context = context, History = reply.Seed, UserText = turn.Text }, Ct);
        }
    }

    private async Task<List<(NotificationOutcome Outcome, string Content)>> HistoryAsync()
    {
        using (_harness.Accessor.Use(Text(OwnerA)))
        {
            await using MarshalDbContext db = _database.CreateContext();
            List<Notification> rows = await db.Notifications.ToListAsync(Ct);
            return [.. rows.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Select(row => (row.Outcome, row.Content))];
        }
    }

    private async Task WaitIdleAsync()
    {
        await WaitForAsync(() => Task.FromResult(_scheduler.IsIdle));
        await Task.Delay(20, Ct);
        await WaitForAsync(() => Task.FromResult(_scheduler.IsIdle));
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow + _patience;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Condition not reached in time.");
            await Task.Delay(10, Ct);
        }
    }

    private EfTaskStore Store() => new(_database.Factory, _harness.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _harness.Time);

    private TaskBudgets Budgets() => new(Store(), new NoSpending(), new ViennaProfiles(), _harness.Templates, Options.Create(new TaskOptions()), _harness.Time);

    private TaskToolSource Tools() => new(
        Store(),
        _signals,
        new EfKillSwitch(_database.Factory, Store(), new EfUserPreferencesStore(_database.Factory, new FixedInstanceTimeZone()), _signals, new FakeAuditLog(), _harness.Accessor, _harness.Time),
        Budgets(),
        _harness.ActiveRuns,
        new ViennaProfiles(),
        Options.Create(new TaskOptions()),
        _harness.Time);

    private FollowUpReplies Replies() => new(
        new EfFollowUpStore(_database.Factory, _harness.Accessor, Options.Create(new NotificationOptions()), _harness.Time),
        new EfPendingActionStore(_database.Factory, _harness.Accessor, _harness.Time),
        _harness.Approvals,
        new NoKeywords(),
        new NeutralJudge(),
        Store(),
        _signals,
        Options.Create(new NotificationOptions()),
        _harness.Time);

    // The example is written for a person; the tool takes JSON. Plain true/false and numbers keep their type.
    private static JsonNode YamlToJson(string path)
    {
        var yaml = new YamlStream();
        using (var reader = new StreamReader(path))
            yaml.Load(reader);
        return Convert(yaml.Documents[0].RootNode) ?? throw new InvalidOperationException("Empty example.");

        static JsonNode? Convert(YamlNode node) => node switch
        {
            YamlMappingNode mapping => new JsonObject(mapping.Children.Select(entry => KeyValuePair.Create(((YamlScalarNode)entry.Key).Value!, Convert(entry.Value)))),
            YamlSequenceNode sequence => new JsonArray([.. sequence.Children.Select(Convert)]),
            YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain, Value: "true" or "false" } scalar => JsonValue.Create(scalar.Value == "true"),
            YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } scalar when long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) => JsonValue.Create(number),
            YamlScalarNode scalar => JsonValue.Create(scalar.Value),
            _ => throw new InvalidOperationException($"Unsupported YAML node {node.NodeType}."),
        };
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marshal.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>The house: entity states the <c>when</c> conditions look at.</summary>
    private sealed class FakeConditions : IConditionEvaluator
    {
        public ConcurrentDictionary<string, string> States { get; } = new();

        public Task<bool> AllHoldAsync(IReadOnlyList<StateCondition> conditions, OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult(conditions.All(condition => condition.HoldsFor(States.GetValueOrDefault(condition.Entity))));
    }

    private sealed class InTheKitchen : IPresenceProvider
    {
        public Task<Presence> GetAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(new Presence(PresenceState.Home, Kitchen));
    }

    /// <summary>What the HA conversation channel does, without HA: stores the follow-up the satellite listens for.</summary>
    private sealed class ConversationChannel(IFollowUpStore followUps) : INotificationChannel
    {
        public ConcurrentQueue<(ChannelMessage Message, FollowUpRecord FollowUp)> Messages { get; } = new();

        public NotificationChannelKind Kind => NotificationChannelKind.VoiceConversation;

        public async Task<Result> DeliverAsync(ChannelMessage message, CancellationToken cancellationToken)
        {
            FollowUpRecord followUp = await followUps.CreateAsync(new NewFollowUp(message.Satellite!, message.TaskId, message.PendingActionId), cancellationToken);
            Messages.Enqueue((message, followUp));
            return Result.Success();
        }
    }

    private sealed class RecordingChannel(NotificationChannelKind kind) : INotificationChannel
    {
        public ConcurrentQueue<ChannelMessage> Messages { get; } = new();

        public NotificationChannelKind Kind => kind;

        public Task<Result> DeliverAsync(ChannelMessage message, CancellationToken cancellationToken)
        {
            Messages.Enqueue(message);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class NoSpending : ITaskSpending
    {
        public Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0m);
    }

    private sealed class NoKeywords : IConfirmationKeywords
    {
        public bool IsConfirmation(string utterance, string language) => false;
    }

    private sealed class NeutralJudge : IConfirmationJudge
    {
        public Task<JudgeVerdict> JudgeAsync(string actionSummary, string userReply, string language, CancellationToken cancellationToken) =>
            Task.FromResult(JudgeVerdict.Neutral);
    }
}
