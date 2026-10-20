using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Models;
using Marshal.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Evals;

/// <summary>
/// Model access for eval replays: a tier the candidate names goes to that configured model, every other tier to
/// the current routing. Judge models are looked up the same way.
/// </summary>
public sealed class EvalModels(IOptions<ModelRoutingOptions> options, IChatClientFactory factory, IModelRouter router)
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="key"/> is a configured model.</summary>
    public bool Exists(string key) => options.Value.Models.ContainsKey(key);

    /// <summary>The configured model <paramref name="key"/>, or <c>model.not_available</c>.</summary>
    public Result<RoutedModel> Get(string key)
    {
        if (!options.Value.Models.TryGetValue(key, out ModelOptions? model))
            return new Error("model.not_available", $"There is no configured model '{key}'.", ErrorType.Validation);

        IChatClient client = _clients.GetOrAdd(key, _ => factory.Create(key, model));
        return new RoutedModel(key, model.Tier, client, ModelReasoningSupport.Effective(model));
    }

    /// <summary>A router that sends the candidate's tiers to its models.</summary>
    public IModelRouter RouterFor(IReadOnlyDictionary<ModelTier, string> models) => new CandidateRouter(this, router, models);

    /// <summary>Configured price of <paramref name="key"/>, if any.</summary>
    public ModelPrice? PriceOf(string key) => options.Value.Models.TryGetValue(key, out ModelOptions? model) ? model.Price : null;

    private sealed class CandidateRouter(EvalModels models, IModelRouter fallback, IReadOnlyDictionary<ModelTier, string> overrides) : IModelRouter
    {
        public Result<RoutedModel> Route(ModelRequest request)
        {
            if (!overrides.TryGetValue(request.Tier, out string? key))
                return fallback.Route(request);

            Result<RoutedModel> model = models.Get(key);
            return model.IsSuccess ? model.Value with { Tier = request.Tier } : model;
        }
    }
}
