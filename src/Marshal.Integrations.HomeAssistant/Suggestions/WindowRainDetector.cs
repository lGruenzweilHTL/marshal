using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Suggestions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Suggestions;

/// <summary>
/// <c>window_rain</c>: a window or door sensor (<c>binary_sensor</c> with device class <c>window</c> or <c>door</c>)
/// is open while the weather forecast has a precipitation probability of at least <c>minProbability</c>. Reads
/// <c>weather.get_forecasts</c> (websocket <c>call_service</c> with <c>return_response</c>): the hourly forecast for the
/// next hour, else today's daily forecast, else nothing.
/// </summary>
public sealed class WindowRainDetector(
    IHomeAssistantCache cache,
    IHomeAssistantCommands commands,
    IOptions<SuggestionOptions> options,
    TimeProvider time) : ISuggestionDetector
{
    public const string DetectorId = "window_rain";

    public string Id => DetectorId;

    public SuggestionTriggers Triggers { get; } = new([new SuggestionEventTrigger("ha", HomeAssistantEventSource.StateChanged)], TimeSpan.FromMinutes(15));

    public async Task<IReadOnlyList<SuggestionCandidate>> EvaluateAsync(SuggestionContext context, CancellationToken cancellationToken)
    {
        if (cache.Current is not { } snapshot)
            return [];

        // A state change matters only when a window or door just opened.
        if (context.Event is { } @event && (@event.Entity is not { } subject || !IsOpening(snapshot, subject)))
            return [];

        HaEntity[] open = [.. OpenSensors(snapshot)];
        if (open.Length == 0)
            return [];

        SuggestionDetectorOptions settings = options.Value.For(DetectorId);
        if (WeatherEntity(snapshot, settings.WeatherEntity) is not { } weather)
            return [];

        if (await ForecastAsync(weather, cancellationToken) is not { } forecast || forecast.Probability < settings.MinProbability)
            return [];

        string day = TimeZoneInfo.ConvertTime(time.GetUtcNow(), context.TimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return [.. open.Select(sensor => new SuggestionCandidate(
            $"{sensor.EntityId}:{day}",
            Facts(snapshot, sensor, forecast)))];
    }

    public Task<bool> StillHoldsAsync(OwnerId owner, string dedupeKey, CancellationToken cancellationToken)
    {
        string entity = dedupeKey[..Math.Max(0, dedupeKey.LastIndexOf(':'))];
        return Task.FromResult(cache.Current is not { } snapshot || IsOpen(snapshot, entity));
    }

    private static Dictionary<string, string> Facts(HaSnapshot snapshot, HaEntity sensor, Forecast forecast)
    {
        var facts = new Dictionary<string, string>
        {
            ["opening"] = snapshot.States.TryGetValue(sensor.EntityId, out HaState? state) ? state.FriendlyName ?? sensor.Name : sensor.Name,
            ["kind"] = sensor.DeviceClass ?? "window",
            ["precipitation probability"] = $"{forecast.Probability.ToString(CultureInfo.InvariantCulture)}%",
            ["forecast"] = forecast.Hourly ? "within the next hour" : "today",
        };
        if (sensor.AreaId is { } area && snapshot.Areas.TryGetValue(area, out HaArea? found))
            facts["area"] = found.Name;
        return facts;
    }

    private static IEnumerable<HaEntity> OpenSensors(HaSnapshot snapshot) =>
        snapshot.Entities.Values.Where(entity => IsOpening(snapshot, entity.EntityId)).OrderBy(entity => entity.EntityId, StringComparer.Ordinal);

    private static bool IsOpening(HaSnapshot snapshot, string entityId) =>
        snapshot.Entities.TryGetValue(entityId, out HaEntity? entity)
        && entity is { Domain: "binary_sensor", DeviceClass: "window" or "door" }
        && IsOpen(snapshot, entityId);

    private static bool IsOpen(HaSnapshot snapshot, string entityId) =>
        snapshot.States.TryGetValue(entityId, out HaState? state) && state.State == "on";

    private static string? WeatherEntity(HaSnapshot snapshot, string? configured) =>
        configured ?? snapshot.States.Keys.Where(id => id.StartsWith("weather.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).FirstOrDefault();

    // Hourly when the entity offers it, else daily; null when neither answers.
    private async Task<Forecast?> ForecastAsync(string weather, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (await ReadAsync(weather, "hourly", cancellationToken) is { Length: > 0 } hourly)
        {
            // Entries whose hour overlaps the next hour.
            int[] next = [.. hourly.Where(entry => entry.At > now - TimeSpan.FromHours(1) && entry.At < now + TimeSpan.FromHours(1)).Select(entry => entry.Probability)];
            return next.Length > 0 ? new Forecast(next.Max(), Hourly: true) : null;
        }

        if (await ReadAsync(weather, "daily", cancellationToken) is { Length: > 0 } daily)
            return new Forecast(daily[0].Probability, Hourly: false);
        return null;
    }

    private async Task<Entry[]?> ReadAsync(string weather, string type, CancellationToken cancellationToken)
    {
        var fields = new JsonObject
        {
            ["domain"] = "weather",
            ["service"] = "get_forecasts",
            ["service_data"] = new JsonObject { ["type"] = type },
            ["target"] = new JsonObject { ["entity_id"] = weather },
            ["return_response"] = true,
        };
        Result<JsonElement> called = await commands.CommandAsync("call_service", fields, cancellationToken);
        if (called.IsFailure
            || !called.Value.TryGetProperty("response", out JsonElement response)
            || !response.TryGetProperty(weather, out JsonElement entity)
            || !entity.TryGetProperty("forecast", out JsonElement forecast)
            || forecast.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return [.. forecast.EnumerateArray().Select(Parse).OfType<Entry>()];
    }

    private static Entry? Parse(JsonElement item)
    {
        if (!item.TryGetProperty("datetime", out JsonElement at) || at.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return null;
        }

        int probability = item.TryGetProperty("precipitation_probability", out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(value.GetDouble())
            : 0;
        return new Entry(parsed, probability);
    }

    private sealed record Entry(DateTimeOffset At, int Probability);

    private sealed record Forecast(int Probability, bool Hourly);
}
