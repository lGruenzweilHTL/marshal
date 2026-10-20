using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Music;

/// <summary>
/// The <c>music.*</c> tools over Music Assistant: search, play, queue, control, transfer, group and ungroup on
/// configured speakers. Speakers are taken by configured name only; without one, the turn's satellite speaker is used.
/// Search and queue output is untrusted (names come from music providers). No tools without Home Assistant or
/// without <c>music.speakers</c>.
/// </summary>
public sealed partial class MusicToolSource(IMusicPlayer player, ISpeakerDirectory speakers, IOptions<HomeAssistantOptions> options) : IToolSource
{
    internal const int SearchLimit = 10;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private static readonly HashSet<string> _webSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "ftp", "file", "rtsp" };

    private readonly IReadOnlyList<ITool> _tools =
    [
        new SearchTool(player),
        new PlayTool(player, speakers),
        new QueueTool(player, speakers),
        new ControlTool(player, speakers),
        new TransferTool(player, speakers),
        new GroupTool(player, speakers),
        new UngroupTool(player, speakers),
    ];

    public string Name => "music";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("music", "Music playback on speakers: songs, albums, artists, playlists, radio, volume and the queue")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(options.Value.Url is null || speakers.Names.Count == 0 ? [] : _tools);

    [GeneratedRegex(@"^[a-z0-9_-]+://(track|album|artist|playlist|radio|podcast|audiobook|folder)/\S+$")]
    private static partial Regex MusicUri();

    /// <summary>True for a search phrase or a Music Assistant URI; false for anything else with <c>://</c>.</summary>
    internal static bool IsPlayable(string item)
    {
        if (!item.Contains("://", StringComparison.Ordinal))
            return true;
        return MusicUri().IsMatch(item) && !_webSchemes.Contains(item[..item.IndexOf("://", StringComparison.Ordinal)]);
    }

    private static ToolDescriptor Descriptor(string name, string description, string schema, bool untrusted = false, string? widget = null) => new()
    {
        Id = new ToolId("music", name),
        Description = description,
        ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
        UntrustedOutput = untrusted,
        TaintKind = untrusted ? "music" : null,
        Fillers = name == "search" ? ToolFillers.Lines("Ich suche die Musik.", "Let me find that music.") : ToolFillers.None,
        Widget = widget,
    };

    private static Error Invalid(string message) => new("music.invalid_arguments", message, ErrorType.Validation);

    private static ToolOutput Json(object value) => new(JsonSerializer.Serialize(value, _json));

    private static string? Text(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Has(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null;

    private static Result<MusicMediaType?> ParseType(JsonElement arguments) => Text(arguments, "type") switch
    {
        "track" => MusicMediaType.Track,
        "album" => MusicMediaType.Album,
        "artist" => MusicMediaType.Artist,
        "playlist" => MusicMediaType.Playlist,
        "radio" => MusicMediaType.Radio,
        null when !Has(arguments, "type") => (MusicMediaType?)null,
        _ => Invalid("type must be track, album, artist, playlist or radio."),
    };

    /// <summary>The named speaker, else the turn's satellite speaker.</summary>
    private static Result<MusicSpeaker> Resolve(ISpeakerDirectory speakers, string? name, MarshalExecutionContext context)
    {
        if (name is null)
        {
            return speakers.ForSatellite(context.SatelliteId) is { } own
                ? own
                : new Error("music.no_speaker", $"This turn has no speaker of its own; name one of: {string.Join(", ", speakers.Names)}.", ErrorType.Validation);
        }

        return speakers.ByName(name) is { } speaker
            ? speaker
            : new Error("music.unknown_speaker", $"There is no speaker '{name}'. Speakers: {string.Join(", ", speakers.Names)}.", ErrorType.NotFound);
    }

    private static object? Item(MusicItem? item) =>
        item is null ? null : new { name = item.Name, artists = item.Artists, album = item.Album, uri = item.Uri };

    private const string SpeakerProperty = """
        "speaker":{"type":"string","description":"Configured speaker name; omit for the speaker in the asking room."}
        """;

    private sealed class SearchTool(IMusicPlayer player) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MusicToolSource.Descriptor("search",
            "Searches music in Music Assistant (all providers). Returns up to 10 tracks, albums, artists, playlists and radio stations each, with name, artists, album and the uri to pass to music_play. Only needed to choose between versions; music_play also takes a search phrase directly.",
            """
            {"type":"object","properties":{
              "query":{"type":"string","description":"What to search for, e.g. 'Innuendo' or 'Queen'."},
              "type":{"type":"string","enum":["track","album","artist","playlist","radio"]}},
             "required":["query"]}
            """,
            untrusted: true);

        public Result Validate(JsonElement arguments) => Parse(arguments) is { IsFailure: true } parsed ? parsed.Error : Result.Success();

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<(string Query, MusicMediaType? Type)> parsed = Parse(invocation.Arguments);
            if (parsed.IsFailure)
                return parsed.Error;

            Result<IReadOnlyList<MusicItem>> found = await player.SearchAsync(parsed.Value.Query, parsed.Value.Type, SearchLimit, cancellationToken);
            if (found.IsFailure)
                return found.Error;

            var lists = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (IGrouping<MusicMediaType?, MusicItem> group in found.Value.GroupBy(item => item.Type))
            {
                if (group.Key is not { } type)
                    continue;
                lists[ListName(type)] = group.Take(SearchLimit).Select(Item).ToArray();
            }

            return Json(lists);
        }

        private static string ListName(MusicMediaType type) => type switch
        {
            MusicMediaType.Track => "tracks",
            MusicMediaType.Album => "albums",
            MusicMediaType.Artist => "artists",
            MusicMediaType.Playlist => "playlists",
            _ => "radio",
        };

        private static Result<(string Query, MusicMediaType? Type)> Parse(JsonElement arguments)
        {
            if (Text(arguments, "query")?.Trim() is not { Length: > 0 } query)
                return Invalid("query is required.");
            Result<MusicMediaType?> type = ParseType(arguments);
            return type.IsFailure ? type.Error : (query, type.Value);
        }
    }

    private sealed class PlayTool(IMusicPlayer player, ISpeakerDirectory speakers) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MusicToolSource.Descriptor("play",
            "Plays music on a music speaker; this is the way to play music. item is a search phrase such as 'Queen - Innuendo' or 'Bohemian Rhapsody', or a uri from music_search; never a web URL. Without speaker, it plays in the asking room. enqueue: play (default, now), replace (replace the queue), next, replace_next, add (end of the queue). radio: true continues with similar music.",
            $$$"""
            {"type":"object","properties":{
              "item":{"type":"string"},
              "type":{"type":"string","enum":["track","album","artist","playlist","radio"]},
              {{{SpeakerProperty}}},
              "enqueue":{"type":"string","enum":["play","replace","next","replace_next","add"]},
              "radio":{"type":"boolean"}},
             "required":["item"]}
            """,
            widget: MusicWidgetData.WidgetKind);

        public Result Validate(JsonElement arguments) => Parse(arguments) is { IsFailure: true } parsed ? parsed.Error : Result.Success();

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<MusicPlayRequest> request = Parse(invocation.Arguments);
            if (request.IsFailure)
                return request.Error;
            Result<MusicSpeaker> speaker = Resolve(speakers, Text(invocation.Arguments, "speaker"), invocation.Context);
            if (speaker.IsFailure)
                return speaker.Error;

            Result played = await player.PlayAsync(speaker.Value, request.Value, cancellationToken);
            if (played.IsFailure)
                return played.Error;

            string what = request.Value.Type is { } type ? $"{HomeAssistantMusicPlayer.TypeName(type)} '{request.Value.Item}'" : $"'{request.Value.Item}'";
            return new ToolOutput(request.Value.Enqueue switch
            {
                MusicEnqueue.Add => $"Added {what} to the end of the queue on {speaker.Value.Name}.",
                MusicEnqueue.Next or MusicEnqueue.ReplaceNext => $"Queued {what} to play next on {speaker.Value.Name}.",
                _ => $"Playing {what} on {speaker.Value.Name}{(request.Value.Radio ? ", then similar music" : "")}.",
            });
        }

        private static Result<MusicPlayRequest> Parse(JsonElement arguments)
        {
            if (Text(arguments, "item")?.Trim() is not { Length: > 0 } item)
                return Invalid("item is required.");
            if (!IsPlayable(item))
                return Invalid("item must be a search phrase or a Music Assistant uri from music_search, never a web URL.");

            Result<MusicMediaType?> type = ParseType(arguments);
            if (type.IsFailure)
                return type.Error;

            MusicEnqueue? enqueue = Text(arguments, "enqueue") switch
            {
                "play" => MusicEnqueue.Play,
                "replace" => MusicEnqueue.Replace,
                "next" => MusicEnqueue.Next,
                "replace_next" => MusicEnqueue.ReplaceNext,
                "add" => MusicEnqueue.Add,
                null when !Has(arguments, "enqueue") => MusicEnqueue.Play,
                _ => null,
            };
            if (enqueue is null)
                return Invalid("enqueue must be play, replace, next, replace_next or add.");

            bool radio = false;
            if (Has(arguments, "radio"))
            {
                JsonValueKind kind = arguments.GetProperty("radio").ValueKind;
                if (kind is not (JsonValueKind.True or JsonValueKind.False))
                    return Invalid("radio must be true or false.");
                radio = kind == JsonValueKind.True;
            }

            return new MusicPlayRequest(item, type.Value, enqueue.Value, radio);
        }
    }

    private sealed class QueueTool(IMusicPlayer player, ISpeakerDirectory speakers) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MusicToolSource.Descriptor("queue",
            "What a music speaker is playing: playback state, the current and the next item, the number of items in the queue, shuffle and repeat. Without speaker, the speaker in the asking room.",
            $$$"""
            {"type":"object","properties":{ {{{SpeakerProperty}}} }}
            """,
            untrusted: true,
            widget: MusicWidgetData.WidgetKind);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<MusicSpeaker> speaker = Resolve(speakers, Text(invocation.Arguments, "speaker"), invocation.Context);
            if (speaker.IsFailure)
                return speaker.Error;

            Result<MusicQueueState> queue = await player.GetQueueAsync(speaker.Value, cancellationToken);
            if (queue.IsFailure)
                return queue.Error;

            MusicQueueState state = queue.Value;
            return Json(new
            {
                speaker = speaker.Value.Name,
                state = state.State,
                current = Item(state.Current),
                next = Item(state.Next),
                items = state.Items,
                shuffle = state.Shuffle,
                repeat = state.Repeat,
            });
        }
    }

    private sealed class ControlTool(IMusicPlayer player, ISpeakerDirectory speakers) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MusicToolSource.Descriptor("control",
            "Controls playback on a music speaker: pause, resume, next, previous, stop, or volume with volume from 0 to 100. Without speaker, the speaker in the asking room.",
            $$$"""
            {"type":"object","properties":{
              "action":{"type":"string","enum":["pause","resume","next","previous","stop","volume"]},
              {{{SpeakerProperty}}},
              "volume":{"type":"integer","minimum":0,"maximum":100}},
             "required":["action"]}
            """);

        public Result Validate(JsonElement arguments) => Parse(arguments) is { IsFailure: true } parsed ? parsed.Error : Result.Success();

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<(MusicAction Action, int? Volume)> parsed = Parse(invocation.Arguments);
            if (parsed.IsFailure)
                return parsed.Error;
            Result<MusicSpeaker> speaker = Resolve(speakers, Text(invocation.Arguments, "speaker"), invocation.Context);
            if (speaker.IsFailure)
                return speaker.Error;

            Result controlled = await player.ControlAsync(speaker.Value, parsed.Value.Action, parsed.Value.Volume, cancellationToken);
            if (controlled.IsFailure)
                return controlled.Error;

            return new ToolOutput(parsed.Value.Action == MusicAction.Volume
                ? $"Volume on {speaker.Value.Name} set to {parsed.Value.Volume}."
                : $"Sent {Text(invocation.Arguments, "action")} to {speaker.Value.Name}.");
        }

        private static Result<(MusicAction Action, int? Volume)> Parse(JsonElement arguments)
        {
            MusicAction? action = Text(arguments, "action") switch
            {
                "pause" => MusicAction.Pause,
                "resume" => MusicAction.Resume,
                "next" => MusicAction.Next,
                "previous" => MusicAction.Previous,
                "stop" => MusicAction.Stop,
                "volume" => MusicAction.Volume,
                _ => null,
            };
            if (action is null)
                return Invalid("action must be pause, resume, next, previous, stop or volume.");

            int? volume = null;
            if (Has(arguments, "volume"))
            {
                JsonElement value = arguments.GetProperty("volume");
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int level) || level is < 0 or > 100)
                    return Invalid("volume must be a whole number from 0 to 100.");
                volume = level;
            }

            if (action == MusicAction.Volume && volume is null)
                return Invalid("volume (0 to 100) is required for action volume.");

            return (action.Value, action == MusicAction.Volume ? volume : null);
        }
    }

    private sealed class TransferTool(IMusicPlayer player, ISpeakerDirectory speakers) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MusicToolSource.Descriptor("transfer",
            "Moves the music (queue and playback) from one speaker to another, e.g. 'continue in the living room'. from defaults to the speaker in the asking room.",
            """
            {"type":"object","properties":{
              "to":{"type":"string","description":"Configured speaker name to continue on."},
              "from":{"type":"string","description":"Configured speaker name that plays now; omit for the asking room's speaker."}},
             "required":["to"]}
            """);

        public Result Validate(JsonElement arguments) =>
            Text(arguments, "to") is { Length: > 0 } ? Result.Success() : Invalid("to is required.");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            if (Validate(invocation.Arguments) is { IsFailure: true } invalid)
                return invalid.Error;
            Result<MusicSpeaker> to = Resolve(speakers, Text(invocation.Arguments, "to"), invocation.Context);
            if (to.IsFailure)
                return to.Error;
            Result<MusicSpeaker> from = Resolve(speakers, Text(invocation.Arguments, "from"), invocation.Context);
            if (from.IsFailure)
                return from.Error;
            if (from.Value.Name == to.Value.Name)
                return Invalid($"The music already plays on {to.Value.Name}; name a different speaker.");

            Result transferred = await player.TransferAsync(from.Value, to.Value, cancellationToken);
            return transferred.IsFailure ? transferred.Error : new ToolOutput($"Moved the music from {from.Value.Name} to {to.Value.Name}.");
        }
    }

    private sealed class GroupTool(IMusicPlayer player, ISpeakerDirectory speakers) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MusicToolSource.Descriptor("group",
            "Joins music speakers into one synced group that plays the same music; the first speaker leads and the others join its playback.",
            """
            {"type":"object","properties":{
              "speakers":{"type":"array","items":{"type":"string"},"minItems":2,"description":"Configured speaker names, leader first."}},
             "required":["speakers"]}
            """);

        public Result Validate(JsonElement arguments) => Parse(arguments) is { IsFailure: true } parsed ? parsed.Error : Result.Success();

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<string[]> names = Parse(invocation.Arguments);
            if (names.IsFailure)
                return names.Error;

            var resolved = new List<MusicSpeaker>();
            foreach (string name in names.Value)
            {
                Result<MusicSpeaker> speaker = Resolve(speakers, name, invocation.Context);
                if (speaker.IsFailure)
                    return speaker.Error;
                resolved.Add(speaker.Value);
            }

            Result grouped = await player.GroupAsync(resolved[0], resolved[1..], cancellationToken);
            return grouped.IsFailure ? grouped.Error : new ToolOutput($"Grouped {string.Join(", ", names.Value)}; {names.Value[0]} leads.");
        }

        private static Result<string[]> Parse(JsonElement arguments)
        {
            string[] names = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("speakers", out JsonElement list) && list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray().Select(name => name.ValueKind == JsonValueKind.String ? name.GetString() : null).OfType<string>().Distinct(StringComparer.Ordinal)]
                : [];
            return names.Length >= 2 ? names : Invalid("speakers must list at least two different speaker names.");
        }
    }

    private sealed class UngroupTool(IMusicPlayer player, ISpeakerDirectory speakers) : ITool
    {
        public ToolDescriptor Descriptor { get; } = MusicToolSource.Descriptor("ungroup",
            "Removes a music speaker from its synced group.",
            """
            {"type":"object","properties":{
              "speaker":{"type":"string","description":"Configured speaker name."}},
             "required":["speaker"]}
            """);

        public Result Validate(JsonElement arguments) =>
            Text(arguments, "speaker") is { Length: > 0 } ? Result.Success() : Invalid("speaker is required.");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            if (Validate(invocation.Arguments) is { IsFailure: true } invalid)
                return invalid.Error;
            Result<MusicSpeaker> speaker = Resolve(speakers, Text(invocation.Arguments, "speaker"), invocation.Context);
            if (speaker.IsFailure)
                return speaker.Error;

            Result ungrouped = await player.UngroupAsync(speaker.Value, cancellationToken);
            return ungrouped.IsFailure ? ungrouped.Error : new ToolOutput($"Removed {speaker.Value.Name} from its group.");
        }
    }
}
