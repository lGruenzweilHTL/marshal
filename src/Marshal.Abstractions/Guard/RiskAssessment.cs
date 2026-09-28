using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// Tier of one concrete call according to the risk rules, with the reasons for a confirm tier.
/// </summary>
/// <param name="Tier">The tier.</param>
/// <param name="Reasons">Why confirmation is needed (empty unless the tier is confirm).</param>
public sealed record RiskAssessment(RiskTier Tier, IReadOnlyList<ConfirmationReasonCode> Reasons);
