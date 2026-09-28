using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Marshal.Testing;

/// <summary>
/// Scripted <see cref="IChatClient"/>: returns queued replies in order and records every request.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>> _script = new();
    private readonly List<FakeChatRequest> _requests = [];
    private readonly Lock _lock = new();

    /// <summary>Every request received, in order.</summary>
    public IReadOnlyList<FakeChatRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Queues a plain text reply.</summary>
    public FakeChatClient ThenText(string text, UsageDetails? usage = null) =>
        Then(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { Usage = usage });

    /// <summary>Queues a reply that requests one tool call by model-facing name.</summary>
    public FakeChatClient ThenToolCall(string toolName, object? arguments = null, string? callId = null) =>
        ThenToolCalls((toolName, arguments, callId));

    /// <summary>Queues a reply that requests several tool calls.</summary>
    public FakeChatClient ThenToolCalls(params (string ToolName, object? Arguments, string? CallId)[] calls)
    {
        var contents = calls
            .Select((call, index) => (AIContent)new FunctionCallContent(
                call.CallId ?? $"call_{_script.Count}_{index}",
                call.ToolName,
                ToDictionary(call.Arguments)))
            .ToList();
        return Then(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)));
    }

    /// <summary>Queues a fixed response.</summary>
    public FakeChatClient Then(ChatResponse response) => Then((_, _, _) => Task.FromResult(response));

    /// <summary>Queues a custom reply function (e.g. one that waits for cancellation).</summary>
    public FakeChatClient Then(Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> reply)
    {
        lock (_lock)
        {
            _script.Enqueue(reply);
        }

        return this;
    }

    /// <summary>Queues a reply that never completes until the call is cancelled.</summary>
    public FakeChatClient ThenHang() => Then(async (_, _, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    });

    /// <summary>Queues a reply that throws.</summary>
    public FakeChatClient ThenThrow(Exception exception) => Then((_, _, _) => Task.FromException<ChatResponse>(exception));

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<ChatMessage>? list, Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? reply) = Dequeue(messages, options);
        return await reply(list, options, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<ChatMessage>? list, Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? reply) = Dequeue(messages, options);
        ChatResponse response = await reply(list, options, cancellationToken);
        foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private (IReadOnlyList<ChatMessage> Messages, Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> Reply) Dequeue(
        IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var list = messages.Select(message => message.Clone()).ToList();
        lock (_lock)
        {
            _requests.Add(new FakeChatRequest(list, options?.Clone()));
            if (_script.Count == 0)
                throw new InvalidOperationException($"FakeChatClient has no scripted reply for request #{_requests.Count}.");

            return (list, _script.Dequeue());
        }
    }

    private static Dictionary<string, object?> ToDictionary(object? arguments)
    {
        if (arguments is null)
            return [];

        JsonElement json = JsonSerializer.SerializeToElement(arguments);
        return json.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
    }
}

/// <summary>
/// One request received by <see cref="FakeChatClient"/>.
/// </summary>
/// <param name="Messages">The messages sent.</param>
/// <param name="Options">The options sent (tools, temperature, response format).</param>
public sealed record FakeChatRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)
{
    /// <summary>Model-facing names of the tools offered in this request.</summary>
    public IReadOnlyList<string> ToolNames => Options?.Tools?.Select(tool => tool.Name).ToList() ?? [];

    /// <summary>Text of the system message(s).</summary>
    public string SystemPrompt => string.Join("\n", Messages.Where(message => message.Role == ChatRole.System).Select(message => message.Text));
}
