using Marshal.Abstractions.Printers;
using Marshal.Api.Hosting;
using Marshal.Api.Versioning;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Printers;

/// <summary>Snapshot links on Marshal's own origin, under the path base and the current API version.</summary>
internal sealed class ApiSnapshotLinks(IOptions<HttpOptions> http) : ISnapshotLinks
{
    public string For(string printer, string id) =>
        $"{http.Value.PathBase?.TrimEnd('/')}/api/{ApiVersions.DocumentName(ApiVersions.V1)}/printers/{Uri.EscapeDataString(printer)}/snapshots/{Uri.EscapeDataString(id)}";
}
