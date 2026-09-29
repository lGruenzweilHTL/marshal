using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Identity;
using Marshal.Api.Tests.Auth;
using Marshal.Data.Audit;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Api.Tests.Http;

public sealed class MemoryApiTests : IAsyncLifetime
{
    private const string Chunks = "/api/v1/memory/chunks";
    private const string Projects = "/api/v1/memory/projects";

    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new MarshalApiFactory(AuthFactory.Local().ToSettings(0));
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Chunks_CreateEditListAndDetail()
    {
        string id = await CreateAsync(new { scope = "general", type = "device", title = "Voron nozzle", content = "0.4 mm nozzle.", importance = 4 });
        HttpResponseMessage edit = await _browser.PutAsJsonAsync($"{Chunks}/{id}", new { content = "0.6 mm nozzle.", importance = 4 }, Ct);
        await CreateAsync(new { scope = "general", type = "preference", title = "Coffee", content = "Likes oat milk." });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        JsonElement detail = await _browser.GetFromJsonAsync<JsonElement>($"{Chunks}/{id}", Ct);
        Assert.Equal("0.6 mm nozzle.", detail.GetProperty("chunk").GetProperty("content").GetString());
        Assert.Equal("user", detail.GetProperty("chunk").GetProperty("createdBy").GetString());
        Assert.Equal("0.4 mm nozzle.", detail.GetProperty("versions")[0].GetProperty("content").GetString());
        List<JsonElement> devices = (await _browser.GetFromJsonAsync<List<JsonElement>>($"{Chunks}?type=device", Ct))!;
        Assert.Equal([id], devices.Select(chunk => chunk.GetProperty("id").GetString()));
        Assert.Equal(2, (await _browser.GetFromJsonAsync<List<JsonElement>>(Chunks, Ct))!.Count);
    }

    [Fact]
    public async Task Chunks_SearchFindsByKeyword()
    {
        await CreateAsync(new { scope = "general", type = "device", title = "Voron nozzle", content = "0.6 mm nozzle." });
        await CreateAsync(new { scope = "general", type = "preference", title = "Coffee", content = "Likes oat milk." });

        List<JsonElement> found = (await _browser.GetFromJsonAsync<List<JsonElement>>($"{Chunks}?query=nozzle", Ct))!;

        Assert.Contains(found, chunk => chunk.GetProperty("title").GetString() == "Voron nozzle");
    }

