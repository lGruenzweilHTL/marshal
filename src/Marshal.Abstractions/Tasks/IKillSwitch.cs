namespace Marshal.Abstractions.Tasks;

/// <summary>
/// The current owner's kill switch for background work. Activating it cancels every unfinished agent task (running
/// runs are stopped) and pauses new agent task starts until it is released. System tasks are not affected. Every
/// use is audited.
/// </summary>
public interface IKillSwitch
{
    Task<bool> IsActiveAsync(CancellationToken cancellationToken);

    /// <summary>Activates the switch; returns how many tasks were cancelled.</summary>
    Task<int> ActivateAsync(CancellationToken cancellationToken);

    Task ReleaseAsync(CancellationToken cancellationToken);
}
