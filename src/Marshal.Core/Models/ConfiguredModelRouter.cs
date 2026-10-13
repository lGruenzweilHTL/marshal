using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Models;

/// <summary>
/// Routes requests to the first configured model of the tier that has all required capabilities.
/// Clients are created on first use and reused.
/// </summary>
public sealed class ConfiguredModelRouter(IOptions<ModelRoutingOptions> options, IChatClientFactory factory) : IModelRouter
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public Result<RoutedModel> Route(ModelRequest request)
    {
        foreach ((string key, ModelOptions model) in options.Value.Models)
        {
            if (model.Tier != request.Tier || (request.Vision && !model.Vision) || (request.Tools && !model.Tools) || model.ContextLength < request.MinContextLength)
                continue;

            IChatClient client = _clients.GetOrAdd(key, _ => factory.Create(key, model));
            return new RoutedModel(key, model.Tier, client, ModelReasoningSupport.Effective(model));
        }

        return new Error("model.not_available", $"No {request.Tier} model with the required capabilities is configured.", ErrorType.Unavailable);
    }
}
