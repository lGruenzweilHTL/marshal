using System.Collections.Concurrent;
using Marshal.Abstractions.Audit;

namespace Marshal.Testing;

/// <summary>
/// Audit log that keeps the entries in memory.
/// </summary>
public sealed class FakeAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public IReadOnlyList<AuditEntry> Entries => [.. _entries];

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }
}
