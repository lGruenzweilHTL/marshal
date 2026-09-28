using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// REST calls with the long-lived token. Failures are results, never retried: an intent may start a timer.
/// </summary>
public sealed class HomeAssistantClient(IHttpClientFactory httpClients, IOptions<HomeAssistantOptions> options) : IHomeAssistantClient
{
    /// <summary>Name of the <see cref="HttpClient"/> used for Home Assistant.</summary>
    public const string HttpClientName = "home-assistant";

    public async Task<Result<JsonElement>> HandleIntentAsync(HaIntentRequest request, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["name"] = request.Name, ["data"] = request.Data.DeepClone(), ["language"] = request.Language };
        if (request.DeviceId is not null)
            body["device_id"] = request.DeviceId;
        if (request.SatelliteId is not null)
            body["satellite_id"] = request.SatelliteId;

        return await SendAsync(HttpMethod.Post, "api/intent/handle", body, cancellationToken);
    }

    public async Task<Result<string>> GetTimeZoneAsync(CancellationToken cancellationToken)
    {
        Result<JsonElement> config = await SendAsync(HttpMethod.Get, "api/config", null, cancellationToken);
        if (config.IsFailure)
            return config.Error;

        return config.Value.ValueKind == JsonValueKind.Object && config.Value.TryGetProperty("time_zone", out JsonElement zone) && zone.GetString() is { } value
            ? value
            : new Error("ha.no_time_zone", "Home Assistant reported no time zone.", ErrorType.Unavailable);
    }

    private async Task<Result<JsonElement>> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        HomeAssistantOptions ha = options.Value;
        using var message = new HttpRequestMessage(method, new Uri(ha.BaseUri, path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ha.Token);
        if (body is not null)
            message.Content = JsonContent.Create(body);

        try
        {
            using HttpClient http = httpClients.CreateClient(HttpClientName);
            using HttpResponseMessage response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new Error("ha.http_error", $"Home Assistant answered {(int)response.StatusCode} for {path}.", ErrorType.Unavailable);

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new Error("ha.unreachable", $"Home Assistant is not reachable: {exception.Message}", ErrorType.Unavailable);
        }
    }
}
