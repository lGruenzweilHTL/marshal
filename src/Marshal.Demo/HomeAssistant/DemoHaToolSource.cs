using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.HomeAssistant;

namespace Marshal.Demo.HomeAssistant;

/// <summary>
/// The home control tools of Home Assistant's MCP server, simulated on the demo home: the same ids
/// (<c>ha.intent__HassTurnOn</c>, ..., <c>ha.homeassistant__GetLiveContext</c>), HA's intent slots as arguments, and
/// <c>confirm</c> as default tier like every MCP tool, so <c>risk.yaml</c> decides as for the real server. Offered
/// only while no MCP server with namespace <c>ha</c> is configured.
/// </summary>
public sealed class DemoHaToolSource(DemoWorld world, IMcpServerCatalog servers) : IToolSource
{
    public const string Namespace = "ha";

    private const string TargetSlots = """
        "name":{"type":"string","description":"Name of a device or entity."},
        "area":{"type":"string","description":"Name of an area."},
        "floor":{"type":"string","description":"Name of a floor."},
        "domain":{"type":"array","items":{"type":"string"},"description":"Domains, e.g. light or switch."},
        "device_class":{"type":"array","items":{"type":"string"}}
        """;

    private readonly IReadOnlyList<ITool> _tools =
    [
        new IntentTool(world, "HassTurnOn", "Turns on/opens/presses a device or entity. For locks, this performs a 'lock' action.", Targeted("", null)),
        new IntentTool(world, "HassTurnOff", "Turns off/closes a device or entity. For locks, this performs an 'unlock' action.", Targeted("", null)),
        new IntentTool(world, "HassLightSet", "Sets the brightness percentage of a light.", Targeted(""","brightness":{"type":"integer","minimum":0,"maximum":100}""", "brightness")),
        new IntentTool(world, "HassClimateSetTemperature", "Sets the target temperature of a climate device or entity.", """{"type":"object","properties":{"name":{"type":"string"},"area":{"type":"string"},"floor":{"type":"string"},"temperature":{"type":"number"}},"required":["temperature"]}"""),
        new LiveContextTool(world),
    ];

    public string Name => Namespace;

    public bool IsBuiltIn => false;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("ha", "Home control: lights, switches, covers, climate, locks and other devices and their state")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(servers.Servers.Any(server => server.Namespace == Namespace) ? [] : _tools);

    private static string Targeted(string extra, string? required) =>
        $"{{\"type\":\"object\",\"properties\":{{{TargetSlots}{extra}}}{(required is null ? "" : $",\"required\":[\"{required}\"]")}}}";

    private static ToolDescriptor Descriptor(string name, string description, string schema) => new()
    {
        Id = new ToolId(Namespace, name),
        Description = description,
        ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        DefaultTier = RiskTier.Confirm,
    };

    private sealed class IntentTool(DemoWorld world, string intent, string description, string schema) : ITool
    {
        public ToolDescriptor Descriptor { get; } = Descriptor($"intent__{intent}", description, schema);

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public Result Validate(JsonElement arguments) =>
            arguments.ValueKind is JsonValueKind.Object or JsonValueKind.Undefined ? Result.Success() : new Error("ha.invalid_arguments", "Arguments must be an object.", ErrorType.Validation);

        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            JsonObject data = invocation.Arguments.ValueKind == JsonValueKind.Object ? JsonNode.Parse(invocation.Arguments.GetRawText())!.AsObject() : [];
            JsonElement response = DemoIntents.Handle(world.Home, world.Time.GetUtcNow(), intent, data, invocation.Context.DeviceId);
            return Task.FromResult<Result<ToolOutput>>(new ToolOutput(response.GetRawText()));
        }
    }

    private sealed class LiveContextTool(DemoWorld world) : ITool
    {
        public ToolDescriptor Descriptor { get; } = Descriptor("homeassistant__GetLiveContext", "Provides real-time information about the CURRENT state, value, or mode of devices, sensors, entities, or areas.", """{"type":"object","properties":{}}""");

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public Result Validate(JsonElement arguments) => Result.Success();

        // HA's live context: a YAML-like list of the exposed entities with state, area and a few attributes.
        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            HaSnapshot snapshot = world.Home.Snapshot;
            var text = new StringBuilder("Live Context: An overview of the areas and the devices in this smart home:\n");
            foreach (HaEntity entity in snapshot.Entities.Values.Where(entity => entity.Exposed).OrderBy(entity => entity.EntityId, StringComparer.Ordinal))
            {
                HaState state = snapshot.States[entity.EntityId];
                text.Append(CultureInfo.InvariantCulture, $"- names: {entity.Name}\n  domain: {entity.Domain}\n  state: '{state.State}'\n");
                if (entity.AreaId is { } area && snapshot.Areas.GetValueOrDefault(area) is { } found)
                    text.Append(CultureInfo.InvariantCulture, $"  areas: {found.Name}\n");
                foreach (string attribute in new[] { "brightness", "current_temperature", "temperature", "unit_of_measurement", "volume_level", "media_title" })
                {
                    if (state.Attributes.TryGetProperty(attribute, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
                        text.Append(CultureInfo.InvariantCulture, $"  {attribute}: {value}\n");
                }
            }

            return Task.FromResult<Result<ToolOutput>>(new ToolOutput(text.ToString()));
        }
    }
}
