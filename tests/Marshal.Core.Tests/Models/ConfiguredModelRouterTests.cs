using Marshal.Abstractions;
using Marshal.Abstractions.Models;
using Marshal.Core.Models;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Models;

public sealed class ConfiguredModelRouterTests
{
    [Fact]
    public void Route_TierPlusVision_SelectsModelWithVision()
    {
        ConfiguredModelRouter router = Router(
            ("fast", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen3" }),
            ("fastVision", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen2.5-vl", Vision = true }));

        Result<RoutedModel> result = router.Route(new ModelRequest(ModelTier.Fast, Vision: true));

        Assert.Equal("fastVision", result.Value.Key);
    }

    [Fact]
    public void Route_TierOnly_SelectsFirstConfiguredModelOfTier()
    {
        ConfiguredModelRouter router = Router(
            ("claude", new ModelOptions { Tier = ModelTier.Standard, Provider = "anthropic", Model = "claude-sonnet" }),
            ("fast", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen3" }),
            ("fastVision", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen2.5-vl", Vision = true }));

        Result<RoutedModel> result = router.Route(new ModelRequest(ModelTier.Fast));

        Assert.Equal("fast", result.Value.Key);
        Assert.Equal(ModelTier.Fast, result.Value.Tier);
    }

    [Fact]
    public void Route_RequiresToolsAndContext_FiltersModels()
    {
        ConfiguredModelRouter router = Router(
            ("small", new ModelOptions { Tier = ModelTier.Standard, Provider = "ollama", Model = "a", Tools = false, ContextLength = 128000 }),
            ("short", new ModelOptions { Tier = ModelTier.Standard, Provider = "ollama", Model = "b", Tools = true, ContextLength = 8000 }),
            ("good", new ModelOptions { Tier = ModelTier.Standard, Provider = "ollama", Model = "c", Tools = true, ContextLength = 32000 }));

        Result<RoutedModel> result = router.Route(new ModelRequest(ModelTier.Standard, Tools: true, MinContextLength: 16000));

        Assert.Equal("good", result.Value.Key);
    }

    [Fact]
    public void Route_NoMatchingModel_FailsWithoutCreatingClient()
    {
        var factory = new CountingFactory();
        ConfiguredModelRouter router = Router(factory, ("fast", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen3" }));

        Result<RoutedModel> result = router.Route(new ModelRequest(ModelTier.Fast, Vision: true));

        Assert.Equal("model.not_available", result.Error.Code);
        Assert.Equal(0, factory.Created);
    }

    [Fact]
    public void Route_NoModelOfTier_Fails()
    {
        ConfiguredModelRouter router = Router(("fast", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen3" }));

        Result<RoutedModel> result = router.Route(new ModelRequest(ModelTier.Advisor));

        Assert.Equal("model.not_available", result.Error.Code);
    }

    [Fact]
    public void Route_SameModelTwice_ReusesClient()
    {
        var factory = new CountingFactory();
        ConfiguredModelRouter router = Router(factory, ("fast", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen3" }));

        IChatClient first = router.Route(new ModelRequest(ModelTier.Fast)).Value.Client;
        IChatClient second = router.Route(new ModelRequest(ModelTier.Fast)).Value.Client;

        Assert.Same(first, second);
        Assert.Equal(1, factory.Created);
    }

    private static ConfiguredModelRouter Router(params (string Key, ModelOptions Options)[] models) => Router(new CountingFactory(), models);

    private static ConfiguredModelRouter Router(CountingFactory factory, params (string Key, ModelOptions Options)[] models)
    {
        var options = new ModelRoutingOptions();
        foreach ((string key, ModelOptions model) in models)
            options.Models[key] = model;

        return new ConfiguredModelRouter(Options.Create(options), factory);
    }

    private sealed class CountingFactory : IChatClientFactory
    {
        public int Created { get; private set; }

        public IChatClient Create(string key, ModelOptions options)
        {
            Created++;
            return new FakeChatClient();
        }
    }
}
