using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Demo.Tests;

public sealed class DemoOptionsTests
{
    [Fact]
    public void NoSection_DemoOffBadgeOn()
    {
        DemoOptions options = Bind([]);

        Assert.Equal((false, true), (options.Enabled, options.ShowBadge));
    }

    [Fact]
    public void Section_Bound()
    {
        DemoOptions options = Bind(new() { ["demo:enabled"] = "true", ["demo:showBadge"] = "false" });

        Assert.Equal((true, false), (options.Enabled, options.ShowBadge));
    }

    private static DemoOptions Bind(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using ServiceProvider provider = new ServiceCollection().AddMarshalDemo(configuration).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<DemoOptions>>().Value;
    }
}
