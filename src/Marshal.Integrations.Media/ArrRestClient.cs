using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Secrets;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Media;

/// <summary>
/// Sonarr and Radarr (API v3, header <c>X-Api-Key</c>): calendars and download queues. Their keys are full-access, so
/// this client sends only <c>GET</c>. One class for both: the APIs share auth, paging and the queue shape.
/// </summary>
public sealed class ArrRestClient(IHttpClientFactory httpClients, IOptions<MediaOptions> options, ISecretStore secrets) : IArrClient
{
    public const string HttpClientName = "arr";

    public bool IsConfigured(ArrKind kind) => options.Value.For(kind)?.Url is not null;

    public async Task<Result<IReadOnlyList<ArrCalendarEntry>>> GetCalendarAsync(ArrKind kind, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        string range = $"start={Uri.EscapeDataString(start.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}&end={Uri.EscapeDataString(end.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
        Result<JsonElement> calendar = await GetAsync(kind, kind == ArrKind.Sonarr ? $"calendar?{range}&includeSeries=true" : $"calendar?{range}", cancellationToken);
        if (calendar.IsFailure)
            return calendar.Error;
        if (calendar.Value.ValueKind != JsonValueKind.Array)
            return Unavailable(kind, "sent an unexpected calendar.");

        List<ArrCalendarEntry> entries = [];
        foreach (JsonElement item in calendar.Value.EnumerateArray())
        {
            if (kind == ArrKind.Sonarr)
            {
                if (Date(item, "airDateUtc") is { } aired)
                    entries.Add(new ArrCalendarEntry(kind, Text(Child(item, "series"), "title") ?? "", Integer(item, "seasonNumber"), Integer(item, "episodeNumber"), Text(item, "title"), null, aired, Flag(item, "hasFile")));
                continue;
            }

            // A movie has up to three release dates; each one in the range is its own entry.
            foreach ((string field, string release) in new[] { ("inCinemas", "cinema"), ("digitalRelease", "digital"), ("physicalRelease", "physical") })
            {
                if (Date(item, field) is { } date && date >= start && date <= end)
                    entries.Add(new ArrCalendarEntry(kind, Text(item, "title") ?? "", null, null, null, release, date, Flag(item, "hasFile")));
            }
        }

        return entries;
    }

    public async Task<Result<IReadOnlyList<ArrQueueItem>>> GetQueueAsync(ArrKind kind, CancellationToken cancellationToken)
    {
        Result<JsonElement> queue = await GetAsync(kind, kind == ArrKind.Sonarr ? "queue?pageSize=50&includeSeries=true&includeEpisode=true" : "queue?pageSize=50&includeMovie=true", cancellationToken);
        if (queue.IsFailure)
            return queue.Error;
        if (!queue.Value.TryGetProperty("records", out JsonElement records) || records.ValueKind != JsonValueKind.Array)
            return Unavailable(kind, "sent an unexpected queue.");

        List<ArrQueueItem> items = [];
        foreach (JsonElement record in records.EnumerateArray())
        {
            double size = Number(record, "size");
            double left = Number(record, "sizeleft");
            JsonElement episode = Child(record, "episode");
            items.Add(new ArrQueueItem(
                kind,
                (kind == ArrKind.Sonarr ? Text(Child(record, "series"), "title") : Text(Child(record, "movie"), "title")) ?? Text(record, "title") ?? "",
                kind == ArrKind.Sonarr ? Integer(episode, "seasonNumber") ?? Integer(record, "seasonNumber") : null,
                kind == ArrKind.Sonarr ? Integer(episode, "episodeNumber") : null,
                size > 0 ? Math.Round(100 * (size - left) / size, 1) : 0,
                Text(record, "timeleft"),
                Date(record, "estimatedCompletionTime"),
                Text(record, "trackedDownloadState") ?? Text(record, "status") ?? "unknown"));
        }

        return items;
    }

    private async Task<Result<JsonElement>> GetAsync(ArrKind kind, string path, CancellationToken cancellationToken)
    {
        MediaService? service = options.Value.For(kind);
        string key = kind == ArrKind.Sonarr ? "sonarr" : "radarr";
        if (service?.Url is not { } url)
            return Unavailable(kind, "is not configured.");
        if (!SecretName.IsValid(service.ApiKeySecret))
            return Unavailable(kind, $"has no API key: media.{key}.apiKeySecret is not set.");

        Result<string> apiKey = await secrets.GetAsync(SecretName.Create(service.ApiKeySecret).Value, cancellationToken);
        if (apiKey.IsFailure)
            return Unavailable(kind, $"has no API key: the secret '{service.ApiKeySecret}' is not set.");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{url.ToString().TrimEnd('/')}/api/v3/{path}"));
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey.Value.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using HttpClient http = httpClients.CreateClient(HttpClientName);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Unavailable(kind, $"rejected the API key ({(int)response.StatusCode}).");
            if (!response.IsSuccessStatusCode)
                return Unavailable(kind, $"answered {(int)response.StatusCode}.");

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Unavailable(kind, $"is not reachable: {exception.Message}");
        }
    }

    private static Error Unavailable(ArrKind kind, string message) => new("media.unavailable", $"{kind} {message}", ErrorType.Unavailable);

    private static JsonElement Child(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement child) && child.ValueKind == JsonValueKind.Object ? child : default;

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Integer(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

    private static double Number(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static bool Flag(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Date(JsonElement item, string name) =>
        Text(item, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date) ? date : null;
}
