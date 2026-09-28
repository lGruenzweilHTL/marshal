using Asp.Versioning;

namespace Marshal.Api.Versioning;

/// <summary>
/// The public API versions, declared once in code. Never configurable: a version describes the contract this
/// code implements.
/// </summary>
internal static class ApiVersions
{
    public static readonly ApiVersion V1 = new(1, 0);

    public static IReadOnlyList<ApiVersion> Supported { get; } = [V1];

    public static IReadOnlyList<ApiVersion> Deprecated { get; } = [];

    /// <summary>OpenAPI document name of a version (<c>v1</c>).</summary>
    public static string DocumentName(ApiVersion version) => $"v{version.MajorVersion}";
}
