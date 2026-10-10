namespace Marshal.Abstractions.Media;

/// <summary>Links to items in the Jellyfin web client, built only from the configured <c>media.jellyfinUrl</c>.</summary>
public interface IJellyfinLinks
{
    /// <summary>The item page for <paramref name="itemId"/>, or null without <c>jellyfinUrl</c> or item id.</summary>
    string? ForItem(string? itemId);
}
