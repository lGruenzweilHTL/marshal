using System.Net;
using System.Text;
using Marshal.Abstractions.Rss;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.FreshRss.Tests.Client;

public sealed class FreshRssTokenTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RejectedToken_IsRenewedOnce()
    {
        var server = new ScriptedServer(expiredAfterFirstLogin: true);
        var client = new FreshRssClient(new PlainHttpClientFactory(server), Options.Create(Alice()));

        IReadOnlyList<RssCategory> categories = (await client.ListCategoriesAsync(Ct)).Value;

        Assert.Equal("Tech", Assert.Single(categories).Label);
        Assert.Equal(2, server.Logins);
    }

    [Fact]
    public async Task TokenReused_AcrossCalls()
    {
        var server = new ScriptedServer(expiredAfterFirstLogin: false);
        var client = new FreshRssClient(new PlainHttpClientFactory(server), Options.Create(Alice()));

        await client.ListCategoriesAsync(Ct);
        await client.ListCategoriesAsync(Ct);

        Assert.Equal(1, server.Logins);
    }

    private static FreshRssOptions Alice() => new() { Url = new Uri("http://freshrss.test/"), UserName = "alice", ApiPassword = "api" };

    private sealed class ScriptedServer(bool expiredAfterFirstLogin) : HttpMessageHandler
    {
        public int Logins { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/accounts/ClientLogin", StringComparison.Ordinal))
            {
                Logins++;
                return Task.FromResult(Text($"SID=x\nLSID=null\nAuth=alice/token{Logins}\n"));
            }

            string? auth = request.Headers.Authorization?.Parameter;
            if (expiredAfterFirstLogin && auth == "auth=alice/token1")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

            return Task.FromResult(Text("""{"tags":[{"id":"user/-/label/Tech","type":"folder"}]}"""));
        }

        private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
    }
}
