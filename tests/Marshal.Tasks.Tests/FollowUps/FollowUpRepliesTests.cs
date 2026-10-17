using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Conversation;
using Marshal.Core.Guard;
using Marshal.Data.Conversations;
using Marshal.Tasks.FollowUps;
using Marshal.Tasks.Notifications;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.FollowUps;

public sealed class FollowUpRepliesTests : IAsyncLifetime
{
    private const string Office = "assist_satellite.office";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingApprovals _approvals = new();
    private readonly FixedJudge _judge = new();
    private readonly WakeCounter _scheduler = new();
    private readonly NotificationOptions _options = new();
    private TestDatabase _database = null!;
    private SessionId _session;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Voice(OwnerA, "de", Office);
        _session = (await new EfSessionStore(_database.Factory, _database.Accessor, _time)
            .CreateAsync(new NewSession(SessionSource.Voice, null, null, new IdentityEvidence(null, null, Office, null), "test"), Ct)).Id;
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task PendingApproval_Keyword_ApprovedThroughTheQueue()
    {
        PendingActionRecord approval = await ApprovalAsync();
        FollowUpRecord asked = await AskAsync(new NewFollowUp(Office, approval.TaskId, approval.Id));

        FollowUpReply? reply = await Replies().HandleAsync(Turn("bestätigt", asked.Nonce), Context(), Ct);

        Assert.Equal(FixedReplies.ApprovalGiven("de"), reply?.FixedReply);
        Assert.Equal((approval.Id, true, DecidedBy.Keyword), Assert.Single(_approvals.Decisions));
        Assert.Equal(0, _judge.Calls);
    }

    [Fact]
    public async Task PendingApproval_JudgedNegative_Rejected()
    {
        PendingActionRecord approval = await ApprovalAsync();
        FollowUpRecord asked = await AskAsync(new NewFollowUp(Office, approval.TaskId, approval.Id));
        _judge.Verdict = JudgeVerdict.Negative;

        FollowUpReply? reply = await Replies().HandleAsync(Turn("lieber nicht", asked.Nonce), Context(), Ct);

        Assert.Equal(FixedReplies.Cancelled("de"), reply?.FixedReply);
        Assert.Equal((approval.Id, false, DecidedBy.Judge), Assert.Single(_approvals.Decisions));
    }

    [Fact]
    public async Task PendingApproval_NeutralReply_NormalTurnAndStillPending()
    {
        PendingActionRecord approval = await ApprovalAsync();
        FollowUpRecord asked = await AskAsync(new NewFollowUp(Office, approval.TaskId, approval.Id));
        _judge.Verdict = JudgeVerdict.Neutral;

        FollowUpReply? reply = await Replies().HandleAsync(Turn("wie spät ist es", asked.Nonce), Context(), Ct);

        Assert.Null(reply);
        Assert.Empty(_approvals.Decisions);
    }

    [Fact]
    public async Task PendingApproval_Refused_SaysItIsClosed()
    {
        PendingActionRecord approval = await ApprovalAsync();
        FollowUpRecord asked = await AskAsync(new NewFollowUp(Office, approval.TaskId, approval.Id));
        _approvals.Refusal = new Error("approval.expired", "expired", ErrorType.Conflict);

        FollowUpReply? reply = await Replies().HandleAsync(Turn("bestätigt", asked.Nonce), Context(), Ct);

        Assert.Equal(FixedReplies.Expired("de"), reply?.FixedReply);
    }

    [Fact]
    public async Task WaitingTask_ReplyBecomesThePayloadAndTheTaskResumes()
    {
        TaskRecord task = await TaskAsync(AgentTaskStatus.Waiting);
        FollowUpRecord asked = await AskAsync(new NewFollowUp(Office, task.Id));

        FollowUpReply? reply = await Replies().HandleAsync(Turn("die blaue bitte", asked.Nonce), Context(), Ct);

        Assert.Equal(FixedReplies.ReplyTaken("de"), reply?.FixedReply);
        TaskRecord resumed = (await Tasks().GetAsync(task.Id, Ct)).Value;
        Assert.Equal((AgentTaskStatus.Waiting, (DateTimeOffset?)_time.GetUtcNow()), (resumed.Status, resumed.NextRunAt));
        using var payload = JsonDocument.Parse(resumed.TriggerPayload!);
        Assert.Equal("die blaue bitte", payload.RootElement.GetProperty("reply").GetProperty("text").GetString());
        Assert.Equal(1, _scheduler.Woken);
    }

