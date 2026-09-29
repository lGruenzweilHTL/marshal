using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Moonraker REST calls with the printer's <c>X-Api-Key</c>, unwrapping <c>{"result": ...}</c> and mapping failures
/// to printer errors.
/// </summary>
internal sealed class MoonrakerRest(IOptions<PrinterOptions> options, IHttpClientFactory httpClients)
{
    /// <summary>Name of the <see cref="HttpClient"/> (resilience pipeline configured by the host).</summary>
    public const string HttpClientName = "moonraker";

    /// <summary>Name of the <see cref="HttpClient"/> for webcam images and streams (no timeout, no retries).</summary>
    public const string WebcamClientName = "moonraker-webcam";

    public PrinterConfig? Find(string printer) =>
        options.Value.Printers.FirstOrDefault(config => config.Url is not null && string.Equals(config.Name, printer, StringComparison.OrdinalIgnoreCase));

    public HttpClient Client() => httpClients.CreateClient(HttpClientName);

    public async Task<Result<JsonElement>> RequestAsync(string printer, HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        if (Find(printer) is not { } config)
            return PrinterErrors.NotFound(printer);

        using var request = new HttpRequestMessage(method, new Uri(config.BaseUri(), path));
        if (config.ApiKey is not null)
            request.Headers.Add("X-Api-Key", config.ApiKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        try
        {
            using HttpResponseMessage response = await Client().SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return PrinterErrors.Unauthorized(config.Name);

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (!response.IsSuccessStatusCode || !root.TryGetProperty("result", out JsonElement result))
            {
                string message = root.TryGetProperty("error", out JsonElement error) && Text(error, "message") is { } text ? text : $"HTTP {(int)response.StatusCode}";
                return PrinterErrors.CommandFailed(config.Name, message);
            }

            return result.Clone();
        }
        catch (HttpRequestException)
        {
            return PrinterErrors.Offline(config.Name);
        }
        catch (JsonException)
        {
            return PrinterErrors.CommandFailed(config.Name, "Moonraker sent an unreadable answer.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PrinterErrors.Offline(config.Name);
        }
    }

    public static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
