using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Maintenance;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Maintenance counters from Home Assistant: an entity's numeric state (unit from <c>unit_of_measurement</c>), else a
/// long-term statistic's latest <c>sum</c> (<c>recorder/statistics_during_period</c>, hourly, the last day). Lists
/// sensors with state class <c>total_increasing</c> whose id or name suggests runtime, cycles or hours as candidates.
/// </summary>
public sealed partial class HaCounterSource(IHomeAssistantCache cache, IHomeAssistantCommands commands, TimeProvider time) : IMaintenanceCounterSource
{
    public bool Handles(MaintenanceSource source) => source.Ha is not null;

    public async Task<Result<MaintenanceReading>> ReadAsync(MaintenanceSource source, CancellationToken cancellationToken)
    {
        if (source.Ha is not { } id)
            return NotFound("");
        if (cache.Current is not { } snapshot)
            return new Error("ha.unavailable", "Home Assistant is not connected.", ErrorType.Unavailable);

        if (snapshot.States.TryGetValue(id, out HaState? state))
        {
            return decimal.TryParse(state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
                ? new MaintenanceReading(value, Unit(state.Attributes))
                : new Error("maintenance.not_a_number", $"'{id}' is '{state.State}', not a number.", ErrorType.Unavailable);
        }

        DateTimeOffset now = time.GetUtcNow();
        Result<JsonElement> statistics = await commands.CommandAsync("recorder/statistics_during_period", new JsonObject
        {
            ["start_time"] = (now - TimeSpan.FromDays(1)).ToString("O", CultureInfo.InvariantCulture),
            ["end_time"] = now.ToString("O", CultureInfo.InvariantCulture),
            ["statistic_ids"] = new JsonArray(id),
            ["period"] = "hour",
            ["types"] = new JsonArray("sum"),
        }, cancellationToken);
        if (statistics.IsFailure)
            return statistics.Error;

        if (statistics.Value.ValueKind == JsonValueKind.Object
            && statistics.Value.TryGetProperty(id, out JsonElement rows)
            && rows.ValueKind == JsonValueKind.Array
            && rows.EnumerateArray().LastOrDefault(row => row.TryGetProperty("sum", out JsonElement sum) && sum.ValueKind == JsonValueKind.Number) is { ValueKind: JsonValueKind.Object } last)
        {
            return new MaintenanceReading(last.GetProperty("sum").GetDecimal(), null);
        }

        return NotFound(id);
    }

    public Task<IReadOnlyList<MaintenanceCounter>> ListCountersAsync(CancellationToken cancellationToken)
    {
        if (cache.Current is not { } snapshot)
            return Task.FromResult<IReadOnlyList<MaintenanceCounter>>([]);

        IReadOnlyList<MaintenanceCounter> counters =
        [
            .. snapshot.States.Values
                .Where(state => state.EntityId.StartsWith("sensor.", StringComparison.Ordinal)
                    && Attribute(state.Attributes, "state_class") == "total_increasing"
                    && (CounterName().IsMatch(state.EntityId) || CounterName().IsMatch(state.FriendlyName ?? "")))
                .OrderBy(state => state.EntityId, StringComparer.Ordinal)
                .Select(state => new MaintenanceCounter(
                    new MaintenanceSource(Ha: state.EntityId),
                    state.FriendlyName ?? state.EntityId,
                    decimal.TryParse(state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? new MaintenanceReading(value, Unit(state.Attributes)) : null)),
        ];
        return Task.FromResult(counters);
    }

    [GeneratedRegex("run_?time|hours|cycle|operating|betriebsstunden|zyklen|laufzeit", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CounterName();

    private static Error NotFound(string id) => new("maintenance.counter_not_found", $"Home Assistant has no entity or statistic '{id}'.", ErrorType.NotFound);

    private static string? Unit(JsonElement attributes) => Attribute(attributes, "unit_of_measurement");

    private static string? Attribute(JsonElement attributes, string name) =>
        attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
