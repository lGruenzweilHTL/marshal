using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Integrations;

public sealed class MediaHostTests
{
    [Fact]
    public async Task NotConfigured_NoMediaTools()
    {
        using var factory = new MarshalApiFactory();
        _ = factory.CreateClient();

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);

        Assert.DoesNotContain(tools.Tools, tool => tool.Id.Namespace == "media");
    }

    [Fact]
    public async Task Configured_MediaToolsWithTiersAndMediaTaint()
    {
        using var factory = new MarshalApiFactory(new()
        {
            ["media:seerr:url"] = "https://seerr.lan/",
            ["media:seerr:apiKeySecret"] = "seerr_api_key",
        });
        _ = factory.CreateClient();

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);

        RegisteredTool[] media = [.. tools.Tools.Where(tool => tool.Id.Namespace == "media")];
        Assert.Equal(["media.downloads", "media.request", "media.requests", "media.search", "media.upcoming"], media.Select(tool => tool.Id.ToString()).Order(StringComparer.Ordinal));
        Assert.Equal(RiskTier.Confirm, media.Single(tool => tool.Id.Name == "request").Descriptor.DefaultTier);
        Assert.All(media.Where(tool => tool.Id.Name != "request"), tool => Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier));
        Assert.All(media, tool => Assert.True(tool.Descriptor.UntrustedOutput));
        Assert.All(media, tool => Assert.Equal("media", tool.Descriptor.TaintKind ?? tool.Id.Namespace));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
