using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Data.Conversations;
using Marshal.Data.Tasks;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class EfConversationReaderTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_NewestFirstPagedByBefore_TaskSessionsExcluded()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId web = await SessionAsync(database, OwnerA, SessionSource.Web);
        await TurnAsync(database, web, Text(OwnerA), "web question", "web answer");
        _time.Advance(TimeSpan.FromHours(1));
        SessionId task = await SessionAsync(database, OwnerA, SessionSource.Task);
        await TurnAsync(database, task, Text(OwnerA) with { Source = SessionSource.Task }, "task input", "task output");
        _time.Advance(TimeSpan.FromHours(1));
        SessionId voice = await SessionAsync(database, OwnerA, SessionSource.Voice);
        await TurnAsync(database, voice, Voice(OwnerA), "lights on", "Done.");
        EfConversationReader reader = Reader(database, OwnerA);

        IReadOnlyList<ConversationSummary> all = await reader.ListAsync(null, 50, Ct);
        IReadOnlyList<ConversationSummary> first = await reader.ListAsync(null, 1, Ct);
        IReadOnlyList<ConversationSummary> next = await reader.ListAsync(first[0].LastTurnAt, 1, Ct);

        Assert.Equal([voice, web], all.Select(conversation => conversation.Id));
        Assert.Equal([voice], first.Select(conversation => conversation.Id));
        Assert.Equal([web], next.Select(conversation => conversation.Id));
    }

    [Fact]
    public async Task List_OtherOwnersSessions_NotReturned()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId others = await SessionAsync(database, OwnerB, SessionSource.Web);
        await TurnAsync(database, others, Text(OwnerB), "secret", "reply");

        Assert.Empty(await Reader(database, OwnerA).ListAsync(null, 50, Ct));
        Assert.Null(await Reader(database, OwnerA).GetAsync(others, Ct));
    }

    [Fact]
    public async Task List_TitleTurnsCanContinueAndVoiceOrigin()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId web = await SessionAsync(database, OwnerA, SessionSource.Web);
        string longInput = "  " + new string('x', 100) + "  ";
        await TurnAsync(database, web, Text(OwnerA), longInput, "a1");
        await TurnAsync(database, web, Text(OwnerA), "second", "a2");
        SessionId closed = await SessionAsync(database, OwnerA, SessionSource.Web, closed: true);
        await TurnAsync(database, closed, Text(OwnerA), "old", "a");
        SessionId voice = await SessionAsync(database, OwnerA, SessionSource.Voice);
        await TurnAsync(database, voice, Voice(OwnerA, satelliteId: "assist_satellite.kitchen", areaId: "kitchen"), "timer", "Set.");
        SessionId api = await SessionAsync(database, OwnerA, SessionSource.Api);
        await TurnAsync(database, api, Text(OwnerA), "ping", "pong");

        var list = (await Reader(database, OwnerA).ListAsync(null, 50, Ct)).ToDictionary(conversation => conversation.Id);

        Assert.Equal(new string('x', 80), list[web].Title);
        Assert.Equal(2, list[web].Turns);
        Assert.True(list[web].CanContinue);
        Assert.False(list[closed].CanContinue);
        Assert.False(list[voice].CanContinue);
        Assert.False(list[api].CanContinue);
        Assert.Equal(("assist_satellite.kitchen", "kitchen"), (list[voice].SatelliteId, list[voice].AreaId));
        Assert.Equal(SessionSource.Api, list[api].Source);
    }

    [Fact]
    public async Task List_SummarizerRunsAreNotTurns()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId web = await SessionAsync(database, OwnerA, SessionSource.Web);
        await TurnAsync(database, web, Text(OwnerA), "hello", "hi");
        await TurnAsync(database, web, Text(OwnerA) with { Source = SessionSource.Task }, null, "{\"candidates\":[]}", trigger: "memory_summarizer");

        ConversationSummary conversation = Assert.Single(await Reader(database, OwnerA).ListAsync(null, 50, Ct));
        ConversationDetail? detail = await Reader(database, OwnerA).GetAsync(web, Ct);

        Assert.Equal(1, conversation.Turns);
        Assert.Single(detail!.Entries);
    }

    [Fact]
    public async Task Get_MixedInputModesArtifactsOutcomeAndBackgroundResult()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId session = await SessionAsync(database, OwnerA, SessionSource.Web);
        var artifact = new ArtifactRef(ArtifactId.New(), "snapshot.jpg", ArtifactKind.Image, "/api/v1/artifacts/x/content");
        RunId voice = await TurnAsync(database, session, Voice(OwnerA, satelliteId: "assist_satellite.kitchen"), "show the printer", "Here it is.", artifacts: [artifact]);
        RunId failed = await TurnAsync(database, session, Text(OwnerA), "and now?", null, outcome: RunOutcome.Error);
        string longReply = new('r', 9000);
        RunId backgrounded = await TurnAsync(database, session, Text(OwnerA), "check all printers", longReply, outcome: RunOutcome.Backgrounded, continueConversation: true);
        TaskId task = await ResultAsync(database, OwnerA, session, "The printer finished at 14:02");
        await ResultAsync(database, OwnerA, null, "Unrelated task");

        ConversationDetail? detail = await Reader(database, OwnerA).GetAsync(session, Ct);

        Assert.NotNull(detail);
        Assert.Equal(4, detail.Entries.Count);
        TurnEntry first = Assert.IsType<TurnEntry>(detail.Entries[0]);
        Assert.Equal((voice, InputMode.Voice, "assist_satellite.kitchen", "show the printer", "Here it is."), (first.RunId, first.InputMode, first.SatelliteId, first.Input, first.Reply));
        Assert.Equal([artifact], first.Artifacts);
        TurnEntry second = Assert.IsType<TurnEntry>(detail.Entries[1]);
        Assert.Equal((failed, InputMode.Text, (string?)null, RunOutcome.Error), (second.RunId, second.InputMode, second.Reply, second.Outcome));
        TurnEntry third = Assert.IsType<TurnEntry>(detail.Entries[2]);
        Assert.Equal((backgrounded, longReply, (bool?)true, RunOutcome.Backgrounded), (third.RunId, third.Reply, third.ContinueConversation, third.Outcome));
        BackgroundResultEntry result = Assert.IsType<BackgroundResultEntry>(detail.Entries[3]);
        Assert.Equal((task, "The printer finished at 14:02"), (result.TaskId, result.Body));
        Assert.Equal(3, detail.Conversation.Turns);
    }

    [Fact]
    public async Task History_BackgroundResultIsAssistantMessageInOrder()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId session = await SessionAsync(database, OwnerA, SessionSource.Web);
        await TurnAsync(database, session, Text(OwnerA), "check the printer", "Working on it.", outcome: RunOutcome.Backgrounded);
        await ResultAsync(database, OwnerA, session, "The printer finished at 14:02");
        await TurnAsync(database, session, Text(OwnerA), "how long did it take?", "Two hours.");

        IReadOnlyList<ChatMessage> history = await new EfConversationHistory(database.Factory).LoadAsync(session, 10, null, Ct);

        Assert.Equal(
            [(ChatRole.User, "check the printer"), (ChatRole.Assistant, "Working on it."), (ChatRole.Assistant, "The printer finished at 14:02"), (ChatRole.User, "how long did it take?"), (ChatRole.Assistant, "Two hours.")],
            history.Select(message => (message.Role, message.Text)));
    }

    [Fact]
    public async Task History_AfterWatermark_ReturnsOnlyNewerEntries()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId session = await SessionAsync(database, OwnerA, SessionSource.Web);
        await TurnAsync(database, session, Text(OwnerA), "q1", "a1");
        DateTimeOffset watermark = _time.GetUtcNow();
        await TurnAsync(database, session, Text(OwnerA), "q2", "a2");
        await ResultAsync(database, OwnerA, session, "result");

        IReadOnlyList<ChatMessage> history = await new EfConversationHistory(database.Factory).LoadAsync(session, 10, watermark, Ct);

        Assert.Equal(["q2", "a2", "result"], history.Select(message => message.Text));
    }

    private EfConversationReader Reader(TestDatabase database, OwnerId owner)
    {
        database.Accessor.Current = Text(owner);
        return new EfConversationReader(database.Factory);
    }

    private async Task<SessionId> SessionAsync(TestDatabase database, OwnerId owner, SessionSource source, bool closed = false)
    {
        await using MarshalDbContext context = database.CreateContext(owner);
        var id = SessionId.New();
        context.Sessions.Add(new Session
        {
            Id = id,
            OwnerId = owner,
            Source = source,
            IdentityEvidence = "{}",
            ResolvedBy = "cookie",
            CreatedAt = _time.GetUtcNow(),
            LastTurnAt = _time.GetUtcNow(),
            ClosedAt = closed ? _time.GetUtcNow() : null,
        });
        await context.SaveChangesAsync(Ct);
        return id;
    }

    private async Task<RunId> TurnAsync(
        TestDatabase database, SessionId session, MarshalExecutionContext context, string? input, string? reply,
        RunOutcome outcome = RunOutcome.Ok, bool? continueConversation = null, IReadOnlyList<ArtifactRef>? artifacts = null, string trigger = "turn")
    {
        _time.Advance(TimeSpan.FromSeconds(10));
        database.Accessor.Current = context;
        var recorder = new DbRunRecorder(database.Factory, [], Options.Create(new RecordingOptions()), _time);
        RunId run = await recorder.StartRunAsync(new RunStart(context with { SessionId = session }, trigger), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Content = "system", Input = input }, Ct);
        if (reply is not null)
            await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Reply, Content = reply, ContinueConversation = continueConversation, Artifacts = artifacts }, Ct);
        await recorder.CompleteRunAsync(run, new RunCompletion(outcome, null, []), Ct);

        await using MarshalDbContext db = database.CreateContext(context.OwnerId);
        Session row = db.Sessions.Single(candidate => candidate.Id == session);
        row.LastTurnAt = _time.GetUtcNow();
        await db.SaveChangesAsync(Ct);
        return run;
    }

    private async Task<TaskId> ResultAsync(TestDatabase database, OwnerId owner, SessionId? origin, string body)
    {
        _time.Advance(TimeSpan.FromSeconds(10));
        await using MarshalDbContext context = database.CreateContext(owner);
        var id = TaskId.New();
        context.AgentTasks.Add(new AgentTask
        {
            Id = id,
            OwnerId = owner,
            CreatedBy = TaskCreator.Task,
            Kind = TaskKind.Agent,
            Goal = "continue",
            TriggerKind = TaskTriggerKind.Now,
            TriggerSpec = "",
            OriginSessionId = origin,
            Status = AgentTaskStatus.Done,
            ResultBody = body,
            ResultAt = _time.GetUtcNow(),
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
        });
        await context.SaveChangesAsync(Ct);
        return id;
    }
}
