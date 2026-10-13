using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Marshal.Core.Models;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Core.Tests.Models;

public sealed class ReasoningRequestTests
{
    private static readonly IChatClient _client = new FakeChatClient();

    [Fact]
    public void Off_NoReasoning() =>
        Assert.Null(ReasoningRequest.For(Model(ModelReasoning.Off), Text()));

    [Fact]
    public void VoiceTurn_NoReasoningWhateverTheSetting() =>
        Assert.Null(ReasoningRequest.For(Model(ModelReasoning.High), Voice()));

    [Theory]
    [InlineData(ModelReasoning.Low, ReasoningEffort.Low)]
    [InlineData(ModelReasoning.Medium, ReasoningEffort.Medium)]
    [InlineData(ModelReasoning.High, ReasoningEffort.High)]
    public void TextTurn_ConfiguredEffortWithFullOutput(ModelReasoning setting, ReasoningEffort effort)
    {
        ReasoningOptions? reasoning = ReasoningRequest.For(Model(setting), Text());

        Assert.Equal((effort, ReasoningOutput.Full), (reasoning?.Effort, reasoning?.Output));
    }

    [Fact]
    public void BackgroundTaskRun_ConfiguredEffort()
    {
        MarshalExecutionContext task = new() { OwnerId = OwnerA, Source = SessionSource.Task, InputMode = InputMode.Text, IsInteractive = false };

        Assert.Equal(ReasoningEffort.Medium, ReasoningRequest.For(Model(ModelReasoning.Medium), task)?.Effort);
    }

    [Fact]
    public void MissingKey_MeansOff() =>
        Assert.Equal(ModelReasoning.Off, ModelReasoningSupport.Effective(new ModelOptions { Provider = "ollama", Model = "qwen3" }));

    [Theory]
    [InlineData("anthropic", "claude-opus-5-5", ModelReasoning.Medium)]
    [InlineData("anthropic", "claude-sonnet-4-6", ModelReasoning.Medium)]
    [InlineData("anthropic", "claude-haiku-4-5", ModelReasoning.Off)]
    [InlineData("anthropic", "claude-sonnet-4-20250514", ModelReasoning.Off)]
    [InlineData("anthropic", "claude-3-5-sonnet-latest", ModelReasoning.Off)]
    [InlineData("ollama", "qwen3:8b", ModelReasoning.Medium)]
    public void Effective_AnthropicBeforeAdaptiveThinking_Off(string provider, string model, ModelReasoning expected) =>
        Assert.Equal(expected, ModelReasoningSupport.Effective(new ModelOptions { Provider = provider, Model = model, Reasoning = "medium" }));

    private static RoutedModel Model(ModelReasoning reasoning) => new("m", ModelTier.Standard, _client, reasoning);
}

public sealed class ModelReasoningCheckTests
{
    [Fact]
    public async Task Start_ModelWithoutReasoningSupport_WarnsNamingTheModel()
    {
        var logger = new ListLogger<ModelReasoningCheck>();
        var options = new ModelRoutingOptions
        {
            Models =
            {
                ["haiku"] = new ModelOptions { Provider = "anthropic", Model = "claude-haiku-4-5", Reasoning = "low" },
                ["opus"] = new ModelOptions { Provider = "anthropic", Model = "claude-opus-5-5", Reasoning = "low" },
                ["sonnetOff"] = new ModelOptions { Provider = "anthropic", Model = "claude-sonnet-4-5" },
            },
        };

        await new ModelReasoningCheck(Microsoft.Extensions.Options.Options.Create(options), logger).StartAsync(TestContext.Current.CancellationToken);

        string entry = Assert.Single(logger.Entries);
        Assert.StartsWith("Warning:", entry, StringComparison.Ordinal);
        Assert.Contains("models.haiku", entry, StringComparison.Ordinal);
    }
}
