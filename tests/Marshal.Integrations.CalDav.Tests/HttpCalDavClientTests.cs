using System.Net;
using Marshal.Abstractions;
using Marshal.Testing;

namespace Marshal.Integrations.CalDav.Tests;

public sealed class HttpCalDavClientTests
{
    private const string Radicale = "http://radicale.lan:5232/";
    private const string Nextcloud = "https://cloud.example.org/remote.php/dav/";
    private const string ICloud = "https://caldav.icloud.com/";

    private readonly FakeCalDavServer _server = new();
    private readonly FakeSecretStore _secrets = new();

    public HttpCalDavClientTests() => _secrets.Values["caldav_password"] = "secret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discover_Radicale_PrincipalHomeAndEventCalendarsOnly()
    {
        _server.OnFixture("PROPFIND", Radicale, "radicale/principal.xml")
            .OnFixture("PROPFIND", Radicale + "lukas/", "radicale/home.xml");
        _server.On("PROPFIND", Radicale + "lukas/", body => new FakeCalDavServer.Response((HttpStatusCode)207, FakeCalDavServer.Fixture(body.Contains("calendar-home-set", StringComparison.Ordinal) ? "radicale/home.xml" : "radicale/collections.xml")));

        CalDavDiscovery discovery = (await Client().DiscoverAsync(Account(Radicale), Ct)).Value;

        Assert.Equal(new Uri(Radicale + "lukas/"), discovery.Principal);
        Assert.Equal(new Uri(Radicale + "lukas/"), discovery.Home);
        Assert.Empty(discovery.Addresses);
        CalDavCalendar personal = Assert.Single(discovery.Calendars);
        Assert.Equal((new Uri(Radicale + "lukas/personal/"), "personal", "Personal", true, true), (personal.Url, personal.Name, personal.DisplayName, personal.CanWrite, personal.SupportsSync));
        Assert.All(_server.Requests, request => Assert.Equal("Basic bHVrYXM6c2VjcmV0", request.Headers["Authorization"]));
        Assert.Equal(["0", "0", "1"], _server.Requests.Select(request => request.Headers["Depth"]));
    }

    [Fact]
    public async Task Discover_Nextcloud_SkipsTasksSchedulingAndTrash()
    {
        _server.OnFixture("PROPFIND", Nextcloud, "nextcloud/principal.xml")
            .OnFixture("PROPFIND", "https://cloud.example.org/remote.php/dav/principals/users/lukas/", "nextcloud/home.xml")
            .OnFixture("PROPFIND", "https://cloud.example.org/remote.php/dav/calendars/lukas/", "nextcloud/collections.xml");

        CalDavDiscovery discovery = (await Client().DiscoverAsync(Account(Nextcloud), Ct)).Value;

        Assert.Equal(["lukas@example.org"], discovery.Addresses);
        Assert.Equal(
            [("personal", "Personal", true), ("work shared", "Work (Anna)", false)],
            discovery.Calendars.Select(calendar => (calendar.Name, calendar.DisplayName, calendar.CanWrite)));
        Assert.Equal(new Uri("https://cloud.example.org/remote.php/dav/calendars/lukas/work%20shared/"), discovery.Calendars[1].Url);
    }

    [Fact]
    public async Task Discover_ICloud_HomeOnAnotherHost()
    {
        _server.OnFixture("PROPFIND", ICloud, "icloud/principal.xml")
            .OnFixture("PROPFIND", "https://caldav.icloud.com/123456789/principal/", "icloud/home.xml")
            .OnFixture("PROPFIND", "https://p42-caldav.icloud.com/123456789/calendars/", "icloud/collections.xml");

        CalDavDiscovery discovery = (await Client().DiscoverAsync(Account(ICloud), Ct)).Value;

        Assert.Equal(["lukas@icloud.com"], discovery.Addresses);
        CalDavCalendar home = Assert.Single(discovery.Calendars);
        Assert.Equal((new Uri("https://p42-caldav.icloud.com/123456789/calendars/home/"), "home"), (home.Url, home.Name));
    }

