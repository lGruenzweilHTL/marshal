namespace Marshal.Abstractions.Health;

/// <summary>
/// One component's current problems. Every subsystem that can degrade registers one; <see cref="ISystemHealth"/>
/// collects them.
/// </summary>
public interface IHealthWarningProvider
{
    /// <summary>The problems that exist right now; empty when the component is fine.</summary>
    ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken);
}
