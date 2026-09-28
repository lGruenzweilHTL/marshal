using System.Globalization;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;
using Microsoft.Extensions.Configuration;

namespace Marshal.Core.Secrets;

/// <summary>
/// Secret references in <c>marshal.yaml</c>: every key ending in <c>Secret</c> whose value is a secret name.
/// Reads the whole configuration tree, so keys of options that are not bound yet are included.
/// </summary>
public sealed class ConfigurationSecretReferences(IConfiguration configuration) : ISecretReferenceSource
{
    public Task<IReadOnlyList<SecretReference>> ListAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        List<SecretReference> references = [.. configuration.AsEnumerable()
            .Where(pair => pair.Key.EndsWith("Secret", StringComparison.OrdinalIgnoreCase) && SecretName.IsValid(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SecretReference(SecretName.Create(pair.Value).Value, $"{MainFile}: {YamlPath(pair.Key)}", OwnerScoped: false))];
        return Task.FromResult<IReadOnlyList<SecretReference>>(references);
    }

    private const string MainFile = "marshal.yaml";

    private static string YamlPath(string key)
    {
        string path = "";
        foreach (string segment in key.Split(':'))
        {
            path += int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                ? $"[{segment}]"
                : (path.Length == 0 ? "" : ".") + segment;
        }

        return path;
    }
}
