using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Marshal.Core.Mcp;

/// <summary>
/// Pins MCP tool definitions (trust on first use). A server's first discovery, when it has no pins, approves every
/// tool. Later a tool without a pin or with a fingerprint other than the approved one waits for an admin; a tool
/// that changes back to its approved definition is approved again. The decision state is kept in memory as well,
/// so <see cref="McpTool.PinState"/> reflects a decision from the next turn on without re-listing.
/// </summary>
public sealed partial class McpToolPinner(
    IMcpToolPins pins,
    TimeProvider time,
    IInstanceOwner? owner,
    INotificationSender? notifications,
    IUserProfileProvider? profiles,
    ILogger<McpToolPinner> logger)
{
    private readonly ConcurrentDictionary<(string Server, string Tool), ToolPinState> _states = new();

    /// <summary>Current decision for a tool; tools this pinner has not seen are pending.</summary>
    public ToolPinState StateOf(string serverId, string toolName) =>
        _states.GetValueOrDefault((serverId, toolName), ToolPinState.Pending);

    /// <summary>Compares a discovery with the pins, stores changes and notifies the owner about newly waiting tools.</summary>
    public async Task ApplyAsync(string serverId, IReadOnlyList<Tool> tools, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        var existing = (await pins.GetAsync(serverId, cancellationToken)).ToDictionary(pin => pin.ToolName, StringComparer.Ordinal);
        bool firstDiscovery = existing.Count == 0;
        List<McpToolPin> changed = [];
        int waiting = 0;
        foreach (Tool tool in tools)
        {
            McpToolDefinition definition = McpToolFingerprint.Of(tool);
            existing.TryGetValue(tool.Name, out McpToolPin? pin);
            McpToolPin? next = Next(serverId, tool.Name, pin, definition, firstDiscovery, now);
            if (next is not null)
            {
                changed.Add(next);
                if (next.State == ToolPinState.Pending)
                    waiting++;
            }

            _states[(serverId, tool.Name)] = (next ?? pin)!.State;
        }

        await pins.SaveAsync(changed, cancellationToken);
        if (waiting > 0)
            await NotifyAsync(serverId, waiting, cancellationToken);
    }

    /// <summary>Approves or rejects a waiting tool; effective for the next turn.</summary>
    public async Task<Result<McpToolPin>> DecideAsync(string serverId, string toolName, bool approve, OwnerId actor, CancellationToken cancellationToken)
    {
        Result<McpToolPin> decided = await pins.DecideAsync(serverId, toolName, approve, actor, time.GetUtcNow(), cancellationToken);
        if (decided.IsSuccess)
            _states[(serverId, toolName)] = decided.Value.State;

        return decided;
    }

    // Null when the stored pin stays as it is.
    private static McpToolPin? Next(string serverId, string toolName, McpToolPin? pin, McpToolDefinition definition, bool firstDiscovery, DateTimeOffset now)
    {
        if (pin is null)
        {
            return firstDiscovery
                ? new McpToolPin(serverId, toolName, definition.Fingerprint, ToolPinState.Approved, definition.Fingerprint, null, definition.Json, now)
                : new McpToolPin(serverId, toolName, definition.Fingerprint, ToolPinState.Pending, null, null, definition.Json, now);
        }

        if (definition.Fingerprint == pin.ApprovedFingerprint)
        {
            return pin.State == ToolPinState.Approved && pin.Fingerprint == definition.Fingerprint
                ? null
                : pin with { Fingerprint = definition.Fingerprint, State = ToolPinState.Approved, PreviousDefinitionJson = null, CurrentDefinitionJson = definition.Json, FirstSeen = now, DecidedAt = null, DecidedBy = null };
        }

        if (definition.Fingerprint == pin.Fingerprint)
            return null;

        // The previous definition shown to the admin is the approved one: the current JSON of an approved pin, or
        // the previous JSON kept while an earlier change waited or was rejected.
        string? approved = pin.State == ToolPinState.Approved ? pin.CurrentDefinitionJson : pin.PreviousDefinitionJson;
        return pin with { Fingerprint = definition.Fingerprint, State = ToolPinState.Pending, PreviousDefinitionJson = approved, CurrentDefinitionJson = definition.Json, FirstSeen = now, DecidedAt = null, DecidedBy = null };
    }

    // Counts only: tool names and descriptions come from the server and never go into a notification.
    private async Task NotifyAsync(string serverId, int waiting, CancellationToken cancellationToken)
    {
        if (owner is null || notifications is null)
            return;

        try
        {
            OwnerId instanceOwner = await owner.GetAsync(cancellationToken);
            string language = profiles is null ? "en" : (await profiles.GetAsync(instanceOwner, cancellationToken)) is { IsSuccess: true } profile ? profile.Value.Language : "en";
            string text = language == "de"
                ? waiting == 1 ? $"Ein Werkzeug des MCP-Servers {serverId} wartet auf Freigabe." : $"{waiting} Werkzeuge des MCP-Servers {serverId} warten auf Freigabe."
                : waiting == 1 ? $"A tool of the MCP server {serverId} waits for approval." : $"{waiting} tools of the MCP server {serverId} wait for approval.";
            Result<Guid> sent = await notifications.SendAsync(new NotificationRequest
            {
                Owner = instanceOwner,
                Source = $"{NotificationRequest.SystemPrefix}mcp",
                Priority = NotificationPriority.Normal,
                Text = text,
                Link = "system/config",
                Tag = $"mcp-pins-{serverId}",
            }, cancellationToken);
            if (sent.IsFailure)
                LogNotifyFailed(serverId, sent.Error.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotifyFailed(serverId, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifying about waiting tools of MCP server {Server} failed: {Reason}")]
    private partial void LogNotifyFailed(string server, string reason);
}
