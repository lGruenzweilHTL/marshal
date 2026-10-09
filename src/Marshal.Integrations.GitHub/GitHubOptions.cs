using System.Text.RegularExpressions;

namespace Marshal.Integrations.GitHub;

/// <summary>
/// The <c>github</c> section of <c>marshal.yaml</c>: a fine-grained, read-only personal access token and the allowlist
/// of repositories the <c>repos.*</c> tools may read, by id. Without repositories there are no tools.
/// </summary>
public sealed partial class GitHubOptions
{
    /// <summary>Name of the secret holding the token; read per request.</summary>
    public string? TokenSecret { get; set; }

    /// <summary>Repository ids (what the model sees) to <c>owner/name</c>.</summary>
    public Dictionary<string, string> Repos { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The configured repositories, ordered by id; entries that fail validation are left out.</summary>
    internal IReadOnlyList<GitHubRepo> Repositories() =>
        [.. Repos.Where(entry => IsRepoId(entry.Key) && IsFullName(entry.Value))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new GitHubRepo(entry.Key, entry.Value.Split('/')[0], entry.Value.Split('/')[1]))];

    internal static bool IsRepoId(string id) => RepoId().IsMatch(id);

    internal static bool IsFullName(string value) => FullName().IsMatch(value);

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex RepoId();

    [GeneratedRegex(@"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$")]
    private static partial Regex FullName();
}

/// <summary>An allowlisted repository: the id the model uses and where it lives on GitHub.</summary>
public sealed record GitHubRepo(string Id, string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";
}
