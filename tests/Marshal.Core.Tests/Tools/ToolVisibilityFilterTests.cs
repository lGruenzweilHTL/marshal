using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Marshal.Testing;

namespace Marshal.Core.Tests.Tools;

public sealed class ToolVisibilityFilterTests
{
    private const string Kitchen = "assist_satellite.kitchen";

    [Fact]
    public async Task Visible_SafeTool_Shown()
    {
        ToolVisibilityFilter filter = await FilterAsync("risk: []", SatelliteProfile.Default, FakeTool.Safe());

        Assert.Equal(["test_safe"], await NamesAsync(filter, TestExecutionContext.Text()));
    }

    [Fact]
    public async Task Visible_ForbiddenByRule_Hidden()
    {
        ToolVisibilityFilter filter = await FilterAsync("""
            risk:
              - match: { tool: "test.safe" }
                tier: forbidden
            """, SatelliteProfile.Default, FakeTool.Safe(), FakeTool.Safe("test.other"));

        Assert.Equal(["test_other"], await NamesAsync(filter, TestExecutionContext.Text()));
    }

    [Fact]
    public async Task Visible_ForbiddenByDefault_Hidden()
    {
        ToolVisibilityFilter filter = await FilterAsync("risk: []", SatelliteProfile.Default, FakeTool.Forbidden());

        Assert.Empty(await NamesAsync(filter, TestExecutionContext.Text()));
    }

    [Fact]
    public async Task Visible_DenyTools_HiddenOnThatSatelliteOnly()
    {
        var kidsRoom = new SatelliteProfile("short", null, ["mail.*"], []);
        ToolVisibilityFilter filter = await FilterAsync("risk: []", kidsRoom, FakeTool.Safe("mail.search"), FakeTool.Safe("test.safe"));

        Assert.Equal(["test_safe"], await NamesAsync(filter, TestExecutionContext.Voice(satelliteId: Kitchen)));
        Assert.Equal(["mail_search", "test_safe"], await NamesAsync(filter, TestExecutionContext.Voice(satelliteId: null)));
    }

    [Fact]
    public async Task Visible_Subagent_ConfirmOnlyHiddenArgumentDependentStays()
    {
        ToolVisibilityFilter filter = await FilterAsync("""
            risk:
              - match: { tool: "ha.*", domain: lock }
                tier: confirm
              - match: { tool: "ha.*" }
                tier: safe
            """, SatelliteProfile.Default, FakeTool.Confirm("printer.start_print"), FakeTool.Safe("ha.HassTurnOn"), FakeTool.Safe("printer.pause"));

        Assert.Equal(["ha_HassTurnOn", "printer_pause"], await NamesAsync(filter, TestExecutionContext.Text() with { Depth = 1 }));
        Assert.Equal(["ha_HassTurnOn", "printer_pause", "printer_start_print"], await NamesAsync(filter, TestExecutionContext.Text()));
    }

    [Fact]
    public async Task Visible_DenyConfirm_ConfirmOnlyToolHidden()
    {
        var profile = new SatelliteProfile("short", null, [], ["printer.*"]);
        ToolVisibilityFilter filter = await FilterAsync("risk: []", profile, FakeTool.Confirm("printer.start_print"), FakeTool.Safe("printer.pause"));

        Assert.Equal(["printer_pause"], await NamesAsync(filter, TestExecutionContext.Voice(satelliteId: Kitchen)));
    }

    [Fact]
    public async Task Visible_DenyConfirm_ArgumentDependentToolStaysVisible()
    {
        var profile = new SatelliteProfile("short", null, [], ["ha.*"]);
        ToolVisibilityFilter filter = await FilterAsync("""
            risk:
              - match: { tool: "ha.*", domain: lock }
                tier: confirm
              - match: { tool: "ha.*" }
                tier: safe
            """, profile, FakeTool.Safe("ha.HassTurnOn"));

        Assert.Equal(["ha_HassTurnOn"], await NamesAsync(filter, TestExecutionContext.Voice(satelliteId: Kitchen)));
    }

    [Fact]
    public async Task Visible_DenyConfirm_PersistsToolInTaintedRunHidden()
    {
        var profile = new SatelliteProfile("short", null, [], ["memory.*"]);
        ToolVisibilityFilter filter = await FilterAsync("risk: []", profile, FakeTool.Persisting());
        MarshalExecutionContext clean = TestExecutionContext.Voice(satelliteId: Kitchen);
        MarshalExecutionContext tainted = TestExecutionContext.Voice(satelliteId: Kitchen);
        tainted.Taint.Mark("mail", 1);

        Assert.Equal(["memory_remember"], await NamesAsync(filter, clean));
        Assert.Empty(await NamesAsync(filter, tainted));
    }

