using System.Globalization;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Api.Hosting;
using Marshal.Api.Versioning;
using Marshal.Core.Displays;
using Marshal.Tasks.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Displays;

/// <summary>
/// Display links: absolute URLs under <c>displays.mediaBaseUrl</c> (else <c>notifications.publicUrl</c>) that a TV
/// fetches without signing in. The signature (Data Protection, own purpose, base64url) sits in the path and covers
/// owner, artifact, page and expiry; the path ends in the rendition's extension because media renderers are picky.
/// </summary>
internal sealed class ApiDisplayLinks(
    IOptions<HttpOptions> http,
    IOptions<DisplayOptions> displays,
    IOptions<NotificationOptions> notifications,
    IDataProtectionProvider protection,
    IExecutionContextAccessor executionContext,
    TimeProvider time) : IDisplayLinks
{
    private readonly IDataProtector _protector = protection.CreateProtector("Marshal.DisplayLinks");

    public string Create(ArtifactId id, int page, string extension)
    {
        OwnerId owner = executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Display links need an execution context.");
        Uri origin = displays.Value.MediaBaseUrl ?? notifications.Value.PublicUrl
            ?? throw new InvalidOperationException("Display links need displays.mediaBaseUrl or notifications.publicUrl.");
        long expires = time.GetUtcNow().AddMinutes(displays.Value.LinkMinutes).ToUnixTimeSeconds();
        string signature = _protector.Protect(string.Create(CultureInfo.InvariantCulture, $"{owner}/{id}/{page}/{expires}"));
        string path = string.Create(CultureInfo.InvariantCulture,
            $"{http.Value.PathBase?.TrimEnd('/')}/api/{ApiVersions.DocumentName(ApiVersions.V1)}/artifacts/{id}/display/{page}/{signature}.{extension}");
        return new Uri(origin, path).ToString();
    }

    /// <summary>The owner a signature was issued for when it covers <paramref name="id"/> and <paramref name="page"/> and has not expired.</summary>
    public OwnerId? Verify(ArtifactId id, int page, string signature)
    {
        string payload;
        try
        {
            payload = _protector.Unprotect(signature);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }

        string[] parts = payload.Split('/');
        return parts.Length == 4
            && OwnerId.TryParse(parts[0], null, out OwnerId owner)
            && parts[1] == id.ToString()
            && parts[2] == page.ToString(CultureInfo.InvariantCulture)
            && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out long expires)
            && time.GetUtcNow().ToUnixTimeSeconds() < expires
                ? owner
                : null;
    }
}
