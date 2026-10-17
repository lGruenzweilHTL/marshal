using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Templates;
using Marshal.Core.Agent;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Tests.Agent;

public sealed class UserAttachmentTests
{
    private static readonly DataContent _snapshot = new(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Run_Attachments_InFirstUserMessageNextToText()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("A parcel on the doormat.");

        await harness.Runner.RunAsync(Request("What is at the door?"), Ct);

        ChatMessage user = harness.Model.Requests[0].Messages.Single(message => message.Role == ChatRole.User);
        Assert.Equal("What is at the door?", user.Contents.OfType<TextContent>().Single().Text);
        Assert.Same(_snapshot, user.Contents.OfType<DataContent>().Single());
    }

    [Fact]
    public async Task Run_Attachments_RouterAskedForTemplateTierWithVision()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("A parcel.");

        await harness.Runner.RunAsync(Request("What is at the door?", AutomationContext()), Ct);

        Assert.Equal(new ModelRequest(ModelTier.Fast, Vision: true, Tools: true), harness.Router.Requests[0]);
    }

    [Fact]
    public async Task Run_NoAttachments_NoVisionRequired()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("Hi.");

        await harness.Runner.RunAsync(new AgentRunRequest { Context = TestExecutionContext.Text(), UserText = "hello" }, Ct);

        Assert.False(harness.Router.Requests[0].Vision);
    }

    [Fact]
    public async Task Run_AttachmentsWithoutVisionModel_FailsBeforeAnyModelCall()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Router.NoVision = true;

        AgentRunResult result = await harness.Runner.RunAsync(Request("What is at the door?"), Ct);

        Assert.Empty(harness.Model.Requests);
        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Equal(AgentRunRequest.NoVisionModelReason, result.OutcomeReason);
    }

    [Fact]
    public async Task Run_Attachments_TaintedBeforeFirstModelCall()
    {
        var remember = FakeTool.Persisting();
        LoopHarness harness = await LoopHarness.CreateAsync(tools: remember);
        MarshalExecutionContext context = TestExecutionContext.Text();
        bool taintedAtFirstCall = false;
        harness.Model
            .Then((_, _, _) =>
            {
                taintedAtFirstCall = context.Taint.IsTainted;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "memory_remember", new Dictionary<string, object?> { ["text"] = "the door code is 1234" })])));
            })
            .ThenText("Should I remember that?");

        await harness.Runner.RunAsync(Request("Remember what the note in the picture says", context), Ct);

        Assert.True(taintedAtFirstCall);
        Assert.Empty(remember.Invocations);
        Assert.Equal(new TaintSource(AgentRunRequest.AttachmentTaintKind, 0), Assert.Single(harness.Recorder.OnlyRun.Completion!.TaintSources));
        Assert.Equal(GuardDecision.EscalatedByTaint, harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.ToolCall).GuardDecision);
    }

    private static MarshalExecutionContext AutomationContext() =>
        TestExecutionContext.Text() with { Source = SessionSource.Automation, IsInteractive = false, Template = AgentTemplate.AiTaskName };

    private static AgentRunRequest Request(string text, MarshalExecutionContext? context = null) =>
        new() { Context = context ?? TestExecutionContext.Text(), UserText = text, UserAttachments = [_snapshot] };
}
