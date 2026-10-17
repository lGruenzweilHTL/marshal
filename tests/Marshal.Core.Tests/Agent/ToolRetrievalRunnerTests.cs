using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Sessions;
using Marshal.Core.Agent;
using Marshal.Core.Tools;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Tests.Agent;

public sealed class ToolRetrievalRunnerTests
{
    private static readonly SessionId _session = SessionId.New();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Run_LoadedNamespaces_PersistedAndReadOnNextTurn()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Hints.Add(new FixedHints("printer"));
        harness.Model.ThenText("printing").ThenText("still printing");

        await harness.Runner.RunAsync(Request("how far is the print?"), Ct);
        harness.Hints.Clear();
        await harness.Runner.RunAsync(Request("and now?"), Ct);

        Assert.Equal(["printer"], harness.SessionTools.Saved[_session]);
        Assert.Contains("printer_status", harness.Model.Requests[1].ToolNames);
        Assert.Contains(harness.Recorder.Runs[1].Steps[0].Notes, note => note is { Kind: LoadReasons.NoteKind, Label: "printer", Detail: LoadReasons.Session });
    }

    [Fact]
    public async Task Run_Subagent_StartsFromCoreAndSavesNothing()
    {
        LoopHarness harness = await HarnessAsync();
        harness.SessionTools.Saved[_session] = ["printer"];
        harness.Model.ThenText("sub done");
        RunId parent = await harness.Recorder.StartRunAsync(new RunStart(TestExecutionContext.Text(), "turn"), Ct);

        await harness.Runner.RunAsync(new AgentRunRequest { Context = TestExecutionContext.Text() with { SessionId = _session, Depth = 1, RunId = parent }, UserText = "check" }, Ct);

        Assert.DoesNotContain("printer_status", harness.Model.Requests[0].ToolNames);
        Assert.Equal(["printer"], harness.SessionTools.Saved[_session]);
    }

    [Fact]
    public async Task FindTools_LoadsNamespace_NextModelCallIncludesIt()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Model.ThenToolCall("agent_find_tools", new { query = "calendar" }).ThenText("You have two meetings.");

        await harness.Runner.RunAsync(Request("what's on today?"), Ct);

        Assert.DoesNotContain("calendar_list_events", harness.Model.Requests[0].ToolNames);
        Assert.Contains("calendar_list_events", harness.Model.Requests[1].ToolNames);
        Assert.Contains("calendar", ToolResult(harness.Model.Requests[1]), StringComparison.Ordinal);
        RunStepRecord step = harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.ToolCall);
        Assert.Contains(step.Notes, note => note is { Label: "calendar", Detail: LoadReasons.FindTools });
    }

    [Fact]
    public async Task FindTools_NamespaceDeniedBySatellite_NeverNamed()
    {
        LoopHarness harness = await HarnessAsync(SatelliteProfile.Default with { DenyTools = ["mail.*"] });
        harness.Model.ThenToolCall("agent_find_tools", new { query = "mail" }).ThenText("I cannot read mail here.");

        await harness.Runner.RunAsync(Request("any new mail?", TestExecutionContext.Voice() with { SessionId = _session }), Ct);

        Assert.DoesNotContain("mail", ToolResult(harness.Model.Requests[1]), StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Model.Requests[1].ToolNames, name => name.StartsWith("mail_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindTools_RetrievalDisabled_Hidden()
    {
        LoopHarness harness = await HarnessAsync();
        harness.ToolsOptions.Retrieval.Enabled = false;
        harness.Model.ThenText("hi");

        await harness.Runner.RunAsync(Request("hello"), Ct);

        Assert.DoesNotContain("agent_find_tools", harness.Model.Requests[0].ToolNames);
        Assert.Contains("printer_status", harness.Model.Requests[0].ToolNames);
    }

    [Fact]
    public async Task FindTools_RetrievalSkippedForFewTools_Hidden()
    {
        LoopHarness harness = await HarnessAsync();
        harness.ToolsOptions.Retrieval.MinTools = 24;
        harness.Model.ThenText("hi");

        await harness.Runner.RunAsync(Request("hello"), Ct);

        Assert.DoesNotContain("agent_find_tools", harness.Model.Requests[0].ToolNames);
    }

    [Fact]
    public async Task FindTools_EveryVisibleNamespaceLoaded_Hidden()
    {
        LoopHarness harness = await HarnessAsync();
        harness.SessionTools.Saved[_session] = ["printer", "calendar", "mail"];
        harness.Model.ThenText("hi");

        await harness.Runner.RunAsync(Request("hello"), Ct);

        Assert.DoesNotContain("agent_find_tools", harness.Model.Requests[0].ToolNames);
    }

    [Fact]
    public async Task Run_CallToVisibleToolOfUnloadedNamespace_LoadsItAndRunsThroughGuard()
    {
        FakeTool printer = new FakeTool("printer.status").Returns("printing, 40 %");
        LoopHarness harness = await HarnessAsync(printer: printer);
        harness.Model.ThenToolCall("printer_status").ThenText("40 percent.");

        await harness.Runner.RunAsync(Request("how far is the print?"), Ct);

        Assert.Single(printer.Invocations);
        RunStepRecord step = harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.ToolCall);
        Assert.Equal(GuardDecision.Safe, step.GuardDecision);
        Assert.Contains(step.Notes, note => note is { Label: "printer", Detail: LoadReasons.Call });
        Assert.Contains("printer_status", harness.Model.Requests[1].ToolNames);
    }

    [Fact]
    public async Task Run_CallToConfirmToolOfUnloadedNamespace_StillNeedsConfirmation()
    {
        LoopHarness harness = await HarnessAsync(printer: FakeTool.Confirm("printer.cancel"));
        harness.Model.ThenToolCall("printer_cancel").ThenText("Cancel the print?");

        AgentRunResult result = await harness.Runner.RunAsync(Request("cancel the print"), Ct);

        Assert.True(result.ContinueConversation);
        Assert.Equal(GuardDecision.Pending, harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.ToolCall).GuardDecision);
    }

    [Fact]
    public async Task Run_CallToSatelliteDeniedTool_StaysDenied()
    {
        LoopHarness harness = await HarnessAsync(SatelliteProfile.Default with { DenyTools = ["mail.*"] });
        harness.Model.ThenToolCall("mail_search").ThenText("Not here.");

        await harness.Runner.RunAsync(Request("search my mail", TestExecutionContext.Voice() with { SessionId = _session }), Ct);

        Assert.Equal(GuardDecision.Denied, harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.ToolCall).GuardDecision);
        Assert.False(harness.Recorder.OnlyRun.Steps.Single(step => step.Kind == RunStepKind.ToolCall).Notes.Any());
    }

    private static Task<LoopHarness> HarnessAsync(SatelliteProfile? kitchen = null, FakeTool? printer = null) =>
        Prepared(LoopHarness.CreateAsync(
            kitchen: kitchen,
            builtIns: harness => [new FindToolsTool(() => harness.Visibility, harness.Selector, Microsoft.Extensions.Options.Options.Create(harness.ToolsOptions))],
            tools:
            [
                new FakeTool("memory.recall"),
                printer ?? new FakeTool("printer.status"),
                new FakeTool("calendar.list_events"),
                new FakeTool("mail.search"),
            ]));

    private static async Task<LoopHarness> Prepared(Task<LoopHarness> created)
    {
        LoopHarness harness = await created;
        harness.ToolsOptions.Retrieval.MinTools = 2;
        return harness;
    }

    private static AgentRunRequest Request(string text, MarshalExecutionContext? context = null) =>
        new() { Context = context ?? TestExecutionContext.Text() with { SessionId = _session }, UserText = text };

    private static string ToolResult(FakeChatRequest request) =>
        string.Join("\n", request.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.Result?.ToString()));
}
