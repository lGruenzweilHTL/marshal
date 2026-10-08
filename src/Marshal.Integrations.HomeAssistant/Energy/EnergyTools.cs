using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant.Energy;

/// <summary>
/// <c>energy.usage</c>: totals of HA's energy dashboard for a period, read from HA's long-term statistics over the
/// websocket (<c>energy/get_prefs</c>, <c>energy/info</c>, <c>recorder/statistics_during_period</c>). The tool maps
/// the dashboard for the model, so no statistic ids or units leak into its decisions. Hidden while the dashboard has
/// no sources; the dashboard is read for visibility at most once per <see cref="PrefsLifetime"/>.
/// </summary>
internal static class EnergyTools
{
    /// <summary>How long a visibility decision from the dashboard prefs is reused.</summary>
    public static readonly TimeSpan PrefsLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Ranges longer than this use daily statistics instead of hourly ones.</summary>
    public const int MaxHourlyDays = 92;

    private const int TopDevices = 5;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> _energyUnits = new(StringComparer.Ordinal) { "Wh", "kWh", "MWh", "GWh", "TWh", "J", "kJ", "MJ", "GJ", "cal", "kcal", "Mcal", "Gcal" };

    public static IReadOnlyList<ITool> Create(IHomeAssistantCache cache, IHomeAssistantCommands commands, IUserProfileProvider profiles, TimeProvider time) =>
        [new UsageTool(cache, commands, profiles, time)];

    /// <summary>The statistic ids of one dashboard, by role; several sources of one kind are summed.</summary>
    internal sealed class Dashboard
    {
        public List<string> GridImport { get; } = [];
        public List<string> GridExport { get; } = [];
        public List<string> Solar { get; } = [];
        public List<string> BatteryCharge { get; } = [];
        public List<string> BatteryDischarge { get; } = [];
        public List<string> Gas { get; } = [];
        public List<string> Water { get; } = [];

        /// <summary>Cost statistics (<c>stat_cost</c>, else HA's generated cost sensor), by the energy they price.</summary>
        public List<string> Cost { get; } = [];
        public List<string> Compensation { get; } = [];
        public List<string> GasCost { get; } = [];
        public List<string> WaterCost { get; } = [];

        /// <summary>Top-level devices (not included in another device's statistic).</summary>
        public List<(string Statistic, string? Name)> Devices { get; } = [];

        public bool HasSources => GridImport.Count + GridExport.Count + Solar.Count + BatteryCharge.Count + BatteryDischarge.Count + Gas.Count + Water.Count > 0;

        public IEnumerable<string> StatisticIds =>
            new[] { GridImport, GridExport, Solar, BatteryCharge, BatteryDischarge, Gas, Water, Cost, Compensation, GasCost, WaterCost }
                .SelectMany(ids => ids)
                .Concat(Devices.Select(device => device.Statistic))
                .Distinct(StringComparer.Ordinal);

        /// <summary>Reads <c>energy/get_prefs</c> in the current and the legacy grid shape; unknown source types are ignored.</summary>
        public static Dashboard Parse(JsonElement prefs, JsonElement costSensors)
        {
            var dashboard = new Dashboard();
            foreach (JsonElement source in Array(prefs, "energy_sources"))
            {
                switch (Text(source, "type"))
                {
                    case "grid":
                        // Legacy shape: flow_from / flow_to lists. Current shape: one flat entry per grid connection.
                        foreach (JsonElement flow in Array(source, "flow_from").Append(source))
                            dashboard.Add(dashboard.GridImport, dashboard.Cost, flow, "stat_energy_from", "stat_cost", costSensors);
                        foreach (JsonElement flow in Array(source, "flow_to").Append(source))
                            dashboard.Add(dashboard.GridExport, dashboard.Compensation, flow, "stat_energy_to", "stat_compensation", costSensors);
                        break;
                    case "solar":
                        Add(dashboard.Solar, Text(source, "stat_energy_from"));
                        break;
                    case "battery":
                        Add(dashboard.BatteryDischarge, Text(source, "stat_energy_from"));
                        Add(dashboard.BatteryCharge, Text(source, "stat_energy_to"));
                        break;
                    case "gas":
                        dashboard.Add(dashboard.Gas, dashboard.GasCost, source, "stat_energy_from", "stat_cost", costSensors);
                        break;
                    case "water":
                        dashboard.Add(dashboard.Water, dashboard.WaterCost, source, "stat_energy_from", "stat_cost", costSensors);
                        break;
                }
            }

            foreach (JsonElement device in Array(prefs, "device_consumption"))
            {
                if (Text(device, "stat_consumption") is { Length: > 0 } statistic && Text(device, "included_in_stat") is null)
                    dashboard.Devices.Add((statistic, Text(device, "name")));
            }

            return dashboard;
        }

