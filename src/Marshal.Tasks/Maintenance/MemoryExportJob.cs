using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;

namespace Marshal.Tasks.Maintenance;

/// <summary>
/// The memory part of the nightly backup: the NDJSON export of every owner (<c>memory-&lt;owner&gt;.ndjson</c>),
/// each written as that owner.
/// </summary>
public sealed class MemoryExportJob(IUserDirectory users, IMemoryExporter exporter, IExecutionContextAccessor executionContext)
{
    /// <summary>Writes one export per owner into <paramref name="directory"/>; returns how many.</summary>
    public async Task<int> ExportAllAsync(string directory, CancellationToken cancellationToken)
    {
        IReadOnlyList<OwnerId> owners = await users.ListAsync(cancellationToken);
        foreach (OwnerId owner in owners)
        {
            using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false });
            await using FileStream file = File.Create(Path.Combine(directory, $"memory-{owner}.ndjson"));
            await exporter.ExportAsync(file, cancellationToken);
        }

        return owners.Count;
    }
}
