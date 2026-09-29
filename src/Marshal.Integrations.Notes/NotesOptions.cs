using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.Notes;

/// <summary>
/// The <c>notes:</c> section of <c>marshal.yaml</c>. Without <see cref="Remote"/> the notes are the Markdown files of
/// a local repository in <c>/data/notes</c> (notes are on when that directory exists); with it, Marshal clones the
/// remote there and keeps it up to date as a read-only mirror.
/// </summary>
public sealed class NotesOptions
{
    /// <summary>Git URL of the notes repository (https, ssh or file); null for a local repository.</summary>
    public string? Remote { get; set; }

    /// <summary>Branch to follow; the remote's default branch when null.</summary>
    public string? Branch { get; set; }

    /// <summary>Name of the secret holding an https access token or an SSH deploy key (private key).</summary>
    public string? CredentialSecret { get; set; }

    /// <summary>The token or key, resolved from the secret store at startup.</summary>
    [Secret(nameof(CredentialSecret))]
    public string? Credential { get; set; }

    /// <summary>How often the remote is pulled and the index updated.</summary>
    public TimeSpan PullInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Longest note text returned by <c>notes.read</c>, in characters.</summary>
    public int MaxReadCharacters { get; set; } = 50_000;
}

/// <summary>Where the notes repository lives (<c>/data/notes</c>).</summary>
public sealed record NotesDirectory(string Path);
