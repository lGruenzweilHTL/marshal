using Marshal.Abstractions;
using Marshal.Testing;

namespace Marshal.Integrations.Media.Tests;

public sealed class MediaClientTests : IAsyncLifetime
{
    private FakeMediaServer _server = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _server = await FakeMediaServer.StartAsync();

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Search_MoviesAndShowsOnly_WithKeyAndEncodedQuery()
    {
        Result<IReadOnlyList<SeerrTitle>> found = await MediaTestSetup.Seerr(_server).SearchAsync("Dune Part Two", Ct);

        Assert.True(found.IsSuccess, found.IsFailure ? found.Error.Message : null);
        Assert.Equal([(MediaType.Movie, 438631, 2021), (MediaType.Movie, 841, 1984), (MediaType.Show, 2948, 2000)], found.Value.Select(title => (title.Type, title.TmdbId, title.Year ?? 0)));
        Assert.Equal((MediaAvailability.Available, "abc123"), (found.Value[0].Availability, found.Value[0].JellyfinItemId));
        Assert.Equal(MediaAvailability.Unknown, found.Value[1].Availability);
        (string method, string path, string query, _) = Assert.Single(_server.Requests);
        Assert.Equal(("GET", "/seerr/api/v1/search", "?query=Dune%20Part%20Two&page=1"), (method, path, query));
    }

    [Fact]
    public async Task Title_ShowSeasonsWithoutSpecials()
    {
        Result<SeerrTitle> show = await MediaTestSetup.Seerr(_server).GetTitleAsync(MediaType.Show, 95396, Ct);

        Assert.Equal(("Severance", 2022, MediaAvailability.PartiallyAvailable), (show.Value.Title, show.Value.Year, show.Value.Availability));
        Assert.Equal([1, 2, 3], show.Value.Seasons!);
        Assert.Equal([1], (await MediaTestSetup.Seerr(_server).GetTitleAsync(MediaType.Show, 2948, Ct)).Value.Seasons!);
    }

    [Fact]
    public async Task Title_Unknown_NotFound()
    {
        Assert.Equal("media.not_found", (await MediaTestSetup.Seerr(_server).GetTitleAsync(MediaType.Movie, 1, Ct)).Error.Code);
    }

