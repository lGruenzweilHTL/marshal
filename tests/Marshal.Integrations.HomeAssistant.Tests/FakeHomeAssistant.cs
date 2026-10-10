using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Marshal.Integrations.HomeAssistant.Tests;

/// <summary>
/// In-process Home Assistant: the websocket API (auth, commands answered from the recorded session, pushed
/// events) and the REST endpoints Marshal uses. Registry and state data can be changed between commands.
/// </summary>
internal sealed class FakeHomeAssistant : IAsyncDisposable
{
    public const string Token = "<token>";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<int, Session> _sessions = new();
    private int _nextSession;

    private FakeHomeAssistant(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Command type → result, initialized from <c>websocket-session.json</c>.</summary>
    public ConcurrentDictionary<string, JsonNode?> Responses { get; } = new(StringComparer.Ordinal);

    /// <summary><c>call_service</c> results by <c>domain/service</c>; other services answer with the recorded result.</summary>
    public ConcurrentDictionary<string, JsonNode?> ServiceResponses { get; } = new(StringComparer.Ordinal);

    /// <summary>Every websocket command received, in order.</summary>
    public ConcurrentQueue<JsonObject> Commands { get; } = new();

    /// <summary>REST requests received (path and body).</summary>
    public ConcurrentQueue<(string Path, JsonNode? Body)> RestRequests { get; } = new();

    /// <summary>Services (<c>domain/service</c>) that answer 400, like HA does for a call the target cannot take.</summary>
    public ConcurrentBag<string> FailingServices { get; } = [];

    /// <summary>Response body of <c>POST /api/intent/handle</c>.</summary>
    public JsonNode IntentResponse { get; set; } = JsonNode.Parse("""{"response_type":"action_done","speech":{}}""")!;

    /// <summary>Response of <c>GET /api/calendars</c>.</summary>
    public JsonArray Calendars { get; } = JsonNode.Parse("""
        [{"entity_id":"calendar.personal","name":"Personal"},{"entity_id":"calendar.work","name":"Work"}]
        """)!.AsArray();

    /// <summary>Events returned by <c>GET /api/calendars/&lt;entity&gt;</c>, per entity (the range is not applied).</summary>
    public ConcurrentDictionary<string, JsonArray> CalendarEvents { get; } = new(StringComparer.Ordinal);

    /// <summary>Query strings of calendar event requests, in order.</summary>
    public ConcurrentQueue<string> CalendarQueries { get; } = new();

    /// <summary>Websocket command types answered with <c>success: false</c> and this error message.</summary>
    public ConcurrentDictionary<string, string> FailingCommands { get; } = new(StringComparer.Ordinal);

    /// <summary>Number of authenticated websocket connections so far.</summary>
    public int Connections => _nextSession;

    /// <summary>When true, commands are not answered (the connection stays open).</summary>
    public bool Stalled { get; set; }

    public TestServer Server => _app.GetTestServer();

    public static async Task<FakeHomeAssistant> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        WebApplication app = builder.Build();
        var fake = new FakeHomeAssistant(app);
        fake.LoadFixture();
        app.UseWebSockets();
        app.Map("/api/websocket", fake.HandleSocketAsync);
        app.MapGet("/api/config", (HttpContext context) => fake.Rest(context, null, fake.Responses["get_config"]));
        app.MapPost("/api/intent/handle", async (HttpContext context) =>
            await fake.Rest(context, await JsonNode.ParseAsync(context.Request.Body), fake.IntentResponse).ExecuteAsync(context));
        app.MapGet("/api/calendars", (HttpContext context) => fake.Rest(context, null, fake.Calendars));
        app.MapGet("/api/calendars/{entity}", (string entity, HttpContext context) =>
        {
            fake.CalendarQueries.Enqueue(context.Request.QueryString.Value ?? "");
            return fake.CalendarEvents.TryGetValue(entity, out JsonArray? events) ? fake.Rest(context, null, events) : Results.NotFound();
        });
        app.MapPost("/api/services/{**service}", async (string service, HttpContext context) =>
        {
            IResult result = fake.Rest(context, await JsonNode.ParseAsync(context.Request.Body), new JsonArray());
            await (fake.FailingServices.Contains(service) ? Results.BadRequest() : result).ExecuteAsync(context);
        });
        await app.StartAsync();
        return fake;
    }

    /// <summary>An entry of a registry list result (by its id field), for changing it.</summary>
    public JsonObject RegistryEntry(string commandType, string idField, string id) =>
        Responses[commandType]!.AsArray().OfType<JsonObject>().Single(entry => (string?)entry[idField] == id);

    /// <summary>Sets a state in the <c>get_states</c> result.</summary>
    public void SetState(string entityId, string state)
    {
        JsonObject entry = Responses["get_states"]!.AsArray().OfType<JsonObject>().Single(candidate => (string?)candidate["entity_id"] == entityId);
        entry["state"] = state;
    }

    /// <summary>Adds (or replaces) an entity state in the <c>get_states</c> result.</summary>
    public void AddState(string entityId, string state, JsonObject attributes)
    {
        JsonArray states = Responses["get_states"]!.AsArray();
        if (states.OfType<JsonObject>().FirstOrDefault(candidate => (string?)candidate["entity_id"] == entityId) is { } existing)
            states.Remove(existing);
        states.Add(new JsonObject { ["entity_id"] = entityId, ["state"] = state, ["attributes"] = attributes, ["last_updated"] = "2026-09-29T08:00:00+00:00" });
    }

