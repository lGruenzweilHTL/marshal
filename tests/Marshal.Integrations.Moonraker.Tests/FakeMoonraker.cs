using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Marshal.Integrations.Moonraker.Tests;

/// <summary>
/// In-process Moonraker: the JSON-RPC websocket (answers from <c>moonraker-session.json</c>, pushed notifications,
/// dropped connections) and REST endpoints answered from <see cref="Rest"/>.
/// </summary>
internal sealed class FakeMoonraker : IAsyncDisposable
{
    public const string ApiKey = "moonraker-key";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<int, WebSocket> _sockets = new();
    private readonly JsonObject _fixture;
    private int _nextSocket;

    private FakeMoonraker(WebApplication app, JsonObject fixture)
    {
        _app = app;
        _fixture = fixture;
        foreach ((string method, JsonNode? result) in fixture["results"]!.AsObject())
            Results[method] = result?.DeepClone();
    }

    /// <summary>JSON-RPC method → result.</summary>
    public ConcurrentDictionary<string, JsonNode?> Results { get; } = new(StringComparer.Ordinal);

    /// <summary>Methods answered with a JSON-RPC error.</summary>
    public ConcurrentDictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);

    /// <summary>JSON-RPC requests received, in order.</summary>
    public ConcurrentQueue<JsonObject> Requests { get; } = new();

    /// <summary><c>X-Api-Key</c> header of each websocket upgrade.</summary>
    public ConcurrentQueue<string?> UpgradeKeys { get; } = new();

    /// <summary>"METHOD /path" → REST result (wrapped in <c>{"result": ...}</c>); unknown routes answer <c>"ok"</c>.</summary>
    public ConcurrentDictionary<string, JsonNode?> Rest { get; } = new(StringComparer.Ordinal);

    /// <summary>Raw REST responses (content type and bytes) by path, e.g. webcam snapshots.</summary>
    public ConcurrentDictionary<string, (string ContentType, byte[] Body)> Raw { get; } = new(StringComparer.Ordinal);

    /// <summary>REST requests received.</summary>
    public ConcurrentQueue<RestRequest> RestRequests { get; } = new();

    public int Connections => _nextSocket;

    public TestServer Server => _app.GetTestServer();

    public static async Task<FakeMoonraker> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        WebApplication app = builder.Build();
        JsonObject fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "moonraker-session.json")))!.AsObject();
        var fake = new FakeMoonraker(app, fixture);
        app.UseWebSockets();
        ((IApplicationBuilder)app).Map("/websocket", branch => branch.Run(fake.HandleSocketAsync));
        app.Run(fake.HandleRestAsync);
        await app.StartAsync();
        return fake;
    }

    /// <summary>Sends each message of a fixture sequence to every connection.</summary>
    public async Task PlayAsync(string sequence)
    {
        foreach (JsonNode? message in _fixture["sequences"]![sequence]!.AsArray())
            await SendAsync(message!.AsObject());
    }

    public async Task SendAsync(JsonObject message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        foreach (WebSocket socket in _sockets.Values)
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    /// <summary>Drops every websocket connection.</summary>
    public void Drop()
    {
        foreach ((int id, WebSocket socket) in _sockets)
        {
            socket.Abort();
            _sockets.TryRemove(id, out _);
        }
    }

    public int Count(string method) => Requests.Count(request => (string?)request["method"] == method);

    public async ValueTask DisposeAsync()
    {
        Drop();
        await _app.DisposeAsync();
    }

    private async Task HandleSocketAsync(HttpContext context)
    {
        UpgradeKeys.Enqueue(context.Request.Headers["X-Api-Key"].FirstOrDefault());
        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        int id = Interlocked.Increment(ref _nextSocket);
        _sockets[id] = socket;
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                JsonObject request = JsonNode.Parse(message.ToArray())!.AsObject();
                Requests.Enqueue(request);
                string method = (string)request["method"]!;
                JsonObject answer = Errors.TryGetValue(method, out string? error)
                    ? new JsonObject { ["jsonrpc"] = "2.0", ["error"] = new JsonObject { ["code"] = 400, ["message"] = error }, ["id"] = request["id"]?.DeepClone() }
                    : new JsonObject { ["jsonrpc"] = "2.0", ["result"] = Results.TryGetValue(method, out JsonNode? value) ? value?.DeepClone() : "ok", ["id"] = request["id"]?.DeepClone() };
                await socket.SendAsync(Encoding.UTF8.GetBytes(answer.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
        {
        }
        finally
        {
            _sockets.TryRemove(id, out _);
        }
    }

    private async Task HandleRestAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        string body = await reader.ReadToEndAsync();
        string path = context.Request.Path.Value ?? "";
        RestRequests.Enqueue(new RestRequest(context.Request.Method, path, context.Request.QueryString.Value ?? "", body, context.Request.Headers["X-Api-Key"].FirstOrDefault()));
        if (Raw.TryGetValue(path, out (string ContentType, byte[] Body) raw))
        {
            context.Response.ContentType = raw.ContentType;
            await context.Response.Body.WriteAsync(raw.Body);
            return;
        }

        if (context.Request.Headers["X-Api-Key"] != ApiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        JsonNode? result = Rest.TryGetValue($"{context.Request.Method} {path}", out JsonNode? configured) ? configured?.DeepClone() : "ok";
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(new JsonObject { ["result"] = result }.ToJsonString());
    }
}

/// <summary>A REST request received by <see cref="FakeMoonraker"/>.</summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Path">Request path.</param>
/// <param name="Query">Query string including <c>?</c>.</param>
/// <param name="Body">Body text.</param>
/// <param name="ApiKey">The <c>X-Api-Key</c> header.</param>
internal sealed record RestRequest(string Method, string Path, string Query, string Body, string? ApiKey);
