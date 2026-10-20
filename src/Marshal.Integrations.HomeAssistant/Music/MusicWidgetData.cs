using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Widgets;

namespace Marshal.Integrations.HomeAssistant.Music;

/// <summary>
/// The <c>music</c> chat widget after <c>music.queue</c> and <c>music.play</c>: the speaker and its queue (state, current
/// and next item, item count, shuffle, repeat). Names come from music providers and are shown as text only.
/// </summary>
public sealed class MusicWidgetData(IMusicPlayer player, ISpeakerDirectory speakers) : IWidgetDataProvider
{
    public const string WidgetKind = "music";

    public string Kind => WidgetKind;

    public async Task<JsonElement?> GetAsync(WidgetRequest request, CancellationToken cancellationToken)
    {
        string? name = request.Arguments.ValueKind == JsonValueKind.Object && request.Arguments.TryGetProperty("speaker", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        MusicSpeaker? speaker = name is null ? speakers.ForSatellite(request.Context.SatelliteId) : speakers.ByName(name);
        if (speaker is null)
            return null;

        Result<MusicQueueState> queue = await player.GetQueueAsync(speaker, cancellationToken);
        return queue.IsFailure ? null : Data(speaker.Name, queue.Value);
    }

    /// <summary>The widget's data shape, shared with the music endpoint the widget refreshes from.</summary>
    public static JsonElement Data(string speaker, MusicQueueState queue) => JsonSerializer.SerializeToElement(new
    {
        speaker,
        state = queue.State,
        current = Item(queue.Current),
        next = Item(queue.Next),
        items = queue.Items,
        shuffle = queue.Shuffle,
        repeat = queue.Repeat,
    }, JsonSerializerOptions.Web);

    private static object? Item(MusicItem? item) => item is null ? null : new { name = item.Name, artists = item.Artists, album = item.Album };
}
