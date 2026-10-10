using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Music;

namespace Marshal.Integrations.HomeAssistant.Music;

/// <summary>
/// Music Assistant through Home Assistant's websocket <c>call_service</c>: the <c>music_assistant</c> services for
/// search, play, queue and transfer, the generic <c>media_player</c> services for controls and grouping. The config
/// entry id that <c>search</c> needs is read once from <c>config_entries/get</c> and read again after a failed call
/// (the entry changes when the integration is set up again). Responses are parsed leniently: unknown fields are
/// ignored and missing ones are null.
/// </summary>
public sealed class HomeAssistantMusicPlayer(IHomeAssistantCommands commands, IHomeAssistantCache cache) : IMusicPlayer
{
    private static readonly (string Key, MusicMediaType Type)[] _resultLists =
    [
        ("tracks", MusicMediaType.Track),
        ("albums", MusicMediaType.Album),
        ("artists", MusicMediaType.Artist),
        ("playlists", MusicMediaType.Playlist),
        ("radio", MusicMediaType.Radio),
    ];

    private volatile string? _configEntryId;

    public async Task<Result<IReadOnlyList<MusicItem>>> SearchAsync(string query, MusicMediaType? type, int limit, CancellationToken cancellationToken)
    {
        Result<string> entry = await ConfigEntryAsync(cancellationToken);
        if (entry.IsFailure)
            return entry.Error;

        var data = new JsonObject { ["config_entry_id"] = entry.Value, ["name"] = query, ["limit"] = limit };
        if (type is { } only)
            data["media_type"] = new JsonArray(TypeName(only));

        Result<JsonElement> response = await CallAsync("music_assistant", "search", target: null, data, returnResponse: true, "the search", cancellationToken);
        if (response.IsFailure)
            return response.Error;

        var items = new List<MusicItem>();
        foreach ((string key, MusicMediaType listType) in _resultLists)
        {
            if (type is not null && type != listType)
                continue;
            items.AddRange(Array(response.Value, key).Take(limit).Select(item => Item(item, listType)).OfType<MusicItem>());
        }

        return items;
    }

    public async Task<Result> PlayAsync(MusicSpeaker speaker, MusicPlayRequest request, CancellationToken cancellationToken)
    {
        var data = new JsonObject { ["media_id"] = request.Item, ["enqueue"] = EnqueueName(request.Enqueue) };
        if (request.Type is { } type)
            data["media_type"] = TypeName(type);
        if (request.Radio)
            data["radio_mode"] = true;

        return await SendAsync("music_assistant", "play_media", speaker, data, cancellationToken);
    }

    public async Task<Result<MusicQueueState>> GetQueueAsync(MusicSpeaker speaker, CancellationToken cancellationToken)
    {
        Result<JsonElement> response = await CallAsync("music_assistant", "get_queue", speaker.Entity, [], returnResponse: true, $"speaker '{speaker.Name}'", cancellationToken);
        if (response.IsFailure)
            return response.Error;

        JsonElement queue = Property(response.Value, speaker.Entity);
        return new MusicQueueState(
            cache.Current?.States.GetValueOrDefault(speaker.Entity)?.State,
            QueueItem(Property(queue, "current_item")),
            QueueItem(Property(queue, "next_item")),
            Property(queue, "items") is { ValueKind: JsonValueKind.Number } count && count.TryGetInt32(out int items) ? items : 0,
            Property(queue, "shuffle_enabled").ValueKind == JsonValueKind.True,
            Text(queue, "repeat_mode"));
    }

    public async Task<Result> ControlAsync(MusicSpeaker speaker, MusicAction action, int? volume, CancellationToken cancellationToken)
    {
        var data = new JsonObject();
        string service = action switch
        {
            MusicAction.Pause => "media_pause",
            MusicAction.Resume => "media_play",
            MusicAction.Next => "media_next_track",
            MusicAction.Previous => "media_previous_track",
            MusicAction.Stop => "media_stop",
            _ => "volume_set",
        };
        if (action == MusicAction.Volume)
            data["volume_level"] = Math.Round(Math.Clamp(volume ?? 0, 0, 100) / 100.0, 2);

        return await SendAsync("media_player", service, speaker, data, cancellationToken);
    }

    public async Task<Result> TransferAsync(MusicSpeaker from, MusicSpeaker to, CancellationToken cancellationToken) =>
        await SendAsync("music_assistant", "transfer_queue", to, new JsonObject { ["source_player"] = from.Entity, ["auto_play"] = true }, cancellationToken);

