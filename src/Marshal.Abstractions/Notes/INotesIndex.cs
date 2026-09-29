namespace Marshal.Abstractions.Notes;

/// <summary>
/// Hybrid (keyword + embedding) search over the notes, separate from memory. Derived data: never exported and
/// rebuildable from the repository at any time.
/// </summary>
public interface INotesIndex
{
    /// <summary>Re-indexes the files of <paramref name="source"/> that changed since the last update, for the current owner.</summary>
    Task<Result<NotesIndexUpdate>> UpdateAsync(INotesSource source, CancellationToken cancellationToken);

    /// <summary>The best matching sections of the current owner's notes.</summary>
    Task<IReadOnlyList<NoteHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

/// <summary>What an index update did.</summary>
/// <param name="Reindexed">Paths whose sections were rebuilt.</param>
/// <param name="Removed">Paths whose sections were dropped because the file is gone.</param>
public sealed record NotesIndexUpdate(IReadOnlyList<string> Reindexed, IReadOnlyList<string> Removed);

/// <summary>A matching note section.</summary>
/// <param name="Path">File path.</param>
/// <param name="Heading">Heading of the section.</param>
/// <param name="Text">Section text.</param>
/// <param name="Score">Relevance from 0 to 1.</param>
public sealed record NoteHit(string Path, string Heading, string Text, double Score);
