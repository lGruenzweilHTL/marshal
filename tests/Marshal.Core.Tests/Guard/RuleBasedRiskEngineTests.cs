using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;
using Marshal.Core.Guard;
using Marshal.Testing;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Guard;

public sealed class RuleBasedRiskEngineTests
{
    private const string HaRules = """
        risk:
          - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
            tier: confirm
          - match: { tool: "ha.*", domain: cover, deviceClass: [garage, door, gate] }
            tier: confirm
          - match: { tool: "ha.*" }
            tier: safe
        """;

    private static readonly RegisteredTool _haTurnOff = External(FakeTool.Safe("ha.HassTurnOff"));

    [Fact]
    public async Task Assess_NoMatchingRule_UsesDescriptorDefault()
    {
        RuleBasedRiskEngine engine = Engine("risk: []");

        RiskAssessment safe = await engine.AssessAsync(BuiltIn(FakeTool.Safe("test.safe")), Args(), Ct);
        RiskAssessment confirm = await engine.AssessAsync(BuiltIn(FakeTool.Confirm("test.confirm")), Args(), Ct);

        Assert.Equal(RiskTier.Safe, safe.Tier);
        Assert.Empty(safe.Reasons);
        Assert.Equal(RiskTier.Confirm, confirm.Tier);
        Assert.Equal([ConfirmationReasonCode.Rule], confirm.Reasons);
    }

    [Fact]
    public async Task Assess_ExternalToolWithoutRule_DefaultsToConfirm()
    {
        RuleBasedRiskEngine engine = Engine("risk: []");
        RegisteredTool tool = External(FakeTool.Safe("search.query"));

        RiskAssessment result = await engine.AssessAsync(tool, Args(), Ct);

        Assert.Equal(RiskTier.Confirm, result.Tier);
        Assert.Equal([ConfirmationReasonCode.Rule], result.Reasons);
        Assert.Equal([RiskTier.Confirm], engine.GetProfile(tool).PossibleTiers);
    }

    [Fact]
    public async Task Assess_ExternalToolWithSafeRule_IsSafe()
    {
        RuleBasedRiskEngine engine = Engine("""
            risk:
              - match: { tool: "search.*" }
                tier: safe
            """);

        RiskAssessment result = await engine.AssessAsync(External(FakeTool.Safe("search.query")), Args(), Ct);

        Assert.Equal(RiskTier.Safe, result.Tier);
    }

    [Fact]
    public async Task Assess_ExternalToolWithForbiddenDefault_StaysForbidden()
    {
        RuleBasedRiskEngine engine = Engine("risk: []");

        RiskAssessment result = await engine.AssessAsync(External(FakeTool.Forbidden("search.wipe")), Args(), Ct);

        Assert.Equal(RiskTier.Forbidden, result.Tier);
    }

    [Fact]
    public async Task Assess_GlobMatch_AppliesRuleTier()
    {
        RuleBasedRiskEngine engine = Engine("""
            risk:
              - match: { tool: "mail.*" }
                tier: forbidden
            """);

        RiskAssessment result = await engine.AssessAsync(BuiltIn(FakeTool.Safe("mail.search")), Args(), Ct);

        Assert.Equal(RiskTier.Forbidden, result.Tier);
    }

    [Fact]
    public async Task Assess_FirstMatchWins()
    {
        RuleBasedRiskEngine engine = Engine("""
            risk:
              - match: { tool: "test.*" }
                tier: safe
              - match: { tool: "test.confirm" }
                tier: forbidden
            """);

        RiskAssessment result = await engine.AssessAsync(BuiltIn(FakeTool.Confirm("test.confirm")), Args(), Ct);

        Assert.Equal(RiskTier.Safe, result.Tier);
    }

    [Fact]
    public async Task Assess_TargetRule_LockIsConfirm()
    {
        RuleBasedRiskEngine engine = Engine(HaRules, Resolver(Target("lock.front", "lock")));

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal(RiskTier.Confirm, result.Tier);
        Assert.Equal([ConfirmationReasonCode.Rule], result.Reasons);
    }

    [Fact]
    public async Task Assess_AreaWithLightsAndLock_HighestTierWins()
    {
        RuleBasedRiskEngine engine = Engine(HaRules, Resolver(Target("light.a", "light"), Target("light.b", "light"), Target("lock.hall", "lock")));

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal(RiskTier.Confirm, result.Tier);
    }

