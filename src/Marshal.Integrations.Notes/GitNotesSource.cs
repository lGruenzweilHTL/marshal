using Marshal.Abstractions;
using Marshal.Abstractions.Notes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Notes;

/// <summary>
/// Markdown notes in a git repository at <see cref="NotesDirectory"/>. A configured remote is cloned there on the
/// first sync and then mirrored (fetch and hard reset to the remote branch). Paths are confined to the repository:
/// no absolute paths, no <c>..</c>, no hidden entries (<c>.git</c>) and no symbolic links anywhere on the way.
/// </summary>
public sealed partial class GitNotesSource(IOptions<NotesOptions> options, NotesDirectory directory, ILogger<GitNotesSource> logger) : INotesSource
{
    private static readonly TimeSpan _gitTimeout = TimeSpan.FromMinutes(2);

    private readonly string _root = Path.GetFullPath(directory.Path);

    public bool IsConfigured => options.Value.Remote is not null || Directory.Exists(_root);

    public Task<Result<IReadOnlyList<NoteFile>>> ListAsync(string? folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
            return Task.FromResult<Result<IReadOnlyList<NoteFile>>>(options.Value.Remote is null ? NotesErrors.NotConfigured() : new List<NoteFile>());

        string start = _root;
        if (!string.IsNullOrWhiteSpace(folder) && folder.Trim('/') is { Length: > 0 } relative && relative != ".")
        {
            if (Resolve(relative) is not { } resolved)
                return Task.FromResult<Result<IReadOnlyList<NoteFile>>>(NotesErrors.PathInvalid(folder));
            if (!Directory.Exists(resolved))
                return Task.FromResult<Result<IReadOnlyList<NoteFile>>>(new List<NoteFile>());
            start = resolved;
        }

        var files = new List<NoteFile>();
        Collect(new DirectoryInfo(start), files);
        return Task.FromResult<Result<IReadOnlyList<NoteFile>>>(files.OrderBy(file => file.Path, StringComparer.Ordinal).ToList());
    }

    public async Task<Result<string>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
            return NotesErrors.NotConfigured();
        if (Resolve(path) is not { } full || !IsMarkdown(full))
            return NotesErrors.PathInvalid(path);
        if (!File.Exists(full))
            return NotesErrors.NotFound(path);

        string text = await File.ReadAllTextAsync(full, cancellationToken);
        int limit = options.Value.MaxReadCharacters;
        return text.Length > limit ? text[..limit] + "\n\n[Note cut at the length limit.]" : text;
    }

    public async Task<Result> SyncAsync(CancellationToken cancellationToken)
    {
        NotesOptions notes = options.Value;
        if (notes.Remote is not { } remote)
            return Result.Success();

        var git = new GitCli(notes.Credential, _gitTimeout);
        Result synced;
        if (!Directory.Exists(_root) || !Directory.EnumerateFileSystemEntries(_root).Any())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
            List<string> clone = ["clone", "--quiet", "--depth", "1"];
            if (notes.Branch is { } branch)
                clone.AddRange(["--branch", branch]);
            clone.AddRange(["--", remote, _root]);
            synced = Outcome(await git.RunAsync(null, clone, cancellationToken));
        }
        else if (!Directory.Exists(Path.Combine(_root, ".git")))
        {
            synced = NotesErrors.SyncFailed($"{_root} exists and is not a clone of the notes repository; move it away or empty it.");
        }
        else
        {
            (bool _, string origin, string _) = await git.RunAsync(_root, ["config", "--get", "remote.origin.url"], cancellationToken);
            synced = origin != remote
                ? NotesErrors.SyncFailed($"{_root} is a clone of another repository ({origin}).")
                : Outcome(await git.RunAsync(_root, ["fetch", "--quiet", "--depth", "1", "origin", notes.Branch ?? "HEAD"], cancellationToken)) is { IsSuccess: false } fetchFailed
                    ? fetchFailed
                    : Outcome(await git.RunAsync(_root, ["reset", "--quiet", "--hard", "FETCH_HEAD"], cancellationToken));
        }

        if (synced.IsFailure)
            LogSyncFailed(synced.Error.Message);
        return synced;
    }

    private static Result Outcome((bool Success, string Output, string Error) run) =>
        run.Success ? Result.Success() : NotesErrors.SyncFailed(run.Error);

    // The full path of a repository-relative path, or null when it could leave the repository.
    private string? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\', StringComparison.Ordinal) || path.Contains('\0', StringComparison.Ordinal))
            return null;

        string current = _root;
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.StartsWith('.'))
                return null;

            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.Exists && entry.LinkTarget is not null)
                return null;
        }

        return current.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? current : null;
    }

    private void Collect(DirectoryInfo folder, List<NoteFile> files)
    {
        foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
        {
            if (entry.LinkTarget is not null || entry.Name.StartsWith('.'))
                continue;

            if (entry is DirectoryInfo child)
                Collect(child, files);
            else if (entry is FileInfo file && IsMarkdown(file.Name))
                files.Add(new NoteFile(Path.GetRelativePath(_root, file.FullName).Replace(Path.DirectorySeparatorChar, '/'), file.Length, file.LastWriteTimeUtc));
        }
    }

    private static bool IsMarkdown(string path) => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notes sync failed: {Reason}")]
    private partial void LogSyncFailed(string reason);
}
