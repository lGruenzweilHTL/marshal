using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Home Assistant's REST API for Marshal's own (non-model) calls.
/// </summary>
public interface IHomeAssistantClient
{
    /// <summary><c>POST /api/intent/handle</c>; returns HA's intent response.</summary>
    Task<Result<JsonElement>> HandleIntentAsync(HaIntentRequest request, CancellationToken cancellationToken);

    /// <summary>HA's configured IANA time zone (<c>GET /api/config</c>).</summary>
    Task<Result<string>> GetTimeZoneAsync(CancellationToken cancellationToken);

    /// <summary>A read-only REST call (<c>GET /api/...</c>, path relative to the HA URL).</summary>
    Task<Result<JsonElement>> GetAsync(string path, CancellationToken cancellationToken);

    /// <summary><c>POST /api/services/&lt;domain&gt;/&lt;service&gt;</c> with <paramref name="data"/>.</summary>
    Task<Result<JsonElement>> CallServiceAsync(string domain, string service, JsonObject data, CancellationToken cancellationToken);
}

/// <summary>An intent for HA's intent handling, bound to the device (and satellite) it is for.</summary>
public sealed record HaIntentRequest(string Name, JsonObject Data, string Language, string? DeviceId, string? SatelliteId);
