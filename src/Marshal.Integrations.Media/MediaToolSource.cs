using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Media;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Media;

/// <summary>
/// The <c>media.*</c> tools over Seerr, Sonarr and Radarr: search, request (confirm tier), the request list, upcoming
/// episodes and releases, and downloads. All output is untrusted: titles and overviews come from TMDB. No tools while
/// <c>media.seerr.url</c> is unset.
/// </summary>
public sealed class MediaToolSource(ISeerrClient seerr, IArrClient arr, IJellyfinLinks links, IOptions<MediaOptions> options, TimeProvider time) : IToolSource
{
    internal const int SearchLimit = 10;
    internal const int OverviewLength = 200;
    internal const int RequestLimit = 20;
    internal const int UpcomingLimit = 50;
    internal const int DefaultDays = 7;
    internal const int MaxDays = 30;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private readonly IReadOnlyList<ITool> _tools =
    [
        new SearchTool(seerr, links),
        new RequestTool(seerr),
        new RequestsTool(seerr, links),
        new UpcomingTool(arr, time),
        new DownloadsTool(arr),
    ];

    public string Name => "media";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("media", "Movies and TV shows: searching, requesting, upcoming episodes, the download queue and Jellyfin")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(options.Value.IsConfigured ? _tools : []);

    private static ToolDescriptor Descriptor(string name, string description, string schema, RiskTier tier = RiskTier.Safe) => new()
    {
        Id = new ToolId("media", name),
        Description = description,
        ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        DefaultTier = tier,
        UntrustedOutput = true,
        Fillers = name == "search" ? ToolFillers.Lines("Ich suche danach.", "Let me look that up.") : ToolFillers.None,
    };

    private static ToolOutput Json(object value) => new(JsonSerializer.Serialize(value, _json));

