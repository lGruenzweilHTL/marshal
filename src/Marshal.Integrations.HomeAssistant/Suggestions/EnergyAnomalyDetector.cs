using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Suggestions;
using Marshal.Integrations.HomeAssistant.Energy;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Suggestions;

/// <summary>
/// <c>energy_anomaly</c>: a device on HA's energy dashboard used at least <c>factor</c> times its median daily
/// consumption of the previous 14 days yesterday. Reads the dashboard (<c>energy/get_prefs</c>) and daily statistics
/// (<c>recorder/statistics_during_period</c>, type <c>change</c>, kWh) like <c>energy.usage</c>. A device with fewer than
/// 14 days of history, or a median of zero, is skipped. Runs every six hours; the dedupe key holds the day.
/// </summary>
public sealed class EnergyAnomalyDetector(IHomeAssistantCache cache, IHomeAssistantCommands commands, IOptions<SuggestionOptions> options, TimeProvider time) : ISuggestionDetector
{
    public const string DetectorId = "energy_anomaly";

    /// <summary>Days of history the median is taken over.</summary>
    public const int HistoryDays = 14;

    public string Id => DetectorId;

    public SuggestionTriggers Triggers { get; } = new([], TimeSpan.FromHours(6));

    public async Task<IReadOnlyList<SuggestionCandidate>> EvaluateAsync(SuggestionContext context, CancellationToken cancellationToken)
    {
        Result<JsonElement> prefs = await commands.CommandAsync("energy/get_prefs", new JsonObject(), cancellationToken);
        if (prefs.IsFailure)
            return [];

        var dashboard = EnergyTools.Dashboard.Parse(prefs.Value, default);
        if (dashboard.Devices.Count == 0)
            return [];

        DateTime today = TimeZoneInfo.ConvertTime(time.GetUtcNow(), context.TimeZone).Date;
        DateTime yesterday = today.AddDays(-1);
        DateTime first = yesterday.AddDays(-HistoryDays);
        Result<JsonElement> statistics = await commands.CommandAsync("recorder/statistics_during_period", new JsonObject
        {
            ["start_time"] = Utc(first, context.TimeZone).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["end_time"] = Utc(today, context.TimeZone).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["statistic_ids"] = new JsonArray([.. dashboard.Devices.Select(device => (JsonNode)device.Statistic)]),
            ["period"] = "day",
            ["types"] = new JsonArray("change"),
            ["units"] = new JsonObject { ["energy"] = "kWh" },
        }, cancellationToken);
        if (statistics.IsFailure || statistics.Value.ValueKind != JsonValueKind.Object)
            return [];

        double factor = options.Value.For(DetectorId).Factor;
        DateTimeOffset yesterdayStart = Utc(yesterday, context.TimeZone);
        var candidates = new List<SuggestionCandidate>();
        foreach ((string statistic, string? name) in dashboard.Devices)
        {
            if (!statistics.Value.TryGetProperty(statistic, out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
                continue;

            Dictionary<DateTimeOffset, double> byDay = Days(rows);
            double[] history = [.. byDay.Where(day => day.Key < yesterdayStart).Select(day => day.Value)];
            if (history.Length < HistoryDays || !byDay.TryGetValue(yesterdayStart, out double used))
                continue;

            double median = Median(history);
            if (median <= 0 || used < factor * median)
                continue;

            string device = name ?? cache.Current?.States.GetValueOrDefault(statistic)?.FriendlyName ?? statistic;
            candidates.Add(new SuggestionCandidate(
                $"{statistic}:{yesterday:yyyy-MM-dd}",
                new Dictionary<string, string>
                {
                    ["device"] = device,
                    ["yesterday"] = $"{Format(used)} kWh",
                    ["usual (median of the previous 14 days)"] = $"{Format(median)} kWh",
                    ["times the usual"] = Format(used / median),
                }));
        }

        return candidates;
    }

    private static Dictionary<DateTimeOffset, double> Days(JsonElement rows)
    {
        var days = new Dictionary<DateTimeOffset, double>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (Start(row) is { } start && row.TryGetProperty("change", out JsonElement change) && change.ValueKind == JsonValueKind.Number)
                days[start] = change.GetDouble();
        }

        return days;
    }

    // HA sends the row start as epoch milliseconds (or an ISO string in older versions).
    private static DateTimeOffset? Start(JsonElement row) => row.TryGetProperty("start", out JsonElement start)
        ? start.ValueKind switch
        {
            JsonValueKind.Number => DateTimeOffset.FromUnixTimeMilliseconds(start.GetInt64()),
            JsonValueKind.String when DateTimeOffset.TryParse(start.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) => parsed,
            _ => null,
        }
        : null;

    private static double Median(double[] values)
    {
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static DateTimeOffset Utc(DateTime local, TimeZoneInfo zone) => new(local, zone.GetUtcOffset(local));

    private static string Format(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
}
