using System.Net.Http.Headers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(Marshal.Integrations.FreshRss.Tests.Client.FreshRssServer))]

namespace Marshal.Integrations.FreshRss.Tests.Client;

/// <summary>
/// The official FreshRSS container with the API enabled. Two test feeds are served by the container itself
/// (<c>http://localhost/feeds/…</c>), so no test reaches the internet. Every test gets its own user, subscribed to
/// both feeds, so read states never overlap.
/// </summary>
public sealed class FreshRssServer : IAsyncLifetime
{
    public const string ApiPassword = "api-secret";

    private const string Root = "/var/www/FreshRSS";

    private readonly IContainer _container = new ContainerBuilder("freshrss/freshrss:1.27.1")
        .WithEnvironment("FRESHRSS_INSTALL", "--api-enabled --base-url http://localhost --default-user admin --language en --db-type sqlite")
        .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Client", "Feeds", "tech.xml")), $"{Root}/p/feeds/")
        .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Client", "Feeds", "science.xml")), $"{Root}/p/feeds/")
        .WithPortBinding(80, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(80).ForPath("/api/greader.php")))
        .Build();

    public Uri Url => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(80)}/");

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a user subscribed to Tech News (category Tech, 3 articles) and Science Daily (Science, 2).</summary>
    public async Task<FreshRssOptions> CreateUserAsync()
    {
        string user = "u" + Guid.NewGuid().ToString("N")[..10];
        ExecResult created = await _container.ExecAsync(["sh", "-c", $"cd {Root} && ./cli/create-user.php --user {user} --api-password {ApiPassword} --no-default-feeds && ./cli/access-permissions.sh"]);
        if (created.ExitCode != 0)
            throw new InvalidOperationException(created.Stderr);

        using var http = new HttpClient { BaseAddress = new Uri(Url, "api/greader.php/") };
        HttpResponseMessage login = await http.PostAsync("accounts/ClientLogin", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = user, ["Passwd"] = ApiPassword }));
        string auth = (await login.EnsureSuccessStatusCode().Content.ReadAsStringAsync()).Split('\n').Single(line => line.StartsWith("Auth=", StringComparison.Ordinal))[5..];
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("GoogleLogin", $"auth={auth}");
        string token = await http.GetStringAsync("reader/api/0/token");
        foreach ((string feed, string label) in new[] { ("tech", "Tech"), ("science", "Science") })
        {
            HttpResponseMessage subscribed = await http.PostAsync("reader/api/0/subscription/edit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ac"] = "subscribe",
                ["s"] = $"feed/http://localhost/feeds/{feed}.xml",
                ["a"] = $"user/-/label/{label}",
                ["T"] = token.Trim(),
            }));
            subscribed.EnsureSuccessStatusCode();
        }

        return new FreshRssOptions { Url = Url, UserName = user, ApiPassword = ApiPassword };
    }
}