    public async Task<Result> GroupAsync(MusicSpeaker leader, IReadOnlyList<MusicSpeaker> members, CancellationToken cancellationToken) =>
        await SendAsync("media_player", "join", leader, new JsonObject { ["group_members"] = new JsonArray([.. members.Select(member => (JsonNode)member.Entity)]) }, cancellationToken);

    public async Task<Result> UngroupAsync(MusicSpeaker speaker, CancellationToken cancellationToken) =>
        await SendAsync("media_player", "unjoin", speaker, [], cancellationToken);

    private async Task<Result> SendAsync(string domain, string service, MusicSpeaker speaker, JsonObject data, CancellationToken cancellationToken)
    {
        Result<JsonElement> response = await CallAsync(domain, service, speaker.Entity, data, returnResponse: false, $"speaker '{speaker.Name}'", cancellationToken);
        return response.IsSuccess ? Result.Success() : response.Error;
    }

    private async Task<Result<JsonElement>> CallAsync(string domain, string service, string? target, JsonObject data, bool returnResponse, string subject, CancellationToken cancellationToken)
    {
        var fields = new JsonObject { ["domain"] = domain, ["service"] = service, ["service_data"] = data };
        if (target is not null)
            fields["target"] = new JsonObject { ["entity_id"] = target };
        if (returnResponse)
            fields["return_response"] = true;

        Result<JsonElement> called = await commands.CommandAsync("call_service", fields, cancellationToken);
        if (called.IsSuccess)
            return Property(called.Value, "response");

        _configEntryId = null;
        return Map(called.Error, subject);
    }

    private async Task<Result<string>> ConfigEntryAsync(CancellationToken cancellationToken)
    {
        if (_configEntryId is { } known)
            return known;

        Result<JsonElement> entries = await commands.CommandAsync("config_entries/get", new JsonObject { ["domain"] = "music_assistant" }, cancellationToken);
        if (entries.IsFailure)
            return Map(entries.Error, "the search");

        JsonElement[] all = Array(entries.Value, "");
        JsonElement? chosen = all.Cast<JsonElement?>().FirstOrDefault(entry => Text(entry!.Value, "state") == "loaded") ?? all.Cast<JsonElement?>().FirstOrDefault();
        if (chosen is not { } entry || Text(entry, "entry_id") is not { Length: > 0 } id)
            return new Error("music.failed", "The Music Assistant integration is not set up in Home Assistant.", ErrorType.Unavailable);

        _configEntryId = id;
        return id;
    }

    private static Error Map(Error error, string subject) => error.Code switch
    {
        "ha.unavailable" => new Error("ha.unreachable", "Home Assistant is not reachable.", ErrorType.Unavailable),
        "ha.command_failed" => new Error("music.failed", $"Music Assistant rejected the call for {subject}: {error.Message}", ErrorType.Unavailable),
        _ => error,
    };

    private static MusicItem? QueueItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;
        // Streams without a library item (e.g. radio) have only the queue item's name.
        return Item(Property(item, "media_item"), null)
            ?? (Text(item, "name") is { } name ? new MusicItem(null, name, [], null, null) : null);
    }

    private static MusicItem? Item(JsonElement item, MusicMediaType? listType)
    {
        if (Text(item, "name") is not { } name)
            return null;

        return new MusicItem(
            ParseType(Text(item, "media_type")) ?? listType,
            name,
            [.. Array(item, "artists").Select(artist => Text(artist, "name")).OfType<string>()],
            Text(Property(item, "album"), "name"),
            Text(item, "uri"));
    }

    private static MusicMediaType? ParseType(string? name) => name switch
    {
        "track" => MusicMediaType.Track,
        "album" => MusicMediaType.Album,
        "artist" => MusicMediaType.Artist,
        "playlist" => MusicMediaType.Playlist,
        "radio" => MusicMediaType.Radio,
        _ => null,
    };

    internal static string TypeName(MusicMediaType type) => type.ToString().ToLower(CultureInfo.InvariantCulture);

    private static string EnqueueName(MusicEnqueue enqueue) => enqueue == MusicEnqueue.ReplaceNext ? "replace_next" : enqueue.ToString().ToLower(CultureInfo.InvariantCulture);

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : default;

    // An array property, or the element itself when name is empty; nothing for anything else.
    private static JsonElement[] Array(JsonElement element, string name)
    {
        JsonElement array = name.Length == 0 ? element : Property(element, name);
        return array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];
    }

    private static string? Text(JsonElement element, string property) =>
        Property(element, property) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
