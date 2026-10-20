using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Memory.Idle;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Memory.Tests.Idle;

/// <summary>Fakes for the idle-time memory jobs: a fast model, a cost cap and a fixed cost per call.</summary>
internal static class IdleTestKit
{
    public static IdleModelCalls Calls(TestDatabase database, FakeChatClient model, InMemoryRunRecorder runs, TimeProvider time, decimal capLeft = 1m) =>
        new(new FastRouter(model), runs, new FixedCost(), new FixedCap(capLeft), database.Accessor, time);

    public sealed class FastRouter(IChatClient client) : IModelRouter
    {
        public Result<RoutedModel> Route(ModelRequest request) =>
            request.Tier == ModelTier.Fast ? new RoutedModel("fast-model", ModelTier.Fast, client) : new Error("model.not_available", "No model.", ErrorType.Unavailable);
    }

    public sealed class FixedCost : ICostCalculator
    {
        public decimal Calculate(string modelKey, UsageDetails? usage) => 0.01m;
    }

    public sealed class FixedCap(decimal left) : IDailyCostCap
    {
        public Task<decimal> LeftTodayAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(left);
    }
}
