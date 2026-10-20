using Marshal.Abstractions.Apps;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Testing;

namespace Marshal.Core.Tests.Agent;

public sealed class AppEventTests
{
    private static readonly McpAppOutput _app = new("charts", "sales_report", "ui://charts/report", "<p>chart</p>");

    [Fact]
    public async Task WebTurn_CallWithApp_StoresInstanceEmitsAppAndKeepsItWithTheReply()
    {
        LoopHarness harness = await LoopHarness.CreateAsync(tools: FakeTool.Safe("charts.sales_report").Returns(_ => new ToolOutput("Sales: 12") { App = _app, CallResult = """{"content":[]}""" }));
        harness.Model.ThenToolCall("charts_sales_report", new { month = "9" }, "call-1").ThenText("Here is the report.");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Web(), harness), Ct);

        AppEvent shown = Assert.Single((await harness.DrainEventsAsync()).OfType<AppEvent>());
        Assert.Equal(("charts", "sales_report", "call-1"), (shown.App.Server, shown.App.Tool, shown.App.CallId));
        NewAppInstance stored = harness.Apps.Instances[shown.App.AppId];
        Assert.Equal(TestExecutionContext.OwnerA, stored.OwnerId);
        Assert.Equal("""{"month":"9"}""", stored.Arguments);
        Assert.Same(_app, stored.App);
        Assert.Equal("""{"content":[]}""", stored.Result);
        RunStepRecord reply = harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.Reply);
        Assert.Equal(shown.App, Assert.Single(reply.Apps!));
    }

    [Theory]
    [InlineData(SessionSource.Voice)]
    [InlineData(SessionSource.Api)]
    public async Task NonWebTurn_NoApp(SessionSource source)
    {
        LoopHarness harness = await LoopHarness.CreateAsync(tools: FakeTool.Safe("charts.sales_report").Returns(_ => new ToolOutput("Sales: 12") { App = _app }));
        harness.Model.ThenToolCall("charts_sales_report").ThenText("ok");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text() with { Source = source }, harness), Ct);

        Assert.Empty((await harness.DrainEventsAsync()).OfType<AppEvent>());
        Assert.Empty(harness.Apps.Instances);
    }

    [Fact]
    public async Task SkippedResource_NoteRecordedWithTheStep()
    {
        var note = new Marshal.Abstractions.Context.ContextNote("app_skipped", "ui://charts/report", "The resource is larger than 1 MB.");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: FakeTool.Safe("charts.sales_report").Returns(_ => new ToolOutput("Sales: 12") { Notes = [note] }));
        harness.Model.ThenToolCall("charts_sales_report").ThenText("ok");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Web(), harness), Ct);

        Assert.Empty((await harness.DrainEventsAsync()).OfType<AppEvent>());
        RunStepRecord step = harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.ToolCall);
        Assert.Contains(note, step.Notes);
    }

    private static AgentRunRequest Request(MarshalExecutionContext context, LoopHarness harness) =>
        new() { Context = context, UserText = "sales report", Events = harness.Events.Writer };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