    [Fact]
    public async Task Assess_OnlyLights_IsSafe()
    {
        RuleBasedRiskEngine engine = Engine(HaRules, Resolver(Target("light.a", "light"), Target("cover.blinds", "cover", "blind")));

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal(RiskTier.Safe, result.Tier);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public async Task Assess_CoverWithGarageDeviceClass_AllConditionsMustMatch()
    {
        RuleBasedRiskEngine engine = Engine(HaRules, Resolver(Target("cover.garage", "cover", "garage")));

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal(RiskTier.Confirm, result.Tier);
    }

    [Fact]
    public async Task Assess_ForbiddenTargetAmongSafe_Forbidden()
    {
        RuleBasedRiskEngine engine = Engine("""
            risk:
              - match: { tool: "ha.*", domain: alarm_control_panel }
                tier: forbidden
              - match: { tool: "ha.*" }
                tier: safe
            """, Resolver(Target("light.a", "light"), Target("alarm.home", "alarm_control_panel")));

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal(RiskTier.Forbidden, result.Tier);
    }

    [Fact]
    public async Task Assess_UnresolvableTarget_ConfirmWithUnresolvedTarget()
    {
        RuleBasedRiskEngine engine = Engine(HaRules, new FakeResolver("ha", new Error("ha.unknown_entity", "unknown")));

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal(RiskTier.Confirm, result.Tier);
        Assert.Equal([ConfirmationReasonCode.UnresolvedTarget], result.Reasons);
    }

    [Fact]
    public async Task Assess_EmptyTargetSet_ConfirmWithUnresolvedTarget()
    {
        RuleBasedRiskEngine engine = Engine(HaRules, Resolver());

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal([ConfirmationReasonCode.UnresolvedTarget], result.Reasons);
    }

    [Fact]
    public async Task Assess_NoResolverForNamespace_ConfirmWithUnresolvedTarget()
    {
        RuleBasedRiskEngine engine = Engine(HaRules);

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal(RiskTier.Confirm, result.Tier);
        Assert.Equal([ConfirmationReasonCode.UnresolvedTarget], result.Reasons);
    }

    [Fact]
    public async Task Assess_ResolverThrows_FailsClosed()
    {
        RuleBasedRiskEngine engine = Engine(HaRules, new ThrowingResolver());

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal([ConfirmationReasonCode.UnresolvedTarget], result.Reasons);
    }

    [Fact]
    public async Task Assess_ResolverOfOtherNamespace_NotUsed()
    {
        RuleBasedRiskEngine engine = Engine(HaRules, new FakeResolver("printer", Result.Success<IReadOnlyList<RiskTarget>>([Target("light.a", "light")])));

        RiskAssessment result = await engine.AssessAsync(_haTurnOff, Args(), Ct);

        Assert.Equal([ConfirmationReasonCode.UnresolvedTarget], result.Reasons);
    }

    [Fact]
    public async Task Assess_ArgumentCondition_MatchesArgumentValue()
    {
        RuleBasedRiskEngine engine = Engine("""
            risk:
              - match: { tool: "printer.run_macro", args: { name: [cancel_print, firmware_restart] } }
                tier: forbidden
              - match: { tool: "printer.run_macro" }
                tier: confirm
            """);
        RegisteredTool macro = BuiltIn(FakeTool.Safe("printer.run_macro"));

        RiskAssessment forbidden = await engine.AssessAsync(macro, Args(new { name = "FIRMWARE_RESTART" }), Ct);
        RiskAssessment confirm = await engine.AssessAsync(macro, Args(new { name = "PARK" }), Ct);
        RiskAssessment missing = await engine.AssessAsync(macro, Args(), Ct);

        Assert.Equal(RiskTier.Forbidden, forbidden.Tier);
        Assert.Equal(RiskTier.Confirm, confirm.Tier);
        Assert.Equal(RiskTier.Confirm, missing.Tier);
    }

    [Fact]
    public async Task Assess_ArgumentConditionOnNumber_ComparesText()
    {
        RuleBasedRiskEngine engine = Engine("""
            risk:
              - match: { tool: "test.*", args: { level: [5] } }
                tier: confirm
            """);

        RiskAssessment result = await engine.AssessAsync(BuiltIn(FakeTool.Safe("test.set")), Args(new { level = 5 }), Ct);

        Assert.Equal(RiskTier.Confirm, result.Tier);
    }

    [Fact]
    public async Task Assess_RulesReload_NextCallUsesNewRules()
    {
        var monitor = new MutableMonitor(Parse("risk: []"));
        var engine = new RuleBasedRiskEngine(monitor, []);
        RegisteredTool tool = BuiltIn(FakeTool.Safe("test.safe"));
        RiskAssessment before = await engine.AssessAsync(tool, Args(), Ct);

        monitor.Value = Parse("""
            risk:
              - match: { tool: "test.safe" }
                tier: forbidden
            """);
        RiskAssessment after = await engine.AssessAsync(tool, Args(), Ct);

        Assert.Equal(RiskTier.Safe, before.Tier);
        Assert.Equal(RiskTier.Forbidden, after.Tier);
    }

    [Fact]
    public void Profile_StaticRule_SingleTier()
    {
        RuleBasedRiskEngine engine = Engine("""
            risk:
              - match: { tool: "mail.*" }
                tier: forbidden
            """);

        RiskProfile profile = engine.GetProfile(BuiltIn(FakeTool.Safe("mail.search")));

        Assert.Equal([RiskTier.Forbidden], profile.PossibleTiers);
        Assert.False(profile.IsArgumentDependent);
    }

    [Fact]
    public void Profile_NoRule_DefaultTierOnly()
    {
        RiskProfile profile = Engine("risk: []").GetProfile(BuiltIn(FakeTool.Confirm()));

        Assert.Equal([RiskTier.Confirm], profile.PossibleTiers);
    }

    [Fact]
    public void Profile_TargetRules_ArgumentDependentIncludingUnresolvedConfirm()
    {
        RiskProfile profile = Engine(HaRules).GetProfile(_haTurnOff);

        Assert.True(profile.IsArgumentDependent);
        Assert.Equal(new HashSet<RiskTier> { RiskTier.Safe, RiskTier.Confirm }, profile.PossibleTiers);
    }

    [Fact]
    public void Profile_ConditionalRuleWithoutFallbackRule_IncludesDefault()
    {
        RiskProfile profile = Engine("""
            risk:
              - match: { tool: "test.*", args: { level: [5] } }
                tier: forbidden
            """).GetProfile(BuiltIn(FakeTool.Safe("test.set")));

        Assert.Equal(new HashSet<RiskTier> { RiskTier.Safe, RiskTier.Forbidden }, profile.PossibleTiers);
    }

    [Theory]
    [InlineData("risk:\n  - match: { domain: lock }\n    tier: confirm\n", "match.tool")]
    [InlineData("risk:\n  - match: { tool: \"ha.*\" }\n    tier: maybe\n", "tier")]
    [InlineData("risk:\n  - match: { tool: \"ha.*\", domain: { nested: x } }\n    tier: safe\n", "domain")]
    [InlineData("risk:\n  - match: { tool: \"ha.*\", args: [x] }\n    tier: safe\n", "args")]
    public void Validate_InvalidRule_NamesProblem(string yaml, string expected)
    {
        Result result = RiskRulesFile.Validate(Parse(yaml));

        Assert.True(result.IsFailure);
        Assert.Contains(expected, result.Error.Message, StringComparison.Ordinal);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RuleBasedRiskEngine Engine(string yaml, params IRiskTargetResolver[] resolvers) =>
        new(new MutableMonitor(Parse(yaml)), resolvers);

    private static RiskRulesFile Parse(string yaml) =>
        new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .Build()
            .Deserialize<RiskRulesFile>(yaml);

    private static RegisteredTool BuiltIn(FakeTool tool) => new(tool, tool.Descriptor.Id.ToString().Replace('.', '_'), "built-in", true);

    private static RegisteredTool External(FakeTool tool) => new(tool, tool.Descriptor.Id.ToString().Replace('.', '_'), "mcp", false);

    private static JsonElement Args(object? value = null) => JsonSerializer.SerializeToElement(value ?? new { });

    private static RiskTarget Target(string id, string domain, string? deviceClass = null)
    {
        var attributes = new Dictionary<string, string> { ["domain"] = domain };
        if (deviceClass is not null)
            attributes["deviceClass"] = deviceClass;

        return new RiskTarget(id, attributes);
    }

    private static FakeResolver Resolver(params RiskTarget[] targets) => new("ha", Result.Success<IReadOnlyList<RiskTarget>>(targets));

    private sealed class FakeResolver(string ns, Result<IReadOnlyList<RiskTarget>> result) : IRiskTargetResolver
    {
        public string Namespace => ns;

        public Task<Result<IReadOnlyList<RiskTarget>>> ResolveAsync(ToolId tool, JsonElement arguments, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingResolver : IRiskTargetResolver
    {
        public string Namespace => "ha";

        public Task<Result<IReadOnlyList<RiskTarget>>> ResolveAsync(ToolId tool, JsonElement arguments, CancellationToken cancellationToken) =>
            throw new HttpRequestException("down");
    }

    private sealed class MutableMonitor(RiskRulesFile value) : IOptionsMonitor<RiskRulesFile>
    {
        public RiskRulesFile Value { get; set; } = value;

        public RiskRulesFile CurrentValue => Value;

        public RiskRulesFile Get(string? name) => Value;

        public IDisposable? OnChange(Action<RiskRulesFile, string?> listener) => null;
    }
}

public sealed class RuleBasedRiskEngineEdgeTests
{
    private const string ArgRule = """
        risk:
          - match: { tool: "test.*", args: { mode: [raw] } }
            tier: forbidden
        """;

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("""{"mode":{"nested":"raw"}}""")]
    [InlineData("""{"mode":null}""")]
    [InlineData("""{"mode":["raw"]}""")]
    [InlineData("""{"other":"raw"}""")]
    public async Task Assess_ArgumentNotAMatchingScalar_RuleDoesNotApply(string arguments)
    {
        RuleBasedRiskEngine engine = GuardTestKit.Engine(ArgRule);

        RiskAssessment result = await engine.AssessAsync(Tool(), System.Text.Json.JsonDocument.Parse(arguments).RootElement, TestContext.Current.CancellationToken);

        Assert.Equal(RiskTier.Safe, result.Tier);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("\"RAW\"")]
    public async Task Assess_BooleanAndCaseInsensitiveArgument_Matched(string value)
    {
        RuleBasedRiskEngine engine = GuardTestKit.Engine("""
            risk:
              - match: { tool: "test.*", args: { mode: [raw, "true"] } }
                tier: forbidden
            """);

        RiskAssessment result = await engine.AssessAsync(Tool(), System.Text.Json.JsonDocument.Parse($$"""{"mode":{{value}}}""").RootElement, TestContext.Current.CancellationToken);

        Assert.Equal(RiskTier.Forbidden, result.Tier);
    }

    [Fact]
    public async Task Assess_TargetLacksConditionAttribute_RuleDoesNotApply()
    {
        RuleBasedRiskEngine engine = GuardTestKit.Engine("""
            risk:
              - match: { tool: "test.*", domain: cover, deviceClass: garage }
                tier: confirm
            """, new FakeTargetResolver("test", new RiskTarget("cover.x", new Dictionary<string, string> { ["domain"] = "cover" })));

        RiskAssessment result = await engine.AssessAsync(Tool(), System.Text.Json.JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);

        Assert.Equal(RiskTier.Safe, result.Tier);
    }

    [Fact]
    public async Task Assess_TargetRuleWithArgumentCondition_BothMustMatch()
    {
        RuleBasedRiskEngine engine = GuardTestKit.Engine("""
            risk:
              - match: { tool: "test.*", domain: lock, args: { action: open } }
                tier: forbidden
            """, new FakeTargetResolver("test", new RiskTarget("lock.x", new Dictionary<string, string> { ["domain"] = "lock" })));

        RiskAssessment open = await engine.AssessAsync(Tool(), System.Text.Json.JsonDocument.Parse("""{"action":"open"}""").RootElement, TestContext.Current.CancellationToken);
        RiskAssessment close = await engine.AssessAsync(Tool(), System.Text.Json.JsonDocument.Parse("""{"action":"close"}""").RootElement, TestContext.Current.CancellationToken);

        Assert.Equal(RiskTier.Forbidden, open.Tier);
        Assert.Equal(RiskTier.Safe, close.Tier);
    }

    [Fact]
    public void Validate_ValidFile_Succeeds()
    {
        Assert.True(RiskRulesFile.Validate(GuardTestKit.ParseRules(ArgRule)).IsSuccess);
    }

    [Theory]
    [InlineData("risk:\n  - match: { tool: \"x.*\", args: { mode: { a: b } } }\n    tier: safe\n", "match.args.mode")]
    [InlineData("risk:\n  - match: { tool: \"x.*\", domain: [] }\n    tier: safe\n", "match.domain")]
    [InlineData("risk:\n  - match: { tool: \"x.*\", domain: [lock, { a: b }] }\n    tier: safe\n", "match.domain")]
    [InlineData("risk:\n  - match: { tool: \"x.*\" }\n    tier: \"1\"\n", "tier")]
    [InlineData("risk:\n  - match: { tool: [a] }\n    tier: safe\n", "match.tool")]
    public void Validate_InvalidShapes_Fail(string yaml, string expected)
    {
        Abstractions.Result result = RiskRulesFile.Validate(GuardTestKit.ParseRules(yaml));

        Assert.Contains(expected, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assess_InvalidCurrentRules_FailsClosedToForbidden()
    {
        var engine = new RuleBasedRiskEngine(new StaticMonitor<RiskRulesFile>(GuardTestKit.ParseRules("risk:\n  - match: { domain: lock }\n    tier: safe\n")), []);

        RiskAssessment result = await engine.AssessAsync(Tool(), System.Text.Json.JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);

        Assert.Equal(RiskTier.Forbidden, result.Tier);
    }

    private static RegisteredTool Tool() => new(FakeTool.Safe("test.do"), "test_do", "test", true);
}
