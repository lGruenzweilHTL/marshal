using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Secrets;

namespace Marshal.Core.Mcp;

/// <summary>
/// Secret references in <c>/config/mcp/*.yaml</c>, so the secrets view lists them with their resolution state.
/// </summary>
public sealed class McpSecretReferences(IMcpServerCatalog catalog) : ISecretReferenceSource
{
    public Task<IReadOnlyList<SecretReference>> ListAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        var references = new List<SecretReference>();
        foreach (McpServerDefinition server in catalog.Servers)
        {
            if (server.AuthTokenSecret is { } token)
                references.Add(new SecretReference(token, $"mcp/{server.File}: authTokenSecret", OwnerScoped: false));

            foreach ((string variable, SecretName secret) in server.EnvironmentSecrets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                references.Add(new SecretReference(secret, $"mcp/{server.File}: envSecrets ({variable})", OwnerScoped: false));
        }

        return Task.FromResult<IReadOnlyList<SecretReference>>(references);
    }
}
