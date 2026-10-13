using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Satellites;
using Marshal.Core.Guard;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Marshal.Core.Tests;

/// <summary>
/// Shared helpers for guard, visibility and loop tests.
/// </summary>
internal static class GuardTestKit
{
    public static RiskRulesFile ParseRules(string yaml) =>
        new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build().Deserialize<RiskRulesFile>(yaml);

    public static RuleBasedRiskEngine Engine(string yaml, params IRiskTargetResolver[] resolvers) =>
        new(new StaticMonitor<RiskRulesFile>(ParseRules(yaml)), resolvers);
}

/// <summary>
/// Options monitor with a settable value.
/// </summary>
internal sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T Value { get; set; } = value;

    public T CurrentValue => Value;

    public T Get(string? name) => Value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// One configured satellite; everything else gets defaults.
/// </summary>
internal sealed class FixedSatellites(string satelliteId, SatelliteProfile profile) : ISatelliteContextProvider
{
    public SatelliteProfile GetProfile(string? id) => id == satelliteId ? profile : SatelliteProfile.Default;

    public Task<SatelliteContext> GetContextAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new SatelliteContext(context.SatelliteId, GetProfile(context.SatelliteId), context.AreaId, null));
}

/// <summary>
/// Resolves every call of a namespace to fixed targets.
/// </summary>
internal sealed class FakeTargetResolver(string ns, params Marshal.Abstractions.Guard.RiskTarget[] targets) : IRiskTargetResolver
{
    public string Namespace => ns;

    public Task<Marshal.Abstractions.Result<IReadOnlyList<Marshal.Abstractions.Guard.RiskTarget>>> ResolveAsync(
        Marshal.Abstractions.Tools.ToolId tool, System.Text.Json.JsonElement arguments, CancellationToken cancellationToken) =>
        Task.FromResult(Marshal.Abstractions.Result.Success<IReadOnlyList<Marshal.Abstractions.Guard.RiskTarget>>(targets));
}

/// <summary>
/// Router that returns the same fake client for every tier (optionally a separate advisor).
/// </summary>
internal sealed class SingleModelRouter(IChatClient client, IChatClient? advisor = null) : Marshal.Abstractions.Models.IModelRouter
{
    public List<Marshal.Abstractions.Models.ModelRequest> Requests { get; } = [];

    public bool Unavailable { get; set; }

    /// <summary>Reasoning setting of the routed (non-advisor) model.</summary>
    public Marshal.Abstractions.Models.ModelReasoning Reasoning { get; set; }

    public Marshal.Abstractions.Result<Marshal.Abstractions.Models.RoutedModel> Route(Marshal.Abstractions.Models.ModelRequest request)
    {
        Requests.Add(request);
        if (Unavailable)
            return new Marshal.Abstractions.Error("model.not_available", "none");

        if (request.Tier == Marshal.Abstractions.Models.ModelTier.Advisor)
        {
            return advisor is null
                ? new Marshal.Abstractions.Error("model.not_available", "no advisor")
                : new Marshal.Abstractions.Models.RoutedModel("advisor", request.Tier, advisor);
        }

        return new Marshal.Abstractions.Models.RoutedModel(request.Tier.ToString().ToLowerInvariant(), request.Tier, client, Reasoning);
    }
}
