using Marshal.Abstractions.Printers;
using Marshal.Api.Hosting;
using Marshal.Api.Versioning;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Printers;

/// <summary>
/// Snapshot links on Marshal's own origin, under the path base and the current API version. Shared links carry a
/// Data Protection signature over printer and snapshot id that expires after <see cref="SharedLifetime"/>.
/// </summary>
internal sealed class ApiSnapshotLinks(IOptions<HttpOptions> http, IDataProtectionProvider protection) : ISnapshotLinks
{
    public static readonly TimeSpan SharedLifetime = TimeSpan.FromDays(1);

    private readonly ITimeLimitedDataProtector _protector = protection.CreateProtector("Marshal.SnapshotLinks").ToTimeLimitedDataProtector();

    public string For(string printer, string id) =>
        $"{http.Value.PathBase?.TrimEnd('/')}/api/{ApiVersions.DocumentName(ApiVersions.V1)}/printers/{Uri.EscapeDataString(printer)}/snapshots/{Uri.EscapeDataString(id)}";

    public string Shared(string printer, string id) =>
        $"{For(printer, id)}/shared?signature={Uri.EscapeDataString(_protector.Protect(Payload(printer, id), SharedLifetime))}";

    /// <summary>True when <paramref name="signature"/> was issued for this snapshot and has not expired.</summary>
    public bool IsValid(string printer, string id, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;
        try
        {
            return _protector.Unprotect(signature, out _) == Payload(printer, id);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static string Payload(string printer, string id) => $"{printer.ToUpperInvariant()}/{id}";
}
