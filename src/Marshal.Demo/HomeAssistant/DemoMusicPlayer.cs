using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Music;
using Marshal.Integrations.HomeAssistant;
using Marshal.Integrations.HomeAssistant.Music;

namespace Marshal.Demo.HomeAssistant;

/// <summary>
/// Music Assistant, simulated: a small library to search, and playback that shows on the speaker's
/// <c>media_player</c> state (title, artist, volume, group members).
/// </summary>
public sealed class DemoMusicPlayer(DemoWorld world) : IMusicPlayer
{
    private static readonly MusicItem[] _library =
    [
        new(MusicMediaType.Track, "So What", ["Miles Davis"], "Kind of Blue", "library://track/1"),
        new(MusicMediaType.Track, "Blue in Green", ["Miles Davis", "Bill Evans"], "Kind of Blue", "library://track/2"),
        new(MusicMediaType.Album, "Kind of Blue", ["Miles Davis"], null, "library://album/1"),
        new(MusicMediaType.Track, "Teardrop", ["Massive Attack"], "Mezzanine", "library://track/3"),
        new(MusicMediaType.Album, "Mezzanine", ["Massive Attack"], null, "library://album/2"),
        new(MusicMediaType.Artist, "Nils Frahm", [], null, "library://artist/1"),
        new(MusicMediaType.Track, "Says", ["Nils Frahm"], "Spaces", "library://track/4"),
        new(MusicMediaType.Playlist, "Sunday Morning", [], null, "library://playlist/1"),
        new(MusicMediaType.Playlist, "Workshop Focus", [], null, "library://playlist/2"),
        new(MusicMediaType.Radio, "FM4", [], null, "library://radio/1"),
    ];

    public Task<Result<IReadOnlyList<MusicItem>>> SearchAsync(string query, MusicMediaType? type, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<MusicItem>>>(_library
            .Where(item => type is null || item.Type == type)
            .Where(item => Matches(item, query))
            .Take(limit)
            .ToList());

    public Task<Result> PlayAsync(MusicSpeaker speaker, MusicPlayRequest request, CancellationToken cancellationToken)
    {
        MusicItem item = _library.FirstOrDefault(candidate => Matches(candidate, request.Item) && (request.Type is null || candidate.Type == request.Type))
            ?? new MusicItem(request.Type, request.Item, [], null, null);
        return Task.FromResult(Set(speaker, "playing", new Dictionary<string, object?>
        {
            ["media_title"] = item.Name,
            ["media_artist"] = item.Artists.Count == 0 ? null : string.Join(", ", item.Artists),
            ["media_album_name"] = item.Album,
        }));
    }

    public Task<Result<MusicQueueState>> GetQueueAsync(MusicSpeaker speaker, CancellationToken cancellationToken)
    {
        if (world.Home.Snapshot.States.GetValueOrDefault(speaker.Entity) is not { } state)
            return Task.FromResult<Result<MusicQueueState>>(Unknown(speaker));

        MusicItem? current = Text(state, "media_title") is { } title
            ? new MusicItem(MusicMediaType.Track, title, Text(state, "media_artist") is { } artist ? [artist] : [], Text(state, "media_album_name"), null)
            : null;
        return Task.FromResult<Result<MusicQueueState>>(new MusicQueueState(state.State, current, null, current is null ? 0 : 1, false, "off"));
    }

    public Task<Result> ControlAsync(MusicSpeaker speaker, MusicAction action, int? volume, CancellationToken cancellationToken) =>
        Task.FromResult(action switch
        {
            MusicAction.Pause => Set(speaker, "paused", null),
            MusicAction.Resume => Set(speaker, "playing", null),
            MusicAction.Stop => Set(speaker, "idle", new Dictionary<string, object?> { ["media_title"] = null, ["media_artist"] = null, ["media_album_name"] = null }),
            MusicAction.Volume => Set(speaker, null, new Dictionary<string, object?> { ["volume_level"] = Math.Clamp(volume ?? 30, 0, 100) / 100.0 }),
            _ => Set(speaker, "playing", null),
        });

    public Task<Result> TransferAsync(MusicSpeaker from, MusicSpeaker to, CancellationToken cancellationToken)
    {
        if (world.Home.Snapshot.States.GetValueOrDefault(from.Entity) is not { } source)
            return Task.FromResult<Result>(Unknown(from));

        var media = new Dictionary<string, object?> { ["media_title"] = Text(source, "media_title"), ["media_artist"] = Text(source, "media_artist"), ["media_album_name"] = Text(source, "media_album_name") };
        Result moved = Set(to, source.State, media);
        return Task.FromResult(moved.IsFailure ? moved : Set(from, "idle", new Dictionary<string, object?> { ["media_title"] = null, ["media_artist"] = null, ["media_album_name"] = null }));
    }

    public Task<Result> GroupAsync(MusicSpeaker leader, IReadOnlyList<MusicSpeaker> members, CancellationToken cancellationToken) =>
        Task.FromResult(Set(leader, null, new Dictionary<string, object?> { ["group_members"] = new[] { leader.Entity }.Concat(members.Select(member => member.Entity)).ToArray() }));

    public Task<Result> UngroupAsync(MusicSpeaker speaker, CancellationToken cancellationToken) =>
        Task.FromResult(Set(speaker, null, new Dictionary<string, object?> { ["group_members"] = null }));

    private Result Set(MusicSpeaker speaker, string? state, Dictionary<string, object?>? attributes)
    {
        if (world.Home.Snapshot.States.GetValueOrDefault(speaker.Entity) is not { } current)
            return Unknown(speaker);

        Result<HaState> set = world.Home.Set(speaker.Entity, state ?? current.State, attributes);
        return set.IsSuccess ? Result.Success() : set.Error;
    }

    private static bool Matches(MusicItem item, string query) =>
        item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Artists.Any(artist => artist.Contains(query, StringComparison.OrdinalIgnoreCase))
        || item.Album?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static string? Text(HaState state, string attribute) =>
        state.Attributes.TryGetProperty(attribute, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Error Unknown(MusicSpeaker speaker) => new("music.unknown_speaker", $"There is no speaker '{speaker.Name}'.", ErrorType.NotFound);
}
