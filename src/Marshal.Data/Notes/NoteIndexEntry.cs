using Marshal.Abstractions.Identity;

namespace Marshal.Data.Notes;

/// <summary>
/// One section (heading and its text) of a note file in the notes index. Derived data: rebuilt from the notes
/// repository per file whenever the file's hash changes, never exported. Its keyword and vector entries live in
/// <c>notes_fts</c> and <c>note_vectors</c> (see <see cref="NotesSchema"/>).
/// </summary>
public sealed class NoteIndexEntry : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    /// <summary>File path relative to the notes repository.</summary>
    public required string Path { get; init; }

    /// <summary>Heading of the section (the file name for text before the first heading).</summary>
    public required string Heading { get; init; }

    /// <summary>Order of the section in the file.</summary>
    public required int Position { get; init; }

    public required string Text { get; init; }

    /// <summary>SHA-256 of the whole file when it was indexed.</summary>
    public required string FileHash { get; init; }

    public required DateTimeOffset IndexedAt { get; init; }
}
