using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Websocket commands on the cache's connection, for the few operations HA offers only there (e.g. updating a
/// calendar event).
/// </summary>
public interface IHomeAssistantCommands
{
    /// <summary>Sends a command; fails with <c>ha.unavailable</c> while disconnected, <c>ha.command_failed</c> when HA rejects it.</summary>
    Task<Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken);
}
