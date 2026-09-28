using System.Collections;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;

namespace Marshal.Core.Secrets;

/// <summary>
/// Operator-managed, read-only secrets: <c>MARSHAL_SECRET_&lt;NAME&gt;</c> environment variables, then files
/// <c>/run/secrets/&lt;name&gt;</c> (Docker secrets).
/// </summary>
public sealed class EnvironmentFileSecretSource(IDictionary environment, string secretsDirectory) : ISecretSource, ISecretCatalog
{
    /// <summary>Prefix of secret environment variables (one underscore, so never part of configuration).</summary>
    public const string EnvironmentPrefix = "MARSHAL_SECRET_";

    /// <inheritdoc />
    public bool IsOwnerScoped => false;

    /// <inheritdoc />
    public async Task<string?> TryGetAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken)
    {
        if (environment[EnvironmentPrefix + name.Value.ToUpperInvariant()] is string fromEnvironment)
            return fromEnvironment;

        string path = Path.Combine(secretsDirectory, name.Value);
        if (!File.Exists(path))
            return null;

        string content = await File.ReadAllTextAsync(path, cancellationToken);
        return content.TrimEnd('\r', '\n');
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SecretListing>> ListAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        IEnumerable<string> fromEnvironment = environment.Keys.OfType<string>()
            .Where(key => key.StartsWith(EnvironmentPrefix, StringComparison.Ordinal))
            .Select(key => key[EnvironmentPrefix.Length..].ToLowerInvariant());
        IEnumerable<string> fromFiles = Directory.Exists(secretsDirectory)
            ? Directory.EnumerateFiles(secretsDirectory).Select(Path.GetFileName).OfType<string>()
            : [];
        List<SecretListing> listed = [.. fromEnvironment.Concat(fromFiles)
            .Where(SecretName.IsValid)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(name => new SecretListing(SecretName.Create(name).Value, null, SecretOrigin.Environment, null))];
        return Task.FromResult<IReadOnlyList<SecretListing>>(listed);
    }
}
