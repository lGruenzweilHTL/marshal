using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class HaTargetResolverTests
{
    private readonly FixtureCache _cache = new();

    [Theory]
    [InlineData("Bed Light")]
    [InlineData("bed light")]
    [InlineData("Nachtlicht")]
    public async Task Name_EntityNameOrAlias_ResolvesEntity(string name)
    {
        IReadOnlyList<RiskTarget> targets = await ResolveAsync("intent__HassTurnOn", new { name });

        RiskTarget target = Assert.Single(targets);
        Assert.Equal("light.bed_light", target.Id);
        Assert.Equal("light", target.Attributes["domain"]);
    }

    [Theory]
    [InlineData("Hall")]
    [InlineData("flur")]
    [InlineData("hall")]
    public async Task Area_ExpandsToEveryExposedEntityInIt(string area)
    {
        IReadOnlyList<RiskTarget> targets = await ResolveAsync("intent__HassTurnOff", new { area });

        Assert.Equal(
            ["cover.garage_door", "light.ceiling_lights", "light.entrance_color_white_lights", "lock.hall_door"],
            targets.Select(target => target.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AreaAndDomain_OnlyThatDomain()
    {
        IReadOnlyList<RiskTarget> targets = await ResolveAsync("intent__HassTurnOff", new { area = "Hall", domain = new[] { "light" } });

        Assert.Equal(["light.ceiling_lights", "light.entrance_color_white_lights"], targets.Select(target => target.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Floor_ExpandsToItsAreas()
    {
        IReadOnlyList<RiskTarget> targets = await ResolveAsync("intent__HassTurnOff", new { floor = "Obergeschoss", domain = new[] { "light" } });

        Assert.Equal("light.bed_light", Assert.Single(targets).Id);
    }

    [Fact]
    public async Task DomainOnly_EveryExposedEntityOfTheDomain()
    {
        IReadOnlyList<RiskTarget> targets = await ResolveAsync("intent__HassTurnOff", new { domain = "lock" });

        Assert.Equal("lock.hall_door", Assert.Single(targets).Id);
    }

    [Fact]
    public async Task DeviceClass_FiltersAndIsReported()
    {
        IReadOnlyList<RiskTarget> targets = await ResolveAsync("intent__HassTurnOff", new { domain = new[] { "cover" }, device_class = new[] { "garage" } });

        RiskTarget target = Assert.Single(targets);
        Assert.Equal("cover.garage_door", target.Id);
        Assert.Equal("garage", target.Attributes["deviceClass"]);
    }

    [Theory]
    [InlineData("""{"name":"Front Door"}""")]
    [InlineData("""{"name":"Does Not Exist"}""")]
    [InlineData("""{"area":"Garden"}""")]
    [InlineData("""{"floor":"Attic"}""")]
    [InlineData("""{"area":"Bedroom","domain":["lock"]}""")]
    [InlineData("""{"name":42}""")]
    public async Task Unresolvable_IsFailure(string arguments)
    {
        Result<IReadOnlyList<RiskTarget>> result = await new HaTargetResolver(_cache).ResolveAsync(new ToolId("ha", "intent__HassTurnOn"), JsonDocument.Parse(arguments).RootElement, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CacheNotReady_IsFailure()
    {
        _cache.Current = null;

        Result<IReadOnlyList<RiskTarget>> result = await new HaTargetResolver(_cache).ResolveAsync(new ToolId("ha", "intent__HassTurnOn"), JsonSerializer.SerializeToElement(new { name = "Bed Light" }), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task NoTargetArguments_SingleUntargetedTarget()
    {
        IReadOnlyList<RiskTarget> targets = await ResolveAsync("homeassistant__GetLiveContext", new { });

        Assert.Empty(Assert.Single(targets).Attributes);
    }

    [Theory]
    [InlineData("intent__HassTurnOff", """{"area":"Hall"}""", RiskTier.Confirm)]
    [InlineData("intent__HassTurnOff", """{"area":"Hall","domain":["light"]}""", RiskTier.Safe)]
    [InlineData("intent__HassTurnOn", """{"name":"Nachtlicht"}""", RiskTier.Safe)]
    [InlineData("intent__HassTurnOff", """{"name":"Garage Door"}""", RiskTier.Confirm)]
    [InlineData("intent__HassSetPosition", """{"name":"Kitchen Window","position":50}""", RiskTier.Safe)]
    [InlineData("homeassistant__GetLiveContext", "{}", RiskTier.Safe)]
    [InlineData("intent__HassTurnOn", """{"name":"Front Door"}""", RiskTier.Confirm)]
    public async Task DefaultRiskRules_WithResolver(string tool, string arguments, RiskTier expected)
    {
        using var rules = YamlConfigFile.Load<RiskRulesFile>(Path.Combine(AppContext.BaseDirectory, "risk.yaml"), new ConfigSecretScanner(), RiskRulesFile.Validate, NullLogger.Instance, watch: false);
        var engine = new RuleBasedRiskEngine(rules, [new HaTargetResolver(_cache)]);
        var registered = new RegisteredTool(new FakeTool($"ha.{tool}"), $"ha_{tool}", "ha", IsBuiltIn: false);

        RiskAssessment assessment = await engine.AssessAsync(registered, JsonDocument.Parse(arguments).RootElement, TestContext.Current.CancellationToken);

        Assert.Equal(expected, assessment.Tier);
    }

    private async Task<IReadOnlyList<RiskTarget>> ResolveAsync(string tool, object arguments)
    {
        Result<IReadOnlyList<RiskTarget>> result = await new HaTargetResolver(_cache).ResolveAsync(new ToolId("ha", tool), JsonSerializer.SerializeToElement(arguments), TestContext.Current.CancellationToken);
        return result.Value;
    }
}
