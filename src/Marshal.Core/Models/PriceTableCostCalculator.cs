using Marshal.Abstractions.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Models;

/// <summary>
/// Cost from the per-model price table. Input token counts include cached tokens, which are charged at the
/// cached price (or the input price when no cached price is set).
/// </summary>
public sealed class PriceTableCostCalculator(IOptions<ModelRoutingOptions> options) : ICostCalculator
{
    private const decimal PerMillion = 1_000_000m;

    public decimal Calculate(string modelKey, UsageDetails? usage)
    {
        if (usage is null || !options.Value.Models.TryGetValue(modelKey, out ModelOptions? model) || model.Price is not { } price)
            return 0m;

        long cached = usage.CachedInputTokenCount ?? 0;
        long input = Math.Max(0, (usage.InputTokenCount ?? 0) - cached);
        long output = usage.OutputTokenCount ?? 0;
        return ((input * price.InputPerMTok) + (cached * (price.CachedPerMTok ?? price.InputPerMTok)) + (output * price.OutputPerMTok)) / PerMillion;
    }
}
