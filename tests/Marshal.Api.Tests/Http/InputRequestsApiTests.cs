using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Auth;
using Marshal.Core.Mcp;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Marshal.Api.Tests.Http;

public sealed class InputRequestsApiTests : IAsyncLifetime
{
    private readonly FakeMcpServer _server = new();
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server.WithTool("upload");
        _server.RequestHandler = parameters =>
        {
            if (parameters.InputResponses is not { Count: > 0 } responses)
            {
                throw new InputRequiredException(new Dictionary<string, InputRequest>
                {
                    ["q"] = InputRequest.ForElicitation(new ElicitRequestParams
                    {
                        Mode = "form",
                        Message = "Which correspondent?",
                        RequestedSchema = new ElicitRequestParams.RequestSchema
                        {
                            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["correspondent"] = new ElicitRequestParams.StringSchema() },
                            Required = ["correspondent"],
                        },
                    }),
                }, "s");
            }

            string name = responses.Values.First().Deserialize(InputResponse.ElicitResultJsonTypeInfo)!.Content!["correspondent"].GetString()!;
            return new CallToolResult { Content = [new TextContentBlock { Text = $"Filed under {name}." }] };
        };
        _factory = new MarshalApiFactory(AuthFactory.Local().ToSettings(0));
        _factory.TestServices = services => services.AddSingleton<IMcpTransportFactory>(new Transports(_server));
        Directory.CreateDirectory(Path.Combine(_factory.ConfigDirectory, "mcp"));
        await File.WriteAllTextAsync(Path.Combine(_factory.ConfigDirectory, "mcp", "paperless.yaml"), "id: paperless\ntransport: http\nurl: http://paperless/mcp\n", Ct);
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
        _factory.Model.ThenText("Filed under ACME.");
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task ListAndGet_ShowTheFormOwnerScoped()
    {
        string id = await AskAsync();
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        JsonElement item = Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/approvals/inputs", Ct))!);
        JsonElement single = await _browser.GetFromJsonAsync<JsonElement>($"/api/v1/approvals/inputs/{id}", Ct);

        Assert.Equal((id, "paperless", "paperless.upload", "form", "Which correspondent?"),
            (item.GetProperty("id").GetString(), item.GetProperty("serverId").GetString(), item.GetProperty("tool").GetString(), item.GetProperty("mode").GetString(), item.GetProperty("message").GetString()));
        Assert.Equal("string", item.GetProperty("requestedSchema").GetProperty("properties").GetProperty("correspondent").GetProperty("type").GetString());
        Assert.Equal("pending", single.GetProperty("status").GetString());
        Assert.Empty((await other.GetFromJsonAsync<List<JsonElement>>("/api/v1/approvals/inputs", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/approvals/inputs/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Answer_RepeatsTheCallAndIsAuditedWithoutTheAnswer()
    {
        string id = await AskAsync();

        HttpResponseMessage response = await _browser.PostAsJsonAsync($"/api/v1/approvals/inputs/{id}", new { content = new { correspondent = "ACME" } }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("answered", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        Assert.Equal(3, _server.CallRequests.Count);
        string? details = await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.Where(entry => entry.Action == "input_request.answer").Select(entry => entry.Details).SingleAsync(Ct));
        Assert.DoesNotContain("ACME", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_InvalidAnswer_Returns400()
    {
        string id = await AskAsync();

        HttpResponseMessage response = await _browser.PostAsJsonAsync($"/api/v1/approvals/inputs/{id}", new { content = new { correspondent = 7 } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Decline_Returns200()
    {
        string id = await AskAsync();

        HttpResponseMessage response = await _browser.PostAsync($"/api/v1/approvals/inputs/{id}/decline", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("declined", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Token_CannotListOrAnswer()
    {
        string id = await AskAsync();
        HttpClient token = await _factory.ClientWithTokenAsync(Abstractions.Tokens.ApiScopes.Admin, Abstractions.Tokens.ApiScopes.ConversationWrite);

        Assert.Equal(HttpStatusCode.Forbidden, (await token.GetAsync("/api/v1/approvals/inputs", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await token.PostAsJsonAsync($"/api/v1/approvals/inputs/{id}", new { content = new { correspondent = "ACME" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync($"/api/v1/approvals/inputs/{id}", new { content = new { correspondent = "ACME" } }, Ct)).StatusCode);
    }

    // The call a web turn makes: the server asks, Marshal freezes the call.
    private async Task<string> AskAsync()
    {
        McpToolSources sources = _factory.Services.GetRequiredService<McpToolSources>();
        await sources.WhenIdleAsync();
        IToolSource source = Assert.Single(((IToolSourceProvider)sources).Sources);
        ITool upload = Assert.Single(await source.GetToolsAsync(Ct));
        var context = new MarshalExecutionContext { OwnerId = _user, Source = SessionSource.Web, IsInteractive = true };
        using (_factory.Services.GetRequiredService<IExecutionContextAccessor>().Use(context))
            await upload.InvokeAsync(new ToolInvocation(JsonDocument.Parse("""{"file":"letter.pdf"}""").RootElement.Clone(), context), Ct);

        return context.Control.InputRequests.Single().ToString();
    }

    private sealed class Transports(FakeMcpServer server) : IMcpTransportFactory
    {
        public IClientTransport Create(McpConnectionSettings settings) => server.CreateTransport();
    }
}
