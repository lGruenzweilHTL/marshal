using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Tokens;
using Marshal.Data;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class ConversationApiTests
{
    private const string Turns = "/api/v1/conversation/turns";

    [Fact]
    public async Task Turn_ValidToken_StreamsDeltasAndFinalEventEchoingClientId()
    {
        using var factory = new MarshalApiFactory();
        factory.Model.ThenText("It is sunny.");
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage response = await client.PostAsJsonAsync(Turns, Body("01JABC", "weather?"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("1.0", response.Headers.GetValues("api-supported-versions").Single(), StringComparison.Ordinal);
        List<(string Event, JsonElement Data)> events = Sse.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("It is sunny.", string.Concat(events.Where(e => e.Event == "delta").Select(e => e.Data.GetProperty("text").GetString())));
        (string name, JsonElement final) = events[^1];
        Assert.Equal("final", name);
        Assert.Equal("It is sunny.", final.GetProperty("speech").GetString());
        Assert.False(final.GetProperty("continueConversation").GetBoolean());
        Assert.Equal("01JABC", final.GetProperty("conversationId").GetString());
    }

    [Fact]
    public async Task Turn_ConfirmTier_ContinueConversationThenKeywordExecutes()
    {
        FakeTool unlock = FakeTool.Confirm("test.unlock").Returns("unlocked");
        using var factory = new MarshalApiFactory(null, unlock);
        factory.Model.ThenToolCall("test_unlock", new { door = "front" }).ThenText("Should I unlock the front door?").ThenText("Done, it is unlocked.");
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        JsonElement first = await FinalAsync(await client.PostAsJsonAsync(Turns, Body("c1", "unlock the front door", inputMode: "voice"), Ct));
        JsonElement second = await FinalAsync(await client.PostAsJsonAsync(Turns, Body("c1", "Confirm!", inputMode: "voice"), Ct));

        Assert.True(first.GetProperty("continueConversation").GetBoolean());
        Assert.Equal("Done, it is unlocked.", second.GetProperty("speech").GetString());
        Assert.False(second.GetProperty("continueConversation").GetBoolean());
        Assert.Equal("""{"door":"front"}""", Assert.Single(unlock.Invocations).Arguments.GetRawText());
    }

    [Fact]
    public async Task Turn_UnknownFollowupRef_Ignored()
    {
        using var factory = new MarshalApiFactory();
        factory.Model.ThenText("ok");
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        JsonElement final = await FinalAsync(await client.PostAsJsonAsync(Turns, Body("c1", "hi") with { FollowupRef = "nope" }, Ct));

        Assert.Equal("ok", final.GetProperty("speech").GetString());
    }

    [Fact]
    public async Task Turn_TokenWithoutConversationScope_403AndNoRun()
    {
        using var factory = new MarshalApiFactory();
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.TasksRead);

        HttpResponseMessage response = await client.PostAsJsonAsync(Turns, Body("c1", "hi"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountRunsAsync(factory));
        Assert.Empty(factory.Model.Requests);
    }

    [Fact]
    public async Task Turn_NoToken_401()
    {
        using var factory = new MarshalApiFactory();

        HttpResponseMessage response = await factory.CreateClient().PostAsJsonAsync(Turns, Body("c1", "hi"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Turn_RevokedToken_401()
    {
        using var factory = new MarshalApiFactory();
        string token = await factory.CreateTokenAsync(ApiScopes.ConversationWrite);
        await RevokeAllAsync(factory);
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        HttpResponseMessage response = await client.PostAsJsonAsync(Turns, Body("c1", "hi"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Turn_TokenClientWithoutConversationId_400ProblemWithCode()
    {
        using var factory = new MarshalApiFactory();
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage response = await client.PostAsJsonAsync(Turns, Body(null, "hi"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("conversation.id_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Turn_UnsupportedVersion_ErrorNamingSupportedVersions()
    {
        using var factory = new MarshalApiFactory();
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v9/conversation/turns", Body("c1", "hi"), Ct);

        Assert.False(response.IsSuccessStatusCode);
        string supported = response.Headers.TryGetValues("api-supported-versions", out IEnumerable<string>? values) ? string.Join(",", values) : "";
        Assert.Contains("1.0", supported + await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versions_Anonymous_ListsSupported()
    {
        using var factory = new MarshalApiFactory();

        JsonElement versions = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/versions", Ct);

        Assert.Contains("1.0", versions.GetProperty("supported").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(JsonValueKind.Array, versions.GetProperty("deprecated").ValueKind);
    }

    [Fact]
    public async Task OpenApi_V1Document_DescribesVersionedTurnEndpoint()
    {
        using var factory = new MarshalApiFactory();

        string document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("/api/v1/conversation/turns", document, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/versions", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathBase_PrefixesVersionedRoutes()
    {
        using var factory = new MarshalApiFactory(new() { ["http:pathBase"] = "/marshal" });
        factory.Model.ThenText("ok");
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage response = await client.PostAsJsonAsync("/marshal" + Turns, Body("c1", "hi"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Startup_InvalidOption_FailsNamingKey()
    {
        using var factory = new MarshalApiFactory(new() { ["agent:maxSteps"] = "0" });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("agent.maxSteps", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_RequiredModelKeyMissing_FailsNamingReference()
    {
        using var factory = new MarshalApiFactory(new()
        {
            ["models:claude:tier"] = "advisor",
            ["models:claude:provider"] = "anthropic",
            ["models:claude:model"] = "claude-sonnet",
            ["models:claude:apiKeySecret"] = "anthropic_key",
        });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("models.claude.apiKeySecret (anthropic_key)", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_RequiredModelKeyFromSecretFile_Starts()
    {
        using var factory = new MarshalApiFactory(new()
        {
            ["models:claude:tier"] = "advisor",
            ["models:claude:provider"] = "anthropic",
            ["models:claude:model"] = "claude-sonnet",
            ["models:claude:apiKeySecret"] = "anthropic_key",
        });
        await File.WriteAllTextAsync(Path.Combine(factory.SecretsDirectory, "anthropic_key"), "sk-ant-test\n", Ct);

        HttpResponseMessage response = await factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Startup_LiteralSecretInRiskFile_Fails()
    {
        using var factory = new MarshalApiFactory();
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "risk.yaml"), "risk: []\ntoken: abc\n", Ct);

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("token", Flatten(error), StringComparison.Ordinal);
        Assert.DoesNotContain("abc", Flatten(error), StringComparison.Ordinal);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TurnBody Body(string? conversationId, string text, string inputMode = "text") =>
        new(conversationId, text, "en", "device-1", "assist_satellite.kitchen", "kitchen", null, inputMode, null);

    private static async Task<JsonElement> FinalAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return Sse.Parse(await response.Content.ReadAsStringAsync(Ct)).Single(e => e.Event == "final").Data;
    }

    private static async Task<int> CountRunsAsync(MarshalApiFactory factory)
    {
        await using MarshalDbContext db = await factory.Services.GetRequiredService<IDbContextFactory<MarshalDbContext>>().CreateDbContextAsync(Ct);
        return await db.Runs.IgnoreQueryFilters().CountAsync(Ct);
    }

    private static async Task RevokeAllAsync(MarshalApiFactory factory)
    {
        await using MarshalDbContext db = await factory.Services.GetRequiredService<IDbContextFactory<MarshalDbContext>>().CreateDbContextAsync(Ct);
        await db.ApiTokens.IgnoreQueryFilters().ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, DateTimeOffset.UnixEpoch), Ct);
    }

    private static string Flatten(Exception error) => error is AggregateException aggregate
        ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
        : error.Message + (error.InnerException is null ? "" : " | " + Flatten(error.InnerException));

    private sealed record TurnBody(string? ConversationId, string Text, string? Language, string? DeviceId, string? SatelliteId, string? AreaId, string? HaUserId, string InputMode, string? FollowupRef);
}
