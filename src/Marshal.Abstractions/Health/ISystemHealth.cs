namespace Marshal.Abstractions.Health;

/// <summary>
/// Marshal's health warnings in one place, for the web UI banner, <c>system.health</c> and tasks.
/// </summary>
public interface ISystemHealth
{
    /// <summary>The warnings of the last evaluation, without evaluating again (cheap; for visibility checks).</summary>
    IReadOnlyList<HealthWarning> Last { get; }

    /// <summary>Evaluates every component now. The warnings are sorted errors first, then by component and code.</summary>
    Task<IReadOnlyList<HealthWarning>> GetWarningsAsync(CancellationToken cancellationToken);
}
