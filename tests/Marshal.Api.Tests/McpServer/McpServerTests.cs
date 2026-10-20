using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Marshal.Api.Tests.McpServer;

public sealed class McpServerTests
{
    private static readonly string[] _readTools = ["memory_recall", "tasks_list", "tasks_status"];

    [Fact]
    public async Task Endpoint_WithoutToken_401()
    {
        using var factory = new MarshalApiFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/mcp", ListRequest(), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_RevokedToken_401()
    {
        using var factory = new MarshalApiFactory();
        (string plaintext, ApiTokenId id) = await CreateTokenAsync(factory, ApiScopes.McpRead);
        using (IServiceScope scope = factory.Services.CreateScope())
        using (Use(factory, scope.ServiceProvider))
            await scope.ServiceProvider.GetRequiredService<IApiTokenService>().RevokeAsync(id, Ct);
        using HttpClient client = Bearer(factory, plaintext);

        HttpResponseMessage response = await client.PostAsync("/mcp", ListRequest(), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_TokenWithoutMcpScope_403()
    {
        using var factory = new MarshalApiFactory();
        using HttpClient client = Bearer(factory, await factory.CreateTokenAsync(ApiScopes.ConversationWrite));

        HttpResponseMessage response = await client.PostAsync("/mcp", ListRequest(), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_CookiePrincipal_403()
    {
        await using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");

        HttpResponseMessage response = await browser.PostAsync("/mcp", ListRequest(), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("2026-07-28")]
    [InlineData("2025-06-18")]
    public async Task ReadToken_ListsReadToolsOnly_ToolsCapabilityOnly(string protocolVersion)
    {
        using var factory = new MarshalApiFactory();
        await using McpClient client = await ConnectAsync(factory, protocolVersion, ApiScopes.McpRead);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Ct);

        // Notes are not configured in the test host, so their tools are not listed.
        Assert.Equal(_readTools.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.NotNull(client.ServerCapabilities.Tools);
        Assert.Null(client.ServerCapabilities.Resources);
        Assert.Null(client.ServerCapabilities.Prompts);
    }

    [Fact]
    public async Task WriteToken_ListsCreateAndAsk_DescriptorsMatch()
    {
        using var factory = new MarshalApiFactory();
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpWrite);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal([.. _readTools, "tasks_create", "ask_marshal"], tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal));
        RegisteredTool recall = factory.Services.GetRequiredService<IToolRegistry>().Current.FindById(new ToolId("memory", "recall"))!;
        McpClientTool listed = tools.Single(tool => tool.Name == "memory_recall");
        Assert.Equal(recall.Descriptor.Description, listed.Description);
        Assert.True(JsonElement.DeepEquals(recall.Descriptor.ParametersSchema, listed.ProtocolTool.InputSchema));
    }

    [Fact]
    public async Task Call_NotAllowedByScope_FailsAsUnknown()
    {
        using var factory = new MarshalApiFactory();
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpRead);

        McpProtocolException error = await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync("tasks_create", new Dictionary<string, object?> { ["goal"] = "x" }, cancellationToken: Ct).AsTask());

        Assert.Contains("Unknown tool", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_RecordedAsApiRunWithMcpTrigger_OwnerScoped()
    {
        using var factory = new MarshalApiFactory();
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpRead);

        CallToolResult result = await client.CallToolAsync("tasks_list", cancellationToken: Ct);

        Assert.NotEqual(true, result.IsError);
        OwnerId owner = await OwnerAsync(factory);
        Marshal.Data.Conversations.Run run = await factory.WithDatabaseAsync(owner, db => db.Runs.SingleAsync(Ct));
        Assert.Equal((SessionSource.Api, "mcp:tasks_list", owner), (run.Source, run.Trigger, run.OwnerId));
        Assert.Equal(0, await factory.WithDatabaseAsync(TestExecutionContext.OwnerB, db => db.Runs.CountAsync(Ct)));
    }

    [Fact]
    public async Task Ask_RunsTurnAndContinuesConversation()
    {
        using var factory = new MarshalApiFactory();
        factory.Model.ThenText("It is 21 degrees.").ThenText("Still 21.");
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpWrite);

        CallToolResult first = await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "how warm is it?" }, cancellationToken: Ct);
        string conversation = first.StructuredContent!.Value.GetProperty("conversationId").GetString()!;
        CallToolResult second = await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "and now?", ["conversationId"] = conversation }, cancellationToken: Ct);

        Assert.Equal("It is 21 degrees.", Text(first));
        Assert.Equal("Still 21.", Text(second));
        Assert.Contains(factory.Model.Requests[1].Messages, message => message.Text == "It is 21 degrees.");
        OwnerId owner = await OwnerAsync(factory);
        List<Marshal.Data.Conversations.Run> runs = await factory.WithDatabaseAsync(owner, db => db.Runs.OrderBy(run => run.StartedAt).ToListAsync(Ct));
        Assert.All(runs, run => Assert.Equal("mcp:ask_marshal", run.Trigger));
        Assert.Single(runs.Select(run => run.SessionId).Distinct());
        Assert.All(runs, run => Assert.False(run.Tainted));
    }

    [Fact]
    public async Task Ask_WithoutConversationId_NewSessionEachCall()
    {
        using var factory = new MarshalApiFactory();
        factory.Model.ThenText("one").ThenText("two");
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpWrite);

        await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "a" }, cancellationToken: Ct);
        await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "b" }, cancellationToken: Ct);

        OwnerId owner = await OwnerAsync(factory);
        Assert.Equal(2, await factory.WithDatabaseAsync(owner, db => db.Runs.Select(run => run.SessionId).Distinct().CountAsync(Ct)));
    }

    [Fact]
    public async Task Ask_ConfirmTier_QueuedForApproval_YesDoesNotConfirm()
    {
        var start = FakeTool.Confirm("test.start_print");
        using var factory = new MarshalApiFactory(null, start);
        factory.Model.ThenToolCall("test_start_print").ThenText("The print waits for your approval.").ThenText("It still waits for approval.");
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpWrite);

        CallToolResult first = await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "start the print" }, cancellationToken: Ct);
        string conversation = first.StructuredContent!.Value.GetProperty("conversationId").GetString()!;
        await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "yes", ["conversationId"] = conversation }, cancellationToken: Ct);

        Assert.Equal("The print waits for your approval.", Text(first));
        Assert.Contains("review queue", ToolResultText(factory.Model.Requests[1]), StringComparison.Ordinal);
        Assert.Empty(start.Invocations);
        OwnerId owner = await OwnerAsync(factory);
        Marshal.Data.Conversations.PendingAction action = await factory.WithDatabaseAsync(owner, db => db.PendingActions.SingleAsync(Ct));
        Assert.Equal((PendingActionStatus.Pending, true), (action.Status, action.Queued));
    }

    [Fact]
    public async Task Ask_LatencyCapOfApiTurnsApplies()
    {
        using var factory = new MarshalApiFactory(new() { ["agent:textLatencyCap"] = "00:00:00.5" });
        factory.Model.ThenHang();
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpWrite);

        CallToolResult result = await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "think hard" }, cancellationToken: Ct);

        Assert.Equal(Marshal.Core.Agent.FixedReplies.LatencyCap("en"), Text(result));
    }

    [Fact]
    public async Task Ask_DailyCapReached_ToolErrorWithoutModelCall()
    {
        using var factory = new MarshalApiFactory(new() { ["tasks:dailyCostCap"] = "0" });
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpWrite);

        CallToolResult result = await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "how warm is it?" }, cancellationToken: Ct);

        Assert.True(result.IsError);
        Assert.Contains("daily cost cap", Text(result), StringComparison.Ordinal);
        Assert.Empty(factory.Model.Requests);
    }

    [Fact]
    public async Task McpRuns_CountAgainstTheDailyCap()
    {
        using var factory = new MarshalApiFactory(new() { ["tasks:dailyCostCap"] = "0.5" });
        OwnerId owner = await OwnerAsync(factory);
        await factory.WithDatabaseAsync(owner, async db =>
        {
            db.Runs.Add(new Marshal.Data.Conversations.Run
            {
                Id = Marshal.Abstractions.Runs.RunId.New(),
                OwnerId = owner,
                Source = SessionSource.Api,
                InputMode = InputMode.Text,
                Language = "en",
                Trigger = "mcp:ask_marshal",
                StartedAt = DateTimeOffset.UtcNow,
                Cost = 0.6m,
            });
            return await db.SaveChangesAsync(Ct);
        });
        await using McpClient client = await ConnectAsync(factory, null, ApiScopes.McpWrite);

        CallToolResult result = await client.CallToolAsync("ask_marshal", new Dictionary<string, object?> { ["text"] = "hi" }, cancellationToken: Ct);

        Assert.True(result.IsError);
        Assert.Empty(factory.Model.Requests);
    }

    private static async Task<McpClient> ConnectAsync(MarshalApiFactory factory, string? protocolVersion, params string[] scopes)
    {
        HttpClient http = Bearer(factory, await factory.CreateTokenAsync(scopes));
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: Ct);
    }

    private static HttpClient Bearer(MarshalApiFactory factory, string token)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<(string Plaintext, ApiTokenId Id)> CreateTokenAsync(MarshalApiFactory factory, params string[] scopes)
    {
        _ = factory.Server;
        using IServiceScope scope = factory.Services.CreateScope();
        using IDisposable context = Use(factory, scope.ServiceProvider, await OwnerAsync(factory));
        CreatedApiToken created = (await scope.ServiceProvider.GetRequiredService<IApiTokenService>().CreateAsync("mcp", scopes, null, Ct)).Value;
        return (created.Plaintext, created.Info.Id);
    }

    private static IDisposable Use(MarshalApiFactory factory, IServiceProvider services, OwnerId? owner = null) =>
        services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner ?? OwnerAsync(factory).GetAwaiter().GetResult(), Source = SessionSource.Api });

    private static async Task<OwnerId> OwnerAsync(MarshalApiFactory factory)
    {
        _ = factory.Server;
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
    }

    private static StringContent ListRequest() => new(
        """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28"}}}""",
        Encoding.UTF8,
        "application/json");

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string ToolResultText(FakeChatRequest request) =>
        string.Concat(request.Messages.SelectMany(message => message.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().Select(result => result.Result?.ToString()));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
