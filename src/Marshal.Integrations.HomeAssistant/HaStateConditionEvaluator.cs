using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// <see cref="IConditionEvaluator"/> on the Home Assistant state cache. <c>$person</c> is the owner's configured
/// person entity; without one, or while HA is disconnected, conditions do not hold.
/// </summary>
public sealed class HaStateConditionEvaluator(IHomeAssistantCache cache, IUserPreferencesStore preferences) : IConditionEvaluator
{
    public async Task<bool> AllHoldAsync(IReadOnlyList<StateCondition> conditions, OwnerId owner, CancellationToken cancellationToken)
    {
        string? person = conditions.Any(condition => condition.Entity == StateCondition.Person)
            ? (await preferences.GetAsync(owner, cancellationToken)).NotificationTargets.Person
            : null;
        if (cache.Current is not { } snapshot)
            return false;

        foreach (StateCondition condition in conditions)
        {
            string? entity = condition.Entity == StateCondition.Person ? person : condition.Entity;
            if (entity is null || !condition.HoldsFor(snapshot.States.GetValueOrDefault(entity)?.State))
                return false;
        }

        return true;
    }
}
