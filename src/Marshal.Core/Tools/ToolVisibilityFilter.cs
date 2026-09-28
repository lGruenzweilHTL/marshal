using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Tools;

/// <summary>
/// The model can't use it → the model can't see it. Hides forbidden tools, tools denied for the satellite,
/// confirm-only tools on satellites that deny confirmation (taint escalation included), and tools whose own
/// visibility condition fails. Tools whose tier depends on arguments stay visible; the guard decides per call.
/// </summary>
public sealed class ToolVisibilityFilter(IToolRegistry registry, IRiskEngine riskEngine, ISatelliteContextProvider satellites) : IToolVisibilityFilter
{
    internal IToolRegistry Registry => registry;

    public async Task<IReadOnlyList<RegisteredTool>> GetVisibleToolsAsync(MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        var visible = new List<RegisteredTool>();
        foreach (RegisteredTool tool in (context.Tools ?? registry.Current).Tools)
        {
            if ((await CheckAsync(tool, context, cancellationToken)).IsVisible)
                visible.Add(tool);
        }

        return visible;
    }

    public async Task<VisibilityVerdict> CheckAsync(RegisteredTool tool, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        HashSet<RiskTier> tiers = [.. riskEngine.GetProfile(tool).PossibleTiers];
        if (context.Taint.IsTainted && tool.Descriptor.Persists && tiers.Remove(RiskTier.Safe))
            tiers.Add(RiskTier.Confirm);

        if (tiers.All(tier => tier == RiskTier.Forbidden))
            return VisibilityVerdict.Hidden("forbidden");

        SatelliteProfile profile = satellites.GetProfile(context.SatelliteId);
        if (profile.DenyTools.Any(pattern => ToolGlob.Matches(pattern, tool.Id)))
            return VisibilityVerdict.Hidden("satellite_denied");

        if (!tiers.Contains(RiskTier.Safe) && profile.DenyConfirm.Any(pattern => ToolGlob.Matches(pattern, tool.Id)))
            return VisibilityVerdict.Hidden("satellite_denies_confirm");

        if (!await tool.Tool.IsAvailableAsync(context, cancellationToken))
            return VisibilityVerdict.Hidden("unavailable");

        return VisibilityVerdict.Visible;
    }
}