    private static string? Text(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Has(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null;

    private static Error Invalid(string message) => new("media.invalid_arguments", message, ErrorType.Validation);

    private static Result<MediaType?> ParseType(JsonElement arguments, bool required)
    {
        return Text(arguments, "type") switch
        {
            "movie" => MediaType.Movie,
            "show" => MediaType.Show,
            null when !Has(arguments, "type") && !required => (MediaType?)null,
            _ => Invalid("type must be 'movie' or 'show'."),
        };
    }

    private static string TypeName(MediaType type) => type == MediaType.Show ? "show" : "movie";

    internal static string AvailabilityName(MediaAvailability availability) => availability switch
    {
        MediaAvailability.Pending => "pending",
        MediaAvailability.Processing => "processing",
        MediaAvailability.PartiallyAvailable => "partially_available",
        MediaAvailability.Available => "available",
        _ => "unknown",
    };

    private static string Shorten(string text) =>
        text.Length <= OverviewLength ? text : string.Concat(text.AsSpan(0, OverviewLength - 1).TrimEnd(), "…");

    private static string Date(DateTimeOffset date) => date.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture);

    private sealed class SearchTool(ISeerrClient seerr, IJellyfinLinks links) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MediaToolSource.Descriptor("search",
            "Searches movies and shows in Seerr (TMDB). Returns up to 10 titles with TMDB id, type, year, a short overview, the number of seasons for shows, availability (unknown, pending, processing, partially_available, available) and a Jellyfin link when available. Use it before media_request and to tell titles with the same name apart.",
            """
            {"type":"object","properties":{
              "query":{"type":"string","description":"Title to search for."},
              "type":{"type":"string","enum":["movie","show"]}},
             "required":["query"]}
            """);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            if (Text(invocation.Arguments, "query")?.Trim() is not { Length: > 0 } query)
                return Invalid("query is required.");
            Result<MediaType?> type = ParseType(invocation.Arguments, required: false);
            if (type.IsFailure)
                return type.Error;

            Result<IReadOnlyList<SeerrTitle>> found = await seerr.SearchAsync(query, cancellationToken);
            if (found.IsFailure)
                return found.Error;

            SeerrTitle[] titles = [.. found.Value.Where(title => type.Value is null || title.Type == type.Value).Take(SearchLimit)];

            // Search results carry no seasons; show details do. A failed detail call only drops the count.
            SeerrTitle[] detailed = await Task.WhenAll(titles.Select(async title =>
                title.Type == MediaType.Show && title.Seasons is null && await seerr.GetTitleAsync(MediaType.Show, title.TmdbId, cancellationToken) is { IsSuccess: true } details
                    ? title with { Seasons = details.Value.Seasons }
                    : title));

            return Json(detailed.Select(title => new
            {
                tmdbId = title.TmdbId,
                type = TypeName(title.Type),
                title = title.Title,
                year = title.Year,
                overview = title.Overview is { } overview ? Shorten(overview) : null,
                seasons = title.Seasons?.Count,
                availability = AvailabilityName(title.Availability),
                jellyfin = links.ForItem(title.JellyfinItemId),
            }));
        }
    }

    private sealed class RequestTool(ISeerrClient seerr) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MediaToolSource.Descriptor("request",
            "Requests a movie or show in Seerr, which downloads it. The user confirms first. Pass the TMDB id, title and year exactly as media_search returned them; a show without seasons is requested with all seasons.",
            """
            {"type":"object","properties":{
              "type":{"type":"string","enum":["movie","show"]},
              "tmdbId":{"type":"integer","minimum":1},
              "title":{"type":"string","description":"Title as media_search returned it."},
              "year":{"type":"integer","description":"Year as media_search returned it."},
              "seasons":{"type":"array","items":{"type":"integer","minimum":1},"description":"Season numbers for a show; omit for all seasons."}},
             "required":["type","tmdbId","title","year"]}
            """, RiskTier.Confirm);

        public Result Validate(JsonElement arguments) => Parse(arguments) is { IsFailure: true } parsed ? parsed.Error : Result.Success();

        public string Summarize(JsonElement arguments, string language)
        {
            if (Parse(arguments) is not { IsSuccess: true } parsed)
                return $"{Descriptor.Id} {arguments.GetRawText()}";

            Request request = parsed.Value;
            string summary = $"Request \"{request.Title} ({request.Year.ToString(CultureInfo.InvariantCulture)})\", {TypeName(request.Type)}";
            return request.Type != MediaType.Show ? summary
                : request.Seasons is null ? $"{summary}, all seasons"
                : $"{summary}, season{(request.Seasons.Count == 1 ? "" : "s")} {string.Join(", ", request.Seasons.Select(season => season.ToString(CultureInfo.InvariantCulture)))}";
        }

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<Request> parsed = Parse(invocation.Arguments);
            if (parsed.IsFailure)
                return parsed.Error;
            Request request = parsed.Value;

            // Checked now, after confirmation: the id must belong to the title and year the user heard.
            Result<SeerrTitle> found = await seerr.GetTitleAsync(request.Type, request.TmdbId, cancellationToken);
            if (found.IsFailure)
                return found.Error;
            SeerrTitle title = found.Value;
            if (!string.Equals(title.Title.Trim(), request.Title.Trim(), StringComparison.OrdinalIgnoreCase) || title.Year != request.Year)
            {
                string actual = title.Year is { } year ? $"{title.Title} ({year.ToString(CultureInfo.InvariantCulture)})" : title.Title;
                return new Error("media.title_mismatch", $"TMDB id {request.TmdbId.ToString(CultureInfo.InvariantCulture)} is \"{actual}\", not the confirmed title. Nothing was requested.", ErrorType.Conflict);
            }

            if (request.Seasons?.FirstOrDefault(season => title.Seasons is not null && !title.Seasons.Contains(season)) is { } missing and > 0)
                return Invalid($"{title.Title} has no season {missing.ToString(CultureInfo.InvariantCulture)}; it has {title.Seasons!.Count.ToString(CultureInfo.InvariantCulture)}.");
            if (title.Availability == MediaAvailability.Available)
                return new Error("media.already_available", $"{title.Title} is already available.", ErrorType.Conflict);
            if (title.Availability is MediaAvailability.Pending or MediaAvailability.Processing)
                return new Error("media.already_requested", $"{title.Title} is already requested.", ErrorType.Conflict);

            Result created = await seerr.CreateRequestAsync(request.Type, request.TmdbId, request.Seasons, cancellationToken);
            if (created.IsFailure)
                return created.Error;

            return Json(new
            {
                requested = true,
                type = TypeName(request.Type),
                title = title.Title,
                year = title.Year,
                seasons = request.Type != MediaType.Show ? null : request.Seasons is null ? "all" : (object)request.Seasons,
            });
        }

        private static Result<Request> Parse(JsonElement arguments)
        {
            Result<MediaType?> type = ParseType(arguments, required: true);
            if (type.IsFailure)
                return type.Error;
            if (!arguments.TryGetProperty("tmdbId", out JsonElement id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int tmdbId) || tmdbId < 1)
                return Invalid("tmdbId must be the TMDB id from media_search.");
            if (Text(arguments, "title")?.Trim() is not { Length: > 0 } title)
                return Invalid("title is required.");
            if (!arguments.TryGetProperty("year", out JsonElement yearValue) || yearValue.ValueKind != JsonValueKind.Number || !yearValue.TryGetInt32(out int year) || year is < 1870 or > 2200)
                return Invalid("year must be the year from media_search.");

            List<int>? seasons = null;
            if (Has(arguments, "seasons"))
            {
                if (type.Value != MediaType.Show)
                    return Invalid("seasons are only for shows.");
                JsonElement list = arguments.GetProperty("seasons");
                if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                    return Invalid("seasons must be a non-empty list of season numbers, or omitted for all seasons.");
                seasons = [];
                foreach (JsonElement season in list.EnumerateArray())
                {
                    if (season.ValueKind != JsonValueKind.Number || !season.TryGetInt32(out int number) || number < 1)
                        return Invalid("seasons must be season numbers from 1.");
                    if (!seasons.Contains(number))
                        seasons.Add(number);
                }

                seasons.Sort();
            }

            return new Request(type.Value!.Value, tmdbId, title, year, seasons);
        }

        private sealed record Request(MediaType Type, int TmdbId, string Title, int Year, IReadOnlyList<int>? Seasons);
    }

    private sealed class RequestsTool(ISeerrClient seerr, IJellyfinLinks links) : ITool
    {
        // Seerr's list filters by media state, not by request status, so the filter is applied here.
        private const int Fetch = 50;

        public ToolDescriptor Descriptor { get; } = MediaToolSource.Descriptor("requests",
            "Lists the newest Seerr requests (at most 20) with title, type, year, request status (pending, approved, declined, failed, completed), availability, requested time and a Jellyfin link when available.",
            """
            {"type":"object","properties":{
              "status":{"type":"string","enum":["pending","approved","declined","failed","completed"]}}}
            """);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            string? status = Text(invocation.Arguments, "status");
            if (status is not (null or "pending" or "approved" or "declined" or "failed" or "completed"))
                return Invalid("status must be pending, approved, declined, failed or completed.");

            Result<IReadOnlyList<SeerrRequest>> list = await seerr.GetRequestsAsync(Fetch, cancellationToken);
            if (list.IsFailure)
                return list.Error;

            SeerrRequest[] requests = [.. list.Value.Where(request => status is null || StatusName(request.Status) == status).Take(RequestLimit)];
            Result<SeerrTitle>[] titles = await Task.WhenAll(requests.Select(request => seerr.GetTitleAsync(request.Type, request.TmdbId, cancellationToken)));

            return Json(requests.Select((request, index) => new
            {
                tmdbId = request.TmdbId,
                type = TypeName(request.Type),
                title = titles[index].IsSuccess ? titles[index].Value.Title : null,
                year = titles[index].IsSuccess ? titles[index].Value.Year : null,
                status = StatusName(request.Status),
                availability = AvailabilityName(request.Availability),
                requestedAt = Date(request.RequestedAt),
                jellyfin = titles[index].IsSuccess ? links.ForItem(titles[index].Value.JellyfinItemId) : null,
            }));
        }

        private static string StatusName(RequestStatus status) => status.ToString().ToLowerInvariant();
    }

    private sealed class UpcomingTool(IArrClient arr, TimeProvider time) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MediaToolSource.Descriptor("upcoming",
            "Upcoming episodes (Sonarr) and movie releases (Radarr) for the next days (default 7, at most 30), at most 50, with title, season and episode, air or release date (UTC) and whether the file is already downloaded.",
            """
            {"type":"object","properties":{
              "days":{"type":"integer","minimum":1,"maximum":30}}}
            """);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            int days = DefaultDays;
            if (Has(invocation.Arguments, "days"))
            {
                JsonElement value = invocation.Arguments.GetProperty("days");
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out days) || days is < 1 or > MaxDays)
                    return Invalid($"days must be between 1 and {MaxDays.ToString(CultureInfo.InvariantCulture)}.");
            }

            DateTimeOffset now = time.GetUtcNow();
            Result<(IReadOnlyList<ArrCalendarEntry> Items, IReadOnlyList<string> Notes)> merged =
                await MergeAsync(arr, kind => arr.GetCalendarAsync(kind, now, now.AddDays(days), cancellationToken));
            if (merged.IsFailure)
                return merged.Error;

            return Json(new
            {
                entries = merged.Value.Items.OrderBy(entry => entry.Date).Take(UpcomingLimit).Select(entry => new
                {
                    kind = entry.Kind == ArrKind.Sonarr ? "episode" : "movie",
                    title = entry.Title,
                    season = entry.Season,
                    episode = entry.Episode,
                    episodeTitle = entry.EpisodeTitle,
                    release = entry.Release,
                    date = Date(entry.Date),
                    downloaded = entry.Downloaded,
                }),
                notes = merged.Value.Notes,
            });
        }
    }

    private sealed class DownloadsTool(IArrClient arr) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MediaToolSource.Descriptor("downloads",
            "The Sonarr and Radarr download queues: title, season and episode for shows, percent done, estimated time left and state.",
            """{"type":"object","properties":{}}""");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<(IReadOnlyList<ArrQueueItem> Items, IReadOnlyList<string> Notes)> merged = await MergeAsync(arr, kind => arr.GetQueueAsync(kind, cancellationToken));
            if (merged.IsFailure)
                return merged.Error;

            return Json(new
            {
                downloads = merged.Value.Items.Select(item => new
                {
                    kind = item.Kind == ArrKind.Sonarr ? "episode" : "movie",
                    title = item.Title,
                    season = item.Season,
                    episode = item.Episode,
                    percent = item.Percent,
                    timeLeft = item.TimeLeft,
                    estimatedCompletion = item.EstimatedCompletion is { } eta ? Date(eta) : null,
                    state = item.State,
                }),
                notes = merged.Value.Notes,
            });
        }
    }

    /// <summary>Sonarr's and Radarr's lists in one; a service that is not configured adds a note instead.</summary>
    private static async Task<Result<(IReadOnlyList<T> Items, IReadOnlyList<string> Notes)>> MergeAsync<T>(IArrClient arr, Func<ArrKind, Task<Result<IReadOnlyList<T>>>> read)
    {
        List<T> items = [];
        List<string> notes = [];
        foreach (ArrKind kind in (ArrKind[])[ArrKind.Sonarr, ArrKind.Radarr])
        {
            if (!arr.IsConfigured(kind))
            {
                notes.Add($"{kind} is not configured.");
                continue;
            }

            Result<IReadOnlyList<T>> list = await read(kind);
            if (list.IsFailure)
                return list.Error;
            items.AddRange(list.Value);
        }

        return (items, notes);
    }
}
