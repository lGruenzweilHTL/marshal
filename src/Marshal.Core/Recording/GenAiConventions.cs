namespace Marshal.Core.Recording;

/// <summary>
/// The OpenTelemetry GenAI semantic conventions version Marshal's spans follow: the version the referenced
/// <c>Microsoft.Extensions.AI</c> implements. Update it in the same change that updates that package.
/// </summary>
public static class GenAiConventions
{
    public const string Version = "1.41";
}
