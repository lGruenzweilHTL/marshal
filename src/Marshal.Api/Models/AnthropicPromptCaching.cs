using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Marshal.Api.Models;

/// <summary>
/// Sets an Anthropic cache breakpoint after the stable part of the system prompt. The runner sends the system prompt
/// as two text contents (static and session sections, then turn sections); the SDK's chat client caches nothing on
/// its own, so without a breakpoint every turn pays for the whole prefix again.
/// </summary>
internal sealed class AnthropicPromptCaching(IChatClient inner) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(WithBreakpoint(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(WithBreakpoint(messages), options, cancellationToken);

    private static List<ChatMessage> WithBreakpoint(IEnumerable<ChatMessage> messages)
    {
        List<ChatMessage> list = [.. messages];
        if (list.Count == 0 || list[0].Role != ChatRole.System || list[0].Contents is not [TextContent stable, _, ..])
            return list;

        // A copy, so the caller's transcript keeps plain contents.
        TextContent marked = new TextContent(stable.Text).WithCacheControl(new CacheControlEphemeral());
        list[0] = new ChatMessage(ChatRole.System, [marked, .. list[0].Contents.Skip(1)]);
        return list;
    }
}
