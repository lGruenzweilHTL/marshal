using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Marshal.ExecutorHost.Tests;

public sealed class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_Ok()
    {
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
