using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;
using Marshal.Core.Guard;
using Marshal.Core.Tools;
using Marshal.Testing;

namespace Marshal.Core.Tests.Guard;

public sealed class GuardTests
{
    private const string Kitchen = "assist_satellite.kitchen";

    private const string LockRules = """
        risk:
          - match: { tool: "ha.*", domain: lock }
            tier: confirm
          - match: { tool: "ha.*" }
            tier: safe
        """;

    [Fact]
    public async Task Evaluate_SafeCall_Allowed()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", FakeTool.Safe());

        GuardVerdict verdict = await guard.EvaluateAsync(Request("test_safe"), Ct);

        Assert.Equal(GuardOutcome.Allow, verdict.Outcome);
        Assert.Equal(GuardDecision.Safe, verdict.Decision);
        Assert.Equal(RiskTier.Safe, verdict.Tier);
    }

    [Fact]
    public async Task Evaluate_UnknownName_Denied()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", FakeTool.Safe());

        GuardVerdict verdict = await guard.EvaluateAsync(Request("test_missing"), Ct);

        Assert.Equal(GuardOutcome.Deny, verdict.Outcome);
        Assert.Equal(GuardDecision.Denied, verdict.Decision);
        Assert.Equal("tool.unknown", verdict.Denial?.Code);
        Assert.Null(verdict.Tool);
    }

    [Fact]
    public async Task Evaluate_KnownButNotOffered_Denied()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", FakeTool.Safe(), FakeTool.Safe("test.other"));

        GuardVerdict verdict = await guard.EvaluateAsync(Request("test_other", offered: new HashSet<string> { "test_safe" }), Ct);

        Assert.Equal("tool.not_offered", verdict.Denial?.Code);
    }

    [Fact]
    public async Task Evaluate_ForbiddenAfterListWasBuilt_Denied()
    {
        ToolCallGuard guard = await GuardAsync("""
            risk:
              - match: { tool: "test.safe" }
                tier: forbidden
            """, FakeTool.Safe());

        GuardVerdict verdict = await guard.EvaluateAsync(Request("test_safe"), Ct);

        Assert.Equal(GuardOutcome.Deny, verdict.Outcome);
        Assert.Equal("tool.forbidden", verdict.Denial?.Code);
    }

    [Fact]
    public async Task Evaluate_ArgumentDependentForbidden_Denied()
    {
        ToolCallGuard guard = await GuardAsync("""
            risk:
              - match: { tool: "test.safe", args: { mode: raw } }
                tier: forbidden
            """, FakeTool.Safe());

        GuardVerdict verdict = await guard.EvaluateAsync(Request("test_safe", new { mode = "raw" }), Ct);

        Assert.Equal("tool.forbidden", verdict.Denial?.Code);
        Assert.Equal(RiskTier.Forbidden, verdict.Tier);
    }

    [Fact]
    public async Task Evaluate_DenyToolsOnSatellite_Denied()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", new SatelliteProfile("short", null, ["test.*"], []), FakeTool.Safe());

        GuardVerdict verdict = await guard.EvaluateAsync(Request("test_safe", context: TestExecutionContext.Voice(satelliteId: Kitchen)), Ct);

        Assert.Equal("tool.not_allowed_from_device", verdict.Denial?.Code);
    }

    [Fact]
    public async Task Evaluate_DenyConfirmOnArgAwareCallResolvingToConfirm_DeniedWithoutConfirmation()
    {
        ToolCallGuard guard = await GuardAsync(LockRules, new SatelliteProfile("short", null, [], ["ha.*"]), [Resolver("lock")], FakeTool.Safe("ha.HassTurnOff"));

        GuardVerdict verdict = await guard.EvaluateAsync(Request("ha_HassTurnOff", context: TestExecutionContext.Voice(satelliteId: Kitchen)), Ct);

        Assert.Equal(GuardOutcome.Deny, verdict.Outcome);
        Assert.Equal("tool.not_allowed_from_device", verdict.Denial?.Code);
        Assert.Contains("not allowed from this device", verdict.Denial?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_DenyConfirmOnArgAwareCallResolvingToSafe_Allowed()
    {
        ToolCallGuard guard = await GuardAsync(LockRules, new SatelliteProfile("short", null, [], ["ha.*"]), [Resolver("light")], FakeTool.Safe("ha.HassTurnOff"));

        GuardVerdict verdict = await guard.EvaluateAsync(Request("ha_HassTurnOff", context: TestExecutionContext.Voice(satelliteId: Kitchen)), Ct);

        Assert.Equal(GuardOutcome.Allow, verdict.Outcome);
    }

    [Fact]
    public async Task Evaluate_SubagentCallResolvingToConfirm_DeniedTellingToReportToCaller()
    {
        ToolCallGuard guard = await GuardAsync(LockRules, SatelliteProfile.Default, [Resolver("lock")], FakeTool.Safe("ha.HassTurnOff"));

        GuardVerdict verdict = await guard.EvaluateAsync(Request("ha_HassTurnOff", context: TestExecutionContext.Text() with { Depth = 1 }), Ct);

        Assert.Equal(GuardOutcome.Deny, verdict.Outcome);
        Assert.Equal(RiskTier.Confirm, verdict.Tier);
        Assert.Equal("subagent.confirm_denied", verdict.Denial?.Code);
        Assert.Contains("caller", verdict.Denial?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_SubagentPersistsInTaintedRun_Denied()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", FakeTool.Persisting());
        MarshalExecutionContext context = TestExecutionContext.Text() with { Depth = 1 };
        context.Taint.Mark("mail", 1);

        GuardVerdict verdict = await guard.EvaluateAsync(Request("memory_remember", context: context), Ct);

        Assert.Equal("subagent.confirm_denied", verdict.Denial?.Code);
    }

    [Fact]
    public async Task Evaluate_ConfirmRule_RequiresConfirmationWithRuleReasonNamingAction()
    {
        ToolCallGuard guard = await GuardAsync(LockRules, SatelliteProfile.Default, [Resolver("lock")], FakeTool.Safe("ha.HassTurnOff"));

        GuardVerdict verdict = await guard.EvaluateAsync(Request("ha_HassTurnOff", new { name = "front door" }), Ct);

        Assert.Equal(GuardOutcome.RequireConfirmation, verdict.Outcome);
        Assert.Equal(GuardDecision.Pending, verdict.Decision);
        ConfirmationReason reason = Assert.Single(verdict.Reasons);
        Assert.Equal(ConfirmationReasonCode.Rule, reason.Code);
        Assert.Contains("front door", reason.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_ConfirmingArgumentPresent_RequiresConfirmationWithItsReason()
    {
        FakeTool create = FakeTool.Safe("tasks.create").With(descriptor => descriptor with
        {
            ConfirmingArguments = new Dictionary<string, ConfirmationReasonCode> { ["overrideQuietHoursUntil"] = ConfirmationReasonCode.QuietHoursOverride },
        });
        ToolCallGuard guard = await GuardAsync("risk: []", create);

        GuardVerdict plain = await guard.EvaluateAsync(Request("tasks_create", new { goal = "x", overrideQuietHoursUntil = (string?)null }), Ct);
        GuardVerdict overriding = await guard.EvaluateAsync(Request("tasks_create", new { goal = "x", overrideQuietHoursUntil = "2026-09-30T08:00:00+02:00" }), Ct);

        Assert.Equal(GuardOutcome.Allow, plain.Outcome);
        Assert.Equal(GuardOutcome.RequireConfirmation, overriding.Outcome);
        ConfirmationReason reason = Assert.Single(overriding.Reasons);
        Assert.Equal(ConfirmationReasonCode.QuietHoursOverride, reason.Code);
        Assert.Contains("quiet hours", reason.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_UnresolvedTarget_ReasonLocalized()
    {
        ToolCallGuard guard = await GuardAsync(LockRules, FakeTool.Safe("ha.HassTurnOff"));

        GuardVerdict english = await guard.EvaluateAsync(Request("ha_HassTurnOff"), Ct);
        GuardVerdict german = await guard.EvaluateAsync(Request("ha_HassTurnOff", context: TestExecutionContext.Text(language: "de")), Ct);

        Assert.Equal(ConfirmationReasonCode.UnresolvedTarget, Assert.Single(english.Reasons).Code);
        Assert.Contains("which device", english.Reasons[0].Sentence, StringComparison.Ordinal);
        Assert.Contains("welches Gerät", german.Reasons[0].Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_PersistsInTaintedRun_EscalatedWithTaintReasonNamingMail()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", FakeTool.Persisting());
        MarshalExecutionContext context = TestExecutionContext.Text();
        context.Taint.Mark("mail", 2);

        GuardVerdict verdict = await guard.EvaluateAsync(Request("memory_remember", context: context), Ct);

        Assert.Equal(GuardOutcome.RequireConfirmation, verdict.Outcome);
        Assert.Equal(GuardDecision.EscalatedByTaint, verdict.Decision);
        Assert.Equal(RiskTier.Confirm, verdict.Tier);
        ConfirmationReason reason = Assert.Single(verdict.Reasons);
        Assert.Equal(ConfirmationReasonCode.Taint, reason.Code);
        Assert.Contains("mail", reason.Sentence, StringComparison.Ordinal);
        Assert.Contains("hidden instructions", reason.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_PersistsInUntaintedRun_Allowed()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", FakeTool.Persisting());

        GuardVerdict verdict = await guard.EvaluateAsync(Request("memory_remember"), Ct);

        Assert.Equal(GuardOutcome.Allow, verdict.Outcome);
    }

    [Fact]
    public async Task Evaluate_ConfirmPersistsToolInTaintedRun_TaintSpokenFirst()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", FakeTool.Persisting().With(d => d with { DefaultTier = RiskTier.Confirm }));
        MarshalExecutionContext context = TestExecutionContext.Text(language: "de");
        context.Taint.Mark("rss", 1);
        context.Taint.Mark("mcp:web", 3);

        GuardVerdict verdict = await guard.EvaluateAsync(Request("memory_remember", context: context), Ct);

        Assert.Equal([ConfirmationReasonCode.Taint, ConfirmationReasonCode.Rule], verdict.Reasons.Select(reason => reason.Code));
        Assert.Contains("RSS", verdict.Reasons[0].Sentence, StringComparison.Ordinal);
        Assert.Contains("web", verdict.Reasons[0].Sentence, StringComparison.Ordinal);
        Assert.Equal(GuardDecision.Pending, verdict.Decision);
    }

    [Fact]
    public async Task Evaluate_ForbiddenPersistsToolInTaintedRun_StaysForbidden()
    {
        ToolCallGuard guard = await GuardAsync("""
            risk:
              - match: { tool: "memory.remember", args: { kind: pinned } }
                tier: forbidden
            """, FakeTool.Persisting());
        MarshalExecutionContext context = TestExecutionContext.Text();
        context.Taint.Mark("mail", 1);

        GuardVerdict verdict = await guard.EvaluateAsync(Request("memory_remember", new { kind = "pinned" }, context), Ct);

        Assert.Equal(GuardOutcome.Deny, verdict.Outcome);
        Assert.Equal(RiskTier.Forbidden, verdict.Tier);
    }

    [Fact]
    public async Task Evaluate_ToolConditionFailsAtCallTime_Denied()
    {
        int calls = 0;
        FakeTool limited = FakeTool.Safe("agent.ask_advisor").AvailableWhen(_ => ++calls <= 1);
        ToolCallGuard guard = await GuardAsync("risk: []", limited);

        GuardVerdict first = await guard.EvaluateAsync(Request("agent_ask_advisor"), Ct);
        GuardVerdict second = await guard.EvaluateAsync(Request("agent_ask_advisor"), Ct);

        Assert.Equal(GuardOutcome.Allow, first.Outcome);
        Assert.Equal("tool.unavailable", second.Denial?.Code);
    }

    [Fact]
    public async Task Evaluate_ConfirmOnlyToolOnDenyConfirmSatellite_Denied()
    {
        ToolCallGuard guard = await GuardAsync("risk: []", new SatelliteProfile("short", null, [], ["test.*"]), FakeTool.Confirm());

        GuardVerdict verdict = await guard.EvaluateAsync(Request("test_confirm", context: TestExecutionContext.Voice(satelliteId: Kitchen)), Ct);

        Assert.Equal("tool.not_allowed_from_device", verdict.Denial?.Code);
    }

    [Fact]
    public async Task Evaluate_InvalidArgumentsOfConfirmTool_DeniedBeforeConfirmation()
    {
        FakeTool tool = FakeTool.Confirm().ValidatesWith(arguments => arguments.TryGetProperty("name", out JsonElement name) && name.GetString() == "PARK"
            ? Result.Success()
            : new Error("printer.macro_not_allowed", "Not allowlisted.", ErrorType.Validation));
        ToolCallGuard guard = await GuardAsync("risk: []", tool);

        GuardVerdict invalid = await guard.EvaluateAsync(Request("test_confirm", new { name = "FIRMWARE_RESTART" }), Ct);
        GuardVerdict valid = await guard.EvaluateAsync(Request("test_confirm", new { name = "PARK" }), Ct);

        Assert.Equal((GuardOutcome.Deny, "printer.macro_not_allowed"), (invalid.Outcome, invalid.Denial?.Code));
        Assert.Empty(invalid.Reasons);
        Assert.Equal(GuardOutcome.RequireConfirmation, valid.Outcome);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GuardRequest Request(string name, object? arguments = null, MarshalExecutionContext? context = null, IReadOnlySet<string>? offered = null) =>
        new(name, JsonSerializer.SerializeToElement(arguments ?? new { }), context ?? TestExecutionContext.Text(), offered ?? new HashSet<string> { name });

    private static Task<ToolCallGuard> GuardAsync(string rules, params ITool[] tools) => GuardAsync(rules, SatelliteProfile.Default, [], tools);

    private static Task<ToolCallGuard> GuardAsync(string rules, SatelliteProfile kitchen, params ITool[] tools) => GuardAsync(rules, kitchen, [], tools);

    private static async Task<ToolCallGuard> GuardAsync(string rules, SatelliteProfile kitchen, IRiskTargetResolver[] resolvers, params ITool[] tools)
    {
        var registry = new ToolRegistry([new FakeToolSource("test", true, tools)]);
        await registry.RefreshAsync(Ct);
        RuleBasedRiskEngine engine = GuardTestKit.Engine(rules, resolvers);
        var satellites = new FixedSatellites(Kitchen, kitchen);
        return new ToolCallGuard(registry, new ToolVisibilityFilter(registry, engine, satellites, new FakeTemplateCatalog()), engine, satellites);
    }

    private static FakeTargetResolver Resolver(string domain) => new("ha", new RiskTarget($"{domain}.x", new Dictionary<string, string> { ["domain"] = domain }));
}
