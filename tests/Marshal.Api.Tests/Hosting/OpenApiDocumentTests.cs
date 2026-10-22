using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Marshal.Api.Auth;
using Marshal.Api.Hosting;
using Marshal.Api.Tests.Http;
using Microsoft.AspNetCore.Authorization;

namespace Marshal.Api.Tests.Hosting;

/// <summary>Operation names and security requirements of the served v1 document.</summary>
public sealed class OpenApiDocumentTests : IAsyncLifetime
{
    private readonly MarshalApiFactory _factory = new();
    private JsonNode _document = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _document = (await _factory.CreateClient().GetFromJsonAsync<JsonNode>("/openapi/v1.json", Ct))!;

    [Fact]
    public void Operation_IsNamedByItsRouteBelowTheVersionBase()
    {
        JsonNode operation = Operation("CreateConversationTurn");

        Assert.Equal("/conversation/turns", operation["summary"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(operation["description"]?.GetValue<string>()));
    }

    [Fact]
    public void EveryOperation_IsNamedByItsPath()
    {
        foreach ((string path, JsonNode? item) in _document["paths"]!.AsObject())
        {
            foreach ((string method, JsonNode? operation) in item!.AsObject())
            {
                Assert.True(path.StartsWith("/api/v1/", StringComparison.Ordinal), path);
                Assert.Equal(path["/api/v1".Length..], operation!["summary"]?.GetValue<string>());
            }
        }
    }

    [Fact]
    public void Document_DeclaresTokenAndSessionSchemes()
    {
        JsonNode schemes = _document["components"]!["securitySchemes"]!;

        Assert.Equal("http", schemes["apiToken"]!["type"]!.GetValue<string>());
        Assert.Equal("bearer", schemes["apiToken"]!["scheme"]!.GetValue<string>());
        Assert.Equal("apiKey", schemes["session"]!["type"]!.GetValue<string>());
        Assert.Equal("cookie", schemes["session"]!["in"]!.GetValue<string>());
        Assert.Equal("marshal_session", schemes["session"]!["name"]!.GetValue<string>());
        Assert.Null(_document["security"]);
    }

    [Theory]
    [InlineData("CreateConversationTurn", "apiToken:conversation:write|session")]
    [InlineData("RunAiTask", "apiToken:aitasks:write|session")]
    [InlineData("GetMe", "apiToken|session")]
    [InlineData("ListApiTokens", "session")]
    [InlineData("GetSystemHealth", "apiToken:admin|session")]
    [InlineData("PostWebhook", "apiToken")]
    [InlineData("ListLoginProviders", "")]
    public void Operation_RequiresTheCredentialsItsEndpointEnforces(string operationId, string expected)
    {
        Assert.Equal(expected, Security(Operation(operationId)));
    }

    [Fact]
    public void EndpointForAdminSessions_RequiresTheSessionOnly()
    {
        // RequireAuthorization(Policies.Human, Policies.Admin): a session and the admin scope, never a token.
        Assert.Equal("session", Security(Operation("ListInstanceSecrets")));
    }

    [Fact]
    public void EndpointWithAnUnmappedPolicy_FailsNamingThePolicyAndTheRoute()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => OpenApiSecurity.Credentials([new AuthorizeAttribute("made-up")], "api/v1/things"));

        Assert.Contains("made-up", error.Message, StringComparison.Ordinal);
        Assert.Contains("api/v1/things", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnonymousEndpoint_NeedsNoCredential()
    {
        Assert.Null(OpenApiSecurity.Credentials([new AuthorizeAttribute(Policies.Human), new AllowAnonymousAttribute()], "api/v1/things"));
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private JsonNode Operation(string operationId) =>
        _document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(operation => operation.Value!))
            .Single(operation => operation["operationId"]?.GetValue<string>() == operationId);

    /// <summary>The requirement objects as "scheme[:scope,scope]" joined with "|" ("" for none).</summary>
    private static string Security(JsonNode operation) =>
        string.Join('|', (operation["security"]?.AsArray() ?? []).Select(requirement => string.Join('&',
            requirement!.AsObject().Select(scheme =>
            {
                string[] scopes = scheme.Value!.AsArray().Select(scope => scope!.GetValue<string>()).ToArray();
                return scopes.Length == 0 ? scheme.Key : $"{scheme.Key}:{string.Join(',', scopes)}";
            }))));
}
