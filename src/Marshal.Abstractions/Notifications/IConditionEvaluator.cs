using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Notifications;

/// <summary>
/// Evaluates delivery conditions against current entity states.
/// </summary>
public interface IConditionEvaluator
{
    /// <summary>
    /// Whether every condition holds now for <paramref name="owner"/> (<c>$person</c> is the owner's person entity).
    /// Unknown entities, unknown states and an unavailable state source count as not holding.
    /// </summary>
    Task<bool> AllHoldAsync(IReadOnlyList<StateCondition> conditions, OwnerId owner, CancellationToken cancellationToken);
}
