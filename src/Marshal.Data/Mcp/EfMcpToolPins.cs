using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Mcp;

/// <summary>
/// <see cref="IMcpToolPins"/> on <c>mcp_tool_pins</c>. The table has no owner filter, so the MCP client reads it
/// without an execution context.
/// </summary>
public sealed class EfMcpToolPins(IDbContextFactory<MarshalDbContext> contexts) : IMcpToolPins
{
    public async Task<IReadOnlyList<McpToolPin>> GetAsync(string serverId, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<McpToolPinRow> rows = await db.McpToolPins.AsNoTracking().Where(row => row.ServerId == serverId).ToListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.ToolName, StringComparer.Ordinal).Select(ToPin)];
    }

    public async Task<IReadOnlyList<McpToolPin>> ListPendingAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<McpToolPinRow> rows = await db.McpToolPins.AsNoTracking().Where(row => row.State == ToolPinState.Pending).ToListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.ServerId, StringComparer.Ordinal).ThenBy(row => row.ToolName, StringComparer.Ordinal).Select(ToPin)];
    }

    public async Task SaveAsync(IReadOnlyList<McpToolPin> pins, CancellationToken cancellationToken)
    {
        if (pins.Count == 0)
            return;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        foreach (IGrouping<string, McpToolPin> server in pins.GroupBy(pin => pin.ServerId, StringComparer.Ordinal))
        {
            string[] names = [.. server.Select(pin => pin.ToolName)];
            Dictionary<string, McpToolPinRow> existing = await db.McpToolPins
                .Where(row => row.ServerId == server.Key && names.Contains(row.ToolName))
                .ToDictionaryAsync(row => row.ToolName, StringComparer.Ordinal, cancellationToken);
            foreach (McpToolPin pin in server)
            {
                if (existing.TryGetValue(pin.ToolName, out McpToolPinRow? row))
                    Copy(pin, row);
                else
                    db.McpToolPins.Add(Copy(pin, new McpToolPinRow { ServerId = pin.ServerId, ToolName = pin.ToolName, Fingerprint = pin.Fingerprint, State = pin.State, CurrentDefinitionJson = pin.CurrentDefinitionJson, FirstSeen = pin.FirstSeen }));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<McpToolPin>> DecideAsync(string serverId, string toolName, bool approve, OwnerId actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        McpToolPinRow? row = await db.McpToolPins.SingleOrDefaultAsync(row => row.ServerId == serverId && row.ToolName == toolName, cancellationToken);
        if (row is null)
            return new Error("mcp.pin_not_found", $"No tool {toolName} of MCP server {serverId} is pinned.", ErrorType.NotFound);

        if (row.State != ToolPinState.Pending)
            return new Error("mcp.pin_not_pending", $"Tool {toolName} of MCP server {serverId} is not waiting for approval.", ErrorType.Conflict);

        row.State = approve ? ToolPinState.Approved : ToolPinState.Rejected;
        if (approve)
        {
            row.ApprovedFingerprint = row.Fingerprint;
            row.PreviousDefinitionJson = null;
        }

        row.DecidedAt = at;
        row.DecidedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
        return ToPin(row);
    }

    private static McpToolPinRow Copy(McpToolPin pin, McpToolPinRow row)
    {
        row.Fingerprint = pin.Fingerprint;
        row.State = pin.State;
        row.ApprovedFingerprint = pin.ApprovedFingerprint;
        row.PreviousDefinitionJson = pin.PreviousDefinitionJson;
        row.CurrentDefinitionJson = pin.CurrentDefinitionJson;
        row.FirstSeen = pin.FirstSeen;
        row.DecidedAt = pin.DecidedAt;
        row.DecidedBy = pin.DecidedBy;
        return row;
    }

    private static McpToolPin ToPin(McpToolPinRow row) => new(
        row.ServerId, row.ToolName, row.Fingerprint, row.State, row.ApprovedFingerprint, row.PreviousDefinitionJson, row.CurrentDefinitionJson, row.FirstSeen, row.DecidedAt, row.DecidedBy);
}
