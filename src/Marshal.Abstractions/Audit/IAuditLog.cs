namespace Marshal.Abstractions.Audit;

/// <summary>
/// Records what humans and machines did outside a run (UI actions, approvals, token and secret changes, CLI).
/// </summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}
