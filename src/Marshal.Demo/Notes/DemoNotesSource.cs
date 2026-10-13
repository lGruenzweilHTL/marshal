using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Notes;

namespace Marshal.Demo.Notes;

/// <summary>
/// The sample notes from <see cref="DemoWorld.Notes"/> while no notes repository is configured; a configured one is
/// used as is. The notes index indexes them like any repository.
/// </summary>
internal sealed class DemoNotesSource(INotesSource inner, DemoWorld world) : INotesSource
{
    public bool IsReal => inner.IsConfigured;

    public bool IsConfigured => true;

    public Task<Result<IReadOnlyList<NoteFile>>> ListAsync(string? folder, CancellationToken cancellationToken)
    {
        if (IsReal)
            return inner.ListAsync(folder, cancellationToken);

        string? prefix = folder?.Trim().Trim('/');
        if (prefix is not null && prefix.Split('/').Contains(".."))
            return Task.FromResult(Result.Failure<IReadOnlyList<NoteFile>>(NotesErrors.PathInvalid(folder!)));

        return Task.FromResult(Result.Success<IReadOnlyList<NoteFile>>([.. world.Notes.Files
            .Where(file => string.IsNullOrEmpty(prefix) || file.Key.StartsWith(prefix + "/", StringComparison.Ordinal))
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => new NoteFile(file.Key, Encoding.UTF8.GetByteCount(file.Value), world.Notes.ModifiedAt[file.Key]))]));
    }

    public Task<Result<string>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (IsReal)
            return inner.ReadAsync(path, cancellationToken);

        string normalized = path.Trim().TrimStart('/');
        if (normalized.Split('/').Contains("..") || !normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Result.Failure<string>(NotesErrors.PathInvalid(path)));

        return Task.FromResult(world.Notes.Files.TryGetValue(normalized, out string? text) ? text : Result.Failure<string>(NotesErrors.NotFound(path)));
    }

    public Task<Result> SyncAsync(CancellationToken cancellationToken) =>
        IsReal ? inner.SyncAsync(cancellationToken) : Task.FromResult(Result.Success());
}
