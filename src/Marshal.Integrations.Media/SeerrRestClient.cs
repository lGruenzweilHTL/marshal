using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Secrets;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Media;

/// <summary>
/// The Seerr API (<c>/api/v1</c>, header <c>X-Api-Key</c>). Only <c>GET</c> requests and <c>POST /request</c> exist.
/// The key is read from the secret store per request, so a replaced secret needs no restart.
/// </summary>
public sealed class SeerrRestClient(IHttpClientFactory httpClients, IOptions<MediaOptions> options, ISecretStore secrets) : ISeerrClient
{
    public const string HttpClientName = "seerr";

    public async Task<Result<IReadOnlyList<SeerrTitle>>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        // Seerr rejects '+' for spaces; EscapeDataString encodes them as %20.
        Result<JsonElement> found = await SendAsync(HttpMethod.Get, $"search?query={Uri.EscapeDataString(query)}&page=1", body: null, cancellationToken);
        if (found.IsFailure)
            return found.Error;

        List<SeerrTitle> titles = [];
        if (found.Value.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement result in results.EnumerateArray())
            {
                if (Text(result, "mediaType") is "movie" or "tv")
                    titles.Add(Title(result, Text(result, "mediaType") == "tv" ? MediaType.Show : MediaType.Movie));
            }
        }

        return titles;
    }

    public async Task<Result<SeerrTitle>> GetTitleAsync(MediaType type, int tmdbId, CancellationToken cancellationToken)
    {
        Result<JsonElement> details = await SendAsync(HttpMethod.Get, $"{Path(type)}/{tmdbId.ToString(CultureInfo.InvariantCulture)}", body: null, cancellationToken);
        return details.IsFailure ? details.Error : Title(details.Value, type);
    }

    public async Task<Result<IReadOnlyList<SeerrRequest>>> GetRequestsAsync(int take, CancellationToken cancellationToken)
    {
        Result<JsonElement> list = await SendAsync(HttpMethod.Get, $"request?take={take.ToString(CultureInfo.InvariantCulture)}&skip=0&sort=added&filter=all", body: null, cancellationToken);
        if (list.IsFailure)
            return list.Error;

        List<SeerrRequest> requests = [];
        if (list.Value.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement request in results.EnumerateArray())
            {
                if (!request.TryGetProperty("media", out JsonElement media) || media.ValueKind != JsonValueKind.Object || Integer(media, "tmdbId") is not { } tmdbId)
                    continue;
                requests.Add(new SeerrRequest(
                    Text(media, "mediaType") == "tv" ? MediaType.Show : MediaType.Movie,
                    tmdbId,
                    RequestStatusOf(Integer(request, "status")),
                    AvailabilityOf(Integer(media, "status")),
                    DateTimeOffset.TryParse(Text(request, "createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset created) ? created : DateTimeOffset.MinValue));
            }
        }

        return requests;
    }

    public async Task<Result> CreateRequestAsync(MediaType type, int tmdbId, IReadOnlyList<int>? seasons, CancellationToken cancellationToken)
    {
        Dictionary<string, object> body = new() { ["mediaType"] = Path(type), ["mediaId"] = tmdbId };
        if (type == MediaType.Show)
            body["seasons"] = seasons is null ? "all" : seasons;

        Result<JsonElement> created = await SendAsync(HttpMethod.Post, "request", JsonContent.Create(body), cancellationToken);
        return created.IsFailure ? created.Error : Result.Success();
    }

    private async Task<Result<JsonElement>> SendAsync(HttpMethod method, string path, HttpContent? body, CancellationToken cancellationToken)
    {
        using (body)
        {
            MediaService? service = options.Value.Seerr;
            if (service?.Url is not { } url)
                return Unavailable("Seerr is not configured.");
            if (!SecretName.IsValid(service.ApiKeySecret))
                return Unavailable("media.seerr.apiKeySecret is not set.");

            Result<string> key = await secrets.GetAsync(SecretName.Create(service.ApiKeySecret).Value, cancellationToken);
            if (key.IsFailure)
                return Unavailable($"The Seerr API key secret '{service.ApiKeySecret}' is not set.");

            using var request = new HttpRequestMessage(method, new Uri($"{url.ToString().TrimEnd('/')}/api/v1/{path}")) { Content = body };
            request.Headers.TryAddWithoutValidation("X-Api-Key", key.Value.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            try
            {
                using HttpClient http = httpClients.CreateClient(HttpClientName);
                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return Unavailable($"Seerr rejected the API key ({(int)response.StatusCode}).");
                if (response.StatusCode == HttpStatusCode.NotFound && method == HttpMethod.Get)
                    return new Error("media.not_found", "Seerr does not know that title.", ErrorType.NotFound);
                if (!response.IsSuccessStatusCode)
                    return Unavailable($"Seerr answered {(int)response.StatusCode}{await ReasonAsync(response, cancellationToken)}.");

                string content = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(content))
                    return default(JsonElement);
                using var document = JsonDocument.Parse(content);
                return document.RootElement.Clone();
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return Unavailable($"Seerr is not reachable: {exception.Message}");
            }
        }
    }

    // Seerr explains rejected requests in {"message": "..."}; short and from the owner's own server.
    private static async Task<string> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return Text(document.RootElement, "message") is { Length: > 0 and <= 200 } message ? $": {message}" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static SeerrTitle Title(JsonElement item, MediaType type)
    {
        string? date = type == MediaType.Show ? Text(item, "firstAirDate") : Text(item, "releaseDate");
        IReadOnlyList<int>? seasons = item.TryGetProperty("seasons", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(season => Integer(season, "seasonNumber")).OfType<int>().Where(number => number > 0).Order()]
            : null;
        JsonElement media = item.TryGetProperty("mediaInfo", out JsonElement info) && info.ValueKind == JsonValueKind.Object ? info : default;
        return new SeerrTitle(
            type,
            Integer(item, "id") ?? 0,
            (type == MediaType.Show ? Text(item, "name") : Text(item, "title")) ?? "",
            date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) ? year : null,
            Text(item, "overview") is { Length: > 0 } overview ? overview : null,
            seasons,
            media.ValueKind == JsonValueKind.Object ? AvailabilityOf(Integer(media, "status")) : MediaAvailability.Unknown,
            media.ValueKind == JsonValueKind.Object && Text(media, "jellyfinMediaId") is { Length: > 0 } jellyfin ? jellyfin : null);
    }

    private static string Path(MediaType type) => type == MediaType.Show ? "tv" : "movie";

    /// <summary>MediaStatus: 1 unknown, 2 pending, 3 processing, 4 partially available, 5 available, 6 blocklisted, 7 deleted.</summary>
    internal static MediaAvailability AvailabilityOf(int? status) => status switch
    {
        2 => MediaAvailability.Pending,
        3 => MediaAvailability.Processing,
        4 => MediaAvailability.PartiallyAvailable,
        5 => MediaAvailability.Available,
        _ => MediaAvailability.Unknown,
    };

    /// <summary>MediaRequestStatus: 1 pending, 2 approved, 3 declined, 4 failed, 5 completed.</summary>
    private static RequestStatus RequestStatusOf(int? status) => status switch
    {
        2 => RequestStatus.Approved,
        3 => RequestStatus.Declined,
        4 => RequestStatus.Failed,
        5 => RequestStatus.Completed,
        _ => RequestStatus.Pending,
    };

    private static Error Unavailable(string message) => new("media.unavailable", message, ErrorType.Unavailable);

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static int? Integer(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
}
