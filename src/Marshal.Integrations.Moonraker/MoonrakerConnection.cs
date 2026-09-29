using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// One Moonraker websocket session speaking JSON-RPC 2.0: numbered requests with their results, and a queue of
/// notifications (method and params). When the socket closes, pending requests fail and the queue completes.
/// </summary>
internal sealed class MoonrakerConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<(string Method, JsonElement Params)> _notifications = Channel.CreateUnbounded<(string, JsonElement)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _receive;
    private int _nextId;

    private MoonrakerConnection(WebSocket socket, TimeSpan timeout)
    {
        _socket = socket;
        _timeout = timeout;
        _receive = Task.Run(ReceiveLoopAsync, CancellationToken.None);
    }

    public ChannelReader<(string Method, JsonElement Params)> Notifications => _notifications.Reader;

    public static async Task<MoonrakerConnection> ConnectAsync(IMoonrakerSocketFactory sockets, Uri baseUri, string? apiKey, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var builder = new UriBuilder(new Uri(baseUri, "websocket")) { Scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" };
        WebSocket socket = await sockets.ConnectAsync(builder.Uri, apiKey, cancellationToken);
        return new MoonrakerConnection(socket, timeout);
    }

    /// <summary>Calls a JSON-RPC method and returns its result; throws <see cref="MoonrakerException"/> on errors, timeout or disconnect.</summary>
    public async Task<JsonElement> CallAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["id"] = id };
        if (parameters is not null)
            message["params"] = parameters;

        try
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

            return await completion.Task.WaitAsync(_timeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new MoonrakerException($"Moonraker did not answer '{method}' in time.", exception);
        }
        catch (WebSocketException exception)
        {
            throw new MoonrakerException("The connection to Moonraker was closed.", exception);
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

        var closed = new MoonrakerException("The connection to Moonraker was closed.", failure ?? new IOException("Closed by Moonraker."));
        foreach (TaskCompletionSource<JsonElement> pending in _pending.Values)
            pending.TrySetException(closed);

        _notifications.Writer.TryComplete(closed);
    }

    private void Dispatch(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return;

        if (message.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number && _pending.TryGetValue(id.GetInt32(), out TaskCompletionSource<JsonElement>? pending))
        {
            if (message.TryGetProperty("error", out JsonElement error))
            {
                string text = error.TryGetProperty("message", out JsonElement details) ? details.GetString() ?? "unknown error" : "unknown error";
                pending.TrySetException(new MoonrakerException($"Moonraker rejected the request: {text}"));
            }
            else
            {
                pending.TrySetResult(message.TryGetProperty("result", out JsonElement result) ? result.Clone() : default);
            }

            return;
        }

        if (message.TryGetProperty("method", out JsonElement method) && method.GetString() is { } name)
            _notifications.Writer.TryWrite((name, message.TryGetProperty("params", out JsonElement parameters) ? parameters.Clone() : default));
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
}
