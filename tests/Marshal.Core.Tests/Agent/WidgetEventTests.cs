using System.Text.Json;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Widgets;
using Marshal.Core.Agent;
using Marshal.Testing;

namespace Marshal.Core.Tests.Agent;

public sealed class WidgetEventTests
{
    [Fact]
    public async Task WebTurn_SuccessfulCallOfWidgetTool_EmitsWidgetAndKeepsItWithTheReply()
    {
        LoopHarness harness = await HarnessAsync(FakeTool.Safe("printer.status").Returns("Printing.").With(descriptor => descriptor with { Widget = "printer" }));
        harness.Model.ThenToolCall("printer_status", new { printer = "voron" }, "call-1").ThenText("It is printing.");

        await harness.Runner.RunAsync(Request(Web(), harness), Ct);

        WidgetEvent widget = Assert.Single((await harness.DrainEventsAsync()).OfType<WidgetEvent>());
        Assert.Equal(("printer", "call-1"), (widget.Widget.Kind, widget.Widget.CallId));
        Assert.Equal("voron", widget.Widget.Data.GetProperty("printer").GetString());
        Assert.Equal("Printing.", widget.Widget.Data.GetProperty("result").GetString());
        RunStepRecord reply = harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.Reply);
        Assert.Equal("printer", Assert.Single(reply.Widgets!).Kind);
    }

    [Theory]
    [InlineData(SessionSource.Voice)]
    [InlineData(SessionSource.Api)]
    public async Task NonWebTurn_NoWidget(SessionSource source)
    {
        LoopHarness harness = await HarnessAsync(FakeTool.Safe("printer.status").With(descriptor => descriptor with { Widget = "printer" }));
        harness.Model.ThenToolCall("printer_status").ThenText("ok");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text() with { Source = source }, harness), Ct);

        Assert.Empty((await harness.DrainEventsAsync()).OfType<WidgetEvent>());
        Assert.Null(harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.Reply).Widgets);
    }

    [Fact]
    public async Task FailedCall_NoWidget()
    {
        LoopHarness harness = await HarnessAsync(FakeTool.Safe("printer.status").Fails("printer.offline", "Offline.").With(descriptor => descriptor with { Widget = "printer" }));
        harness.Model.ThenToolCall("printer_status").ThenText("offline");

        await harness.Runner.RunAsync(Request(Web(), harness), Ct);

        Assert.Empty((await harness.DrainEventsAsync()).OfType<WidgetEvent>());
    }

    [Fact]
    public async Task ProviderReturnsNothing_NoWidget()
    {
        LoopHarness harness = await HarnessAsync(FakeTool.Safe("printer.status").With(descriptor => descriptor with { Widget = "printer" }));
        harness.Widgets.Clear();
        harness.Widgets.Add(new EchoWidget("printer", empty: true));
        harness.Model.ThenToolCall("printer_status").ThenText("ok");

        await harness.Runner.RunAsync(Request(Web(), harness), Ct);

        Assert.Empty((await harness.DrainEventsAsync()).OfType<WidgetEvent>());
    }

    private static async Task<LoopHarness> HarnessAsync(FakeTool tool)
    {
        LoopHarness harness = await LoopHarness.CreateAsync(tools: tool);
        harness.Widgets.Add(new EchoWidget("printer"));
        return harness;
    }

    private static MarshalExecutionContext Web() => TestExecutionContext.Text() with { Source = SessionSource.Web };

    private static AgentRunRequest Request(MarshalExecutionContext context, LoopHarness harness) =>
        new() { Context = context, UserText = "how is the print?", Events = harness.Events.Writer };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class EchoWidget(string kind, bool empty = false) : IWidgetDataProvider
    {
        public string Kind => kind;

        public Task<JsonElement?> GetAsync(WidgetRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<JsonElement?>(empty
                ? null
                : JsonSerializer.SerializeToElement(new { printer = request.Arguments.TryGetProperty("printer", out JsonElement printer) ? printer.GetString() : null, result = request.Output.Content }));
    }
}