    [Fact]
    public async Task Visible_ToolSpecificCondition_Applied()
    {
        FakeTool advisor = FakeTool.Safe("agent.ask_advisor").AvailableWhen(context => context.InputMode != InputMode.Voice);
        ToolVisibilityFilter filter = await FilterAsync("risk: []", SatelliteProfile.Default, advisor);

        Assert.Equal(["agent_ask_advisor"], await NamesAsync(filter, TestExecutionContext.Text()));
        Assert.Empty(await NamesAsync(filter, TestExecutionContext.Voice()));
    }

    [Fact]
    public async Task Check_ReturnsReasonCodes()
    {
        var profile = new SatelliteProfile("short", null, ["test.denied"], ["test.confirm"]);
        FakeTool unavailable = FakeTool.Safe("test.unavailable").AvailableWhen(_ => false);
        ToolVisibilityFilter filter = await FilterAsync("risk: []", profile, FakeTool.Forbidden(), FakeTool.Safe("test.denied"), FakeTool.Confirm(), unavailable, FakeTool.Safe());
        MarshalExecutionContext context = TestExecutionContext.Voice(satelliteId: Kitchen);

        Assert.Equal("forbidden", (await filter.CheckAsync(Tool(filter, "test.forbidden"), context, Ct)).Reason);
        Assert.Equal("satellite_denied", (await filter.CheckAsync(Tool(filter, "test.denied"), context, Ct)).Reason);
        Assert.Equal("satellite_denies_confirm", (await filter.CheckAsync(Tool(filter, "test.confirm"), context, Ct)).Reason);
        Assert.Equal("unavailable", (await filter.CheckAsync(Tool(filter, "test.unavailable"), context, Ct)).Reason);
        Assert.True((await filter.CheckAsync(Tool(filter, "test.safe"), context, Ct)).IsVisible);
    }

    [Fact]
    public async Task Template_AllowlistHidesOtherTools()
    {
        var templates = new FakeTemplateCatalog();
        templates.Items.Add(FakeTemplateCatalog.Template("printer", tools: ["printer.*"]));
        ToolVisibilityFilter filter = await FilterAsync("risk: []", SatelliteProfile.Default, templates, FakeTool.Safe("printer.status"), FakeTool.Safe("mail.search"));
        MarshalExecutionContext context = TestExecutionContext.Text() with { Template = "printer" };

        Assert.Equal(["printer_status"], await NamesAsync(filter, context));
        Assert.Equal("template_denied", (await filter.CheckAsync(Tool(filter, "mail.search"), context, Ct)).Reason);
        Assert.Equal(["mail_search", "printer_status"], await NamesAsync(filter, TestExecutionContext.Text()));
    }

    [Fact]
    public async Task Visible_ContextSnapshot_WinsOverLaterRegistryState()
    {
        var source = new FakeToolSource("test", true, FakeTool.Safe("test.a"));
        var registry = new ToolRegistry([source]);
        MarshalExecutionContext context = TestExecutionContext.Text() with { Tools = await registry.RefreshAsync(Ct) };
        var filter = new ToolVisibilityFilter(registry, GuardTestKit.Engine("risk: []"), new FixedSatellites(Kitchen, SatelliteProfile.Default), new FakeTemplateCatalog());

        source.Tools.Add(FakeTool.Safe("test.b"));
        await registry.RefreshAsync(Ct);

        Assert.Equal(["test_a"], await NamesAsync(filter, context));
        Assert.Equal(["test_a", "test_b"], await NamesAsync(filter, TestExecutionContext.Text()));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RegisteredTool Tool(ToolVisibilityFilter filter, string id) =>
        filter.Registry.Current.FindById(ToolId.Parse(id).Value) ?? throw new InvalidOperationException(id);

    private static async Task<List<string>> NamesAsync(ToolVisibilityFilter filter, MarshalExecutionContext context) =>
        [.. (await filter.GetVisibleToolsAsync(context, Ct)).Select(tool => tool.ModelName).Order(StringComparer.Ordinal)];

    private static Task<ToolVisibilityFilter> FilterAsync(string rules, SatelliteProfile kitchenProfile, params ITool[] tools) =>
        FilterAsync(rules, kitchenProfile, new FakeTemplateCatalog(), tools);

    private static async Task<ToolVisibilityFilter> FilterAsync(string rules, SatelliteProfile kitchenProfile, FakeTemplateCatalog templates, params ITool[] tools)
    {
        var registry = new ToolRegistry([new FakeToolSource("test", true, tools)]);
        await registry.RefreshAsync(Ct);
        return new ToolVisibilityFilter(registry, GuardTestKit.Engine(rules), new FixedSatellites(Kitchen, kitchenProfile), templates);
    }
}
