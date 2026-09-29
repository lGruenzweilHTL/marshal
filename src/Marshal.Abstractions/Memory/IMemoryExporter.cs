namespace Marshal.Abstractions.Memory;

/// <summary>
/// NDJSON export and import of the current owner's memory: one object per line, stable field order, no embeddings.
/// </summary>
public interface IMemoryExporter
{
    /// <summary>Writes projects, chunks, versions and edges.</summary>
    Task ExportAsync(Stream output, CancellationToken cancellationToken);

    /// <summary>Adds the exported rows that do not exist yet and recomputes their embeddings.</summary>
    Task<Result<MemoryImportReport>> ImportAsync(Stream input, CancellationToken cancellationToken);
}

/// <summary>What an import added.</summary>
/// <param name="Projects">Projects added.</param>
/// <param name="Chunks">Chunks added.</param>
/// <param name="Versions">Versions added.</param>
/// <param name="Edges">Edges added.</param>
/// <param name="Skipped">Rows that already existed.</param>
public sealed record MemoryImportReport(int Projects, int Chunks, int Versions, int Edges, int Skipped);
