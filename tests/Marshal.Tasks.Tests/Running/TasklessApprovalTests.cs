using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Core.Agent;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Running;

public sealed class TasklessApprovalTests
{
    private static readonly SessionId _session = SessionId.New();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConfirmTierCall_QueuedWithoutTask_RunContinuesWithWaitingResult()
    {
        var create = FakeTool.Confirm("calendar.create_event");
        TaskLoopHarness harness = await TaskLoopHarness.CreateAsync(tools: create);
        harness.Model.ThenToolCall("calendar_create_event", new { title = "Dentist" }).ThenText("I asked for approval to add the dentist appointment.");

        AgentRunResult result = await harness.Agent.RunAsync(Request(), Ct);

        PendingActionRecord queued = Assert.Single(harness.Approvals.Queued);
        Assert.Equal(((Abstractions.Tasks.TaskId?)null, true), (queued.TaskId, queued.Queued));
        Assert.Null(await harness.PendingActions.GetPendingAsync(_session, Ct));
        Assert.Empty(create.Invocations);
        Assert.Contains("review queue", ToolResult(harness.Model.Requests[1]), StringComparison.Ordinal);
        Assert.Equal((RunOutcome.Ok, false, "I asked for approval to add the dentist appointment."), (result.Outcome, result.ContinueConversation, result.Reply));
        Assert.Null(result.PendingAction);
    }

    [Fact]
    public async Task PersistsCallEscalatedByTaint_QueuedWithoutTask()
    {
        var remember = FakeTool.Persisting();
        TaskLoopHarness harness = await TaskLoopHarness.CreateAsync(tools: [FakeTool.Untrusted(), remember]);
        harness.Model.ThenToolCall("mail_read").ThenToolCall("memory_remember", new { text = "door code" }).ThenText("Queued.");

        await harness.Agent.RunAsync(Request(), Ct);

        PendingActionRecord queued = Assert.Single(harness.Approvals.Queued);
        Assert.Equal(ConfirmationReasonCode.Taint, queued.Reasons[0].Code);
        Assert.Empty(remember.Invocations);
        Assert.Equal(GuardDecision.EscalatedByTaint, harness.Recorder.OnlyRun.Steps.Single(step => step.Tool?.ToString() == "memory.remember").GuardDecision);
    }

    [Fact]
    public async Task SecondConfirmTierCall_AlsoQueued()
    {
        var create = FakeTool.Confirm("calendar.create_event");
        TaskLoopHarness harness = await TaskLoopHarness.CreateAsync(tools: create);
        harness.Model
            .ThenToolCall("calendar_create_event", new { title = "Dentist" })
            .ThenToolCall("calendar_create_event", new { title = "Barber" })
            .ThenText("Both wait for approval.");

        await harness.Agent.RunAsync(Request(), Ct);

        Assert.Equal(2, harness.Approvals.Queued.Count);
    }

    [Fact]
    public async Task InteractiveTurn_StillHoldsSessionPendingAction()
    {
        var create = FakeTool.Confirm("calendar.create_event");
        TaskLoopHarness harness = await TaskLoopHarness.CreateAsync(tools: create);
        harness.Model.ThenToolCall("calendar_create_event", new { title = "Dentist" }).ThenText("Add it?");

        AgentRunResult result = await harness.Agent.RunAsync(new AgentRunRequest { Context = Text() with { SessionId = _session }, UserText = "add the dentist" }, Ct);

        Assert.Empty(harness.Approvals.Queued);
        Assert.True(result.ContinueConversation);
        Assert.NotNull(await harness.PendingActions.GetPendingAsync(_session, Ct));
    }

    private static AgentRunRequest Request() => new()
    {
        Context = Text() with { SessionId = _session, Source = SessionSource.Automation, IsInteractive = false },
        UserText = "Add the dentist appointment from the letter",
        Trigger = "ai_task:letter",
    };

    private static string ToolResult(FakeChatRequest request) =>
        string.Join("\n", request.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.Result?.ToString()));
}
