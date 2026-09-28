using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// One authenticated websocket session: numbered commands with their results, and a queue of events. When the
/// socket closes, pending commands fail and the event queue completes.
/// </summary>
internal sealed class HaConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly TimeSpan _commandTimeout;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task _receive = Task.CompletedTask;
    private int _nextId;

    private HaConnection(WebSocket socket, TimeSpan commandTimeout)
    {
        _socket = socket;
        _commandTimeout = commandTimeout;
    }

    /// <summary>Events (the <c>event</c> object of each event message) in arrival order.</summary>
    public ChannelReader<JsonElement> Events => _events.Reader;

    public static async Task<HaConnection> ConnectAsync(IHomeAssistantSocketFactory sockets, Uri uri, string token, TimeSpan commandTimeout, CancellationToken cancellationToken)
    {
        WebSocket socket = await sockets.ConnectAsync(uri, cancellationToken);
        var connection = new HaConnection(socket, commandTimeout);
        try
        {
            await connection.AuthenticateAsync(token, cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        connection._receive = Task.Run(connection.ReceiveLoopAsync, CancellationToken.None);
        return connection;
    }

    /// <summary>Sends a command and returns its <c>result</c>; throws when HA reports an error, on timeout or disconnect.</summary>
    public async Task<JsonElement> CommandAsync(string type, JsonObject? fields, CancellationToken cancellationToken)
    {
        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        JsonObject message = fields?.DeepClone().AsObject() ?? [];
        message["id"] = id;
        message["type"] = type;
        try
        {
            await SendAsync(message, cancellationToken);
            return await completion.Task.WaitAsync(_commandTimeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new HomeAssistantException($"Home Assistant did not answer '{type}' in time.", exception);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _socket.Abort();
        await _receive;
        _socket.Dispose();
        _stop.Dispose();
        _send.Dispose();
    }

    private async Task AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        using JsonDocument required = await ReceiveAsync(cancellationToken) ?? throw new HomeAssistantException("Home Assistant closed the connection before authentication.");
        if (Type(required.RootElement) != "auth_required")
            throw new HomeAssistantException($"Unexpected first message '{Type(required.RootElement)}' from Home Assistant.");

        await SendAsync(new JsonObject { ["type"] = "auth", ["access_token"] = token }, cancellationToken);
        using JsonDocument answer = await ReceiveAsync(cancellationToken) ?? throw new HomeAssistantException("Home Assistant closed the connection during authentication.");
        string? type = Type(answer.RootElement);
        if (type == "auth_invalid")
            throw new HomeAssistantException("Home Assistant rejected the access token (auth_invalid).");
        if (type != "auth_ok")
            throw new HomeAssistantException($"Unexpected authentication answer '{type}' from Home Assistant.");
    }

    private async Task ReceiveLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (await ReceiveAsync(_stop.Token) is { } document)
            {
                using (document)
                    Dispatch(document.RootElement);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or IOException)
        {
            failure = exception;
        }

        var closed = new HomeAssistantException("The connection to Home Assistant was closed.", failure ?? new IOException("Closed by Home Assistant."));
        foreach (TaskCompletionSource<JsonElement> pending in _pending.Values)
            pending.TrySetException(closed);

        _events.Writer.TryComplete(closed);
    }

    private void Dispatch(JsonElement message)
    {
        switch (Type(message))
        {
            case "result" when message.TryGetProperty("id", out JsonElement id) && _pending.TryGetValue(id.GetInt32(), out TaskCompletionSource<JsonElement>? pending):
                if (message.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.True)
                {
                    pending.TrySetResult(message.TryGetProperty("result", out JsonElement result) ? result.Clone() : default);
                }
                else
                {
                    string error = message.TryGetProperty("error", out JsonElement details) && details.TryGetProperty("message", out JsonElement text) ? text.GetString() ?? "unknown error" : "unknown error";
                    pending.TrySetException(new HomeAssistantException($"Home Assistant rejected the command: {error}"));
                }

                break;
            case "event" when message.TryGetProperty("event", out JsonElement @event):
                _events.Writer.TryWrite(@event.Clone());
                break;
        }
    }

    private async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await _send.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _send.Release();
        }
    }

    private async Task<JsonDocument?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(16 * 1024);
        while (true)
        {
            Memory<byte> memory = buffer.GetMemory(16 * 1024);
            ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(memory, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            buffer.Advance(result.Count);
            if (result.EndOfMessage)
                return JsonDocument.Parse(buffer.WrittenMemory);
        }
    }

    private static string? Type(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty("type", out JsonElement type) ? type.GetString() : null;
}
