using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Guard;

/// <summary>
/// Second line behind the visibility filter. Pipeline: known and offered → still visible → valid arguments →
/// risk rules → taint escalation → subagent and satellite policy → decision. Every stage may only raise the tier.
/// </summary>
public sealed class ToolCallGuard(IToolRegistry registry, IToolVisibilityFilter visibility, IRiskEngine riskEngine, ISatelliteContextProvider satellites) : IGuard
{
    private static readonly Error _notFromDevice = new("tool.not_allowed_from_device", "This action is not allowed from this device.", ErrorType.Forbidden);

    private static readonly Error _notInSubagent = new(
        "subagent.confirm_denied",
        "Not done: this action needs the user's confirmation, which a subagent cannot ask for. Report the needed action to your caller in your answer.",
        ErrorType.Forbidden);

    public async Task<GuardVerdict> EvaluateAsync(GuardRequest request, CancellationToken cancellationToken)
    {
        RegisteredTool? tool = (request.Context.Tools ?? registry.Current).FindByModelName(request.ModelName);
        if (tool is null)
            return Deny(null, null, new Error("tool.unknown", $"There is no tool named '{request.ModelName}'.", ErrorType.NotFound));

        if (!request.OfferedModelNames.Contains(request.ModelName))
            return Deny(tool, null, new Error("tool.not_offered", $"The tool '{request.ModelName}' is not available right now.", ErrorType.Forbidden));

        VisibilityVerdict visible = await visibility.CheckAsync(tool, request.Context, cancellationToken);
        if (!visible.IsVisible)
            return Deny(tool, null, HiddenError(visible.Reason));

        Result valid = tool.Tool.Validate(request.Arguments);
        if (valid.IsFailure)
            return Deny(tool, null, valid.Error);

        RiskAssessment assessment = await riskEngine.AssessAsync(tool, request.Arguments, cancellationToken);
        RiskTier tier = assessment.Tier;
        if (tier == RiskTier.Forbidden)
            return Deny(tool, tier, new Error("tool.forbidden", "This action is not allowed.", ErrorType.Forbidden));

        string language = request.Context.Language;
        var reasons = new List<ConfirmationReason>();
        bool escalated = false;
        if (request.Context.Taint.IsTainted && tool.Descriptor.Persists)
        {
            escalated = tier == RiskTier.Safe;
            tier = RiskTier.Confirm;
            reasons.Add(ConfirmationReasonText.Taint(request.Context.Taint.Sources, language));
        }

        foreach (ConfirmationReasonCode code in assessment.Reasons)
        {
            reasons.Add(code == ConfirmationReasonCode.UnresolvedTarget
                ? ConfirmationReasonText.UnresolvedTarget(language)
                : ConfirmationReasonText.Rule(tool.Tool.Summarize(request.Arguments, language), language));
        }

        if (tier == RiskTier.Safe)
            return new GuardVerdict { Outcome = GuardOutcome.Allow, Decision = GuardDecision.Safe, Tool = tool, Tier = tier };

        if (request.Context.IsSubagent)
            return Deny(tool, tier, _notInSubagent);

        SatelliteProfile profile = satellites.GetProfile(request.Context.SatelliteId);
        if (profile.DenyConfirm.Any(pattern => ToolGlob.Matches(pattern, tool.Id)))
            return Deny(tool, tier, _notFromDevice);

        return new GuardVerdict
        {
            Outcome = GuardOutcome.RequireConfirmation,
            Decision = escalated ? GuardDecision.EscalatedByTaint : GuardDecision.Pending,
            Tool = tool,
            Tier = tier,
            Reasons = reasons,
        };
    }

    private static Error HiddenError(string? reason) => reason switch
    {
        "forbidden" => new Error("tool.forbidden", "This action is not allowed.", ErrorType.Forbidden),
        "satellite_denied" or "satellite_denies_confirm" => _notFromDevice,
        "subagent_confirm" => _notInSubagent,
        _ => new Error("tool.unavailable", "This tool is not available right now.", ErrorType.Forbidden),
    };

    private static GuardVerdict Deny(RegisteredTool? tool, RiskTier? tier, Error error) => new()
    {
        Outcome = GuardOutcome.Deny,
        Decision = GuardDecision.Denied,
        Tool = tool,
        Tier = tier,
        Denial = error,
    };
}
