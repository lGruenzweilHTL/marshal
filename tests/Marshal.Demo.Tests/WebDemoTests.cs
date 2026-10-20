using Marshal.Abstractions.Tools;
using Marshal.Abstractions.Web;

namespace Marshal.Demo.Tests;

public sealed class WebDemoTests
{
    [Fact]
    public async Task DemoOff_WebSearchStaysUnconfigured()
    {
        await using var demo = DemoServices.Create(demo: false);

        Assert.False(demo.Get<IWebSearchProvider>().IsConfigured);
    }

    [Fact]
    public async Task NothingConfigured_WebSearchSimulatedWithoutNetwork()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string found = await DemoServices.InvokeAsync(tools["web.search"], """{"query":"christmas market","limit":2}""");

        Assert.Contains(DemoIntegration.Web, demo.Get<DemoIntegrations>().SimulatedAtStartup);
        Assert.Contains("opens on 20 November", found, StringComparison.Ordinal);
        Assert.Contains("en.wikipedia.org", found, StringComparison.Ordinal);
        Assert.DoesNotContain("Voron", found, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearxngConfigured_DemoStepsAside()
    {
        await using var demo = DemoServices.Create(new Dictionary<string, string?> { ["web:searxng:url"] = "http://searxng.invalid" });

        Assert.DoesNotContain(DemoIntegration.Web, demo.Get<DemoIntegrations>().SimulatedAtStartup);
    }
}
