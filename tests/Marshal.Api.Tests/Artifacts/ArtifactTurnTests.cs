using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Microsoft.Extensions.AI;

namespace Marshal.Api.Tests.Artifacts;

public sealed class ArtifactTurnTests : IAsyncLifetime
{
    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await AuthFactory.CreateAsync(AuthFactory.Local());

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task WebTurn_ListsAndShowsAnArtifact_StreamsItAndTaintsTheRun()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        using var form = new MultipartFormDataContent { { new ByteArrayContent("solid cube\nfacet normal 0 0 1\nendsolid cube\n"u8.ToArray()), "file", "cube.stl" } };
        string id = (await (await browser.PostAsync("/api/v1/artifacts", form, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        _factory.Model.ThenToolCall("artifacts_list", new { kind = "model3d" }).ThenToolCall("artifacts_show", new { id }).ThenText("Here is the cube.");

        HttpResponseMessage response = await browser.PostAsJsonAsync("/api/v1/conversation/turns", new { text = "show me the cube" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<(string Event, JsonElement Data)> events = Sse.Parse(await response.Content.ReadAsStringAsync(Ct));
        JsonElement artifact = Assert.Single(events, entry => entry.Event == "artifact").Data;
        Assert.Equal((id, "cube.stl", "model3d"), (artifact.GetProperty("id").GetString(), artifact.GetProperty("name").GetString(), artifact.GetProperty("kind").GetString()));
        Assert.EndsWith($"/api/v1/artifacts/{id}/content", artifact.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.True(events.FindIndex(entry => entry.Event == "artifact") < events.FindIndex(entry => entry.Event == "final"));
        Assert.Contains(_factory.Model.Requests[1].Messages, message => message.Contents.OfType<FunctionResultContent>().Any(result => result.Result?.ToString()?.Contains("cube.stl", StringComparison.Ordinal) == true));
        string runId = (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct))![0].GetProperty("id").GetString()!;
        string detail = await browser.GetStringAsync($"/api/v1/runs/{runId}", Ct);
        Assert.Contains("\"artifact\"", detail, StringComparison.Ordinal);
    }
}
