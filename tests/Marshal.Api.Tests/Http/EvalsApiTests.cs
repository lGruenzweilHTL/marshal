using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Auth;

namespace Marshal.Api.Tests.Http;

public sealed class EvalsApiTests : IAsyncLifetime
{
    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["models:fast:price:inputPerMTok"] = "1";
        settings["models:fast:price:outputPerMTok"] = "1";
        settings["models:standard:price:inputPerMTok"] = "1";
        settings["models:standard:price:outputPerMTok"] = "1";
        _factory = new MarshalApiFactory(settings);
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Rating_StoredOnTheRunAndShownInListAndDetail()
    {
        _factory.Model.ThenText("done");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string run = await TurnAsync(browser, "turn off the kitchen light");

        HttpResponseMessage rated = await browser.PutAsJsonAsync($"/api/v1/runs/{run}/rating", new { rating = "down", note = "wrong light" }, Ct);

        Assert.Equal(HttpStatusCode.OK, rated.StatusCode);
        JsonElement body = await rated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("caseId").ValueKind);
        JsonElement detail = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{run}", Ct);
        Assert.Equal("down", detail.GetProperty("run").GetProperty("rating").GetString());
        Assert.Equal("wrong light", detail.GetProperty("run").GetProperty("ratingNote").GetString());
        JsonElement listed = (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct))![0];
        Assert.Equal("down", listed.GetProperty("rating").GetString());
    }

    [Fact]
    public async Task Rating_ShownOnTheTurnOfTheConversation()
    {
        _factory.Model.ThenText("done");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string run = await TurnAsync(browser, "hi");
        await browser.PutAsJsonAsync($"/api/v1/runs/{run}/rating", new { rating = "up" }, Ct);
        string session = (await browser.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{run}", Ct)).GetProperty("run").GetProperty("sessionId").GetString()!;

        JsonElement detail = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{session}", Ct);

        JsonElement turn = Assert.Single(detail.GetProperty("entries").EnumerateArray());
        Assert.Equal(run, turn.GetProperty("runId").GetString());
        Assert.Equal("up", turn.GetProperty("rating").GetString());
    }

    [Fact]
    public async Task Rating_Cleared()
    {
        _factory.Model.ThenText("done");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string run = await TurnAsync(browser, "hi");
        await browser.PutAsJsonAsync($"/api/v1/runs/{run}/rating", new { rating = "up" }, Ct);

        HttpResponseMessage cleared = await browser.DeleteAsync($"/api/v1/runs/{run}/rating", Ct);

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        JsonElement detail = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{run}", Ct);
        Assert.False(detail.GetProperty("run").TryGetProperty("rating", out JsonElement rating) && rating.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Rating_ForeignRun_NotFound()
    {
        _factory.Model.ThenText("done");
        (HttpClient alice, _) = await Browser.SignInAsync(_factory, "user");
        (HttpClient bob, _) = await Browser.SignInAsync(_factory, "user");
        string run = await TurnAsync(alice, "hi");

        HttpResponseMessage response = await bob.PutAsJsonAsync($"/api/v1/runs/{run}/rating", new { rating = "up" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rating_MachineToken_Forbidden()
    {
        _factory.Model.ThenText("done");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string run = await TurnAsync(browser, "hi");
        HttpClient token = await _factory.ClientWithTokenAsync(ApiScopes.Admin, ApiScopes.ConversationWrite);

        HttpResponseMessage response = await token.PutAsJsonAsync($"/api/v1/runs/{run}/rating", new { rating = "up" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rating_NoteTooLongOrUnknownRating_Returns400()
    {
        _factory.Model.ThenText("done");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string run = await TurnAsync(browser, "hi");

        HttpResponseMessage longNote = await browser.PutAsJsonAsync($"/api/v1/runs/{run}/rating", new { rating = "down", note = new string('x', 1001) }, Ct);
        HttpResponseMessage unknown = await browser.PutAsJsonAsync($"/api/v1/runs/{run}/rating", new { rating = "meh" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, longNote.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Rating_WithLearningOn_CreatesCase()
    {
        _factory.Model.ThenText("done");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        JsonElement preferences = await browser.GetFromJsonAsync<JsonElement>("/api/v1/me/preferences", Ct);
        Dictionary<string, object?> update = JsonSerializer.Deserialize<Dictionary<string, object?>>(preferences.GetRawText())!;
        update["helpMarshalLearn"] = true;
        (await browser.PutAsJsonAsync("/api/v1/me/preferences", update, Ct)).EnsureSuccessStatusCode();
        string run = await TurnAsync(browser, "hi");

        JsonElement rated = await (await browser.PutAsJsonAsync($"/api/v1/runs/{run}/rating", new { rating = "up" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(JsonValueKind.String, rated.GetProperty("caseId").ValueKind);
        JsonElement detail = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{run}", Ct);
        Assert.Equal(rated.GetProperty("caseId").GetString(), detail.GetProperty("evalCaseId").GetString());
    }

    [Fact]
    public async Task Runs_FilterByRating()
    {
        _factory.Model.ThenText("one").ThenText("two");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string first = await TurnAsync(browser, "first");
        await TurnAsync(browser, "second");
        await browser.PutAsJsonAsync($"/api/v1/runs/{first}/rating", new { rating = "down" }, Ct);

        List<JsonElement> down = (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs?rating=down", Ct))!;
        List<JsonElement> up = (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs?rating=up", Ct))!;
        HttpResponseMessage invalid = await browser.GetAsync("/api/v1/runs?rating=sideways", Ct);

        Assert.Equal(first, Assert.Single(down).GetProperty("id").GetString());
        Assert.Empty(up);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task SaveAsCase_AdminOnly_ListedEditedAndDeleted()
    {
        _factory.Model.ThenText("done");
        (HttpClient admin, _) = await Browser.SignInAsync(_factory, "admin");
        string run = await TurnAsync(admin, "turn off the kitchen light");
        (HttpClient user, _) = await Browser.SignInAsync(_factory, "user");

        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"/api/v1/runs/{run}/eval-case", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/evals/cases", Ct)).StatusCode);

        HttpResponseMessage saved = await admin.PostAsync($"/api/v1/runs/{run}/eval-case", null, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        JsonElement evalCase = await saved.Content.ReadFromJsonAsync<JsonElement>(Ct);
        string id = evalCase.GetProperty("id").GetString()!;
        Assert.Equal("unrated", evalCase.GetProperty("expectation").GetString());
        Assert.Equal("turn off the kitchen light", evalCase.GetProperty("input").GetString());

        List<JsonElement> cases = (await admin.GetFromJsonAsync<List<JsonElement>>("/api/v1/evals/cases", Ct))!;
        Assert.Equal(id, Assert.Single(cases).GetProperty("id").GetString());

        HttpResponseMessage noted = await admin.PutAsJsonAsync($"/api/v1/evals/cases/{id}/note", new { note = "should turn off the ceiling light" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, noted.StatusCode);
        JsonElement fetched = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/evals/cases/{id}", Ct);
        Assert.Equal("should turn off the ceiling light", fetched.GetProperty("note").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/evals/cases/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/evals/cases/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task EvalRun_EstimatedStartedAndReported()
    {
        _factory.Model.ThenText("done");
        (HttpClient admin, _) = await Browser.SignInAsync(_factory, "admin");
        string run = await TurnAsync(admin, "hi");
        string caseId = (await (await admin.PostAsync($"/api/v1/runs/{run}/eval-case", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        _factory.Model.ThenText("done");
        var request = new { caseIds = new[] { caseId }, models = new Dictionary<string, string> { ["fast"] = "fast", ["standard"] = "standard" } };

        JsonElement estimate = await (await admin.PostAsJsonAsync("/api/v1/evals/estimate", request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        HttpResponseMessage started = await admin.PostAsJsonAsync("/api/v1/evals/runs", request, Ct);

        Assert.True(estimate.GetProperty("estimatedCost").GetDecimal() > 0);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        string id = (await started.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        JsonElement report = await WaitForEndAsync(admin, id);
        Assert.Equal("completed", report.GetProperty("run").GetProperty("status").GetString());
        Assert.Equal("fast", report.GetProperty("run").GetProperty("candidate").GetProperty("models").GetProperty("fast").GetString());
        JsonElement result = Assert.Single(report.GetProperty("results").EnumerateArray());
        Assert.Equal(caseId, result.GetProperty("caseId").GetString());
        Assert.Equal("done", result.GetProperty("reply").GetString());
        Assert.Single((await admin.GetFromJsonAsync<List<JsonElement>>("/api/v1/evals/runs", Ct))!);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/evals/runs/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task EvalRun_InvalidCandidate_Returns400()
    {
        _factory.Model.ThenText("done");
        (HttpClient admin, _) = await Browser.SignInAsync(_factory, "admin");
        string run = await TurnAsync(admin, "hi");
        string caseId = (await (await admin.PostAsync($"/api/v1/runs/{run}/eval-case", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;

        HttpResponseMessage unknownModel = await admin.PostAsJsonAsync("/api/v1/evals/runs", new { caseIds = new[] { caseId }, models = new Dictionary<string, string> { ["fast"] = "gpt-9" } }, Ct);
        HttpResponseMessage unknownTier = await admin.PostAsJsonAsync("/api/v1/evals/runs", new { caseIds = new[] { caseId }, models = new Dictionary<string, string> { ["huge"] = "fast" } }, Ct);
        HttpResponseMessage unknownTemplate = await admin.PostAsJsonAsync("/api/v1/evals/runs", new { caseIds = new[] { caseId }, models = new Dictionary<string, string>(), template = "nope" }, Ct);
        HttpResponseMessage unknownCase = await admin.PostAsJsonAsync("/api/v1/evals/runs", new { caseIds = new[] { Guid.NewGuid() }, models = new Dictionary<string, string>() }, Ct);
        HttpResponseMessage noCases = await admin.PostAsJsonAsync("/api/v1/evals/runs", new { caseIds = Array.Empty<string>(), models = new Dictionary<string, string>() }, Ct);

        Assert.All([unknownModel, unknownTier, unknownTemplate, unknownCase, noCases], response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
    }

    [Fact]
    public async Task OpenApi_HasEvalEndpoints()
    {
        string document = await _factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("/api/v1/runs/{id}/rating", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/evals/runs", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/evals/cases", document, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> WaitForEndAsync(HttpClient admin, string id)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            JsonElement report = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/evals/runs/{id}", Ct);
            if (report.GetProperty("run").GetProperty("status").GetString() is not ("queued" or "running"))
                return report;
            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("The eval run did not end.");
    }

    private static async Task<string> TurnAsync(HttpClient browser, string text)
    {
        await (await browser.PostAsJsonAsync("/api/v1/conversation/turns", new { text }, Ct)).Content.ReadAsStringAsync(Ct);
        return (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct))![0].GetProperty("id").GetString()!;
    }
}
