using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Integrations;

public sealed class ProxmoxHostTests
{
    [Fact]
    public async Task NotConfigured_NoInfraTools()
    {
        using var factory = new MarshalApiFactory();
        _ = factory.CreateClient();

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);

        Assert.DoesNotContain(tools.Tools, tool => tool.Id.Namespace == "infra");
    }

    [Fact]
    public async Task Configured_InfraToolsSafeAndTrusted()
    {
        using var factory = new MarshalApiFactory(new()
        {
            ["proxmox:url"] = "https://pve.lan:8006/",
            ["proxmox:tokenId"] = "marshal@pve!marshal",
            ["proxmox:tokenSecret"] = "proxmox_token",
        });
        _ = factory.CreateClient();

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);

        RegisteredTool[] infra = [.. tools.Tools.Where(tool => tool.Id.Namespace == "infra")];
        Assert.Equal(["infra.guests", "infra.overview", "infra.storage", "infra.tasks"], infra.Select(tool => tool.Id.ToString()).Order(StringComparer.Ordinal));
        Assert.All(infra, tool => Assert.Equal((RiskTier.Safe, false), (tool.Descriptor.DefaultTier, tool.Descriptor.UntrustedOutput)));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
