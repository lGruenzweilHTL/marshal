using System.Net;

namespace Marshal.ExecutorHost.Tests;

public sealed class HealthTests
{
    [Fact]
    public async Task Health_Ok()
    {
        using var host = new HostFactory();

        HttpResponseMessage response = await host.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