    /// <summary>Sends an event to every connection subscribed to its type.</summary>
    public async Task SendEventAsync(string eventType, JsonObject data)
    {
        foreach (Session session in _sessions.Values)
        {
            if (!session.Subscriptions.TryGetValue(eventType, out int id))
                continue;

            var message = new JsonObject
            {
                ["id"] = id,
                ["type"] = "event",
                ["event"] = new JsonObject { ["event_type"] = eventType, ["data"] = data, ["origin"] = "LOCAL" },
            };
            await session.SendAsync(message);
        }
    }

    /// <summary>A <c>state_changed</c> event with a new state for <paramref name="entityId"/>.</summary>
    public Task SendStateChangedAsync(string entityId, string state, string? friendlyName = null)
    {
        var attributes = new JsonObject();
        if (friendlyName is not null)
            attributes["friendly_name"] = friendlyName;

        return SendEventAsync("state_changed", new JsonObject
        {
            ["entity_id"] = entityId,
            ["new_state"] = new JsonObject { ["entity_id"] = entityId, ["state"] = state, ["attributes"] = attributes },
        });
    }

    /// <summary>Closes every connection abruptly.</summary>
    public void Drop()
    {
        foreach (Session session in _sessions.Values)
            session.Socket.Abort();
    }

    /// <summary>Number of received commands of <paramref name="type"/>.</summary>
    public int CountCommands(string type) => Commands.Count(command => (string?)command["type"] == type);

    public async ValueTask DisposeAsync()
    {
        Drop();
        await _app.DisposeAsync();
    }

    private void LoadFixture()
    {
        JsonArray messages = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "websocket-session.json")))!.AsArray();
        var types = new Dictionary<int, string>();
        foreach (JsonNode? entry in messages)
        {
            JsonObject message = entry!["message"]!.AsObject();
            if ((string?)entry["from"] == "client" && message["id"] is { } id)
                types[(int)id] = (string)message["type"]!;
            else if ((string?)message["type"] == "result" && types.TryGetValue((int)message["id"]!, out string? type) && !Responses.ContainsKey(type))
                Responses[type] = message["result"]?.DeepClone();
        }
    }

    private IResult Rest(HttpContext context, JsonNode? body, JsonNode? response)
    {
        RestRequests.Enqueue((context.Request.Path, body));
        return context.Request.Headers.Authorization == $"Bearer {Token}"
            ? Results.Text(response?.ToJsonString() ?? "null", "application/json")
            : Results.Unauthorized();
    }

    private async Task HandleSocketAsync(HttpContext context)
    {
        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        var session = new Session(socket);
        await session.SendAsync(new JsonObject { ["type"] = "auth_required", ["ha_version"] = "2026.9.4" });
        JsonObject? auth = await session.ReceiveAsync();
        if (auth is null || (string?)auth["access_token"] != Token)
        {
            await session.SendAsync(new JsonObject { ["type"] = "auth_invalid", ["message"] = "Invalid access token or password" });
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            return;
        }

        await session.SendAsync(new JsonObject { ["type"] = "auth_ok", ["ha_version"] = "2026.9.4" });
        int number = Interlocked.Increment(ref _nextSession);
        _sessions[number] = session;
        try
        {
            while (await session.ReceiveAsync() is { } command)
            {
                Commands.Enqueue(command);
                if (Stalled)
                    continue;

                string type = (string)command["type"]!;
                if (type == "subscribe_events")
                    session.Subscriptions[(string)command["event_type"]!] = (int)command["id"]!;

                if (FailingCommands.TryGetValue(type, out string? failure))
                {
                    await session.SendAsync(new JsonObject
                    {
                        ["id"] = (int)command["id"]!,
                        ["type"] = "result",
                        ["success"] = false,
                        ["error"] = new JsonObject { ["code"] = "home_assistant_error", ["message"] = failure },
                    });
                    continue;
                }

                JsonNode? result = type switch
                {
                    "subscribe_events" => null,
                    "call_service" when ServiceResponses.TryGetValue($"{command["domain"]}/{command["service"]}", out JsonNode? response) => response?.DeepClone(),
                    _ => Responses.GetValueOrDefault(type)?.DeepClone(),
                };
                await session.SendAsync(new JsonObject { ["id"] = (int)command["id"]!, ["type"] = "result", ["success"] = true, ["result"] = result });
            }
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _sessions.TryRemove(number, out _);
        }
    }

    private sealed class Session(WebSocket socket)
    {
        private readonly SemaphoreSlim _send = new(1, 1);

        public WebSocket Socket => socket;

        public ConcurrentDictionary<string, int> Subscriptions { get; } = new(StringComparer.Ordinal);

        public async Task SendAsync(JsonObject message)
        {
            await _send.WaitAsync();
            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                _send.Release();
            }
        }

        public async Task<JsonObject?> ReceiveAsync()
        {
            var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
            using var text = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                text.Write(buffer.Array!, 0, result.Count);
                if (result.EndOfMessage)
                    return JsonNode.Parse(text.ToArray())!.AsObject();
            }
        }
    }
}
