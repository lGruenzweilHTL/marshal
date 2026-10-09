using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Proxmox.Tests;

public sealed class ProxmoxOptionsTests
{
    [Fact]
    public void Valid_Succeeds()
    {
        Assert.Null(Failure(Valid()));
    }

    [Fact]
    public void NotConfigured_Succeeds()
    {
        Assert.Null(Failure([]));
    }

    [Theory]
    [InlineData("proxmox:url", "http://pve.lan:8006/", "proxmox.url ")]
    [InlineData("proxmox:tokenId", "marshal", "proxmox.tokenId ")]
    [InlineData("proxmox:tokenSecret", "", "proxmox.tokenSecret ")]
    [InlineData("proxmox:certificateFingerprint", "AB:CD", "proxmox.certificateFingerprint ")]
    public void Invalid_FailsNamingTheKey(string key, string value, string expected)
    {
        Dictionary<string, string?> settings = Valid();
        settings[key] = value;

        Assert.StartsWith(expected, Failure(settings), StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Valid() => new()
    {
        ["proxmox:url"] = "https://pve.lan:8006/",
        ["proxmox:tokenId"] = "marshal@pve!marshal",
        ["proxmox:tokenSecret"] = "proxmox_token",
        ["proxmox:certificateFingerprint"] = new string('A', 64),
    };

    private static string? Failure(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using ServiceProvider services = new ServiceCollection().AddMarshalProxmox(configuration).BuildServiceProvider();
        try
        {
            _ = services.GetRequiredService<IOptions<ProxmoxOptions>>().Value;
            return null;
        }
        catch (OptionsValidationException failure)
        {
            return failure.Failures.First();
        }
    }
}
