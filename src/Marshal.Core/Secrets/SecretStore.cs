using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;

namespace Marshal.Core.Secrets;

/// <summary>
/// Resolves secrets from the configured sources: environment / files first, then owner rows (only for
/// references on user-owned rows), then instance rows.
/// </summary>
public sealed class SecretStore(IEnumerable<ISecretSource> sources) : ISecretStore
{
    private readonly ISecretSource[] _operatorSources = [.. sources.Where(source => !source.IsOwnerScoped)];
    private readonly ISecretSource[] _databaseSources = [.. sources.Where(source => source.IsOwnerScoped)];

    /// <inheritdoc />
    public Task<Result<string>> GetAsync(SecretName name, CancellationToken cancellationToken) =>
        ResolveAsync(name, owner: null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<string>> GetForOwnerAsync(SecretName name, OwnerId owner, CancellationToken cancellationToken) =>
        ResolveAsync(name, owner, cancellationToken);

    private async Task<Result<string>> ResolveAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken)
    {
        foreach (ISecretSource source in _operatorSources)
        {
            if (await source.TryGetAsync(name, null, cancellationToken) is { } value)
                return value;
        }

        if (owner is not null)
        {
            foreach (ISecretSource source in _databaseSources)
            {
                if (await source.TryGetAsync(name, owner, cancellationToken) is { } value)
                    return value;
            }
        }

        foreach (ISecretSource source in _databaseSources)
        {
            if (await source.TryGetAsync(name, null, cancellationToken) is { } value)
                return value;
        }

        return new Error("secret.not_found", $"Secret '{name}' was not found.", ErrorType.NotFound);
    }
}