    [Fact]
    public async Task Requests_StatusesAndTimes()
    {
        Result<IReadOnlyList<SeerrRequest>> requests = await MediaTestSetup.Seerr(_server).GetRequestsAsync(50, Ct);

        Assert.Equal(
            [(MediaType.Show, 95396, RequestStatus.Approved, MediaAvailability.PartiallyAvailable), (MediaType.Movie, 841, RequestStatus.Pending, MediaAvailability.Pending), (MediaType.Movie, 438631, RequestStatus.Completed, MediaAvailability.Available)],
            requests.Value.Select(request => (request.Type, request.TmdbId, request.Status, request.Availability)));
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 18, 0, 0, TimeSpan.Zero), requests.Value[0].RequestedAt);
        Assert.Contains("take=50", Assert.Single(_server.Requests).Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRequest_PostsMovieAndShowBodies()
    {
        SeerrRestClient seerr = MediaTestSetup.Seerr(_server);

        Assert.True((await seerr.CreateRequestAsync(MediaType.Movie, 841, null, Ct)).IsSuccess);
        Assert.True((await seerr.CreateRequestAsync(MediaType.Show, 95396, null, Ct)).IsSuccess);
        Assert.True((await seerr.CreateRequestAsync(MediaType.Show, 95396, [2, 3], Ct)).IsSuccess);

        Assert.Equal(
            ["""{"mediaType":"movie","mediaId":841}""", """{"mediaType":"tv","mediaId":95396,"seasons":"all"}""", """{"mediaType":"tv","mediaId":95396,"seasons":[2,3]}"""],
            _server.Requests.Select(request => request.Body));
        Assert.All(_server.Requests, request => Assert.Equal(("POST", "/seerr/api/v1/request"), (request.Method, request.Path)));
    }

    [Fact]
    public async Task WrongKey_Unavailable()
    {
        var secrets = new FakeSecretStore();
        secrets.Values["seerr_api_key"] = "wrong";

        Result<IReadOnlyList<SeerrTitle>> found = await MediaTestSetup.Seerr(_server, secrets: secrets).SearchAsync("Dune", Ct);

        Assert.Equal("media.unavailable", found.Error.Code);
        Assert.Contains("rejected the API key", found.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSecret_UnavailableNamingTheSecret_NothingSent()
    {
        Result<IReadOnlyList<SeerrTitle>> found = await MediaTestSetup.Seerr(_server, secrets: new FakeSecretStore()).SearchAsync("Dune", Ct);
        Result<IReadOnlyList<ArrQueueItem>> queue = await MediaTestSetup.Arr(_server, secrets: new FakeSecretStore()).GetQueueAsync(ArrKind.Sonarr, Ct);

        Assert.Equal("media.unavailable", found.Error.Code);
        Assert.Contains("seerr_api_key", found.Error.Message, StringComparison.Ordinal);
        Assert.Contains("sonarr_api_key", queue.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Unreachable_Unavailable()
    {
        var seerr = new SeerrRestClient(new Refusing(), Microsoft.Extensions.Options.Options.Create(MediaTestSetup.Options()), MediaTestSetup.Secrets());
        var arr = new ArrRestClient(new Refusing(), Microsoft.Extensions.Options.Options.Create(MediaTestSetup.Options()), MediaTestSetup.Secrets());

        Assert.Equal("media.unavailable", (await seerr.SearchAsync("Dune", Ct)).Error.Code);
        Assert.Equal("media.unavailable", (await arr.GetQueueAsync(ArrKind.Radarr, Ct)).Error.Code);
    }

    [Fact]
    public async Task SonarrCalendar_EpisodesWithSeries()
    {
        DateTimeOffset start = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

        Result<IReadOnlyList<ArrCalendarEntry>> calendar = await MediaTestSetup.Arr(_server).GetCalendarAsync(ArrKind.Sonarr, start, start.AddDays(7), Ct);

        Assert.Equal(new ArrCalendarEntry(ArrKind.Sonarr, "Severance", 2, 4, "Woe's Hollow", null, new DateTimeOffset(2026, 10, 12, 2, 0, 0, TimeSpan.Zero), false), calendar.Value[0]);
        Assert.True(calendar.Value[1].Downloaded);
        (_, string path, string query, _) = Assert.Single(_server.Requests);
        Assert.Equal("/sonarr/api/v3/calendar", path);
        Assert.Contains("includeSeries=true", query, StringComparison.Ordinal);
        Assert.Contains("start=2026-10-10T00%3A00%3A00", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RadarrCalendar_ReleasesInRangeOnly()
    {
        DateTimeOffset start = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

        Result<IReadOnlyList<ArrCalendarEntry>> calendar = await MediaTestSetup.Arr(_server).GetCalendarAsync(ArrKind.Radarr, start, start.AddDays(7), Ct);

        ArrCalendarEntry release = Assert.Single(calendar.Value);
        Assert.Equal(("Dune: Part Three", "digital", new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero)), (release.Title, release.Release, release.Date));
        Assert.Equal("/radarr/api/v3/calendar", Assert.Single(_server.Requests).Path);
    }

    [Fact]
    public async Task Queues_PercentAndTitles()
    {
        ArrRestClient arr = MediaTestSetup.Arr(_server);

        ArrQueueItem episode = Assert.Single((await arr.GetQueueAsync(ArrKind.Sonarr, Ct)).Value);
        ArrQueueItem movie = Assert.Single((await arr.GetQueueAsync(ArrKind.Radarr, Ct)).Value);

        Assert.Equal(new ArrQueueItem(ArrKind.Sonarr, "Severance", 2, 3, 75.0, "00:05:00", new DateTimeOffset(2026, 10, 10, 12, 5, 0, TimeSpan.Zero), "downloading"), episode);
        Assert.Equal(("Dune", 0.0, null), (movie.Title, movie.Percent, movie.Season));
        Assert.All(_server.Requests, request => Assert.Equal("GET", request.Method));
    }

    [Fact]
    public void NotConfigured_ReportedPerKind()
    {
        var arr = new ArrRestClient(new Refusing(), Microsoft.Extensions.Options.Options.Create(MediaTestSetup.Options(arr: false)), MediaTestSetup.Secrets());

        Assert.False(arr.IsConfigured(ArrKind.Sonarr));
        Assert.True(MediaTestSetup.Arr(_server).IsConfigured(ArrKind.Radarr));
    }

    private sealed class Refusing : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler());

        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Connection refused");
        }
    }
}
