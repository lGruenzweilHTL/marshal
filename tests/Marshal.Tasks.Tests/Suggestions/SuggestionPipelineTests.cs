using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Suggestions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Marshal.Data.Conversations;
using Marshal.Tasks.Suggestions;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Suggestions;

public sealed class SuggestionPipelineTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeChatClient _model = new();
    private readonly InMemoryRunRecorder _runs = new();
    private readonly FakeNotificationSender _sender = new();
    private readonly FakeUserPreferencesStore _preferences = new();
    private readonly FakeDetector _detector = new("window_rain");
    private readonly FakeTool _powerOff = FakeTool.Safe("ha.turn_off").Returns("Switched off.");
    private readonly FakeTool _confirm = FakeTool.Confirm("printer.cancel").Returns("Cancelled.");
    private readonly ListLogger<SuggestionResponder> _responderLog = new();
    private SuggestionOptions _options = new();
    private ToolRegistry _registry = null!;
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _registry = new ToolRegistry([new FakeToolSource("test", true, _powerOff, _confirm)]);
        await _registry.RefreshAsync(Ct);
        LearningOn(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task ToggleOff_DetectorNotEvaluated()
    {
        _preferences.Stored[OwnerA] = Preferences(learn: false);
        _detector.Returns(Candidate("bedroom"));

        int sent = await Pipeline().EvaluateAsync(_detector, null, Ct);

        Assert.Equal(0, sent);
        Assert.Empty(_detector.Owners);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task OnlyOwnersWithToggleOn()
    {
        _detector.Returns(Candidate("bedroom"));
        _model.ThenText("""{"text":"Close the bedroom window."}""");

        await Pipeline().EvaluateAsync(_detector, null, Ct);

        Assert.Equal([OwnerA], _detector.Owners);
    }

    [Fact]
    public async Task MutedDetector_NotEvaluated()
    {
        await AsAsync(OwnerA, store => store.MuteAsync("window_rain", _time.GetUtcNow().AddDays(7), Ct));
        _detector.Returns(Candidate("bedroom"));

        int sent = await Pipeline().EvaluateAsync(_detector, null, Ct);

        Assert.Equal(0, sent);
        Assert.Empty(_detector.Owners);
    }

    [Fact]
    public async Task DisabledDetector_NotEvaluated()
    {
        _options = new SuggestionOptions { Detectors = { ["window_rain"] = new SuggestionDetectorOptions { Enabled = false } } };
        _detector.Returns(Candidate("bedroom"));

        Assert.Equal(0, await Pipeline().EvaluateAsync(_detector, null, Ct));
        Assert.Empty(_detector.Owners);
    }

    [Fact]
    public async Task SameDedupeKey_WithinCooldown_Dropped_AfterCooldown_SentAgain()
    {
        _detector.Returns(Candidate("bedroom"));
        _model.ThenText("""{"text":"Close the bedroom window."}""").ThenText("""{"text":"Close it again."}""");

        Assert.Equal(1, await Pipeline().EvaluateAsync(_detector, null, Ct));
        _time.Advance(TimeSpan.FromHours(11));
        Assert.Equal(0, await Pipeline().EvaluateAsync(_detector, null, Ct));
        _time.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, await Pipeline().EvaluateAsync(_detector, null, Ct));
        Assert.Equal(2, _model.Requests.Count);
    }

    [Fact]
    public async Task Wording_FastTierWithoutTools_OwnersLanguage_FactsOnly()
    {
        _preferences.Stored[OwnerA] = Preferences(language: "de");
        _detector.Returns(Candidate("bedroom"));
        _model.ThenText("""{"text":"Schließ das Schlafzimmerfenster, es regnet bald."}""");

        await Pipeline().EvaluateAsync(_detector, null, Ct);

        FakeChatRequest request = Assert.Single(_model.Requests);
        Assert.Empty(request.ToolNames);
        Assert.Contains("German", request.SystemPrompt);
        Assert.Contains("two sentences", request.SystemPrompt);
        string input = request.Messages.Single(message => message.Role == ChatRole.User).Text;
        Assert.Contains("window: Bedroom window", input);
        Assert.Contains("probability: 80%", input);
        Assert.Equal("suggestion:window_rain", _runs.OnlyRun.Start.Trigger);
        SuggestionRecord stored = Assert.Single(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
        Assert.Equal("Schließ das Schlafzimmerfenster, es regnet bald.", stored.Text);
    }

    [Fact]
    public async Task InvalidWording_NothingSent()
    {
        _detector.Returns(Candidate("bedroom"));
        _model.ThenText("not json");

        Assert.Equal(0, await Pipeline().EvaluateAsync(_detector, null, Ct));
        Assert.Empty(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
    }

    [Fact]
    public async Task Exploring_InboxOnly_NoNotification()
    {
        _detector.Returns(Candidate("bedroom"));
        _model.ThenText("""{"text":"Close the bedroom window."}""");

        await Pipeline().EvaluateAsync(_detector, null, Ct);

        SuggestionRecord stored = Assert.Single(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
        Assert.Equal(SuggestionDelivery.Inbox, stored.Delivery);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task WantedDetector_PushNormal_AcceptDismissActions_NeverVoice()
    {
        await SeedAnswersAsync(OwnerA, "window_rain", accepted: 5, dismissed: 1);
        _detector.Returns(Candidate("bedroom"));
        _model.ThenText("""{"text":"Close the bedroom window."}""");

        await Pipeline().EvaluateAsync(_detector, null, Ct);

        NotificationRequest push = Assert.Single(_sender.Sent);
        Assert.Equal(NotificationPriority.Normal, push.Priority);
        Assert.Equal(DeliveryChannel.Push, push.Channel);
        Assert.Null(push.OverrideQuietHoursUntil);
        Assert.Equal("suggestion:window_rain", push.Source);
        SuggestionRecord stored = (await AsAsync(OwnerA, store => store.ListOpenAsync(Ct))).Single(record => record.DedupeKey == "bedroom");
        Assert.Equal(SuggestionDelivery.Push, stored.Delivery);
        Assert.Equal(
            [SuggestionPipeline.AcceptPrefix + stored.Nonce, SuggestionPipeline.DismissPrefix + stored.Nonce],
            push.Actions.Select(action => action.Id));
    }

    [Fact]
    public async Task UnwantedDetector_Dropped_ModelNotCalled()
    {
        await SeedAnswersAsync(OwnerA, "window_rain", accepted: 1, dismissed: 6);
        _detector.Returns(Candidate("bedroom"));

        Assert.Equal(0, await Pipeline().EvaluateAsync(_detector, null, Ct));
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task Expire_PastExpiryOrConditionGone_CountsUnchanged()
    {
        _detector.Returns(Candidate("bedroom"), Candidate("kitchen"));
        _model.ThenText("""{"text":"Bedroom."}""").ThenText("""{"text":"Kitchen."}""");
        await Pipeline().EvaluateAsync(_detector, null, Ct);

        _detector.NoLongerHolds.Add("kitchen");
        await Pipeline().ExpireAsync([_detector], Ct);
        Assert.Equal(["bedroom"], (await AsAsync(OwnerA, store => store.ListOpenAsync(Ct))).Select(record => record.DedupeKey));

        _time.Advance(TimeSpan.FromHours(6));
        await Pipeline().ExpireAsync([_detector], Ct);
        Assert.Empty(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
        SuggestionDetectorStats stats = await AsAsync(OwnerA, store => store.GetStatsAsync("window_rain", Ct));
        Assert.Equal((2, 0, 0), (stats.Sent, stats.Accepted, stats.Dismissed));
    }

    [Theory]
    [InlineData(-1, 0.3, 0.6, 6, "explore")]
    [InlineData(3, 0.7, 0.6, 6, "minAcceptance")]
    [InlineData(3, 0.3, 1.2, 6, "pushAcceptance")]
    [InlineData(3, 0.3, 0.6, 0, "expiry")]
    public void Options_Invalid_NameTheKey(int explore, double min, double push, int expiryHours, string key)
    {
        var options = new SuggestionOptions { Explore = explore, MinAcceptance = min, PushAcceptance = push, Expiry = TimeSpan.FromHours(expiryHours) };

        Assert.Contains(key, options.Validate());
    }

    [Fact]
    public void Options_DetectorId_AndDefaultsValid()
    {
        Assert.Null(new SuggestionOptions().Validate());
        Assert.Contains("Window-Rain", new SuggestionOptions { Detectors = { ["Window-Rain"] = new() } }.Validate());
        Assert.Contains("factor", new SuggestionOptions { Detectors = { ["energy_anomaly"] = new() { Factor = 1 } } }.Validate());
    }

    // 1.4: proposed actions

    [Fact]
    public async Task ProposedAction_FrozenWithSuggestionTtl_NotQueued()
    {
        _detector.Returns(Candidate("voron", PowerOff()));
        _model.ThenText("""{"text":"The print is done. Switch the Voron off?"}""");

        await Pipeline().EvaluateAsync(_detector, null, Ct);

        SuggestionRecord stored = Assert.Single(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
        PendingActionRecord frozen = (await AsAsync(OwnerA, store => Pending().GetAsync(stored.PendingActionId!.Value, Ct)))!;
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromHours(6), frozen.ExpiresAt);
        Assert.Equal(stored.ExpiresAt, frozen.ExpiresAt);
        Assert.False(frozen.Queued);
        Assert.Equal("Switch off switch.voron_power", stored.ActionSummary);
        Assert.Empty(await AsAsync(OwnerA, _ => Pending().ListQueuedAsync(Ct)));
    }

    [Fact]
    public async Task Accept_InInbox_RunsActionOnce_CountsAccepted()
    {
        SuggestionRecord stored = await SendWithActionAsync(PowerOff());

        Result<SuggestionAnswer> answer = await AsAsync(OwnerA, _ => Responder().AnswerAsync(stored.Id, accept: true, DecidedBy.Ui, Ct));
        Result<SuggestionAnswer> again = await AsAsync(OwnerA, _ => Responder().AnswerAsync(stored.Id, accept: true, DecidedBy.Ui, Ct));

        Assert.Equal("Switched off.", answer.Value.ActionResult);
        Assert.True(again.IsFailure);
        ToolInvocation invocation = Assert.Single(_powerOff.Invocations);
        Assert.Equal("switch.voron_power", invocation.Arguments.GetProperty("entity_id").GetString());
        Assert.Equal(OwnerA, invocation.Context.OwnerId);
        Assert.Equal(PendingActionStatus.Approved, (await AsAsync(OwnerA, _ => Pending().GetAsync(stored.PendingActionId!.Value, Ct)))!.Status);
        Assert.Equal(1, (await AsAsync(OwnerA, store => store.GetStatsAsync("print_done_away", Ct))).Accepted);
    }

    [Fact]
    public async Task ConfirmTierAction_ReasonInPush_AcceptCountsAsConfirmation()
    {
        await SeedAnswersAsync(OwnerA, "print_done_away", accepted: 5, dismissed: 0);
        SuggestionRecord stored = await SendWithActionAsync(new ProposedAction(new ToolId("printer", "cancel"), JsonSerializer.SerializeToElement(new { }), "Cancel the print"));

        NotificationRequest push = Assert.Single(_sender.Sent);
        Assert.Contains("This needs your confirmation: Cancel the print.", push.Text);
        Assert.True(await Responder().HandlePushActionAsync(SuggestionPipeline.AcceptPrefix + stored.Nonce, Ct));

        Assert.Single(_confirm.Invocations);
        Assert.Equal(PendingActionStatus.Approved, await DecisionAsync(stored));
    }

    [Fact]
    public async Task Dismiss_RunsNothing_CountsDismissed()
    {
        SuggestionRecord stored = await SendWithActionAsync(PowerOff());

        Result<SuggestionAnswer> answer = await AsAsync(OwnerA, _ => Responder().AnswerAsync(stored.Id, accept: false, DecidedBy.Ui, Ct));

        Assert.True(answer.IsSuccess);
        Assert.Null(answer.Value.ActionResult);
        Assert.Empty(_powerOff.Invocations);
        Assert.Equal(PendingActionStatus.Rejected, await DecisionAsync(stored));
        Assert.Equal(1, (await AsAsync(OwnerA, store => store.GetStatsAsync("print_done_away", Ct))).Dismissed);
    }

    [Fact]
    public async Task PushAccept_ValidNonce_RunsOnce_ResultSentLow()
    {
        SuggestionRecord stored = await SendWithActionAsync(PowerOff());

        Assert.True(await Responder().HandlePushActionAsync(SuggestionPipeline.AcceptPrefix + stored.Nonce, Ct));
        Assert.False(await Responder().HandlePushActionAsync(SuggestionPipeline.AcceptPrefix + stored.Nonce, Ct));

        Assert.Single(_powerOff.Invocations);
        NotificationRequest result = Assert.Single(_sender.Sent);
        Assert.Equal(NotificationPriority.Low, result.Priority);
        Assert.Contains("Switched off.", result.Text);
    }

    [Fact]
    public async Task PushAction_WrongNonce_IgnoredAndLogged()
    {
        SuggestionRecord stored = await SendWithActionAsync(PowerOff());

        Assert.False(await Responder().HandlePushActionAsync(SuggestionPipeline.AcceptPrefix + "wrong", Ct));
        Assert.False(await Responder().HandlePushActionAsync("MARSHAL_APPROVE_" + stored.Nonce, Ct));

        Assert.Empty(_powerOff.Invocations);
        Assert.Contains(_responderLog.Entries, entry => entry.Contains("unknown nonce", StringComparison.Ordinal));
        Assert.Single(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
    }

    [Fact]
    public async Task Accept_AfterExpiry_RunsNothing()
    {
        SuggestionRecord stored = await SendWithActionAsync(PowerOff());
        _time.Advance(TimeSpan.FromHours(7));

        Result<SuggestionAnswer> answer = await AsAsync(OwnerA, _ => Responder().AnswerAsync(stored.Id, accept: true, DecidedBy.Ui, Ct));

        Assert.True(answer.IsFailure);
        Assert.Empty(_powerOff.Invocations);
        SuggestionDetectorStats stats = await AsAsync(OwnerA, store => store.GetStatsAsync("print_done_away", Ct));
        Assert.Equal((0, 0), (stats.Accepted, stats.Dismissed));
    }

    [Fact]
    public async Task Accept_OtherOwnersSuggestion_NotFound()
    {
        SuggestionRecord stored = await SendWithActionAsync(PowerOff());

        Result<SuggestionAnswer> answer = await AsAsync(OwnerB, _ => Responder().AnswerAsync(stored.Id, accept: true, DecidedBy.Ui, Ct));

        Assert.Equal(ErrorType.NotFound, answer.Error.Type);
        Assert.Empty(_powerOff.Invocations);
    }

    private async Task<SuggestionRecord> SendWithActionAsync(ProposedAction action)
    {
        var detector = new FakeDetector("print_done_away");
        detector.Returns(Candidate("voron", action));
        _model.ThenText("""{"text":"The print is done."}""");
        await Pipeline().EvaluateAsync(detector, null, Ct);
        return Assert.Single(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
    }

    private async Task<PendingActionStatus> DecisionAsync(SuggestionRecord stored) =>
        (await AsAsync(OwnerA, _ => Pending().GetAsync(stored.PendingActionId!.Value, Ct)))!.Status;

    private static ProposedAction PowerOff() =>
        new(new ToolId("ha", "turn_off"), JsonSerializer.SerializeToElement(new { entity_id = "switch.voron_power" }), "Switch off switch.voron_power");

    private static SuggestionCandidate Candidate(string key, ProposedAction? action = null) =>
        new(key, new Dictionary<string, string> { ["window"] = "Bedroom window", ["probability"] = "80%" }, action);

    private void LearningOn(OwnerId owner) => _preferences.Stored[owner] = Preferences();

    private static UserPreferencesRecord Preferences(bool learn = true, string language = "en") =>
        new(language, "Europe/Vienna", [], NotificationTargets.None, null, learn);

    // Answers recorded directly in the store, as earlier suggestions would have.
    private async Task SeedAnswersAsync(OwnerId owner, string detector, int accepted, int dismissed)
    {
        for (int index = 0; index < accepted + dismissed; index++)
        {
            var record = new SuggestionRecord(
                Guid.CreateVersion7(), owner, detector, $"seed-{index}", "Earlier.", new Dictionary<string, string>(), SuggestionDelivery.Inbox,
                SuggestionStatus.Open, Guid.NewGuid().ToString("N"), null, null, _time.GetUtcNow().AddDays(-2), _time.GetUtcNow().AddDays(-2).AddHours(6));
            await AsAsync(owner, store => store.AddAsync(record, Ct));
            await AsAsync(owner, store => store.DecideAsync(record.Id, index < accepted ? SuggestionStatus.Accepted : SuggestionStatus.Dismissed, _time.GetUtcNow(), Ct));
        }
    }

    private EfSuggestionStore Store() => new(_database.Factory, _database.Accessor);

    private EfPendingActionStore Pending() => new(_database.Factory, _database.Accessor, _time);

    private SuggestionPipeline Pipeline() => new(
        new Directory(),
        _preferences,
        Store(),
        new BetaAcceptancePolicy(Options.Create(_options)),
        new ModelSuggestionWriter(new BackgroundModelCalls(new FastRouter(_model), _runs, new FixedCost(), new FixedCap(), _database.Accessor, _time)),
        _sender,
        Pending(),
        _registry,
        new DefaultTierRiskEngine(),
        _database.Accessor,
        Options.Create(_options),
        _time,
        NullLogger<SuggestionPipeline>.Instance);

    private SuggestionResponder Responder() => new(
        Store(), Pending(), _registry, new DefaultTierRiskEngine(), _sender, _database.Accessor, _time, _responderLog);

    private async Task<T> AsAsync<T>(OwnerId owner, Func<EfSuggestionStore, Task<T>> action)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        return await action(Store());
    }

    private async Task AsAsync(OwnerId owner, Func<EfSuggestionStore, Task> action)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        await action(Store());
    }

    private sealed class FakeDetector(string id) : ISuggestionDetector
    {
        private IReadOnlyList<SuggestionCandidate> _candidates = [];

        public string Id { get; } = id;

        public SuggestionTriggers Triggers { get; } = new([new SuggestionEventTrigger("ha", "state_changed")], TimeSpan.FromMinutes(15));

        public List<OwnerId> Owners { get; } = [];

        public HashSet<string> NoLongerHolds { get; } = [];

        public void Returns(params SuggestionCandidate[] candidates) => _candidates = candidates;

        public Task<IReadOnlyList<SuggestionCandidate>> EvaluateAsync(SuggestionContext context, CancellationToken cancellationToken)
        {
            Owners.Add(context.Owner);
            return Task.FromResult(_candidates);
        }

        public Task<bool> StillHoldsAsync(OwnerId owner, string dedupeKey, CancellationToken cancellationToken) =>
            Task.FromResult(!NoLongerHolds.Contains(dedupeKey));
    }

    private sealed class Directory : IUserDirectory
    {
        public Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnerId>>([OwnerA, OwnerB]);
    }

    private sealed class FastRouter(IChatClient client) : IModelRouter
    {
        public Result<RoutedModel> Route(ModelRequest request) =>
            request.Tier == ModelTier.Fast ? new RoutedModel("fast-model", ModelTier.Fast, client) : new Error("model.not_available", "No model.", ErrorType.Unavailable);
    }

    private sealed class FixedCost : ICostCalculator
    {
        public decimal Calculate(string modelKey, UsageDetails? usage) => 0.01m;
    }

    private sealed class FixedCap : IDailyCostCap
    {
        public Task<decimal> LeftTodayAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(1m);
    }

    private sealed class DefaultTierRiskEngine : IRiskEngine
    {
        public Task<RiskAssessment> AssessAsync(RegisteredTool tool, JsonElement arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new RiskAssessment(tool.Descriptor.DefaultTier, tool.Descriptor.DefaultTier == RiskTier.Confirm ? [ConfirmationReasonCode.Rule] : []));

        public RiskProfile GetProfile(RegisteredTool tool) => throw new NotSupportedException();
    }
}
