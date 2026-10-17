using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Api.Tests.Auth;
using Microsoft.Extensions.AI;

namespace Marshal.Api.Tests.Http;

public sealed class RunsApiTests : IAsyncLifetime
{
    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        // 1 per million input tokens: a 1,000,000-token call costs exactly 1.
        settings["models:fast:price:inputPerMTok"] = "1";
        settings["models:fast:price:outputPerMTok"] = "0";
        settings["models:standard:price:inputPerMTok"] = "1";
        settings["models:standard:price:outputPerMTok"] = "0";
        _factory = new MarshalApiFactory(settings);
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Runs_ListsOwnRunsNewestFirstWithFilters()
    {
        _factory.Model.ThenText("one").ThenText("two");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        await TurnAsync(browser, "first");
        await TurnAsync(browser, "second");

        List<JsonElement>? runs = await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct);
        List<JsonElement>? voice = await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs?source=voice", Ct);

        Assert.Equal(2, runs?.Count);
        Assert.All(runs!, run => Assert.Equal("web", run.GetProperty("source").GetString()));
        Assert.True(runs![0].GetProperty("startedAt").GetDateTimeOffset() >= runs[1].GetProperty("startedAt").GetDateTimeOffset());
        Assert.Empty(voice!);
    }

    [Fact]
    public async Task RunDetail_ShowsStepsAndIsOwnerScoped()
    {
        _factory.Model.ThenText("hello there");
        (HttpClient alice, _) = await Browser.SignInAsync(_factory, "user");
        (HttpClient bob, _) = await Browser.SignInAsync(_factory, "user");
        await TurnAsync(alice, "hi");
        string id = (await alice.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct))![0].GetProperty("id").GetString()!;

        JsonElement detail = await alice.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{id}", Ct);
        HttpResponseMessage foreign = await bob.GetAsync($"/api/v1/runs/{id}", Ct);

        List<string?> kinds = [.. detail.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("kind").GetString())];
        Assert.Equal(["context", "modelCall", "reply"], kinds);
        Assert.Equal("hi", detail.GetProperty("steps")[0].GetProperty("input").GetString());
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Cost_ReportsDaysInTheUsersTimeZoneWithTheRecordedCost()
    {
        _factory.Model.ThenText("priced", new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 10 });
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        await TurnAsync(browser, "hi");

        JsonElement report = await browser.GetFromJsonAsync<JsonElement>("/api/v1/cost", Ct);

        Assert.Equal("Europe/Vienna", report.GetProperty("timeZone").GetString());
        Assert.Equal(1m, report.GetProperty("total").GetDecimal());
        Assert.Equal(0m, report.GetProperty("advisor").GetDecimal());
        JsonElement day = Assert.Single(report.GetProperty("days").EnumerateArray());
        Assert.Equal(1m, day.GetProperty("byTier").EnumerateObject().Single().Value.GetDecimal());
        Assert.Equal(1m, (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct))![0].GetProperty("cost").GetDecimal());
    }

    [Fact]
    public async Task Runs_TimeToFirstDelta_ListedAndSortedSlowestFirst()
    {
        _factory.Model.ThenText("one").ThenText("two").ThenText("three");
        (HttpClient browser, Abstractions.Identity.OwnerId owner) = await Browser.SignInAsync(_factory, "user");
        await TurnAsync(browser, "first");
        await TurnAsync(browser, "second");
        await TurnAsync(browser, "third");
        List<JsonElement> listed = (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct))!;
        Assert.All(listed, run => Assert.True(run.GetProperty("timeToFirstDeltaMs").GetInt32() >= 0));
        string slow = listed[1].GetProperty("id").GetString()!;
        await _factory.WithDatabaseAsync(owner, async db =>
        {
            List<Data.Conversations.Run> rows = [.. db.Runs];
            foreach (Data.Conversations.Run run in rows)
                run.TimeToFirstDeltaMs = run.Id.ToString() == slow ? 1600 : 300;
            rows.First(run => run.Id.ToString() != slow).TimeToFirstDeltaMs = null;
            return await db.SaveChangesAsync(Ct);
        });

        List<JsonElement> sorted = (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs?sort=timeToFirstDelta", Ct))!;

        Assert.Equal(slow, sorted[0].GetProperty("id").GetString());
        Assert.Equal(2, sorted.Count);
    }

    [Fact]
    public async Task Runs_UnknownSort_Returns400()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/v1/runs?sort=cost", Ct)).StatusCode);
    }

    [Fact]
    public async Task RunDetail_ListsLoadedNamespacesWithReasons()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["tools:retrieval:enabled"] = "true";
        settings["tools:retrieval:minTools"] = "0";
        await using var factory = new MarshalApiFactory(settings);
        factory.Model.ThenText("hello");
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");
        await TurnAsync(browser, "hi");
        string id = (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct))![0].GetProperty("id").GetString()!;

        JsonElement detail = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{id}", Ct);

        JsonElement agent = detail.GetProperty("loadedNamespaces").EnumerateArray().First();
        Assert.Equal(("agent", "core"), (agent.GetProperty("name").GetString(), agent.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task OpenApi_RunFieldsForLatencyAndToolRetrieval()
    {
        string document = await _factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("\"timeToFirstDeltaMs\"", document, StringComparison.Ordinal);
        Assert.Contains("\"loadedNamespaces\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runs_UnknownFilterValue_Returns400()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/v1/runs?source=satellite", Ct)).StatusCode);
    }

    [Fact]
    public async Task Cost_InvalidRange_Returns400()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage response = await browser.GetAsync("/api/v1/cost?from=2026-09-28&to=2026-09-01", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task TurnAsync(HttpClient browser, string text) =>
        await (await browser.PostAsJsonAsync("/api/v1/conversation/turns", new { text }, Ct)).Content.ReadAsStringAsync(Ct);
}
