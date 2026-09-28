using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Testing;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class HaAreaContextContributorTests
{
    private readonly FixtureCache _cache = new();

    [Fact]
    public async Task KitchenTurn_ListsKitchenStatesOnly()
    {
        string text = (await ContributeAsync(TestExecutionContext.Voice(areaId: "kitchen"), "turn off the light"))!;

        Assert.Contains("Kitchen Lights (light.kitchen_lights): on", text, StringComparison.Ordinal);
        Assert.Contains("Kitchen Window (cover.kitchen_window): closed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("light.ceiling_lights", text, StringComparison.Ordinal);
        Assert.DoesNotContain("light.bed_light", text, StringComparison.Ordinal);
        Assert.Contains("GetLiveContext", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MentionedEntity_ListedWithItsArea()
    {
        string text = (await ContributeAsync(TestExecutionContext.Voice(areaId: "kitchen"), "Is the nachtlicht still on?"))!;

        Assert.Contains("Bed Light (light.bed_light, Bedroom): on", text, StringComparison.Ordinal);
        Assert.DoesNotContain("light.ceiling_lights", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoAreaNoMention_NoSection()
    {
        Assert.Null(await ContributeAsync(TestExecutionContext.Text(), "What is the capital of Austria?"));
    }

    [Fact]
    public async Task MentionedNonExposedEntity_NotListed()
    {
        Assert.Null(await ContributeAsync(TestExecutionContext.Text(), "Is the front door locked?"));
    }

    [Fact]
    public async Task CacheNotReady_NoSection()
    {
        _cache.Current = null;

        Assert.Null(await ContributeAsync(TestExecutionContext.Voice(areaId: "kitchen"), "turn off the light"));
    }

    [Fact]
    public async Task AreaDirectory_NamesAreaAndFloor()
    {
        var directory = new HaAreaDirectory(_cache);

        Assert.Equal(("Kitchen", "Ground Floor"), await directory.DescribeAsync("kitchen", TestContext.Current.CancellationToken));
        Assert.Equal(("garden", null), await directory.DescribeAsync("garden", TestContext.Current.CancellationToken));
    }

    private Task<string?> ContributeAsync(MarshalExecutionContext context, string text) =>
        new HaAreaContextContributor(_cache).ContributeAsync(new ContextRequest(context, text), TestContext.Current.CancellationToken);
}
