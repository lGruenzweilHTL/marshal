using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Marshal.Api.Models;
using Microsoft.Extensions.AI;

namespace Marshal.Api.Tests.Models;

public sealed class AnthropicPromptCachingTests
{
    private const string Stream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-4-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hi."}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":2}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SystemPromptInTwoParts_BreakpointAfterTheStablePart()
    {
        var server = new FakeHttp();
        AnthropicPromptCaching client = Client(server);
        ChatMessage system = new(ChatRole.System, [new TextContent("You are Marshal.\n\n"), new TextContent("It is 10:05.")]);

        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync([system, new ChatMessage(ChatRole.User, "hi")], cancellationToken: Ct))
        {
        }

        JsonElement blocks = server.Body.GetProperty("system");
        Assert.Equal("ephemeral", blocks[0].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.False(blocks[1].TryGetProperty("cache_control", out _));
        Assert.Equal("It is 10:05.", blocks[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SystemPromptInOnePart_NoBreakpoint()
    {
        var server = new FakeHttp();

        await foreach (ChatResponseUpdate _ in Client(server).GetStreamingResponseAsync([new ChatMessage(ChatRole.System, "You are Marshal."), new ChatMessage(ChatRole.User, "hi")], cancellationToken: Ct))
        {
        }

        Assert.DoesNotContain("cache_control", server.Body.ToString(), StringComparison.Ordinal);
    }

    private static AnthropicPromptCaching Client(FakeHttp server) =>
        new(new AnthropicClient { ApiKey = "test", BaseUrl = "http://anthropic.test", HttpClient = new HttpClient(server), MaxRetries = 0 }.AsIChatClient("claude-sonnet-4-5"));

    private sealed class FakeHttp : HttpMessageHandler
    {
        public JsonElement Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Stream.Replace("\r\n", "\n", StringComparison.Ordinal), Encoding.UTF8, "text/event-stream") };
        }
    }
}
