using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.Media.Tests;

public sealed class MediaToolsTests
{
    private readonly FakeSeerr _seerr = new();
    private readonly FakeArr _arr = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SeerrTitle Dune2021 = new(MediaType.Movie, 438631, "Dune", 2021, new string('x', 300), null, MediaAvailability.Unknown, null);
    private static readonly SeerrTitle Dune1984 = new(MediaType.Movie, 841, "Dune", 1984, "In the year 10,191.", null, MediaAvailability.Available, "abc123");
    private static readonly SeerrTitle DuneShow = new(MediaType.Show, 2948, "Frank Herbert's Dune", 2000, null, null, MediaAvailability.Unknown, null);
    private static readonly SeerrTitle Severance = new(MediaType.Show, 95396, "Severance", 2022, null, [1, 2, 3], MediaAvailability.Unknown, null);

    public MediaToolsTests()
    {
        _seerr.Titles.AddRange([Dune2021, Dune1984, DuneShow with { Seasons = [1] }, Severance]);
        _seerr.Found.AddRange([Dune2021, Dune1984, DuneShow]);
    }

    [Fact]
    public async Task Tools_TiersAndUntrustedOutput()
    {
        IReadOnlyList<ITool> tools = await Source().GetToolsAsync(Ct);

        Assert.Equal(["media.search", "media.request", "media.requests", "media.upcoming", "media.downloads"], tools.Select(tool => tool.Descriptor.Id.ToString()));
        Assert.Equal(RiskTier.Confirm, tools[1].Descriptor.DefaultTier);
        Assert.All(tools.Where(tool => tool.Descriptor.Id.Name != "request"), tool => Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier));
        Assert.All(tools, tool => Assert.True(tool.Descriptor.UntrustedOutput));
    }

    [Fact]
    public async Task NotConfigured_NoTools()
    {
        var source = new MediaToolSource(_seerr, _arr, new JellyfinLinks(Options.Create(new MediaOptions())), Options.Create(new MediaOptions()), _time);

        Assert.Empty(await source.GetToolsAsync(Ct));
    }

    [Fact]
    public async Task Search_AmbiguousTitleListedSeparately_WithSeasonsAndLinks()
    {
        JsonElement found = await InvokeAsync("search", new { query = "Dune" });

        Assert.Equal([(438631, "movie", 2021), (841, "movie", 1984), (2948, "show", 2000)],
            found.EnumerateArray().Select(title => (title.GetProperty("tmdbId").GetInt32(), title.GetProperty("type").GetString(), title.GetProperty("year").GetInt32())));
        Assert.Equal(MediaToolSource.OverviewLength, found[0].GetProperty("overview").GetString()!.Length);
        Assert.Equal(("available", "https://jellyfin.lan/web/#/details?id=abc123"), (found[1].GetProperty("availability").GetString(), found[1].GetProperty("jellyfin").GetString()));
        Assert.Equal(1, found[2].GetProperty("seasons").GetInt32());
        Assert.Equal(JsonValueKind.Null, found[0].GetProperty("jellyfin").ValueKind);
    }

    [Fact]
    public async Task Search_TypeFilterAndLimit()
    {
        _seerr.Found.AddRange(Enumerable.Range(1, 20).Select(i => Dune2021 with { TmdbId = 10000 + i }));

        Assert.Equal(MediaToolSource.SearchLimit, (await InvokeAsync("search", new { query = "Dune" })).GetArrayLength());
        Assert.Equal("show", Assert.Single((await InvokeAsync("search", new { query = "Dune", type = "show" })).EnumerateArray()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Request_Summary_NamesTitleYearTypeAndSeasons()
    {
        ITool request = await ToolAsync("request");

        Assert.Equal("Request \"Dune (2021)\", movie", request.Summarize(Args(new { type = "movie", tmdbId = 438631, title = "Dune", year = 2021 }), "en"));
        Assert.Equal("Request \"Severance (2022)\", show, all seasons", request.Summarize(Args(new { type = "show", tmdbId = 95396, title = "Severance", year = 2022 }), "en"));
        Assert.Equal("Request \"Severance (2022)\", show, seasons 2, 3", request.Summarize(Args(new { type = "show", tmdbId = 95396, title = "Severance", year = 2022, seasons = new[] { 3, 2 } }), "en"));
    }

    [Fact]
    public async Task Request_Movie_Created()
    {
        JsonElement result = await InvokeAsync("request", new { type = "movie", tmdbId = 438631, title = "dune ", year = 2021 });

        Assert.True(result.GetProperty("requested").GetBoolean());
        Assert.Equal((MediaType.Movie, 438631, (IReadOnlyList<int>?)null), Assert.Single(_seerr.Created));
    }

    [Fact]
    public async Task Request_ShowWithoutSeasons_AllSeasons()
    {
        JsonElement result = await InvokeAsync("request", new { type = "show", tmdbId = 95396, title = "Severance", year = 2022 });

        Assert.Equal("all", result.GetProperty("seasons").GetString());
        Assert.Null(Assert.Single(_seerr.Created).Seasons);
    }

    [Fact]
    public async Task Request_ExplicitSeasons_SortedAndUnknownSeasonInvalid()
    {
        await InvokeAsync("request", new { type = "show", tmdbId = 95396, title = "Severance", year = 2022, seasons = new[] { 3, 2, 3 } });
        Result<ToolOutput> unknown = await RunAsync("request", new { type = "show", tmdbId = 95396, title = "Severance", year = 2022, seasons = new[] { 5 } });

        Assert.Equal([2, 3], Assert.Single(_seerr.Created).Seasons!);
        Assert.Equal("media.invalid_arguments", unknown.Error.Code);
    }

    [Fact]
    public async Task Request_WrongIdForConfirmedTitle_MismatchAndNothingRequested()
    {
        Result<ToolOutput> result = await RunAsync("request", new { type = "movie", tmdbId = 841, title = "Dune", year = 2021 });

        Assert.Equal("media.title_mismatch", result.Error.Code);
        Assert.Contains("Dune (1984)", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_seerr.Created);
    }

    [Fact]
    public async Task Request_UnknownId_NotFound()
    {
        Assert.Equal("media.not_found", (await RunAsync("request", new { type = "movie", tmdbId = 1, title = "Nothing", year = 2000 })).Error.Code);
        Assert.Empty(_seerr.Created);
    }

    [Theory]
    [InlineData(MediaAvailability.Available, "media.already_available")]
    [InlineData(MediaAvailability.Pending, "media.already_requested")]
    [InlineData(MediaAvailability.Processing, "media.already_requested")]
    public async Task Request_AvailableOrRequested_Fails(MediaAvailability availability, string code)
    {
        _seerr.Titles[0] = Dune2021 with { Availability = availability };

        Assert.Equal(code, (await RunAsync("request", new { type = "movie", tmdbId = 438631, title = "Dune", year = 2021 })).Error.Code);
        Assert.Empty(_seerr.Created);
    }

    [Theory]
    [InlineData("""{"type":"film","tmdbId":1,"title":"x","year":2000}""")]
    [InlineData("""{"type":"movie","tmdbId":0,"title":"x","year":2000}""")]
    [InlineData("""{"type":"movie","tmdbId":1,"title":" ","year":2000}""")]
    [InlineData("""{"type":"movie","tmdbId":1,"title":"x"}""")]
    [InlineData("""{"type":"movie","tmdbId":1,"title":"x","year":2000,"seasons":[1]}""")]
    [InlineData("""{"type":"show","tmdbId":1,"title":"x","year":2000,"seasons":[0]}""")]
    public async Task Request_InvalidArguments_DeniedByValidate(string json)
    {
        Result validation = (await ToolAsync("request")).Validate(JsonDocument.Parse(json).RootElement);

        Assert.Equal("media.invalid_arguments", validation.Error.Code);
    }

    [Fact]
    public async Task Requests_TitlesStatusFilterAndLinks()
    {
        _seerr.Requests.AddRange(
        [
            new SeerrRequest(MediaType.Show, 95396, RequestStatus.Approved, MediaAvailability.PartiallyAvailable, _time.GetUtcNow().AddDays(-1)),
            new SeerrRequest(MediaType.Movie, 841, RequestStatus.Completed, MediaAvailability.Available, _time.GetUtcNow().AddDays(-2)),
        ]);

        JsonElement all = await InvokeAsync("requests", new { });
        JsonElement completed = await InvokeAsync("requests", new { status = "completed" });

        Assert.Equal([("Severance", "approved", "partially_available"), ("Dune", "completed", "available")],
            all.EnumerateArray().Select(request => (request.GetProperty("title").GetString(), request.GetProperty("status").GetString(), request.GetProperty("availability").GetString())));
        JsonElement dune = Assert.Single(completed.EnumerateArray());
        Assert.Equal((1984, "https://jellyfin.lan/web/#/details?id=abc123"), (dune.GetProperty("year").GetInt32(), dune.GetProperty("jellyfin").GetString()));
    }

    [Fact]
    public async Task Requests_AtMostTwenty()
    {
        _seerr.Requests.AddRange(Enumerable.Range(0, 30).Select(i => new SeerrRequest(MediaType.Movie, 841, RequestStatus.Pending, MediaAvailability.Pending, _time.GetUtcNow())));

        Assert.Equal(MediaToolSource.RequestLimit, (await InvokeAsync("requests", new { })).GetArrayLength());
    }

    [Fact]
    public async Task Upcoming_NextEpisode_MergedSortedForSevenDays()
    {
        _arr.Calendars[ArrKind.Sonarr] = [new ArrCalendarEntry(ArrKind.Sonarr, "Severance", 2, 4, "Woe's Hollow", null, _time.GetUtcNow().AddDays(3), false)];
        _arr.Calendars[ArrKind.Radarr] = [new ArrCalendarEntry(ArrKind.Radarr, "Dune: Part Three", null, null, null, "digital", _time.GetUtcNow().AddDays(1), false)];

        JsonElement upcoming = await InvokeAsync("upcoming", new { });

        JsonElement[] entries = [.. upcoming.GetProperty("entries").EnumerateArray()];
        Assert.Equal(["Dune: Part Three", "Severance"], entries.Select(entry => entry.GetProperty("title").GetString()));
        Assert.Equal(("episode", 2, 4, "2026-10-13T12:00Z"), (entries[1].GetProperty("kind").GetString(), entries[1].GetProperty("season").GetInt32(), entries[1].GetProperty("episode").GetInt32(), entries[1].GetProperty("date").GetString()));
        Assert.All(_arr.Ranges, range => Assert.Equal((_time.GetUtcNow(), _time.GetUtcNow().AddDays(7)), range));
        Assert.Empty(upcoming.GetProperty("notes").EnumerateArray());
    }

    [Fact]
    public async Task Upcoming_SeerrOnly_EmptyWithNotes()
    {
        _arr.Configured.Clear();

        JsonElement upcoming = await InvokeAsync("upcoming", new { days = 30 });

        Assert.Empty(upcoming.GetProperty("entries").EnumerateArray());
        Assert.Equal(["Sonarr is not configured.", "Radarr is not configured."], upcoming.GetProperty("notes").EnumerateArray().Select(note => note.GetString()));
    }

    [Fact]
    public async Task Upcoming_DaysOutOfRange_Invalid()
    {
        Assert.Equal("media.invalid_arguments", (await RunAsync("upcoming", new { days = 31 })).Error.Code);
    }

    [Fact]
    public async Task Downloads_BothQueues()
    {
        _arr.Queues[ArrKind.Sonarr] = [new ArrQueueItem(ArrKind.Sonarr, "Severance", 2, 3, 75, "00:05:00", null, "downloading")];
        _arr.Queues[ArrKind.Radarr] = [new ArrQueueItem(ArrKind.Radarr, "Dune", null, null, 0, null, null, "downloading")];

        JsonElement downloads = (await InvokeAsync("downloads", new { })).GetProperty("downloads");

        Assert.Equal([("episode", "Severance", 75.0), ("movie", "Dune", 0.0)],
            downloads.EnumerateArray().Select(item => (item.GetProperty("kind").GetString(), item.GetProperty("title").GetString(), item.GetProperty("percent").GetDouble())));
    }

    [Fact]
    public async Task ServiceFailure_ReturnedAsToolError()
    {
        _arr.Failure = new Error("media.unavailable", "Sonarr is not reachable.", ErrorType.Unavailable);

        Assert.Equal("media.unavailable", (await RunAsync("downloads", new { })).Error.Code);
    }

    private static JsonElement Args(object arguments) => JsonSerializer.SerializeToElement(arguments);

    private async Task<JsonElement> InvokeAsync(string name, object arguments)
    {
        Result<ToolOutput> result = await RunAsync(name, arguments);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return JsonDocument.Parse(result.Value.Content).RootElement.Clone();
    }

    private async Task<Result<ToolOutput>> RunAsync(string name, object arguments) =>
        await (await ToolAsync(name)).InvokeAsync(new ToolInvocation(Args(arguments), TestExecutionContext.Text()), Ct);

    private async Task<ITool> ToolAsync(string name) => (await Source().GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == name);

    private MediaToolSource Source()
    {
        IOptions<MediaOptions> options = Options.Create(MediaTestSetup.Options());
        return new MediaToolSource(_seerr, _arr, new JellyfinLinks(options), options, _time);
    }

    private sealed class FakeSeerr : ISeerrClient
    {
        public List<SeerrTitle> Found { get; } = [];

        public List<SeerrTitle> Titles { get; } = [];

        public List<SeerrRequest> Requests { get; } = [];

        public List<(MediaType Type, int TmdbId, IReadOnlyList<int>? Seasons)> Created { get; } = [];

        public Task<Result<IReadOnlyList<SeerrTitle>>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<Result<IReadOnlyList<SeerrTitle>>>(Found.ToList());

        public Task<Result<SeerrTitle>> GetTitleAsync(MediaType type, int tmdbId, CancellationToken cancellationToken) =>
            Task.FromResult(Titles.FirstOrDefault(title => title.Type == type && title.TmdbId == tmdbId) is { } title
                ? (Result<SeerrTitle>)title
                : new Error("media.not_found", "Seerr does not know that title.", ErrorType.NotFound));

        public Task<Result<IReadOnlyList<SeerrRequest>>> GetRequestsAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<Result<IReadOnlyList<SeerrRequest>>>(Requests.Take(take).ToList());

        public Task<Result> CreateRequestAsync(MediaType type, int tmdbId, IReadOnlyList<int>? seasons, CancellationToken cancellationToken)
        {
            Created.Add((type, tmdbId, seasons));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakeArr : IArrClient
    {
        public HashSet<ArrKind> Configured { get; } = [ArrKind.Sonarr, ArrKind.Radarr];

        public Dictionary<ArrKind, IReadOnlyList<ArrCalendarEntry>> Calendars { get; } = new() { [ArrKind.Sonarr] = [], [ArrKind.Radarr] = [] };

        public Dictionary<ArrKind, IReadOnlyList<ArrQueueItem>> Queues { get; } = new() { [ArrKind.Sonarr] = [], [ArrKind.Radarr] = [] };

        public List<(DateTimeOffset Start, DateTimeOffset End)> Ranges { get; } = [];

        public Error? Failure { get; set; }

        public bool IsConfigured(ArrKind kind) => Configured.Contains(kind);

        public Task<Result<IReadOnlyList<ArrCalendarEntry>>> GetCalendarAsync(ArrKind kind, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
        {
            Ranges.Add((start, end));
            return Task.FromResult(Failure is { } failure ? failure : (Result<IReadOnlyList<ArrCalendarEntry>>)(List<ArrCalendarEntry>)[.. Calendars[kind]]);
        }

        public Task<Result<IReadOnlyList<ArrQueueItem>>> GetQueueAsync(ArrKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(Failure is { } failure ? failure : (Result<IReadOnlyList<ArrQueueItem>>)(List<ArrQueueItem>)[.. Queues[kind]]);
    }
}
