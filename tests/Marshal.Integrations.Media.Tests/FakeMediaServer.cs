using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Marshal.Integrations.Media.Tests;

/// <summary>
/// In-process Seerr (<c>/seerr/api/v1</c>), Sonarr (<c>/sonarr/api/v3</c>) and Radarr (<c>/radarr/api/v3</c>) in the
/// shapes of their APIs, each checking its own <c>X-Api-Key</c>.
/// </summary>
internal sealed class FakeMediaServer : IAsyncDisposable
{
    public const string SeerrKey = "seerr-key";
    public const string SonarrKey = "sonarr-key";
    public const string RadarrKey = "radarr-key";

    private readonly WebApplication _app;

    private FakeMediaServer(WebApplication app) => _app = app;

    public ConcurrentQueue<(string Method, string Path, string Query, string Body)> Requests { get; } = new();

    public JsonObject Search { get; set; } = Parse("""
        {"page":1,"totalPages":1,"totalResults":4,"results":[
          {"id":438631,"mediaType":"movie","title":"Dune","releaseDate":"2021-09-15","overview":"Paul Atreides, a brilliant and gifted young man born into a great destiny beyond his understanding, must travel to the most dangerous planet in the universe to ensure the future of his family and his people. As malevolent forces explode into conflict over the planet's exclusive supply of the most precious resource in existence, only those who can conquer their fear will survive.","mediaInfo":{"status":5,"jellyfinMediaId":"abc123"}},
          {"id":841,"mediaType":"movie","title":"Dune","releaseDate":"1984-12-14","overview":"In the year 10,191, the world is at war."},
          {"id":2948,"mediaType":"tv","name":"Frank Herbert's Dune","firstAirDate":"2000-12-03","overview":"Mini series."},
          {"id":1000,"mediaType":"person","name":"Denis Villeneuve"}
        ]}
        """);

    public Dictionary<string, JsonObject> Titles { get; } = new()
    {
        ["movie/438631"] = Parse("""{"id":438631,"title":"Dune","releaseDate":"2021-09-15","mediaInfo":{"status":5,"jellyfinMediaId":"abc123"}}"""),
        ["movie/841"] = Parse("""{"id":841,"title":"Dune","releaseDate":"1984-12-14"}"""),
        ["tv/2948"] = Parse("""{"id":2948,"name":"Frank Herbert's Dune","firstAirDate":"2000-12-03","seasons":[{"seasonNumber":0},{"seasonNumber":1}]}"""),
        ["tv/95396"] = Parse("""{"id":95396,"name":"Severance","firstAirDate":"2022-02-17","seasons":[{"seasonNumber":1},{"seasonNumber":2},{"seasonNumber":3}],"mediaInfo":{"status":4}}"""),
        ["tv/1399"] = Parse("""{"id":1399,"name":"Game of Thrones","firstAirDate":"2011-04-17","seasons":[{"seasonNumber":1}],"mediaInfo":{"status":3}}"""),
    };

    public JsonObject RequestList { get; set; } = Parse("""
        {"pageInfo":{"pages":1,"results":3},"results":[
          {"id":3,"status":2,"createdAt":"2026-10-08T18:00:00.000Z","media":{"tmdbId":95396,"mediaType":"tv","status":4}},
          {"id":2,"status":1,"createdAt":"2026-10-07T18:00:00.000Z","media":{"tmdbId":841,"mediaType":"movie","status":2}},
          {"id":1,"status":5,"createdAt":"2026-10-01T18:00:00.000Z","media":{"tmdbId":438631,"mediaType":"movie","status":5,"jellyfinMediaId":"abc123"}}
        ]}
        """);

    public JsonArray SonarrCalendar { get; set; } = (JsonArray)JsonNode.Parse("""
        [
          {"seriesId":1,"seasonNumber":2,"episodeNumber":4,"title":"Woe's Hollow","airDateUtc":"2026-10-12T02:00:00Z","hasFile":false,"series":{"title":"Severance"}},
          {"seriesId":2,"seasonNumber":1,"episodeNumber":1,"title":"Pilot","airDateUtc":"2026-10-10T20:00:00Z","hasFile":true,"series":{"title":"Andor"}}
        ]
        """)!;

    public JsonArray RadarrCalendar { get; set; } = (JsonArray)JsonNode.Parse("""
        [
          {"title":"Dune: Part Three","inCinemas":"2026-12-18T00:00:00Z","digitalRelease":"2026-10-11T00:00:00Z","hasFile":false}
        ]
        """)!;

    public JsonObject SonarrQueue { get; set; } = Parse("""
        {"page":1,"pageSize":50,"totalRecords":1,"records":[
          {"title":"Severance.S02E03.1080p","size":2000000000,"sizeleft":500000000,"timeleft":"00:05:00","estimatedCompletionTime":"2026-10-10T12:05:00Z","status":"downloading","trackedDownloadState":"downloading","series":{"title":"Severance"},"episode":{"seasonNumber":2,"episodeNumber":3}}
        ]}
        """);

    public JsonObject RadarrQueue { get; set; } = Parse("""
        {"page":1,"pageSize":50,"totalRecords":1,"records":[
          {"title":"Dune.2021.2160p","size":10000000000,"sizeleft":10000000000,"status":"queued","trackedDownloadState":"downloading","movie":{"title":"Dune"}}
        ]}
        """);

    public TestServer Server => _app.GetTestServer();

    public static async Task<FakeMediaServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        WebApplication app = builder.Build();
        var fake = new FakeMediaServer(app);
        app.Use(async (context, next) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            fake.Requests.Enqueue((context.Request.Method, context.Request.Path, context.Request.QueryString.Value ?? "", body));
            string path = context.Request.Path.Value ?? "";
            string expected = path.StartsWith("/seerr/", StringComparison.Ordinal) ? SeerrKey : path.StartsWith("/sonarr/", StringComparison.Ordinal) ? SonarrKey : RadarrKey;
            if (context.Request.Headers["X-Api-Key"] != expected)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        app.MapGet("/seerr/api/v1/search", () => Json(fake.Search));
        app.MapGet("/seerr/api/v1/{type}/{id:int}", (string type, int id) =>
            fake.Titles.TryGetValue($"{type}/{id}", out JsonObject? title) ? Json(title) : Results.NotFound(new { message = "Not found" }));
        app.MapGet("/seerr/api/v1/request", () => Json(fake.RequestList));
        app.MapPost("/seerr/api/v1/request", () => Results.Text("""{"id":4,"status":2}""", "application/json", statusCode: 201));
        app.MapGet("/sonarr/api/v3/calendar", () => Json(fake.SonarrCalendar));
        app.MapGet("/radarr/api/v3/calendar", () => Json(fake.RadarrCalendar));
        app.MapGet("/sonarr/api/v3/queue", () => Json(fake.SonarrQueue));
        app.MapGet("/radarr/api/v3/queue", () => Json(fake.RadarrQueue));
        await app.StartAsync();
        return fake;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static IResult Json(JsonNode node) => Results.Text(node.ToJsonString(), "application/json");
}
