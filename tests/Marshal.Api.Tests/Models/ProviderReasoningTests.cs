using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace Marshal.Api.Tests.Models;

/// <summary>
/// Provider spike for reasoning: each provider SDK, talking to a fake HTTP server, turns a reasoning request into its
/// own request flag and the provider's reasoning stream into <see cref="TextReasoningContent"/>.
/// </summary>
public sealed class ProviderReasoningTests
{
    private static readonly ChatOptions _reasoning = new() { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium, Output = ReasoningOutput.Full } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ollama_ThinkFlagSentAndThinkingStreamedAsReasoning()
    {
        var server = new FakeHttp("application/x-ndjson", """
            {"model":"qwen3","created_at":"2026-10-12T10:00:00Z","message":{"role":"assistant","content":"","thinking":"Check the printer first."},"done":false}
            {"model":"qwen3","created_at":"2026-10-12T10:00:01Z","message":{"role":"assistant","content":"It is printing."},"done":false}
            {"model":"qwen3","created_at":"2026-10-12T10:00:02Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","prompt_eval_count":10,"eval_count":5}

            """);
        using var client = new OllamaApiClient(new HttpClient(server) { BaseAddress = new Uri("http://ollama.test") }, "qwen3");

        (string reasoning, string text) = await StreamAsync(client);

        Assert.Equal(("Check the printer first.", "It is printing."), (reasoning, text));
        Assert.True(server.Body.GetProperty("think").ValueKind is JsonValueKind.True or JsonValueKind.String, server.Body.ToString());
    }

    [Fact]
    public async Task OpenAiCompatible_ReasoningContentStreamedAsReasoning()
    {
        var server = new FakeHttp("text/event-stream", """
            data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"qwen3","choices":[{"index":0,"delta":{"role":"assistant","reasoning_content":"Check the printer first."},"finish_reason":null}]}

            data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"qwen3","choices":[{"index":0,"delta":{"content":"It is printing."},"finish_reason":null}]}

            data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"qwen3","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]


            """);
        IChatClient client = new OpenAIClient(new ApiKeyCredential("none"), new OpenAIClientOptions { Endpoint = new Uri("http://llama.test/v1"), Transport = new HttpClientPipelineTransport(new HttpClient(server)) })
            .GetChatClient("qwen3")
            .AsIChatClient();

        (string reasoning, string text) = await StreamAsync(client);

        Assert.Equal(("Check the printer first.", "It is printing."), (reasoning, text));
        Assert.Equal("medium", server.Body.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task Anthropic_ThinkingRequestedAndStreamedAsReasoningWithSignature()
    {
        var server = new FakeHttp("text/event-stream", """
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-4-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Check the printer first."}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-123"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: content_block_start
            data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"It is printing."}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":1}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":20}}

            event: message_stop
            data: {"type":"message_stop"}


            """);
        var anthropic = new AnthropicClient { ApiKey = "test", BaseUrl = "http://anthropic.test", HttpClient = new HttpClient(server), MaxRetries = 0 };
        IChatClient client = anthropic.AsIChatClient("claude-sonnet-4-5");

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "printer?")], _reasoning, Ct))
            updates.Add(update);

        string reasoning = string.Concat(updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().Select(content => content.Text));
        Assert.Equal("Check the printer first.", reasoning);
        Assert.Equal("It is printing.", string.Concat(updates.Select(update => update.Text)));
        Assert.Contains(updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>(), content => content.ProtectedData == "sig-123");
        Assert.Equal("adaptive", server.Body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("summarized", server.Body.GetProperty("thinking").GetProperty("display").GetString());
        Assert.Equal("medium", server.Body.GetProperty("output_config").GetProperty("effort").GetString());

        // The signed thinking block goes back unchanged with the next tool round.
        var response = updates.ToChatResponse();
        List<ChatMessage> transcript = [new ChatMessage(ChatRole.User, "printer?"), .. response.Messages, new ChatMessage(ChatRole.User, "and now?")];
        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync(transcript, _reasoning, Ct))
        {
        }

        JsonElement replayed = server.Body.GetProperty("messages")[1].GetProperty("content")[0];
        Assert.Equal(("thinking", "Check the printer first.", "sig-123"), (replayed.GetProperty("type").GetString(), replayed.GetProperty("thinking").GetString(), replayed.GetProperty("signature").GetString()));
    }

    private static async Task<(string Reasoning, string Text)> StreamAsync(IChatClient client)
    {
        var reasoning = new StringBuilder();
        var text = new StringBuilder();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "printer?")], _reasoning, Ct))
        {
            foreach (TextReasoningContent content in update.Contents.OfType<TextReasoningContent>())
                reasoning.Append(content.Text);
            text.Append(update.Text);
        }

        return (reasoning.ToString(), text.ToString());
    }

    /// <summary>Answers every request with one canned streaming body and keeps the last request's JSON.</summary>
    private sealed class FakeHttp(string contentType, string body) : HttpMessageHandler
    {
        public JsonElement Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.Replace("\r\n", "\n", StringComparison.Ordinal), Encoding.UTF8, contentType) };
        }
    }
}
