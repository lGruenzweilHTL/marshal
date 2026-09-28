using System.Security.Cryptography;
using System.Text;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;

namespace Marshal.Memory;

/// <summary>
/// Deterministic chunk ids: <c>base32(sha256(owner | scope | project | type | normalize(title)))[..26]</c>. The project
/// enters by its stable id (empty for <see cref="ChunkScope.General"/>), never by name.
/// </summary>
public static class ChunkIds
{
    /// <summary>The id of a chunk identity.</summary>
    public static ChunkId Compute(OwnerId owner, ChunkScope scope, ProjectId? project, ChunkType type, string title)
    {
        if ((scope == ChunkScope.Project) != project.HasValue)
            throw new ArgumentException("A project chunk needs a project id; a general chunk must not have one.", nameof(project));

        string key = string.Join('|',
            owner.Value.ToString("D"),
            scope.ToString().ToLowerInvariant(),
            project?.Value.ToString("D") ?? "",
            type.ToString().ToLowerInvariant(),
            Normalize(title));
        return ChunkId.FromHash(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    /// <summary>Unicode NFKC, lowercase, punctuation removed, whitespace collapsed to single spaces and trimmed.</summary>
    public static string Normalize(string title)
    {
        string compatible = title.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var text = new StringBuilder(compatible.Length);
        bool space = false;
        foreach (char character in compatible)
        {
            if (char.IsWhiteSpace(character))
            {
                space = text.Length > 0;
                continue;
            }

            if (char.IsPunctuation(character))
                continue;

            if (space)
                text.Append(' ');
            space = false;
            text.Append(character);
        }

        return text.ToString();
    }
}
