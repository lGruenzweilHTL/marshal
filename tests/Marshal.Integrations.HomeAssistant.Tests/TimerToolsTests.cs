using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class TimerToolsTests : IAsyncLifetime
{
    private FakeHomeAssistant _ha = null!;
    private IReadOnlyList<ITool> _tools = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MarshalExecutionContext Satellite => TestExecutionContext.Voice() with { DeviceId = "4f1c9e" };

    public async ValueTask InitializeAsync()
    {
        _ha = await FakeHomeAssistant.StartAsync();
        var client = new HomeAssistantClient(new TestServerHttp(_ha.Server), Options.Create(new HomeAssistantOptions { Url = new Uri("http://localhost/"), Token = FakeHomeAssistant.Token }));
        _tools = TimerTools.Create(client);
    }

    [Fact]
    public void Descriptors_SafeTimerTools()
    {
        Assert.Equal(["timers.start", "timers.cancel", "timers.status"], _tools.Select(tool => tool.Descriptor.Id.ToString()));
        Assert.All(_tools, tool => Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier));
    }

    [Fact]
    public async Task Visibility_HiddenWithoutDevice()
    {
        foreach (ITool tool in _tools)
        {
            Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct));
            Assert.True(await tool.IsAvailableAsync(Satellite, Ct));
        }
    }

    [Fact]
    public async Task Visibility_HiddenWhenHaMcpOffersTimerIntents()
    {
        var mcpTimer = new RegisteredTool(new FakeTool("ha.intent__HassStartTimer"), "ha_intent__HassStartTimer", "ha", IsBuiltIn: false);
        MarshalExecutionContext context = Satellite with { Tools = new ToolSnapshot([mcpTimer], []) };

        Assert.False(await _tools[0].IsAvailableAsync(context, Ct));
    }

    [Fact]
    public async Task Start_CallsHaIntentWithTurnDevice()
    {
        _ha.IntentResponse = JsonNode.Parse("""{"response_type":"action_done","speech":{"plain":{"speech":"Timer started for 10 minutes."}}}""")!;

        Result<ToolOutput> result = await _tools[0].InvokeAsync(Invocation("""{"duration":"10m","name":"pasta"}"""), Ct);

        Assert.Equal("Timer started for 10 minutes.", result.Value.Content);
        (string path, JsonNode? body) = Assert.Single(_ha.RestRequests);
        Assert.Equal("/api/intent/handle", path);
        Assert.Equal("HassStartTimer", (string?)body!["name"]);
        Assert.Equal("4f1c9e", (string?)body["device_id"]);
        Assert.Equal("en", (string?)body["language"]);
        Assert.Equal(10, (int)body["data"]!["minutes"]!);
        Assert.Equal("pasta", (string?)body["data"]!["name"]);
    }

    [Theory]
    [InlineData("1h30m", 1, 30, 0)]
    [InlineData("90s", 0, 0, 90)]
    [InlineData("PT2H5M", 2, 5, 0)]
    [InlineData("25 minutes", 0, 25, 0)]
    public async Task Start_DurationFormats(string duration, int hours, int minutes, int seconds)
    {
        await _tools[0].InvokeAsync(Invocation($$"""{"duration":"{{duration}}"}"""), Ct);

        JsonNode data = Assert.Single(_ha.RestRequests).Body!["data"]!;
        Assert.Equal((hours, minutes, seconds), ((int?)data["hours"] ?? 0, (int?)data["minutes"] ?? 0, (int?)data["seconds"] ?? 0));
    }

    [Theory]
    [InlineData("""{"duration":"soon"}""")]
    [InlineData("""{"duration":"0m"}""")]
    [InlineData("{}")]
    public async Task Start_InvalidDuration_ValidationFailureWithoutCall(string arguments)
    {
        Result<ToolOutput> result = await _tools[0].InvokeAsync(Invocation(arguments), Ct);

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_ha.RestRequests);
    }

    [Fact]
    public async Task Cancel_And_Status_UseTheirIntents()
    {
        await _tools[1].InvokeAsync(Invocation("""{"name":"pasta"}"""), Ct);
        await _tools[2].InvokeAsync(Invocation("{}"), Ct);

        Assert.Equal(["HassCancelTimer", "HassTimerStatus"], _ha.RestRequests.Select(request => (string?)request.Body!["name"]));
        Assert.Equal("pasta", (string?)_ha.RestRequests.First().Body!["data"]!["name"]);
    }

    [Fact]
    public async Task Status_WithoutSpeech_ReturnsTimerSlots()
    {
        _ha.IntentResponse = JsonNode.Parse("""{"response_type":"action_done","speech":{},"speech_slots":{"timers":[{"name":"pasta","seconds_left":300}]}}""")!;

        Result<ToolOutput> result = await _tools[2].InvokeAsync(Invocation("{}"), Ct);

        Assert.Contains("pasta", result.Value.Content, StringComparison.Ordinal);
        Assert.Contains("300", result.Value.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HaError_IsFailedResultWithItsSpeech()
    {
        _ha.IntentResponse = JsonNode.Parse("""{"response_type":"error","speech":{"plain":{"speech":"Device does not support timers"}},"data":{"code":"failed_to_handle"}}""")!;

        Result<ToolOutput> result = await _tools[0].InvokeAsync(Invocation("""{"duration":"10m"}"""), Ct);

        Assert.Equal("ha.intent_failed", result.Error.Code);
        Assert.Equal("Device does not support timers", result.Error.Message);
    }

    public async ValueTask DisposeAsync() => await _ha.DisposeAsync();

    private static ToolInvocation Invocation(string arguments) => new(JsonDocument.Parse(arguments).RootElement.Clone(), Satellite);
}
