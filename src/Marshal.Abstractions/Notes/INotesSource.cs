namespace Marshal.Abstractions.Notes;

/// <summary>
/// The user's Markdown notes (a git repository in v1). Read-only for Marshal; paths are relative to the repository
/// and never leave it. Note content is untrusted.
/// </summary>
public interface INotesSource
{
    /// <summary>False when no notes are configured; the notes tools are hidden then.</summary>
    bool IsConfigured { get; }

    /// <summary>Markdown files below <paramref name="folder"/> (the whole repository when null), sorted by path.</summary>
    Task<Result<IReadOnlyList<NoteFile>>> ListAsync(string? folder, CancellationToken cancellationToken);

    /// <summary>The text of a Markdown file; <c>notes.path_invalid</c> for anything outside the repository.</summary>
    Task<Result<string>> ReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Clones or pulls the remote repository; nothing for a local one.</summary>
    Task<Result> SyncAsync(CancellationToken cancellationToken);
}

/// <summary>A Markdown file of the notes.</summary>
/// <param name="Path">Path relative to the repository, with '/' separators.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="ModifiedAt">Last write time.</param>
public sealed record NoteFile(string Path, long Size, DateTimeOffset ModifiedAt);
