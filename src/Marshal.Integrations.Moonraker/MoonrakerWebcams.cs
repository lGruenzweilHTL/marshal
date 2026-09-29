using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Webcams from Moonraker's <c>/server/webcams/list</c>; relative URLs (the usual <c>/webcam/?action=…</c>) are
/// resolved against the printer URL.
/// </summary>
public sealed class MoonrakerWebcams(IOptions<PrinterOptions> options, IHttpClientFactory httpClients) : IPrinterWebcams
{
    private readonly MoonrakerRest _rest = new(options, httpClients);

    public async Task<Result<PrinterWebcam>> FindAsync(string printer, CancellationToken cancellationToken)
    {
        Result<JsonElement> result = await _rest.RequestAsync(printer, HttpMethod.Get, "server/webcams/list", null, cancellationToken);
        if (result.IsFailure)
            return result.Error;

        PrinterConfig config = _rest.Find(printer)!;
        if (result.Value.TryGetProperty("webcams", out JsonElement webcams) && webcams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement webcam in webcams.EnumerateArray())
            {
                bool enabled = !webcam.TryGetProperty("enabled", out JsonElement flag) || flag.ValueKind != JsonValueKind.False;
                if (enabled && Resolve(config, MoonrakerRest.Text(webcam, "snapshot_url")) is { } snapshot)
                    return new PrinterWebcam(MoonrakerRest.Text(webcam, "name") ?? "webcam", snapshot, Resolve(config, MoonrakerRest.Text(webcam, "stream_url")));
            }
        }

        return PrinterErrors.NoWebcam(config.Name);
    }

    private static Uri? Resolve(PrinterConfig config, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(config.BaseUri(), url, out Uri? resolved))
            return null;

        return resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps ? resolved : null;
    }
}
