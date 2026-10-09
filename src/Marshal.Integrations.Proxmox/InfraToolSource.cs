using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Proxmox;

/// <summary>
/// The <c>infra.*</c> tools over Proxmox VE: overview, guests, storage and tasks. All safe and trusted (names and
/// statuses come from the owner's cluster or PVE itself; notes and descriptions are never returned). No tools while
/// <c>proxmox.url</c> is unset.
/// </summary>
public sealed class InfraToolSource(IProxmoxClient client, IOptions<ProxmoxOptions> options) : IToolSource
{
    /// <summary>Storage use above this share counts as full in the overview.</summary>
    internal const double FullStorage = 0.8;

    /// <summary>Most tasks <c>infra.tasks</c> returns.</summary>
    internal const int TaskLimit = 50;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private readonly IReadOnlyList<ITool> _tools = [new OverviewTool(client), new GuestsTool(client), new StorageTool(client), new TasksTool(client)];

    public string Name => "infra";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(options.Value.IsConfigured ? _tools : []);

    private static ToolDescriptor Descriptor(string name, string description, string schema) => new()
    {
        Id = new ToolId("infra", name),
        Description = description,
        ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    private static ToolOutput Json(object value) => new(JsonSerializer.Serialize(value, _json));

    private static double Percent(double fraction) => Math.Round(fraction * 100, 1);

    private static double Percent(long used, long total) => total <= 0 ? 0 : Math.Round(100.0 * used / total, 1);

    private static double Gigabytes(long bytes) => Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1);

    private static double Hours(long seconds) => Math.Round(seconds / 3600.0, 1);

