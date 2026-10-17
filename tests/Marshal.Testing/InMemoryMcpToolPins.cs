using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;

namespace Marshal.Testing;

/// <summary>
/// MCP tool pins in a dictionary, with the decision rules of the EF store.
/// </summary>
public sealed class InMemoryMcpToolPins : IMcpToolPins
{
    public ConcurrentDictionary<(string Server, string Tool), McpToolPin> Pins { get; } = new();

    public Task<IReadOnlyList<McpToolPin>> GetAsync(string serverId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<McpToolPin>>([.. Pins.Values.Where(pin => pin.ServerId == serverId).OrderBy(pin => pin.ToolName, StringComparer.Ordinal)]);

    public Task<IReadOnlyList<McpToolPin>> ListPendingAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<McpToolPin>>([.. Pins.Values.Where(pin => pin.State == ToolPinState.Pending)
            .OrderBy(pin => pin.ServerId, StringComparer.Ordinal).ThenBy(pin => pin.ToolName, StringComparer.Ordinal)]);

    public Task SaveAsync(IReadOnlyList<McpToolPin> pins, CancellationToken cancellationToken)
    {
        foreach (McpToolPin pin in pins)
            Pins[(pin.ServerId, pin.ToolName)] = pin;

        return Task.CompletedTask;
    }

    public Task<Result<McpToolPin>> DecideAsync(string serverId, string toolName, bool approve, OwnerId actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (!Pins.TryGetValue((serverId, toolName), out McpToolPin? pin))
            return Task.FromResult<Result<McpToolPin>>(new Error("mcp.pin_not_found", "No such pin.", ErrorType.NotFound));

        if (pin.State != ToolPinState.Pending)
            return Task.FromResult<Result<McpToolPin>>(new Error("mcp.pin_not_pending", "Not pending.", ErrorType.Conflict));

        McpToolPin decided = approve
            ? pin with { State = ToolPinState.Approved, ApprovedFingerprint = pin.Fingerprint, PreviousDefinitionJson = null, DecidedAt = at, DecidedBy = actor }
            : pin with { State = ToolPinState.Rejected, DecidedAt = at, DecidedBy = actor };
        Pins[(serverId, toolName)] = decided;
        return Task.FromResult<Result<McpToolPin>>(decided);
    }
}
