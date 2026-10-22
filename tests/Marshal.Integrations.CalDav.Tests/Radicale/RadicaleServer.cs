using System.Net.Http.Headers;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Marshal.Integrations.CalDav.Tests.Radicale;

/// <summary>
/// A Radicale container (htpasswd auth, user <c>lukas</c>). Every test creates its own calendar collection, so the
/// tests never see each other's events.
/// </summary>
public sealed class RadicaleServer : IAsyncLifetime
{
    public const string Username = "lukas";
    public const string Password = "secret";

    // Radicale 3.8.3, pinned by digest: the image only publishes moving tags.
    private readonly IContainer _container = new ContainerBuilder("tomsquest/docker-radicale@sha256:539b8f91b620a06b8b4198fc7ff170d50457a5e5560fb2ac5556570ad45c0e64")
        .WithResourceMapping(Encoding.UTF8.GetBytes(Config), "/config/config")
        .WithResourceMapping(Encoding.UTF8.GetBytes($"{Username}:{Password}\n"), "/config/users")
        .WithPortBinding(5232, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(5232).ForPath("/.web/")))
        .Build();

    private const string Config = """
        [server]
        hosts = 0.0.0.0:5232
        [auth]
        type = htpasswd
        htpasswd_filename = /config/users
        htpasswd_encryption = plain
        [storage]
        filesystem_folder = /data/collections
        [rights]
        type = owner_only
        """;

    public Uri Url => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(5232)}/");

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a calendar collection under the user's home and returns its name.</summary>
    public async Task<string> CreateCalendarAsync(CancellationToken cancellationToken)
    {
        string name = "c" + Guid.NewGuid().ToString("N")[..10];
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}")));

        // The principal collection exists only after a first authenticated request.
        using var principal = new HttpRequestMessage(new HttpMethod("PROPFIND"), new Uri(Url, $"{Username}/"));
        principal.Headers.Add("Depth", "0");
        (await http.SendAsync(principal, cancellationToken)).EnsureSuccessStatusCode();

        using var create = new HttpRequestMessage(new HttpMethod("MKCALENDAR"), new Uri(Url, $"{Username}/{name}/"))
        {
            Content = new StringContent($"""
                <?xml version="1.0" encoding="utf-8"?>
                <C:mkcalendar xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
                  <D:set><D:prop><D:displayname>{name}</D:displayname>
                    <C:supported-calendar-component-set><C:comp name="VEVENT"/></C:supported-calendar-component-set>
                  </D:prop></D:set>
                </C:mkcalendar>
                """, Encoding.UTF8, "application/xml"),
        };
        (await http.SendAsync(create, cancellationToken)).EnsureSuccessStatusCode();
        return name;
    }
}
