using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;

namespace Marshal.Testing;

/// <summary>
/// Records secret writes and mirrors them into a <see cref="FakeSecretStore"/> so later reads see them.
/// </summary>
public sealed class FakeSecretWriter(FakeSecretStore store) : ISecretWriter
{
    /// <summary>Writes in order: name, value, owner (null for instance secrets).</summary>
    public List<(string Name, string Value, OwnerId? Owner)> Writes { get; } = [];

    public Task SetAsync(SecretName name, string value, OwnerId? owner, CancellationToken cancellationToken)
    {
        Writes.Add((name.Value, value, owner));
        store.Values[name.Value] = value;
        return Task.CompletedTask;
    }

    public Task<Result> DeleteAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken) =>
        Task.FromResult(store.Values.Remove(name.Value)
            ? Result.Success()
            : Result.Failure(new Error("secret.not_found", $"Secret '{name}' is not set.", ErrorType.NotFound)));
}