        private void Add(List<string> energy, List<string> money, JsonElement source, string energyKey, string moneyKey, JsonElement costSensors)
        {
            if (Text(source, energyKey) is not { Length: > 0 } statistic)
                return;

            Add(energy, statistic);
            string? priced = Text(source, moneyKey) ?? Text(costSensors, statistic);
            Add(money, priced);
        }

        private static void Add(List<string> list, string? statistic)
        {
            if (statistic is { Length: > 0 } && !list.Contains(statistic, StringComparer.Ordinal))
                list.Add(statistic);
        }
    }

    private sealed class UsageTool(IHomeAssistantCache cache, IHomeAssistantCommands commands, IUserProfileProvider profiles, TimeProvider time) : ITool
    {
        private readonly Lock _lock = new();
        private (bool Visible, DateTimeOffset At)? _visibility;

        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("energy", "usage"),
            Description = "Electricity, gas and water use from the Home Assistant energy dashboard for a period: grid import and export, solar, battery, household consumption, cost and the devices that used the most. compareWith: previous adds the previous period of the same length, for 'more than last week?'.",
            ParametersSchema = JsonDocument.Parse($$$"""
                {"type":"object","properties":{
                  "period":{"type":"string","enum":[{{{string.Join(",", EnergyPeriods.All.Select(period => $"\"{period}\""))}}}],"description":"week = since Monday, month = since the 1st, both until now."},
                  "from":{"type":"string","description":"First day of a range, e.g. 2026-10-01."},
                  "to":{"type":"string","description":"Last day of a range (inclusive)."},
                  "compareWith":{"type":"string","enum":["previous"]}},
                 "required":["period"]}
                """).RootElement.Clone(),
            DefaultTier = RiskTier.Safe,
        };

