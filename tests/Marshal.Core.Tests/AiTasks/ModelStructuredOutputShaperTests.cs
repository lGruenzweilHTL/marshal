using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Core.AiTasks;
using Marshal.Core.Models;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.AiTasks;

public sealed class ModelStructuredOutputShaperTests
{
    private static readonly JsonElement _schema = JsonDocument.Parse("""
        {"type":"object","properties":{"visitor":{"type":"string"},"parcel":{"type":"boolean"}},"required":["visitor","parcel"]}
        """).RootElement.Clone();

    private readonly FakeChatClient _model = new();
    private readonly InMemoryRunRecorder _recorder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ValidFirstTry_ReturnsObjectWithOneCall()
    {
        _model.ThenText("""{"visitor":"courier","parcel":true}""");
        MarshalExecutionContext context = await ContextAsync();

        Result<JsonElement> shaped = await Shaper().ShapeAsync(Request(), context, Ct);

        Assert.Equal("courier", shaped.Value.GetProperty("visitor").GetString());
        FakeChatRequest call = Assert.Single(_model.Requests);
        ChatResponseFormatJson format = Assert.IsType<ChatResponseFormatJson>(call.Options?.ResponseFormat);
        Assert.True(JsonElement.DeepEquals(_schema, format.Schema!.Value));
        string input = string.Join("\n", call.Messages.Select(message => message.Text));
        Assert.Contains("Who is at the door?", input, StringComparison.Ordinal);
        Assert.Contains("A courier with a parcel.", input, StringComparison.Ordinal);
        Assert.Empty(call.ToolNames);
    }

    [Fact]
    public async Task InvalidThenValid_SecondCallCarriesTheErrors()
    {
        _model.ThenText("""{"visitor":"courier"}""").ThenText("""{"visitor":"courier","parcel":true}""");
        MarshalExecutionContext context = await ContextAsync();

        Result<JsonElement> shaped = await Shaper().ShapeAsync(Request(), context, Ct);

        Assert.True(shaped.IsSuccess);
        Assert.Equal(2, _model.Requests.Count);
        string repair = string.Join("\n", _model.Requests[1].Messages.Select(message => message.Text));
        Assert.Contains("$.parcel: required property is missing", repair, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotJsonThenValid_SecondCallSaysItWasNotJson()
    {
        _model.ThenText("A courier.").ThenText("""{"visitor":"courier","parcel":false}""");
        MarshalExecutionContext context = await ContextAsync();

        Result<JsonElement> shaped = await Shaper().ShapeAsync(Request(), context, Ct);

        Assert.True(shaped.IsSuccess);
        Assert.Contains("not valid JSON", string.Join("\n", _model.Requests[1].Messages.Select(message => message.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidTwice_FailsWithInvalidOutput()
    {
        _model.ThenText("""{"visitor":1}""").ThenText("""{"visitor":2}""");
        MarshalExecutionContext context = await ContextAsync();

        Result<JsonElement> shaped = await Shaper().ShapeAsync(Request(), context, Ct);

        Assert.Equal(StructuredOutput.InvalidOutputCode, shaped.Error.Code);
        Assert.Equal(2, _model.Requests.Count);
    }

    [Fact]
    public async Task BothCalls_RecordedAsModelCallStepsOfTheRun()
    {
        _model.ThenText("{}").ThenText("""{"visitor":"courier","parcel":true}""");
        MarshalExecutionContext context = await ContextAsync();

        await Shaper().ShapeAsync(Request(), context, Ct);

        RunStepRecord[] steps = [.. _recorder.OnlyRun.Steps];
        Assert.Equal(2, steps.Length);
        Assert.All(steps, step => Assert.Equal((RunStepKind.ModelCall, "standard", "standard"), (step.Kind, step.Model, step.Tier)));
        Assert.Equal("""{"visitor":"courier","parcel":true}""", steps[1].Content);
        Assert.Equal(2, context.Usage.ModelCalls);
    }

    private async Task<MarshalExecutionContext> ContextAsync()
    {
        RunId run = await _recorder.StartRunAsync(new RunStart(TestExecutionContext.Text(), "ai_task:door"), Ct);
        return TestExecutionContext.Text() with { RunId = run };
    }

    private StructuredOutputRequest Request() =>
        new(new RoutedModel("standard", ModelTier.Standard, _model), "Who is at the door?", "A courier with a parcel.", _schema);

    private ModelStructuredOutputShaper Shaper() =>
        new(_recorder, new PriceTableCostCalculator(Options.Create(new ModelRoutingOptions())), TimeProvider.System);
}
