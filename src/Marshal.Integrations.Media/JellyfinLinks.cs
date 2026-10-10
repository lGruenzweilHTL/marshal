using Marshal.Abstractions.Media;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Media;

/// <summary>Item pages of the Jellyfin web client: <c>&lt;jellyfinUrl&gt;/web/#/details?id=&lt;item&gt;</c>.</summary>
public sealed class JellyfinLinks(IOptions<MediaOptions> options) : IJellyfinLinks
{
    public string? ForItem(string? itemId) =>
        options.Value.JellyfinUrl is { } root && !string.IsNullOrWhiteSpace(itemId)
            ? $"{root.ToString().TrimEnd('/')}/web/#/details?id={Uri.EscapeDataString(itemId.Trim())}"
            : null;
}
