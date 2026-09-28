using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;

namespace Marshal.Testing;

/// <summary>
/// Secret store over a dictionary of name → value (instance and owner lookups see the same values).
/// </summary>
public sealed class FakeSecretStore : ISecretStore
{
    /// <summary>Secret values by name; may be changed during a test.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<Result<string>> GetAsync(SecretName name, CancellationToken cancellationToken) =>
        Task.FromResult(Values.TryGetValue(name.Value, out string? value)
            ? Result.Success(value)
            : Result.Failure<string>(new Error("secret.not_found", $"Secret '{name}' is not set.", ErrorType.NotFound)));

    /// <inheritdoc />
    public Task<Result<string>> GetForOwnerAsync(SecretName name, OwnerId owner, CancellationToken cancellationToken) =>
        GetAsync(name, cancellationToken);
}
