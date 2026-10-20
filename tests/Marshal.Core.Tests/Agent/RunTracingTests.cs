using System.Diagnostics;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Sessions;
using Marshal.Core.Agent;
using Marshal.Core.Recording;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Tests.Agent;

public sealed class RunTracingTests
{
    [Fact]
    public async Task Run_ToolCall_InvokeAgentRootIsCurrentAndToolSpanIsItsChild()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        var seen = new List<Activity?>();
        Activity? duringTool = null;
        FakeTool tool = FakeTool.Safe().Returns(_ =>
        {
            duringTool = Activity.Current;
            return new Marshal.Abstractions.Tools.ToolOutput("21 degrees");
        });
        LoopHarness harness = await LoopHarness.CreateAsync(tools: tool);
        harness.Model
            .Then((_, _, _) =>
            {
                seen.Add(Activity.Current);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-7", "test_safe")])));
            })
            .Then((_, _, _) =>
            {
                seen.Add(Activity.Current);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "It is 21 degrees.")));
            });
        var session = SessionId.New();

        AgentRunResult result = await harness.Runner.RunAsync(Request("how warm is it?", TestExecutionContext.Text() with { SessionId = session }), Ct);

        IReadOnlyList<Activity> trace = collector.TraceOf("marshal.run_id", result.RunId.ToString());
        Activity root = Assert.Single(trace, activity => activity.OperationName == "invoke_agent");
        Assert.Equal(default, root.ParentSpanId);
        Assert.Equal("invoke_agent default", root.DisplayName);
        Assert.Equal("invoke_agent", root.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("default", root.GetTagItem("gen_ai.agent.name"));
        Assert.Equal(session.ToString(), root.GetTagItem("gen_ai.conversation.id"));
        Assert.Equal("api", root.GetTagItem("marshal.source"));
        Assert.Equal("text", root.GetTagItem("marshal.input_mode"));
        Assert.Equal("turn", root.GetTagItem("marshal.trigger"));
        Assert.Equal("ok", root.GetTagItem("marshal.outcome"));
        Assert.Equal(false, root.GetTagItem("marshal.tainted"));
        Assert.All(seen, current => Assert.Equal(root.SpanId, current?.SpanId));
        Assert.Equal(2, seen.Count);

        Activity span = Assert.Single(trace, activity => activity.OperationName == "execute_tool");
        Assert.Equal(root.SpanId, span.ParentSpanId);
        Assert.Equal(span.SpanId, duringTool?.SpanId);
        Assert.Equal("execute_tool test_safe", span.DisplayName);
        Assert.Equal("execute_tool", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("test_safe", span.GetTagItem("gen_ai.tool.name"));
        Assert.Equal("call-7", span.GetTagItem("gen_ai.tool.call.id"));
        Assert.Equal("function", span.GetTagItem("gen_ai.tool.type"));
        Assert.Equal("test.safe", span.GetTagItem("marshal.tool"));
        Assert.Equal("safe", span.GetTagItem("marshal.risk_tier"));
        Assert.Equal("safe", span.GetTagItem("marshal.guard_decision"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task Run_ToolFails_ToolSpanHasErrorStatusAndType()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        LoopHarness harness = await LoopHarness.CreateAsync(tools: FakeTool.Safe().Fails("device.offline", "The device is offline."));
        harness.Model.ThenToolCall("test_safe").ThenText("It is offline.");

        AgentRunResult result = await harness.Runner.RunAsync(Request("turn it on"), Ct);

        Activity span = Assert.Single(collector.TraceOf("marshal.run_id", result.RunId.ToString()), activity => activity.OperationName == "execute_tool");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("device.offline", span.GetTagItem("error.type"));
    }

    [Fact]
    public async Task Run_ForbiddenTool_ToolSpanRecordsDenial()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        LoopHarness harness = await LoopHarness.CreateAsync(tools: FakeTool.Forbidden());
        harness.Model.ThenToolCall("test_forbidden").ThenText("Not allowed.");

        AgentRunResult result = await harness.Runner.RunAsync(Request("do it"), Ct);

        Activity span = Assert.Single(collector.TraceOf("marshal.run_id", result.RunId.ToString()), activity => activity.OperationName == "execute_tool");
        Assert.Equal("denied", span.GetTagItem("marshal.guard_decision"));
    }

    [Fact]
    public async Task Run_UntrustedTool_RootIsTainted()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        LoopHarness harness = await LoopHarness.CreateAsync(tools: FakeTool.Untrusted().Returns("Dear user"));
        harness.Model.ThenToolCall("mail_read").ThenText("One mail.");

        AgentRunResult result = await harness.Runner.RunAsync(Request("read my mail"), Ct);

        Activity root = Assert.Single(collector.TraceOf("marshal.run_id", result.RunId.ToString()), activity => activity.OperationName == "invoke_agent");
        Assert.Equal(true, root.GetTagItem("marshal.tainted"));
    }

    [Fact]
    public async Task Run_WithoutContentCapture_NoSpanCarriesContent()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        LoopHarness harness = await LoopHarness.CreateAsync(tools: FakeTool.Untrusted().Returns("Dear user, the secret code is 4711"));
        harness.Model.ThenToolCall("mail_read", new { folder = "inbox-private" }).ThenText("The code is 4711.");

        AgentRunResult result = await harness.Runner.RunAsync(Request("what does the mail from bob say?"), Ct);

        IReadOnlyList<Activity> trace = collector.TraceOf("marshal.run_id", result.RunId.ToString());
        List<string> values = [.. trace.SelectMany(activity => activity.TagObjects).Select(tag => tag.Value?.ToString() ?? "")];
        Assert.DoesNotContain(values, value => value.Contains("4711", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains("inbox-private", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains("bob", StringComparison.Ordinal));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgentRunRequest Request(string text, MarshalExecutionContext? context = null) =>
        new() { Context = context ?? TestExecutionContext.Text(), UserText = text };
}