    private static string? Text(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Error Invalid(string message) => new("infra.invalid_arguments", message, ErrorType.Validation);

    private static IEnumerable<ProxmoxGuest> Guests(ProxmoxResources resources) => resources.Guests.Where(guest => !guest.Template);

    private sealed class OverviewTool(IProxmoxClient client) : ITool
    {
        public ToolDescriptor Descriptor { get; } = InfraToolSource.Descriptor("overview",
            "Overview of the Proxmox cluster: nodes with CPU, memory and uptime, how many VMs and containers run, and storages over 80 % full.",
            """{"type":"object","properties":{}}""");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<ProxmoxResources> resources = await client.GetResourcesAsync(cancellationToken);
            if (resources.IsFailure)
                return resources.Error;

            ProxmoxResources cluster = resources.Value;
            return Json(new
            {
                nodes = cluster.Nodes.OrderBy(node => node.Name, StringComparer.Ordinal).Select(node => new
                {
                    name = node.Name,
                    online = node.Online,
                    cpuPercent = Percent(node.Cpu),
                    memoryPercent = Percent(node.Memory, node.MaxMemory),
                    uptimeHours = Hours(node.Uptime),
                }),
                guests = Guests(cluster).GroupBy(guest => guest.Status).OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count()),
                fullStorages = StorageTool.Distinct(cluster).Where(storage => storage.Total > 0 && (double)storage.Used / storage.Total > FullStorage)
                    .Select(storage => new { name = storage.Name, node = storage.Shared ? null : storage.Node, percentUsed = Percent(storage.Used, storage.Total) }),
            });
        }
    }

    private sealed class GuestsTool(IProxmoxClient client) : ITool
    {
        public ToolDescriptor Descriptor { get; } = InfraToolSource.Descriptor("guests",
            "Lists the VMs and containers on Proxmox with id, name, node, status, CPU, memory and uptime, optionally only on one node or with one status.",
            """
            {"type":"object","properties":{
              "node":{"type":"string","description":"Node name."},
              "status":{"type":"string","enum":["running","stopped","paused"]}}}
            """);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            string? status = Text(invocation.Arguments, "status");
            if (status is not (null or "running" or "stopped" or "paused"))
                return Invalid($"Unknown status '{status}'; use running, stopped or paused.");

            Result<ProxmoxResources> resources = await client.GetResourcesAsync(cancellationToken);
            if (resources.IsFailure)
                return resources.Error;

            string? node = Text(invocation.Arguments, "node");
            return Json(Guests(resources.Value)
                .Where(guest => node is null || string.Equals(guest.Node, node, StringComparison.OrdinalIgnoreCase))
                .Where(guest => status is null || guest.Status == status)
                .OrderBy(guest => guest.VmId)
                .Select(guest => new
                {
                    id = guest.VmId,
                    name = guest.Name,
                    type = guest.Type == "lxc" ? "container" : "vm",
                    node = guest.Node,
                    status = guest.Status,
                    cpuPercent = Percent(guest.Cpu),
                    memoryGb = Gigabytes(guest.Memory),
                    maxMemoryGb = Gigabytes(guest.MaxMemory),
                    uptimeHours = Hours(guest.Uptime),
                }));
        }
    }

    private sealed class StorageTool(IProxmoxClient client) : ITool
    {
        public ToolDescriptor Descriptor { get; } = InfraToolSource.Descriptor("storage",
            "Lists the Proxmox storages with type, used and total size in GB and percent used. Shared storages appear once.",
            """{"type":"object","properties":{}}""");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<ProxmoxResources> resources = await client.GetResourcesAsync(cancellationToken);
            if (resources.IsFailure)
                return resources.Error;

            return Json(Distinct(resources.Value).Select(storage => new
            {
                name = storage.Name,
                node = storage.Shared ? null : storage.Node,
                type = storage.Type,
                shared = storage.Shared,
                usedGb = Gigabytes(storage.Used),
                totalGb = Gigabytes(storage.Total),
                percentUsed = Percent(storage.Used, storage.Total),
            }));
        }

        // cluster/resources lists a shared storage once per node.
        internal static IEnumerable<ProxmoxStorage> Distinct(ProxmoxResources resources) =>
            resources.Storages
                .GroupBy(storage => storage.Shared ? storage.Name : $"{storage.Node}/{storage.Name}", StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(storage => storage.Name, StringComparer.Ordinal)
                .ThenBy(storage => storage.Node, StringComparer.Ordinal);
    }

    private sealed class TasksTool(IProxmoxClient client) : ITool
    {
        public ToolDescriptor Descriptor { get; } = InfraToolSource.Descriptor("tasks",
            "Lists the newest Proxmox tasks (backups, migrations, starts, ...) with type, guest id, node, user, start and end time and status, at most 50. failedOnly keeps tasks that ended with a status other than OK.",
            """
            {"type":"object","properties":{
              "failedOnly":{"type":"boolean"},
              "since":{"type":"string","format":"date-time","description":"Only tasks started after this time (ISO 8601)."}}}
            """);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            DateTimeOffset? since = null;
            if (Text(invocation.Arguments, "since") is { } sinceText)
            {
                if (!DateTimeOffset.TryParse(sinceText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
                    return Invalid($"since '{sinceText}' is not an ISO 8601 time.");
                since = parsed;
            }

            bool failedOnly = invocation.Arguments.ValueKind == JsonValueKind.Object
                && invocation.Arguments.TryGetProperty("failedOnly", out JsonElement failed) && failed.ValueKind == JsonValueKind.True;

            Result<IReadOnlyList<ProxmoxTask>> tasks = await client.GetTasksAsync(cancellationToken);
            if (tasks.IsFailure)
                return tasks.Error;

            return Json(tasks.Value
                .Where(task => since is null || task.Start > since)
                .Where(task => !failedOnly || task.Status is { } status && status != "OK")
                .OrderByDescending(task => task.Start)
                .Take(TaskLimit)
                .Select(task => new
                {
                    type = task.Type,
                    guestId = task.GuestId,
                    node = task.Node,
                    user = task.User,
                    start = task.Start.ToString("O", CultureInfo.InvariantCulture),
                    end = task.End?.ToString("O", CultureInfo.InvariantCulture),
                    status = task.Status ?? "running",
                }));
        }
    }
}
