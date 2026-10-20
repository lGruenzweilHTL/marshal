using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mcp;
using Marshal.Api.Apps;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Core.Mcp;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Marshal.Api.Tests.Apps;

public sealed class McpAppsApiTests : IAsyncLifetime
{
    private const string Html = "<!doctype html><script>parent.postMessage('hi', '*')</script>";

    private readonly FakeMcpServer _server = new();
    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server.WithAppTool("sales_report", "ui://charts/report").WithTool("delete_report");
        _server.Resources["ui://charts/report"] = (McpToolSource.AppMimeType, Html, null);
        _server.Handler = (name, arguments) => new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"{name}:{(arguments.TryGetProperty("month", out JsonElement month) ? month.GetString() : "-")}" }],
            StructuredContent = JsonSerializer.SerializeToElement(new { total = 12 }),
        };
        _factory = new MarshalApiFactory(AuthFactory.Local().ToSettings(0));
        _factory.TestServices = services => services.AddSingleton<IMcpTransportFactory>(new Transports(_server));
        Directory.CreateDirectory(Path.Combine(_factory.ConfigDirectory, "mcp"));
        await File.WriteAllTextAsync(Path.Combine(_factory.ConfigDirectory, "mcp", "charts.yaml"), "id: charts\ntransport: http\nurl: http://charts/mcp\npinTools: false\napps: true\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(_factory.ConfigDirectory, "risk.yaml"), "risk:\n  - match: { tool: \"charts.sales_report\" }\n    tier: safe\n", Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task WebTurn_AppTool_StreamsAppEventAndServesTheSandboxedHtml()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string appId = await ShowAppAsync(browser);

        HttpResponseMessage html = await browser.GetAsync($"/mcp-apps/{appId}", Ct);

        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Equal("text/html", html.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Html, await html.Content.ReadAsStringAsync(Ct));
        Assert.Equal(AppEndpoints.ContentSecurityPolicy, string.Join(";", html.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(
            "sandbox allow-scripts; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'self'",
            AppEndpoints.ContentSecurityPolicy);
    }

    [Fact]
    public async Task App_CallDataAndAvailability()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string appId = await ShowAppAsync(browser);

        JsonElement app = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/apps/{appId}", Ct);

        Assert.Equal(("charts", "sales_report", true), (app.GetProperty("server").GetString(), app.GetProperty("tool").GetString(), app.GetProperty("available").GetBoolean()));
        Assert.Equal("9", app.GetProperty("arguments").GetProperty("month").GetString());
        Assert.Equal(12, app.GetProperty("result").GetProperty("structuredContent").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task OtherOwner_404()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string appId = await ShowAppAsync(browser);
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/mcp-apps/{appId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/apps/{appId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/apps/{appId}/tool-calls", new { name = "sales_report" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task ToolCall_SameServer_RunsAsOwnRunAndReturnsTheResult()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");
        string appId = await ShowAppAsync(browser);

        HttpResponseMessage response = await browser.PostAsJsonAsync($"/api/v1/apps/{appId}/tool-calls", new { name = "sales_report", arguments = new { month = "10" } }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("sales_report:10", result.GetProperty("content")[0].GetProperty("text").GetString());
        Data.Conversations.Run run = await _factory.WithDatabaseAsync(user, db => db.Runs.SingleAsync(entry => entry.Trigger == "app:charts/sales_report", Ct));
        Assert.Equal(SessionSource.Web, run.Source);
        Assert.Null(run.SessionId);
        Data.Conversations.RunStep step = await _factory.WithDatabaseAsync(user, db => db.RunSteps.SingleAsync(entry => entry.RunId == run.Id, Ct));
        Assert.Equal("charts.sales_report", step.Tool);
    }

    [Fact]
    public async Task ToolCall_ConfirmTier_DeniedWithoutPendingAction()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");
        string appId = await ShowAppAsync(browser);

        HttpResponseMessage response = await browser.PostAsJsonAsync($"/api/v1/apps/{appId}/tool-calls", new { name = "delete_report" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(AppEndpoints.ConfirmDenied, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Calls, call => call.Name == "delete_report");
        Assert.False(await _factory.WithDatabaseAsync(user, db => db.PendingActions.AnyAsync(Ct)));
        Data.Conversations.RunStep step = await _factory.WithDatabaseAsync(user, db => db.RunSteps.SingleAsync(entry => entry.Tool == "charts.delete_report", Ct));
        Assert.Equal(Abstractions.Guard.GuardDecision.Denied, step.GuardDecision);
    }

    [Theory]
    [InlineData("HassTurnOn")]
    [InlineData("ha_HassTurnOn")]
    [InlineData("")]
    public async Task ToolCall_NotOfTheAppsServer_Denied(string name)
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string appId = await ShowAppAsync(browser);

        HttpResponseMessage response = await browser.PostAsJsonAsync($"/api/v1/apps/{appId}/tool-calls", new { name }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiTurn_NoAppEvent()
    {
        using HttpClient machine = await _factory.ClientWithTokenAsync(Abstractions.Tokens.ApiScopes.ConversationWrite);
        _factory.Model.ThenToolCall("charts_sales_report", new { month = "9" }).ThenText("Here it is.");

        HttpResponseMessage response = await machine.PostAsJsonAsync("/api/v1/conversation/turns", new { conversationId = "c1", text = "sales report", inputMode = "text" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Sse.Parse(await response.Content.ReadAsStringAsync(Ct)), entry => entry.Event == "app");
    }

    private async Task<string> ShowAppAsync(HttpClient browser)
    {
        _factory.Model.ThenToolCall("charts_sales_report", new { month = "9" }, "call-1").ThenText("Here is the report.");
        HttpResponseMessage response = await browser.PostAsJsonAsync("/api/v1/conversation/turns", new { text = "sales report" }, Ct);
        List<(string Event, JsonElement Data)> events = Sse.Parse(await response.Content.ReadAsStringAsync(Ct));
        JsonElement app = Assert.Single(events, entry => entry.Event == "app").Data;
        Assert.Equal(("charts", "sales_report", "call-1"), (app.GetProperty("server").GetString(), app.GetProperty("tool").GetString(), app.GetProperty("toolCallId").GetString()));
        Assert.True(events.FindIndex(entry => entry.Event == "app") < events.FindIndex(entry => entry.Event == "final"));
        return app.GetProperty("appId").GetString()!;
    }

    private sealed class Transports(FakeMcpServer server) : IMcpTransportFactory
    {
        public IClientTransport Create(McpConnectionSettings settings) => server.CreateTransport();
    }
}
