using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Identity;
using Marshal.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Api.Tests.Http;

public sealed class LiveEventsApiTests : IAsyncLifetime
{
    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await AuthFactory.CreateAsync(AuthFactory.Local());

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Events_UnknownTopic_Returns400NamingIt()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage response = await browser.GetAsync("/api/v1/events?topics=runs,foo", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("foo", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_Anonymous_Returns401()
    {
        HttpResponseMessage response = await Browser.Create(_factory).GetAsync("/api/v1/events?topics=runs", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Events_RunsTopic_DeliversOnlyTheOwnersRuns()
    {
        _factory.Model.ThenText("for bob").ThenText("for alice");
        (HttpClient alice, OwnerId aliceId) = await Browser.SignInAsync(_factory, "user");
        (HttpClient bob, _) = await Browser.SignInAsync(_factory, "user");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events?topics=runs");
        using HttpResponseMessage stream = await alice.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(Ct));

        await (await bob.PostAsJsonAsync("/api/v1/conversation/turns", new { text = "hi" }, Ct)).Content.ReadAsStringAsync(Ct);
        await (await alice.PostAsJsonAsync("/api/v1/conversation/turns", new { text = "hi" }, Ct)).Content.ReadAsStringAsync(Ct);
        List<(string Event, JsonElement Data)> events = await ReadUntilAsync(reader, "run.completed");

        Guid aliceRun = await _factory.WithDatabaseAsync(aliceId, db => db.Runs.Select(run => run.Id.Value).SingleAsync(Ct));
        Assert.Equal(["run.started", "run.completed"], events.Select(entry => entry.Event));
        Assert.All(events, entry => Assert.Equal("runs", entry.Data.GetProperty("topic").GetString()));
        Assert.All(events, entry => Assert.Equal(aliceRun.ToString(), entry.Data.GetProperty("payload").GetProperty("runId").GetString()));
    }

    private static async Task<List<(string Event, JsonElement Data)>> ReadUntilAsync(StreamReader reader, string lastEvent)
    {
        var events = new List<(string, JsonElement)>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string name = "message";
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal) && name != "ping")
            {
                events.Add((name, JsonDocument.Parse(line[5..]).RootElement.Clone()));
                if (name == lastEvent)
                    break;
            }
        }

        return events;
    }
}
