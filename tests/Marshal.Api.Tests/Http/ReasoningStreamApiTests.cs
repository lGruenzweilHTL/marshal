using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Tokens;
using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Auth;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class ReasoningStreamApiTests
{
    private const string Turns = "/api/v1/conversation/turns";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CookieTurn_StreamsReasoningAndToolEvents()
    {
        await using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());
        Script(factory);
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");

        List<(string Event, JsonElement Data)> events = await StreamAsync(await browser.PostAsJsonAsync(Turns, new { text = "how is the printer?" }, Ct));

        Assert.Equal(["reasoning", "tool", "tool", "reasoning", "delta", "final"], events.Select(e => e.Event));
        Assert.Equal("Check the printer first.", events[0].Data.GetProperty("text").GetString());
        JsonElement running = events[1].Data;
        Assert.Equal(("call-1", "printer.status", "printer=voron", "running"), (running.GetProperty("id").GetString(), running.GetProperty("tool").GetString(), running.GetProperty("arguments").GetString(), running.GetProperty("state").GetString()));
        JsonElement done = events[2].Data;
        Assert.Equal(("call-1", "ok", "Printing, layer 112 of 268."), (done.GetProperty("id").GetString(), done.GetProperty("state").GetString(), done.GetProperty("result").GetString()));
        Assert.True(done.TryGetProperty("durationMs", out _));
    }

    [Fact]
    public async Task TokenTurn_SameModel_NoReasoningOrToolEvents()
    {
        using var factory = new MarshalApiFactory();
        Script(factory);
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        List<(string Event, JsonElement Data)> events = await StreamAsync(await client.PostAsJsonAsync(
            Turns, new { conversationId = "01JABC", text = "how is the printer?", language = "en", deviceId = "device-1", satelliteId = "assist_satellite.kitchen", inputMode = "text" }, Ct));

        Assert.Equal(["delta", "final"], events.Select(e => e.Event));
    }

    private static void Script(MarshalApiFactory factory)
    {
        FakeTool status = FakeTool.Safe("printer.status").Returns("Printing, layer 112 of 268.\nNozzle 240 °C.");
        factory.TestServices = services => services.AddSingleton<ITool>(status);
        factory.Model
            .Then(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("Check the printer first."), new FunctionCallContent("call-1", "printer_status", new Dictionary<string, object?> { ["printer"] = "voron" })])))
            .Then(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("It is printing."), new TextContent("Layer 112 of 268.")])));
    }

    private static async Task<List<(string Event, JsonElement Data)>> StreamAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Sse.Parse(await response.Content.ReadAsStringAsync(Ct));
    }
}