    [Fact]
    public async Task DeliveredResult_SessionSeededWithSpeechAndBody()
    {
        TaskRecord task = await TaskAsync(AgentTaskStatus.Done, new TaskResult("Drei Artikel heute.", "Drei Artikel heute.\n\n1. A\n2. B\n3. C"));
        FollowUpRecord asked = await AskAsync(new NewFollowUp(Office, task.Id));
        MarshalExecutionContext context = Context();

        FollowUpReply? reply = await Replies().HandleAsync(Turn("erzähl mehr über den zweiten", asked.Nonce), context, Ct);

        Assert.Null(reply?.FixedReply);
        Assert.Equal([ChatRole.System, ChatRole.Assistant], reply!.Seed.Select(message => message.Role));
        Assert.Contains("2. B", reply.Seed[0].Text, StringComparison.Ordinal);
        Assert.Contains(task.Id.ToString(), reply.Seed[0].Text, StringComparison.Ordinal);
        Assert.Contains("<untrusted-data", reply.Seed[0].Text, StringComparison.Ordinal);
        Assert.Equal("Drei Artikel heute.", reply.Seed[1].Text);
        Assert.Equal(["task_result"], context.Taint.Sources.Select(source => source.Kind));
        Assert.Equal(AgentTaskStatus.Done, (await Tasks().GetAsync(task.Id, Ct)).Value.Status);
    }

    [Fact]
    public async Task InvalidReferences_NormalTurnAndNothingChanges()
    {
        TaskRecord task = await TaskAsync(AgentTaskStatus.Waiting);
        FollowUpRecord asked = await AskAsync(new NewFollowUp(Office, task.Id));

        Assert.Null(await Replies().HandleAsync(Turn("ja", "unknown-nonce-123"), Context(), Ct));
        Assert.Null(await Replies().HandleAsync(Turn("ja", asked.Nonce) with { Evidence = new IdentityEvidence(null, null, "assist_satellite.kitchen", null) }, Context("assist_satellite.kitchen"), Ct));
        _time.Advance(_options.FollowUpTtl);
        Assert.Null(await Replies().HandleAsync(Turn("ja", asked.Nonce), Context(), Ct));

        Assert.Null((await Tasks().GetAsync(task.Id, Ct)).Value.TriggerPayload);
    }

    [Fact]
    public async Task ConsumedReference_OnlyTheFirstReplyIsLinked()
    {
        TaskRecord task = await TaskAsync(AgentTaskStatus.Waiting);
        FollowUpRecord asked = await AskAsync(new NewFollowUp(Office, task.Id));

        await Replies().HandleAsync(Turn("die blaue", asked.Nonce), Context(), Ct);
        FollowUpReply? second = await Replies().HandleAsync(Turn("nein die rote", asked.Nonce), Context(), Ct);

        Assert.Null(second);
        Assert.Equal(1, _scheduler.Woken);
    }

    [Fact]
    public async Task ReplyWindow_TurnFromTheSameSatelliteWithoutReference_Linked()
    {
        TaskRecord task = await TaskAsync(AgentTaskStatus.Waiting);
        await AskAsync(new NewFollowUp(Office, task.Id));
        _time.Advance(TimeSpan.FromSeconds(10));

        FollowUpReply? reply = await Replies().HandleAsync(Turn("die blaue", null), Context(), Ct);
        FollowUpReply? text = await Replies().HandleAsync(Turn("die blaue", null) with { InputMode = InputMode.Text, Evidence = new IdentityEvidence(null, null, null, null) }, Context(null), Ct);

        Assert.Equal(FixedReplies.ReplyTaken("de"), reply?.FixedReply);
        Assert.Null(text);
    }

