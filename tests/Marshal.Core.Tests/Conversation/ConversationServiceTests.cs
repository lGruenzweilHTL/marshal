using System.Threading.Channels;
using Marshal.Abstractions;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Core.Agent;
using Marshal.Core.Conversation;
using Marshal.Core.Guard;
using Marshal.Core.Sessions;
using Marshal.Core.Tests.Agent;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Core.Tests.Conversation;

public sealed class ConversationServiceTests
{
    private readonly ApiTokenId _token = ApiTokenId.New();
    private readonly FakeTool _unlock = FakeTool.Confirm("test.unlock").Returns("unlocked");
    private readonly FakeJudge _judge = new();

    [Fact]
    public async Task Turn_EchoesClientConversationId()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenText("Hi.");

        TurnOutcome outcome = (await service.HandleAsync(Turn("hello", "01JABC"), null, Ct)).Value;

        Assert.Equal(new TurnOutcome("Hi.", false, "01JABC"), outcome);
    }

    [Fact]
    public async Task Turn_ConfirmTier_ContinueConversationTrue()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenToolCall("test_unlock").ThenText("Unlock the door?");

        TurnOutcome outcome = (await service.HandleAsync(Turn("unlock"), null, Ct)).Value;

        Assert.True(outcome.ContinueConversation);
        Assert.Equal("Unlock the door?", outcome.Speech);
    }

    [Fact]
    public async Task Turn_KeywordConfirmation_ExecutesFrozenActionAndModelPhrasesReply()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenToolCall("test_unlock", new { door = "front" }).ThenText("Unlock the front door?").ThenText("The front door is unlocked.");
        await service.HandleAsync(Turn("unlock the front door"), null, Ct);

        TurnOutcome outcome = (await service.HandleAsync(Turn("Confirm!"), null, Ct)).Value;

        Assert.Equal("The front door is unlocked.", outcome.Speech);
        Assert.False(outcome.ContinueConversation);
        Assert.Equal("""{"door":"front"}""", Assert.Single(_unlock.Invocations).Arguments.GetRawText());
        Assert.Equal(0, _judge.Calls);
        RecordedRun confirmationRun = harness.Recorder.Runs[1];
        Assert.Equal("confirmation", confirmationRun.Start.Trigger);
        Assert.Contains(confirmationRun.Steps, step => step.GuardDecision == GuardDecision.ConfirmedByKeyword);
        FunctionResultContent result = harness.Model.Requests[2].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Last();
        Assert.Equal("unlocked", result.Result);
    }

    [Fact]
    public async Task Turn_JudgeNegative_FixedCancelledReplyWithoutModelCall()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenToolCall("test_unlock").ThenText("Unlock?");
        await service.HandleAsync(Turn("unlock"), null, Ct);
        _judge.Verdict = JudgeVerdict.Negative;

        TurnOutcome outcome = (await service.HandleAsync(Turn("no, leave it"), null, Ct)).Value;

        Assert.Equal(FixedReplies.Cancelled("en"), outcome.Speech);
        Assert.Equal(2, harness.Model.Requests.Count);
        Assert.Empty(_unlock.Invocations);
        Assert.Contains(harness.Recorder.Runs[1].Steps, step => step.GuardDecision == GuardDecision.JudgeNegative);
    }

    [Fact]
    public async Task Turn_JudgeNeutral_DropsAndHandlesAsNormalTurn()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenToolCall("test_unlock").ThenText("Unlock?").ThenText("It is sunny.");
        await service.HandleAsync(Turn("unlock"), null, Ct);
        _judge.Verdict = JudgeVerdict.Neutral;

        TurnOutcome outcome = (await service.HandleAsync(Turn("what's the weather?"), null, Ct)).Value;

        Assert.Equal("It is sunny.", outcome.Speech);
        Assert.Empty(_unlock.Invocations);
        Assert.Equal(PendingActionStatus.Dropped, harness.Store.Decisions.Values.Single().Status);
        Assert.Equal("what's the weather?", harness.Model.Requests[2].Messages[^1].Text);
    }

    [Fact]
    public async Task Turn_ConfirmAfterExpiry_ExpiredReply()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenToolCall("test_unlock").ThenText("Unlock?");
        await service.HandleAsync(Turn("unlock"), null, Ct);
        harness.Time.Advance(TimeSpan.FromSeconds(61));

        TurnOutcome outcome = (await service.HandleAsync(Turn("confirm"), null, Ct)).Value;

        Assert.Equal(FixedReplies.Expired("en"), outcome.Speech);
        Assert.Empty(_unlock.Invocations);
    }

    [Fact]
    public async Task Turn_ToolOutputContainingKeyword_NeverConfirms()
    {
        FakeTool mail = FakeTool.Untrusted().Returns("confirm");
        (ConversationService service, LoopHarness harness) = await ServiceAsync(mail);
        harness.Model
            .ThenToolCalls(("mail_read", null, "m"), ("test_unlock", null, "u"))
            .ThenText("confirm");

        TurnOutcome outcome = (await service.HandleAsync(Turn("read mail and unlock"), null, Ct)).Value;

        Assert.True(outcome.ContinueConversation);
        Assert.Empty(harness.Store.Decisions);
        Assert.Empty(_unlock.Invocations);
    }

    [Fact]
    public async Task Turn_UnknownFollowupRef_Ignored()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenText("ok");

        Result<TurnOutcome> outcome = await service.HandleAsync(Turn("hi") with { FollowupRef = "unknown-ref" }, null, Ct);

        Assert.Equal("ok", outcome.Value.Speech);
    }

    [Fact]
    public async Task Turn_WithoutLanguage_UsesOwnerPreference()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenText("Hallo.");

        await service.HandleAsync(Turn("hallo") with { Language = null }, null, Ct);

        Assert.Equal("de", harness.Recorder.OnlyRun.Start.Context.Language);
    }

    [Fact]
    public async Task Turn_VoiceGetsVoiceCap_TextGetsTextCap()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenText("a").ThenText("b");

        await service.HandleAsync(Turn("x") with { InputMode = InputMode.Voice }, null, Ct);
        await service.HandleAsync(Turn("y", "other"), null, Ct);

        Assert.Equal([TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(60)], harness.RunnerSpy.LatencyCaps);
    }

    [Fact]
    public async Task Turn_HistoryOfSessionPassed()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenText("first answer").ThenText("second answer");

        await service.HandleAsync(Turn("first"), null, Ct);
        await service.HandleAsync(Turn("second"), null, Ct);

        Assert.Equal(2, harness.History.Loads);
    }

    [Fact]
    public async Task Turn_StreamsDeltas()
    {
        (ConversationService service, LoopHarness harness) = await ServiceAsync();
        harness.Model.ThenText("streamed");
        var channel = Channel.CreateUnbounded<TurnEvent>();

        await service.HandleAsync(Turn("x"), channel.Writer, Ct);

        channel.Writer.Complete();
        List<TurnEvent> events = [];
        await foreach (TurnEvent turnEvent in channel.Reader.ReadAllAsync(Ct))
            events.Add(turnEvent);
        Assert.Equal("streamed", string.Concat(events.OfType<DeltaEvent>().Select(delta => delta.Text)));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ConversationTurn Turn(string text, string conversationId = "01JABC") => new()
    {
        PrincipalOwner = OwnerA,
        ApiTokenId = _token,
        Source = SessionSource.Voice,
        ConversationId = conversationId,
        Text = text,
        Language = "en",
        Evidence = new IdentityEvidence(null, "device", "assist_satellite.kitchen", "kitchen"),
    };

    private async Task<(ConversationService Service, LoopHarness Harness)> ServiceAsync(params Abstractions.Tools.ITool[] extra)
    {
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [_unlock, .. extra]);
        var sessions = new InMemorySessionStore(harness.Accessor, harness.Time);
        var resolver = new SessionResolver(sessions, new FixedIdentity(), harness.Store, harness.Accessor, Options.Create(new SessionOptions()), harness.Time);
        var flow = new ConfirmationFlow(harness.Store, new ConfiguredConfirmationKeywords(Options.Create(new ConfirmationOptions())), _judge, harness.Registry, Tests.GuardTestKit.Engine("risk: []"), harness.Time);
        var service = new ConversationService(resolver, flow, harness.RunnerSpy, harness.History, new FixedProfiles(), harness.Accessor, Options.Create(harness.Options));
        return (service, harness);
    }

    private sealed class FixedIdentity : IIdentityResolver
    {
        public Task<IdentityResolution> ResolveAsync(IdentityEvidence evidence, CancellationToken cancellationToken) =>
            Task.FromResult(new IdentityResolution(OwnerA, "owner_default"));
    }

    private sealed class FixedProfiles : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Lukas", "de", "Europe/Vienna"));
    }

    private sealed class FakeJudge : IConfirmationJudge
    {
        public JudgeVerdict Verdict { get; set; } = JudgeVerdict.Failed;

        public int Calls { get; private set; }

        public Task<JudgeVerdict> JudgeAsync(string actionSummary, string userReply, string language, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Verdict);
        }
    }
}
