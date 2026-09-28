namespace Marshal.Abstractions.Guard;

/// <summary>
/// Decides every tool call server-side: known and offered, still visible, risk tier, satellite policy and taint
/// escalation. Tiers are only ever raised.
/// </summary>
public interface IGuard
{
    Task<GuardVerdict> EvaluateAsync(GuardRequest request, CancellationToken cancellationToken);
}
