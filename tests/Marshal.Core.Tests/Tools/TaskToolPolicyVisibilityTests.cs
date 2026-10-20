using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Tests.Agent;
using Marshal.Core.Tools;
using Marshal.Testing;

namespace Marshal.Core.Tests.Tools;

public sealed class TaskToolPolicyVisibilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TaskRun_SeesOnlyPolicyTools()
    {
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [FakeTool.Safe("mail.create_draft"), FakeTool.Safe("printer.status"), FakeTool.Safe("printer.pause")]);
        harness.Model.ThenText("Done.");
        MarshalExecutionContext task = TestExecutionContext.Text() with { IsInteractive = false, ToolPolicy = ["printer.status"] };

        await harness.Runner.RunAsync(new AgentRunRequest { Context = task, UserText = "check the print" }, Ct);

        Assert.Equal(["printer_status"], harness.Model.Requests[0].ToolNames);
    }

    [Fact]
    public async Task Visibility_ReasonNamesThePolicy()
    {
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [FakeTool.Safe("mail.create_draft")]);
        RegisteredTool draft = harness.Registry.Current.Tools.Single();

        VisibilityVerdict verdict = await harness.Visibility.CheckAsync(draft, TestExecutionContext.Text() with { ToolPolicy = ["printer.*"] }, Ct);
        VisibilityVerdict noPolicy = await harness.Visibility.CheckAsync(draft, TestExecutionContext.Text(), Ct);

        Assert.Equal((false, "outside_policy"), (verdict.IsVisible, verdict.Reason));
        Assert.True(noPolicy.IsVisible);
    }

    [Fact]
    public async Task FindTools_NeverNamesANamespaceOutsideThePolicy()
    {
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [FakeTool.Safe("mail.search"), FakeTool.Safe("mail.read"), FakeTool.Safe("printer.status")]);
        harness.ToolsOptions.Retrieval.Enabled = true;
        var tool = new FindToolsTool(() => harness.Visibility, harness.Selector, Microsoft.Extensions.Options.Options.Create(harness.ToolsOptions));
        MarshalExecutionContext task = TestExecutionContext.Text() with { ToolPolicy = ["printer.*"] };

        Result<ToolOutput> found = await tool.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(new { query = "mail" }), task), Ct);

        Assert.DoesNotContain("mail", found.Value.Content, StringComparison.Ordinal);
    }
}
