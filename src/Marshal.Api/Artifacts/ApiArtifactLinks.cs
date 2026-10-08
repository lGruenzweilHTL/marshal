using System.Globalization;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Identity;
using Marshal.Api.Hosting;
using Marshal.Api.Versioning;
using Marshal.Artifacts;
using Marshal.Tasks.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Artifacts;

/// <summary>
/// Artifact content links on Marshal's own origin, under the path base and the current API version. Signed links
/// carry a Data Protection signature over owner, artifact and expiry; the expiry is checked against
/// <see cref="TimeProvider"/>, so it is testable. Signed links are absolute when <c>notifications.publicUrl</c> is set.
/// </summary>
internal sealed class ApiArtifactLinks(
    IOptions<HttpOptions> http,
    IOptions<ArtifactOptions> options,
    IOptions<NotificationOptions> notifications,
    IDataProtectionProvider protection,
    Abstractions.Execution.IExecutionContextAccessor executionContext,
    TimeProvider time) : IArtifactLinks
{
    private readonly IDataProtector _protector = protection.CreateProtector("Marshal.ArtifactLinks");

    public string Content(ArtifactId id) =>
        $"{http.Value.PathBase?.TrimEnd('/')}/api/{ApiVersions.DocumentName(ApiVersions.V1)}/artifacts/{id}/content";

    public string SignedContent(ArtifactId id)
    {
        OwnerId owner = executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Signed artifact links need an execution context.");
        long expires = time.GetUtcNow().AddMinutes(options.Value.SignedLinkMinutes).ToUnixTimeSeconds();
        string signature = _protector.Protect(string.Create(CultureInfo.InvariantCulture, $"{owner}/{id}/{expires}"));
        string relative = $"{Content(id)}/signed?signature={Uri.EscapeDataString(signature)}";
        return notifications.Value.PublicUrl is { } origin ? new Uri(origin, relative).ToString() : relative;
    }

    /// <summary>The owner a signature was issued for when it is for <paramref name="id"/> and has not expired.</summary>
    public OwnerId? Verify(ArtifactId id, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return null;

        string payload;
        try
        {
            payload = _protector.Unprotect(signature);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }

        string[] parts = payload.Split('/');
        return parts.Length == 3
            && OwnerId.TryParse(parts[0], null, out OwnerId owner)
            && parts[1] == id.ToString()
            && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long expires)
            && time.GetUtcNow().ToUnixTimeSeconds() < expires
                ? owner
                : null;
    }
}
