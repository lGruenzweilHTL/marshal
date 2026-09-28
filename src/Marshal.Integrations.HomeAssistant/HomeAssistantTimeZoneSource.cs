using Marshal.Abstractions;
using Marshal.Abstractions.Identity;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// The instance time zone: <c>instance.timeZone</c> when configured, else HA's configured time zone (from the
/// cache, or <c>/api/config</c> before the cache is loaded), else UTC. <paramref name="client"/> is null when
/// Home Assistant is not configured.
/// </summary>
public sealed class HomeAssistantTimeZoneSource(string? configured, IHomeAssistantCache cache, IHomeAssistantClient? client) : IInstanceTimeZone
{
    public const string Fallback = "UTC";

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        if (configured is not null)
            return configured;

        if (cache.Current?.TimeZone is { } cached && IsKnown(cached))
            return cached;

        if (client is null)
            return Fallback;

        Result<string> fromHa = await client.GetTimeZoneAsync(cancellationToken);
        return fromHa.IsSuccess && IsKnown(fromHa.Value) ? fromHa.Value : Fallback;
    }

    private static bool IsKnown(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
