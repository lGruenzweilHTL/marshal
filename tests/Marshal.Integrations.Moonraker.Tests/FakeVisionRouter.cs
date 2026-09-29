using Marshal.Abstractions;
using Marshal.Abstractions.Models;
using Marshal.Testing;

namespace Marshal.Integrations.Moonraker.Tests;

/// <summary>Routes to fake clients per tier; only tiers listed in <see cref="Vision"/> have a vision model.</summary>
internal sealed class FakeVisionRouter
    : IModelRouter
{
    public Dictionary<ModelTier, FakeChatClient> Vision { get; } = [];

    public List<ModelRequest> Requests { get; } = [];

    public Result<RoutedModel> Route(ModelRequest request)
    {
        Requests.Add(request);
        return request.Vision && Vision.TryGetValue(request.Tier, out FakeChatClient? client)
            ? new RoutedModel($"{request.Tier.ToString().ToLowerInvariant()}-vision", request.Tier, client)
            : new Error("model.not_available", "No model.", ErrorType.Unavailable);
    }
}
