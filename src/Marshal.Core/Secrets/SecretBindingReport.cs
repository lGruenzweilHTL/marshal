namespace Marshal.Core.Secrets;

/// <summary>
/// Result of resolving the <c>[Secret]</c> properties of an options object. Lists only key paths and secret
/// names, never values.
/// </summary>
public sealed record SecretBindingReport(
    IReadOnlyList<string> MissingRequired,
    IReadOnlyList<string> MissingOptional,
    IReadOnlyList<string> BoundFromConfiguration)
{
    /// <summary>True when startup may continue.</summary>
    public bool IsValid => MissingRequired.Count == 0 && BoundFromConfiguration.Count == 0;
}
