namespace Marshal.Core.Configuration;

/// <summary>
/// A literal secret found in a configuration file. Never carries the value.
/// </summary>
/// <param name="File">File name.</param>
/// <param name="KeyPath">Dotted key path, with <c>[i]</c> for sequence items.</param>
/// <param name="Rule">The rule that matched.</param>
public sealed record ConfigSecretFinding(string File, string KeyPath, ConfigSecretRule Rule)
{
    /// <inheritdoc />
    public override string ToString() => Rule switch
    {
        ConfigSecretRule.InvalidReference => $"{File}: '{KeyPath}' must hold a secret name (^[a-z][a-z0-9_]{{0,63}}$), not a value.",
        ConfigSecretRule.ForbiddenKey => $"{File}: '{KeyPath}' names a credential. Store the value as a secret and reference it with a '...Secret' key.",
        _ => $"{File}: the value of '{KeyPath}' looks like a credential. Store it as a secret and reference it by name.",
    };
}