        public async ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken)
        {
            DateTimeOffset now = time.GetUtcNow();
            lock (_lock)
            {
                if (_visibility is { } known && now - known.At < PrefsLifetime)
                    return known.Visible;
            }

            Result<JsonElement> prefs = await commands.CommandAsync("energy/get_prefs", new JsonObject(), cancellationToken);
            bool visible;
            if (prefs.IsSuccess)
                visible = Dashboard.Parse(prefs.Value, default).HasSources;
            else if (prefs.Error.Code == "ha.unavailable")
                // Disconnected: keep the last decision (calls fail with ha.unavailable until HA is back).
                return _visibility?.Visible ?? false;
            else
                visible = false; // HA answers not_found while the dashboard was never set up.

            lock (_lock)
                _visibility = (visible, now);
            return visible;
        }

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            JsonElement arguments = invocation.Arguments;
            string? compare = Text(arguments, "compareWith");
            if (compare is not (null or "previous"))
                return new Error("energy.invalid_arguments", "compareWith must be 'previous'.", ErrorType.Validation);

            TimeZoneInfo zone = await ZoneAsync(invocation.Context.OwnerId, cancellationToken);
            Result<EnergyPeriod> period = EnergyPeriods.Resolve(Text(arguments, "period"), Text(arguments, "from"), Text(arguments, "to"), compare is not null, time.GetUtcNow(), zone);
            if (period.IsFailure)
                return period.Error;

            Result<JsonElement> prefs = await commands.CommandAsync("energy/get_prefs", new JsonObject(), cancellationToken);
            if (prefs.IsFailure)
                return prefs.Error;
            Result<JsonElement> info = await commands.CommandAsync("energy/info", new JsonObject(), cancellationToken);
            JsonElement costSensors = info.IsSuccess && info.Value.ValueKind == JsonValueKind.Object && info.Value.TryGetProperty("cost_sensors", out JsonElement sensors) ? sensors : default;
            var dashboard = Dashboard.Parse(prefs.Value, costSensors);
            if (!dashboard.HasSources)
                return new Error("energy.not_configured", "The Home Assistant energy dashboard has no sources.", ErrorType.NotFound);

            Result<Totals> current = await TotalsAsync(dashboard, period.Value.Current, cancellationToken);
            if (current.IsFailure)
                return current.Error;
            Usage usage = Build(dashboard, period.Value.Current, current.Value);
            if (period.Value.Previous is { } previousWindow)
            {
                Result<Totals> previous = await TotalsAsync(dashboard, previousWindow, cancellationToken);
                if (previous.IsFailure)
                    return previous.Error;
                usage = usage with { Previous = Build(dashboard, previousWindow, previous.Value) };
            }

            return new ToolOutput(JsonSerializer.Serialize(usage, _json));
        }

        private async Task<TimeZoneInfo> ZoneAsync(OwnerId owner, CancellationToken cancellationToken)
        {
            Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
            return profile.IsSuccess && TimeZoneInfo.TryFindSystemTimeZoneById(profile.Value.TimeZone, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Utc;
        }

        // One request per period: the change of every statistic the dashboard uses, summed over the rows.
        private async Task<Result<Totals>> TotalsAsync(Dashboard dashboard, EnergyWindow window, CancellationToken cancellationToken)
        {
            bool daily = (window.LocalEnd - window.LocalStart).TotalDays > MaxHourlyDays;
            Result<JsonElement> statistics = await commands.CommandAsync("recorder/statistics_during_period", new JsonObject
            {
                ["start_time"] = window.Start.ToString("O", CultureInfo.InvariantCulture),
                ["end_time"] = window.End.ToString("O", CultureInfo.InvariantCulture),
                ["statistic_ids"] = new JsonArray([.. dashboard.StatisticIds.Select(id => (JsonNode)id)]),
                ["period"] = daily ? "day" : "hour",
                ["types"] = new JsonArray("change"),
                ["units"] = new JsonObject { ["energy"] = "kWh", ["volume"] = "m³" },
            }, cancellationToken);
            if (statistics.IsFailure)
                return statistics.Error;

            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            if (statistics.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty statistic in statistics.Value.EnumerateObject())
                {
                    totals[statistic.Name] = Array(statistic.Value, "")
                        .Select(row => row.TryGetProperty("change", out JsonElement change) && change.ValueKind == JsonValueKind.Number ? change.GetDouble() : 0)
                        .Sum();
                }
            }

            return new Totals(totals, daily);
        }

        private Usage Build(Dashboard dashboard, EnergyWindow window, Totals totals)
        {
            double? Sum(List<string> ids) => ids.Count == 0 ? null : ids.Sum(id => totals.Values.GetValueOrDefault(id));

            double? gridImport = Sum(dashboard.GridImport);
            double? gridExport = Sum(dashboard.GridExport);
            double? solar = Sum(dashboard.Solar);
            double? charge = Sum(dashboard.BatteryCharge);
            double? discharge = Sum(dashboard.BatteryDischarge);
            bool electricity = gridImport is not null || solar is not null || discharge is not null;
            double? consumption = electricity ? (gridImport ?? 0) + (solar ?? 0) + (discharge ?? 0) - (gridExport ?? 0) - (charge ?? 0) : null;

            HaSnapshot? snapshot = cache.Current;
            Utility? gas = dashboard.Gas.Count == 0 ? null : new Utility(Round(Sum(dashboard.Gas)), GasUnit(snapshot, dashboard.Gas[0]), Money(Sum(dashboard.GasCost)));
            Utility? water = dashboard.Water.Count == 0 ? null : new Utility(Round(Sum(dashboard.Water)), "m³", Money(Sum(dashboard.WaterCost)));
            Device[]? devices = dashboard.Devices.Count == 0
                ? null
                : [.. dashboard.Devices
                    .Select(device => new Device(device.Name ?? snapshot?.States.GetValueOrDefault(device.Statistic)?.FriendlyName ?? device.Statistic, Round(totals.Values.GetValueOrDefault(device.Statistic)) ?? 0))
                    .OrderByDescending(device => device.Value)
                    .ThenBy(device => device.Name, StringComparer.Ordinal)
                    .Take(TopDevices)];

            return new Usage(
                new Span(Local(window.LocalStart), Local(window.LocalEnd), totals.Daily ? "day" : "hour"),
                "kWh",
                snapshot?.Currency,
                Round(gridImport), Round(gridExport), Round(solar), Round(charge), Round(discharge), Round(consumption),
                Money(Sum(dashboard.Cost)), Money(Sum(dashboard.Compensation)),
                gas, water, devices, null);
        }

        // Gas is reported in kWh when HA's statistic is an energy statistic, else in m³ (the volume unit requested).
        private static string GasUnit(HaSnapshot? snapshot, string statistic) =>
            snapshot?.States.GetValueOrDefault(statistic)?.Attributes is { ValueKind: JsonValueKind.Object } attributes
            && Text(attributes, "unit_of_measurement") is { } unit && _energyUnits.Contains(unit)
                ? "kWh"
                : "m³";

        private static string Local(DateTime time) => time.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

        private static double? Round(double? value) => value is { } number ? Math.Round(number, 1, MidpointRounding.AwayFromZero) : null;

        private static double? Money(double? value) => value is { } number ? Math.Round(number, 2, MidpointRounding.AwayFromZero) : null;
    }

    private sealed record Totals(Dictionary<string, double> Values, bool Daily);

    private sealed record Span(string From, string To, string Resolution);

    private sealed record Utility(double? Value, string Unit, double? Cost);

    private sealed record Device(string Name, double Value);

    private sealed record Usage(
        Span Period,
        string Unit,
        string? Currency,
        double? GridImport,
        double? GridExport,
        double? Solar,
        double? BatteryCharge,
        double? BatteryDischarge,
        double? Consumption,
        double? Cost,
        double? Compensation,
        Utility? Gas,
        Utility? Water,
        Device[]? TopDevices,
        Usage? Previous);

    // An array property, or the element itself when name is empty; nothing for anything else.
    private static JsonElement[] Array(JsonElement element, string name)
    {
        JsonElement array = name.Length == 0 ? element : element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : default;
        return array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
