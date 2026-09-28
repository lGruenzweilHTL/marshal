using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Skills;
using Marshal.Core.Tests.Agent;
using Marshal.Testing;

namespace Marshal.Core.Tests.Skills;

public sealed class SkillToolsTests
{
    private readonly FakeSkillCatalog _catalog = new();
    private readonly FakeTemplateCatalog _templates = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Index_ListsNamesAndDescriptionsWithoutBodies()
    {
        _catalog.Items.Add(FakeSkillCatalog.Skill("rss-digest", "Summarizes RSS.", "SECRET-BODY-1"));
        _catalog.Items.Add(FakeSkillCatalog.Skill("briefing-format", "How to format briefings.", "SECRET-BODY-2"));
        _catalog.Items.Add(FakeSkillCatalog.Skill("printer-help", "Printer troubleshooting.", "SECRET-BODY-3"));

        string text = (await new SkillIndexContributor(_catalog, _templates).ContributeAsync(new ContextRequest(TestExecutionContext.Text(), "hi"), Ct))!;

        Assert.Contains("- rss-digest: Summarizes RSS.", text, StringComparison.Ordinal);
        Assert.Contains("- briefing-format: How to format briefings.", text, StringComparison.Ordinal);
        Assert.Contains("- printer-help: Printer troubleshooting.", text, StringComparison.Ordinal);
        Assert.Contains("agent_load_skill", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-BODY", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_NoSkills_NoSection()
    {
        Assert.Null(await new SkillIndexContributor(_catalog, _templates).ContributeAsync(new ContextRequest(TestExecutionContext.Text(), "hi"), Ct));
    }

    [Fact]
    public async Task Load_ReturnsBodyAndListsFiles()
    {
        _catalog.Items.Add(FakeSkillCatalog.Skill("rss-digest", body: "1. Search unread.", files: new() { ["references/format.md"] = "Short." }));

        Result<ToolOutput> result = await new LoadSkillTool(_catalog, _templates).InvokeAsync(Invocation(new { name = "rss-digest" }), Ct);

        Assert.Contains("1. Search unread.", result.Value.Content, StringComparison.Ordinal);
        Assert.Contains("references/format.md", result.Value.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ExtraFile_ReturnsItsContent()
    {
        _catalog.Items.Add(FakeSkillCatalog.Skill("rss-digest", files: new() { ["references/format.md"] = "Most important first." }));

        Result<ToolOutput> result = await new LoadSkillTool(_catalog, _templates).InvokeAsync(Invocation(new { name = "rss-digest", file = "references/format.md" }), Ct);

        Assert.Equal("Most important first.", result.Value.Content);
    }

    [Theory]
    [InlineData("unknown", null, "skill.not_found")]
    [InlineData("rss-digest", "../other/SKILL.md", "skill.file_not_found")]
    [InlineData("rss-digest", "missing.md", "skill.file_not_found")]
    public async Task Load_Unknown_FailsNamingAvailable(string name, string? file, string code)
    {
        _catalog.Items.Add(FakeSkillCatalog.Skill("rss-digest"));

        Result<ToolOutput> result = await new LoadSkillTool(_catalog, _templates).InvokeAsync(Invocation(new { name, file }), Ct);

        Assert.Equal(code, result.Error.Code);
        Assert.Contains("rss-digest", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_HiddenWithoutSkills()
    {
        var tool = new LoadSkillTool(_catalog, _templates);

        Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct));
        _catalog.Items.Add(FakeSkillCatalog.Skill("rss-digest"));
        Assert.True(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task Load_InRun_DoesNotTaint()
    {
        _catalog.Items.Add(FakeSkillCatalog.Skill("rss-digest"));
        LoopHarness harness = await LoopHarness.CreateAsync(rules: "risk: []", tools: new LoadSkillTool(_catalog, _templates));
        harness.Model.ThenToolCall("agent_load_skill", new { name = "rss-digest" }).ThenText("Done.");

        AgentRunResult result = await harness.Runner.RunAsync(new AgentRunRequest { Context = TestExecutionContext.Text(), UserText = "digest", Trigger = "test" }, Ct);

        Assert.Equal(RunOutcome.Ok, result.Outcome);
        Assert.Empty(harness.Recorder.OnlyRun.Completion!.TaintSources);
        Assert.Equal(new ToolId("agent", "load_skill"), new LoadSkillTool(_catalog, _templates).Descriptor.Id);
        Assert.False(new LoadSkillTool(_catalog, _templates).Descriptor.UntrustedOutput);
    }

    [Fact]
    public async Task TemplateSkills_OnlyThoseIndexedAndLoadable()
    {
        _catalog.Items.Add(FakeSkillCatalog.Skill("rss-digest"));
        _catalog.Items.Add(FakeSkillCatalog.Skill("printer-help"));
        _templates.Items.Add(FakeTemplateCatalog.Template("reader", skills: ["rss-digest"]));
        _templates.Items.Add(FakeTemplateCatalog.Template("bare", skills: []));
        MarshalExecutionContext reader = TestExecutionContext.Text() with { Template = "reader" };

        string index = (await new SkillIndexContributor(_catalog, _templates).ContributeAsync(new ContextRequest(reader, "hi"), Ct))!;
        Result<ToolOutput> denied = await new LoadSkillTool(_catalog, _templates).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(new { name = "printer-help" }), reader), Ct);

        Assert.Contains("rss-digest", index, StringComparison.Ordinal);
        Assert.DoesNotContain("printer-help", index, StringComparison.Ordinal);
        Assert.Equal("skill.not_found", denied.Error.Code);
        Assert.False(await new LoadSkillTool(_catalog, _templates).IsAvailableAsync(TestExecutionContext.Text() with { Template = "bare" }, Ct));
    }

    private static ToolInvocation Invocation(object arguments) => new(JsonSerializer.SerializeToElement(arguments), TestExecutionContext.Text());
}
