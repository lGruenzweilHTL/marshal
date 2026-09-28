using Marshal.Abstractions.Models;
using Marshal.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Models;

public sealed class PriceTableCostCalculatorTests
{
    private readonly PriceTableCostCalculator _calculator = new(Options.Create(new ModelRoutingOptions
    {
        Models =
        {
            ["claude"] = new ModelOptions
            {
                Tier = ModelTier.Standard,
                Provider = "anthropic",
                Model = "claude-sonnet",
                Price = new ModelPrice { InputPerMTok = 3m, OutputPerMTok = 15m, CachedPerMTok = 0.3m },
            },
            ["noCachePrice"] = new ModelOptions
            {
                Tier = ModelTier.Standard,
                Provider = "anthropic",
                Model = "x",
                Price = new ModelPrice { InputPerMTok = 2m, OutputPerMTok = 4m },
            },
            ["local"] = new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen3" },
        },
    }));

    [Fact]
    public void Calculate_PricedModel_UsesPricePerMillion()
    {
        decimal cost = _calculator.Calculate("claude", new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 100_000 });

        Assert.Equal(4.5m, cost);
    }

    [Fact]
    public void Calculate_CachedTokens_UseCachedPriceAndAreNotChargedAsInput()
    {
        decimal cost = _calculator.Calculate("claude", new UsageDetails { InputTokenCount = 1_000_000, CachedInputTokenCount = 500_000, OutputTokenCount = 0 });

        Assert.Equal(1.65m, cost);
    }

    [Fact]
    public void Calculate_NoCachedPrice_ChargesCachedAsInput()
    {
        decimal cost = _calculator.Calculate("noCachePrice", new UsageDetails { InputTokenCount = 1_000_000, CachedInputTokenCount = 500_000 });

        Assert.Equal(2m, cost);
    }

    [Fact]
    public void Calculate_UnpricedModel_IsFree()
    {
        Assert.Equal(0m, _calculator.Calculate("local", new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 1_000_000 }));
    }

    [Fact]
    public void Calculate_UnknownModelOrNoUsage_IsFree()
    {
        Assert.Equal(0m, _calculator.Calculate("unknown", new UsageDetails { InputTokenCount = 10 }));
        Assert.Equal(0m, _calculator.Calculate("claude", null));
    }
}
