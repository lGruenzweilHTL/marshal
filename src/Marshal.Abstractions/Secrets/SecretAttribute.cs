namespace Marshal.Abstractions.Secrets;

/// <summary>
/// Marks an options property that holds a secret value. It is never bound from configuration; it is resolved
/// at startup from <see cref="ISecretStore"/> using the secret name in <see cref="ReferenceProperty"/>.
/// </summary>
/// <param name="referenceProperty">Name of the sibling property holding the secret name (e.g. <c>ApiKeySecret</c>).</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SecretAttribute(string referenceProperty) : Attribute
{
    /// <summary>Name of the sibling property holding the secret name.</summary>
    public string ReferenceProperty { get; } = referenceProperty;

    /// <summary>
    /// True when startup must fail if the reference does not resolve (model keys, OIDC client secret, HA token).
    /// Optional secrets only produce a health warning.
    /// </summary>
    public bool Required { get; init; } = true;
}
