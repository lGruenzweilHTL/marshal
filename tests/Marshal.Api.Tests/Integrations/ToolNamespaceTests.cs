using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Demo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Integrations;

public sealed class ToolNamespaceTests
{
    [Fact]
    public async Task BuiltInSources_DescribeEveryNamespaceTheyUse()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["demo:enabled"] = "true";
        IConfiguration demo = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["demo:enabled"] = "true" }).Build();
        await using var factory = new MarshalApiFactory(settings) { TestServices = services => services.AddMarshalDemo(demo) };
        await AuthFactory.WriteClientSecretAsync(factory);
        _ = factory.Server;

        var missing = new List<string>();
        foreach (IToolSource source in factory.Services.GetServices<IToolSource>())
        {
            HashSet<string> described = [.. source.Namespaces.Where(item => !string.IsNullOrWhiteSpace(item.Description)).Select(item => item.Name)];
            foreach (ITool tool in await source.GetToolsAsync(TestContext.Current.CancellationToken))
            {
                if (!described.Contains(tool.Descriptor.Id.Namespace))
                    missing.Add($"{source.Name}: {tool.Descriptor.Id.Namespace}");
            }
        }

        Assert.Empty(missing.Distinct());
    }
}