    [Fact]
    public async Task Chunks_InvalidInput_ValidationProblem()
    {
        HttpResponseMessage response = await _browser.PostAsJsonAsync(Chunks, new { scope = "general", type = "fact", title = "", content = "x" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("memory.invalid_chunk", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PinAndUnpin_PinnedChunkInjectedInEveryTurn()
    {
        string id = await CreateAsync(new { scope = "general", type = "fact", title = "Allergy", content = "Allergic to peanuts." });
        _factory.Model.ThenText("Noted.").ThenText("Okay.");

        Assert.Equal(HttpStatusCode.NoContent, (await _browser.PutAsync($"{Chunks}/{id}/pin", null, Ct)).StatusCode);
        await TurnAsync("What should I cook?");
        Assert.Equal(HttpStatusCode.NoContent, (await _browser.DeleteAsync($"{Chunks}/{id}/pin", Ct)).StatusCode);
        await TurnAsync("And tomorrow?");

        Assert.Contains("Allergic to peanuts.", _factory.Model.Requests[0].SystemPrompt, StringComparison.Ordinal);
        Assert.False((await _browser.GetFromJsonAsync<JsonElement>($"{Chunks}/{id}", Ct)).GetProperty("chunk").GetProperty("pinned").GetBoolean());
        List<JsonElement> pinned = (await _browser.GetFromJsonAsync<List<JsonElement>>($"{Chunks}?pinned=true", Ct))!;
        Assert.Empty(pinned);
    }

    [Fact]
    public async Task SupersedeAndHistory_PredecessorVersionAndCurrentInOrder()
    {
        string old = await CreateAsync(new { scope = "general", type = "device", title = "Printer nozzle", content = "0.4 mm." });
        HttpResponseMessage superseded = await _browser.PostAsJsonAsync($"{Chunks}/{old}/supersede", new { scope = "general", type = "device", title = "Voron nozzle", content = "0.5 mm." }, Ct);
        string successor = (await superseded.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        await _browser.PutAsJsonAsync($"{Chunks}/{successor}", new { content = "0.6 mm.", importance = 3 }, Ct);

        List<JsonElement> history = (await _browser.GetFromJsonAsync<List<JsonElement>>($"{Chunks}/{successor}/history", Ct))!;

        Assert.Equal(HttpStatusCode.Created, superseded.StatusCode);
        Assert.Equal(
            [("predecessor", "0.4 mm."), ("version", "0.5 mm."), ("current", "0.6 mm.")],
            history.Select(entry => (entry.GetProperty("kind").GetString(), entry.GetProperty("content").GetString())));
    }

    [Fact]
    public async Task Delete_SoftDeletedHiddenFromListUnlessRequested()
    {
        string id = await CreateAsync(new { scope = "general", type = "fact", title = "Old router", content = "In the attic." });

        Assert.Equal(HttpStatusCode.NoContent, (await _browser.DeleteAsync($"{Chunks}/{id}", Ct)).StatusCode);

        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>(Chunks, Ct))!);
        JsonElement deleted = Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>($"{Chunks}?includeDeleted=true", Ct))!);
        Assert.NotEqual(JsonValueKind.Null, deleted.GetProperty("deletedAt").ValueKind);
    }

    [Fact]
    public async Task Audit_ListsOpenContradictionsAndStaleSections()
    {
        await CreateAsync(new { scope = "general", type = "fact", title = "Kettle", content = "In the kitchen." });

        JsonElement audit = await _browser.GetFromJsonAsync<JsonElement>("/api/v1/memory/audit", Ct);

        Assert.Equal(JsonValueKind.Array, audit.GetProperty("stale").ValueKind);
        Assert.Equal(JsonValueKind.Array, audit.GetProperty("contradictions").ValueKind);
        Assert.Equal(JsonValueKind.Array, audit.GetProperty("orphans").ValueKind);
        Assert.Equal(JsonValueKind.Array, audit.GetProperty("danglingEdges").ValueKind);
    }

    [Fact]
    public async Task Export_NdjsonWithTheChunk()
    {
        await CreateAsync(new { scope = "general", type = "fact", title = "Kettle", content = "In the kitchen." });

        HttpResponseMessage response = await _browser.GetAsync("/api/v1/memory/export", Ct);

        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("In the kitchen.", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_GraphAndScatterOfTheUsersChunks()
    {
        string printer = await CreateAsync(new { scope = "general", type = "device", title = "Printer", content = "A Voron 2.4." });
        string nozzle = await CreateAsync(new { scope = "general", type = "device", title = "Nozzle", content = "0.6 mm." });

        JsonElement graph = await _browser.GetFromJsonAsync<JsonElement>("/api/v1/memory/graph", Ct);
        List<JsonElement> scatter = (await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/memory/scatter", Ct))!;

        Assert.Equal(2, graph.GetProperty("nodes").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, graph.GetProperty("edges").ValueKind);
        Assert.Equal(new HashSet<string?> { printer, nozzle }, scatter.Select(point => point.GetProperty("id").GetString()).ToHashSet());
        Assert.All(scatter, point => Assert.Equal(JsonValueKind.Number, point.GetProperty("x").ValueKind));
    }

    [Fact]
    public async Task Projects_CreateAliasesAndChunksOfTheProject()
    {
        HttpResponseMessage created = await _browser.PostAsJsonAsync(Projects, new { name = "Voron", description = "The 3D printer." }, Ct);
        string project = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        HttpResponseMessage duplicate = await _browser.PostAsJsonAsync(Projects, new { name = "voron", description = "" }, Ct);
        HttpResponseMessage alias = await _browser.PostAsJsonAsync($"{Projects}/{project}/aliases", new { phrase = "the printer" }, Ct);
        string aliasId = (await alias.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        HttpResponseMessage rejected = await _browser.PutAsJsonAsync($"{Projects}/{project}/aliases/{aliasId}", new { status = "rejected" }, Ct);
        await _browser.PutAsJsonAsync($"{Projects}/{project}", new { name = "Voron 2.4", description = "The big printer." }, Ct);
        await CreateAsync(new { scope = "project", projectId = project, type = "procedure", title = "Calibration", content = "Run the z offset macro." });

        JsonElement listed = Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>(Projects, Ct))!);
        List<JsonElement> chunks = (await _browser.GetFromJsonAsync<List<JsonElement>>($"{Chunks}?projectId={project}", Ct))!;
        List<JsonElement> feedback = (await _browser.GetFromJsonAsync<List<JsonElement>>($"{Projects}/{project}/feedback", Ct))!;

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(("Voron 2.4", "The big printer.", 1), (listed.GetProperty("name").GetString(), listed.GetProperty("description").GetString(), listed.GetProperty("chunkCount").GetInt32()));
        JsonElement storedAlias = Assert.Single(listed.GetProperty("aliases").EnumerateArray());
        Assert.Equal(("the printer", "rejected"), (storedAlias.GetProperty("phrase").GetString(), storedAlias.GetProperty("status").GetString()));
        Assert.Equal("Calibration", Assert.Single(chunks).GetProperty("title").GetString());
        Assert.Empty(feedback);
    }

    [Fact]
    public async Task Projects_DeleteLeavesOrphansInTheAudit()
    {
        string project = (await (await _browser.PostAsJsonAsync(Projects, new { name = "Voron", description = "" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        string chunk = await CreateAsync(new { scope = "project", projectId = project, type = "procedure", title = "Calibration", content = "Run the z offset macro." });

        Assert.Equal(HttpStatusCode.NoContent, (await _browser.DeleteAsync($"{Projects}/{project}", Ct)).StatusCode);

        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>(Projects, Ct))!);
        JsonElement audit = await _browser.GetFromJsonAsync<JsonElement>("/api/v1/memory/audit", Ct);
        Assert.Equal(chunk, Assert.Single(audit.GetProperty("orphans").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Changes_WrittenToTheAuditLogWithTheUserAsActor()
    {
        string id = await CreateAsync(new { scope = "general", type = "fact", title = "Allergy", content = "Allergic to peanuts." });
        await _browser.PutAsync($"{Chunks}/{id}/pin", null, Ct);
        await _browser.DeleteAsync($"{Chunks}/{id}/pin", Ct);
        await _browser.DeleteAsync($"{Chunks}/{id}", Ct);
        await _browser.PostAsJsonAsync(Projects, new { name = "Voron", description = "" }, Ct);

        List<AuditEvent> audit = await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.Where(entry => entry.Action.StartsWith("memory.")).OrderBy(entry => entry.CreatedAt).ToListAsync(Ct));

        Assert.Equal(["memory.chunk.create", "memory.chunk.pin", "memory.chunk.unpin", "memory.chunk.delete", "memory.project.create"], audit.Select(entry => entry.Action));
        Assert.All(audit, entry => Assert.Equal(_user.Value.ToString(), entry.ActorId));
        Assert.Equal(id, audit[1].Target);
    }

    [Fact]
    public async Task Ownership_OtherUsersChunksAndProjectsAreNotFound()
    {
        string id = await CreateAsync(new { scope = "general", type = "fact", title = "Allergy", content = "Allergic to peanuts." });
        string project = (await (await _browser.PostAsJsonAsync(Projects, new { name = "Voron", description = "" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Chunks}/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"{Chunks}/{id}/pin", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Chunks}/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Projects}/{project}", Ct)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<JsonElement>>(Chunks, Ct))!);
        Assert.Empty((await other.GetFromJsonAsync<List<JsonElement>>(Projects, Ct))!);
    }

    [Fact]
    public async Task Tokens_CannotReachTheMemoryView()
    {
        HttpClient token = await _factory.ClientWithTokenAsync(Abstractions.Tokens.ApiScopes.ConversationWrite);

        Assert.Equal(HttpStatusCode.Forbidden, (await token.GetAsync(Chunks, Ct)).StatusCode);
    }

    private async Task<string> CreateAsync(object body)
    {
        HttpResponseMessage response = await _browser.PostAsJsonAsync(Chunks, body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    private async Task TurnAsync(string text)
    {
        HttpResponseMessage response = await _browser.PostAsJsonAsync("/api/v1/conversation/turns", new { text }, Ct);
        await response.Content.ReadAsStringAsync(Ct);
    }
}