    [Fact]
    public async Task Discover_WrongPassword_AuthError()
    {
        _server.Credentials = "lukas:other";
        _server.OnFixture("PROPFIND", Radicale, "radicale/principal.xml");

        Result<CalDavDiscovery> discovery = await Client().DiscoverAsync(Account(Radicale), Ct);

        Assert.Equal(("caldav.auth_failed", ErrorType.Forbidden), (discovery.Error.Code, discovery.Error.Type));
    }

    [Fact]
    public async Task Discover_MissingSecret_AuthErrorWithoutRequest()
    {
        _secrets.Values.Clear();

        Result<CalDavDiscovery> discovery = await Client().DiscoverAsync(Account(Radicale), Ct);

        Assert.Equal("caldav.auth_failed", discovery.Error.Code);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Query_TimeRangeInUtc_ReturnsResourcesWithETags()
    {
        _server.OnFixture("REPORT", Radicale + "lukas/personal/", "radicale/query.xml");

        IReadOnlyList<CalDavResource> resources = (await Client().QueryAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/"),
            new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.FromHours(1)), Ct)).Value;

        FakeCalDavServer.Recorded request = Assert.Single(_server.Requests);
        Assert.Equal("1", request.Headers["Depth"]);
        Assert.Contains("calendar-query", request.Body, StringComparison.Ordinal);
        Assert.Contains("start=\"20261019T000000Z\" end=\"20261102T000000Z\"", request.Body, StringComparison.Ordinal);
        CalDavResource standup = Assert.Single(resources);
        Assert.Equal(new Uri(Radicale + "lukas/personal/weekly-standup.ics"), standup.Href);
        Assert.Equal("\"7d8f7074fb8c6e5ceb34c38dffc085430f7be062fc31d716cb87ae1bbe3c0525\"", standup.ETag);
        Assert.Contains("UID:weekly-standup", standup.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_WithoutToken_EverythingAndTheNewToken()
    {
        _server.OnFixture("REPORT", Radicale + "lukas/personal/", "radicale/sync-initial.xml");

        CalDavSyncResult sync = (await Client().SyncAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/"), null, Ct)).Value;

        Assert.Contains("<D:sync-token />", Assert.Single(_server.Requests).Body.Replace("<D:sync-token></D:sync-token>", "<D:sync-token />", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal("http://radicale.org/ns/sync/dd8fbe2a06df3f4b96bfb85fb5852f02becea9e6d9fda7fd63555ebebcef3367", sync.Token);
        CalDavChange change = Assert.Single(sync.Changes);
        Assert.Equal((new Uri(Radicale + "lukas/personal/weekly-standup.ics"), false), (change.Href, change.Removed));
    }

    [Fact]
    public async Task Sync_WithToken_ChangedAndRemoved()
    {
        _server.OnFixture("REPORT", Radicale + "lukas/personal/", "radicale/sync-changes.xml");

        CalDavSyncResult sync = (await Client().SyncAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/"), "http://radicale.org/ns/sync/dd8f", Ct)).Value;

        Assert.Contains("<D:sync-token>http://radicale.org/ns/sync/dd8f</D:sync-token>", Assert.Single(_server.Requests).Body, StringComparison.Ordinal);
        Assert.Equal(
            [("weekly-standup.ics", true, null), ("dentist.ics", false, "\"4af66d192ec435a96fe6338323de378c6bbf1c0c92ad5095b1a54fa9ed02aa3b\"")],
            sync.Changes.Select(change => (change.Href.Segments[^1], change.Removed, change.ETag)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Sync_InvalidToken_InvalidTokenError(HttpStatusCode status)
    {
        _server.On("REPORT", Radicale + "lukas/personal/", status, FakeCalDavServer.Fixture("radicale/sync-invalid.xml"));

        Result<CalDavSyncResult> sync = await Client().SyncAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/"), "stale", Ct);

        Assert.Equal("caldav.invalid_sync_token", sync.Error.Code);
    }

    [Fact]
    public async Task ListETags_PropfindDepthOne()
    {
        _server.On("PROPFIND", Radicale + "lukas/personal/", (HttpStatusCode)207, FakeCalDavServer.Fixture("radicale/sync-initial.xml"));

        IReadOnlyDictionary<Uri, string> etags = (await Client().ListETagsAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/"), Ct)).Value;

        Assert.Equal("1", Assert.Single(_server.Requests).Headers["Depth"]);
        Assert.Equal("\"7d8f7074fb8c6e5ceb34c38dffc085430f7be062fc31d716cb87ae1bbe3c0525\"", etags[new Uri(Radicale + "lukas/personal/weekly-standup.ics")]);
    }

    [Fact]
    public async Task FindByUid_UidTextMatch()
    {
        _server.OnFixture("REPORT", Radicale + "lukas/personal/", "radicale/query.xml");

        CalDavResource? found = (await Client().FindByUidAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/"), "weekly-standup", Ct)).Value;

        string body = Assert.Single(_server.Requests).Body;
        Assert.Contains("<C:prop-filter name=\"UID\"><C:text-match collation=\"i;octet\">weekly-standup</C:text-match></C:prop-filter>", body, StringComparison.Ordinal);
        Assert.Equal(new Uri(Radicale + "lukas/personal/weekly-standup.ics"), found!.Href);
    }

    [Fact]
    public async Task FindByUid_None_Null()
    {
        _server.On("REPORT", Radicale + "lukas/personal/", (HttpStatusCode)207, "<multistatus xmlns=\"DAV:\"/>");

        Assert.Null((await Client().FindByUidAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/"), "missing", Ct)).Value);
    }

    [Fact]
    public async Task Get_DataAndETag()
    {
        _server.On("GET", Radicale + "lukas/personal/dentist.ics", HttpStatusCode.OK, "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", "\"abc\"");

        CalDavResource resource = (await Client().GetAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/dentist.ics"), Ct)).Value;

        Assert.Equal(("\"abc\"", "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n"), (resource.ETag, resource.Data));
    }

    [Fact]
    public async Task Put_Existing_SendsIfMatch_ReturnsNewETag()
    {
        _server.On("PUT", Radicale + "lukas/personal/dentist.ics", HttpStatusCode.NoContent, "", "\"new\"");

        Result<string?> etag = await Client().PutAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/dentist.ics"), "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", "\"abc\"", Ct);

        FakeCalDavServer.Recorded request = Assert.Single(_server.Requests);
        Assert.Equal("\"abc\"", request.Headers["If-Match"]);
        Assert.False(request.Headers.ContainsKey("If-None-Match"));
        Assert.Equal("\"new\"", etag.Value);
    }

    [Fact]
    public async Task Put_New_SendsIfNoneMatch()
    {
        _server.On("PUT", Radicale + "lukas/personal/new.ics", HttpStatusCode.Created);

        Result<string?> etag = await Client().PutAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/new.ics"), "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", null, Ct);

        Assert.Equal("*", Assert.Single(_server.Requests).Headers["If-None-Match"]);
        Assert.True(etag.IsSuccess);
        Assert.Null(etag.Value);
    }

    [Fact]
    public async Task Put_ChangedMeanwhile_Conflict()
    {
        _server.On("PUT", Radicale + "lukas/personal/dentist.ics", HttpStatusCode.PreconditionFailed);

        Result<string?> etag = await Client().PutAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/dentist.ics"), "x", "\"old\"", Ct);

        Assert.Equal(("caldav.conflict", ErrorType.Conflict), (etag.Error.Code, etag.Error.Type));
    }

    [Fact]
    public async Task ServerError_Unavailable()
    {
        _server.On("REPORT", Radicale + "lukas/personal/", HttpStatusCode.BadGateway);

        Result<CalDavSyncResult> sync = await Client().SyncAsync(Account(Radicale), new Uri(Radicale + "lukas/personal/"), null, Ct);

        Assert.Equal(("caldav.failed", ErrorType.Unavailable), (sync.Error.Code, sync.Error.Type));
    }

    private HttpCalDavClient Client() => new(new HandlerClientFactory(_server), _secrets);

    private static CalDavAccount Account(string url) => new("test", new Uri(url), "lukas", "caldav_password");
}
