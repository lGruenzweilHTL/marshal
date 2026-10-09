using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Displays;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Displays;

/// <summary>
/// Plays a display link with <c>media_player.play_media</c>. HA's DLNA renderer integration maps the content type
/// (<c>image</c>, <c>video</c>) to the UPnP class and builds the DIDL metadata itself.
/// </summary>
public sealed class HomeAssistantDisplayPlayer(IHomeAssistantClient client, IOptions<HomeAssistantOptions> options) : IDisplayPlayer
{
    public bool IsAvailable => options.Value.Url is not null;

    public async Task<Result> PlayAsync(DisplaySurface display, string url, DisplayMediaType type, CancellationToken cancellationToken)
    {
        Result<JsonElement> called = await client.CallServiceAsync("media_player", "play_media", new JsonObject
        {
            ["entity_id"] = display.Entity,
            ["media_content_id"] = url,
            ["media_content_type"] = type == DisplayMediaType.Video ? "video" : "image",
        }, cancellationToken);

        if (called.IsSuccess)
            return Result.Success();

        return called.Error.Code == "ha.http_error"
            ? new Error("display.failed", $"The display '{display.Name}' did not accept the media (is it switched on?). {called.Error.Message}", ErrorType.Unavailable)
            : called.Error;
    }
}