    [Fact]
    public async Task ReplyWindow_Passed_NormalTurn()
    {
        TaskRecord task = await TaskAsync(AgentTaskStatus.Waiting);
        await AskAsync(new NewFollowUp(Office, task.Id));
        _time.Advance(_options.FollowUpReplyWindow + TimeSpan.FromSeconds(1));

        Assert.Null(await Replies().HandleAsync(Turn("die blaue", null), Context(), Ct));
    }

    private ConversationTurn Turn(string text, string? followupRef) => new()
    {
        PrincipalOwner = OwnerA,
        Source = SessionSource.Voice,
        Text = text,
        Evidence = new IdentityEvidence(null, null, Office, null),
        InputMode = InputMode.Voice,
        FollowupRef = followupRef,
    };

    private MarshalExecutionContext Context(string? satellite = Office) => Voice(OwnerA, "de", satellite) with { SessionId = _session };

    private async Task<FollowUpRecord> AskAsync(NewFollowUp followUp) => await FollowUps().CreateAsync(followUp, Ct);

    private async Task<TaskRecord> TaskAsync(AgentTaskStatus status, TaskResult? result = null)
    {
        TaskRecord task = (await Tasks().CreateAsync(new NewTask { Goal = "Ask which filament.", Trigger = TaskTrigger.At(_time.GetUtcNow().AddDays(1)) }, Ct)).Value;
        return (await Tasks().UpdateAsync(task with { Status = status, NextRunAt = null, Result = result, ResultAt = result is null ? null : _time.GetUtcNow() }, Ct)).Value;
    }

    private async Task<PendingActionRecord> ApprovalAsync()
    {
        TaskRecord task = await TaskAsync(AgentTaskStatus.Waiting);
        return await Actions().FreezeAsync(new FreezeRequest(
            null, null, new ToolId("printer", "start_print"), """{"file":"benchy.gcode"}""", "Druck starten", [], "de", _time.GetUtcNow().AddMinutes(10), task.Id, Queued: true), Ct);
    }

    private FollowUpReplies Replies() => new(
        FollowUps(),
        Actions(),
        _approvals,
        new GermanKeywords(),
        _judge,
        Tasks(),
        _scheduler,
        Options.Create(_options),
        _time);

    private EfFollowUpStore FollowUps() => new(_database.Factory, _database.Accessor, Options.Create(_options), _time);

    private EfPendingActionStore Actions() => new(_database.Factory, _database.Accessor, _time);

    private EfTaskStore Tasks() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private sealed class RecordingApprovals : IApprovalQueue
    {
        public List<(PendingActionId Id, bool Approve, DecidedBy By)> Decisions { get; } = [];

        public Error? Refusal { get; set; }

        public Task<PendingActionRecord> EnqueueAsync(FreezeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PendingActionRecord>> ListPendingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<PendingActionRecord>> DecideAsync(PendingActionId id, bool approve, DecidedBy decidedBy, CancellationToken cancellationToken)
        {
            if (Refusal is { } refusal)
                return Task.FromResult<Result<PendingActionRecord>>(refusal);

            Decisions.Add((id, approve, decidedBy));
            return Task.FromResult<Result<PendingActionRecord>>(new PendingActionRecord
            {
                Id = id,
                OwnerId = OwnerA,
                Tool = new ToolId("printer", "start_print"),
                ArgumentsJson = "{}",
                Summary = "Druck starten",
                Reasons = [],
                Nonce = "n",
                Language = "de",
                CreatedAt = DateTimeOffset.UnixEpoch,
                ExpiresAt = DateTimeOffset.UnixEpoch,
                Status = approve ? PendingActionStatus.Approved : PendingActionStatus.Rejected,
            });
        }
    }

    private sealed class GermanKeywords : IConfirmationKeywords
    {
        public bool IsConfirmation(string utterance, string language) => utterance.Trim().Equals("bestätigt", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedJudge : IConfirmationJudge
    {
        public JudgeVerdict Verdict { get; set; } = JudgeVerdict.Positive;

        public int Calls { get; private set; }

        public Task<JudgeVerdict> JudgeAsync(string actionSummary, string userReply, string language, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Verdict);
        }
    }

    private sealed class WakeCounter : ITaskScheduler
    {
        public int Woken { get; private set; }

        public void Wake() => Woken++;

        public void CancelRun(TaskId task)
        {
        }
    }
}
