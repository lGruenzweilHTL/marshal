using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Integrations.HomeAssistant;

namespace Marshal.Demo.HomeAssistant;

/// <summary>
/// Home Assistant's websocket commands, simulated: service calls, the energy dashboard with hourly statistics for
/// grid and solar, and zone creation. Other commands return <c>ha.service_not_supported</c>.
/// </summary>
public sealed class DemoHomeAssistantCommands(DemoWorld world) : IHomeAssistantCommands
{
    private const string Grid = "sensor.grid_consumption";
    private const string Solar = "sensor.solar_production";

    public Task<Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken) =>
        Task.FromResult(type switch
        {
            "call_service" => CallService(fields),
            "energy/get_prefs" => Result.Success(JsonSerializer.SerializeToElement(new
            {
                energy_sources = new object[]
                {
                    new { type = "grid", stat_energy_from = Grid, stat_energy_to = (string?)null, stat_cost = (string?)null },
                    new { type = "solar", stat_energy_from = Solar },
                },
                device_consumption = Array.Empty<object>(),
            })),
            "energy/info" => Result.Success(JsonSerializer.SerializeToElement(new { cost_sensors = new { } })),
            "recorder/statistics_during_period" => Statistics(fields),
            "zone/create" => CreateZone(fields),
            "config_entries/get" => Result.Success(JsonSerializer.SerializeToElement(Array.Empty<object>())),
            _ => new Error("ha.service_not_supported", $"The demo does not simulate the command {type}.", ErrorType.Validation),
        });

    private Result<JsonElement> CallService(JsonObject fields)
    {
        JsonObject data = (fields["service_data"] as JsonObject)?.DeepClone().AsObject() ?? [];
        if ((fields["target"] as JsonObject)?["entity_id"] is { } target)
            data["entity_id"] = target.DeepClone();
        Result<IReadOnlyList<HaState>> called = DemoServiceCalls.Call(world.Home, (string?)fields["domain"] ?? "", (string?)fields["service"] ?? "", data);
        return called.IsSuccess ? DemoServiceCalls.Response(called.Value) : called.Error;
    }

    // A plausible household: grid draw peaks in the morning and evening, solar follows the daylight hours.
    private Result<JsonElement> Statistics(JsonObject fields)
    {
        if (!DateTimeOffset.TryParse((string?)fields["start_time"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset start)
            || !DateTimeOffset.TryParse((string?)fields["end_time"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset end))
        {
            return new Error("ha.invalid_command", "start_time and end_time are required.", ErrorType.Validation);
        }

        bool daily = (string?)fields["period"] == "day";
        TimeZoneInfo zone = world.Home.TimeZone;
        var grid = new List<object>();
        var solar = new List<object>();
        for (DateTimeOffset at = start; at < end; at = daily ? at.AddDays(1) : at.AddHours(1))
        {
            int hours = daily ? 24 : 1;
            double gridChange = 0, solarChange = 0;
            for (int hour = 0; hour < hours; hour++)
            {
                int local = TimeZoneInfo.ConvertTime(at.AddHours(hour), zone).Hour;
                gridChange += local is >= 6 and < 9 or >= 17 and < 22 ? 0.62 : 0.21;
                solarChange += local is >= 8 and < 17 ? 0.9 * Math.Sin(Math.PI * (local - 8 + 0.5) / 9) : 0;
            }

            long ms = at.ToUnixTimeMilliseconds();
            grid.Add(new { start = ms, change = Math.Round(gridChange, 3) });
            solar.Add(new { start = ms, change = Math.Round(solarChange, 3) });
        }

        return JsonSerializer.SerializeToElement(new Dictionary<string, List<object>> { [Grid] = grid, [Solar] = solar });
    }

    private Result<JsonElement> CreateZone(JsonObject fields)
    {
        string name = (string?)fields["name"] ?? "Zone";
        string storageId = name.ToLowerInvariant().Replace(' ', '_');
        world.Home.AddZone($"zone.{storageId}", name, (double?)fields["latitude"] ?? 0, (double?)fields["longitude"] ?? 0, (int?)fields["radius"] ?? 100);
        return JsonSerializer.SerializeToElement(new { id = storageId, name, latitude = (double?)fields["latitude"], longitude = (double?)fields["longitude"], radius = (int?)fields["radius"] ?? 100, passive = false });
    }
}
