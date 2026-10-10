using Marshal.Abstractions;
using Marshal.Abstractions.Music;

namespace Marshal.Integrations.HomeAssistant.Music;

/// <summary>Media types the music tools search and play.</summary>
public enum MusicMediaType
{
    Track,
    Album,
    Artist,
    Playlist,
    Radio,
}

/// <summary>Where <c>play</c> puts the item in the speaker's queue (Music Assistant's <c>enqueue</c>).</summary>
public enum MusicEnqueue
{
    Play,
    Replace,
    Next,
    ReplaceNext,
    Add,
}

/// <summary>Playback controls.</summary>
public enum MusicAction
{
    Pause,
    Resume,
    Next,
    Previous,
    Stop,
    Volume,
}

/// <summary>
/// A search result or queue item with its Music Assistant URI (<c>&lt;provider&gt;://&lt;type&gt;/&lt;id&gt;</c>). Names
/// come from music providers (untrusted).
/// </summary>
public sealed record MusicItem(MusicMediaType? Type, string Name, IReadOnlyList<string> Artists, string? Album, string? Uri);

/// <summary>What to play and how. <c>Item</c> is a search phrase or a Music Assistant URI; Music Assistant resolves it.</summary>
public sealed record MusicPlayRequest(string Item, MusicMediaType? Type, MusicEnqueue Enqueue, bool Radio);

/// <summary>
/// A speaker's queue: the player entity's state (<c>playing</c>, <c>paused</c>, <c>idle</c>, ...; null when unknown),
/// the current and next item, the number of items, shuffle and repeat mode.
/// </summary>
public sealed record MusicQueueState(string? State, MusicItem? Current, MusicItem? Next, int Items, bool Shuffle, string? Repeat);

/// <summary>
/// Music Assistant playback on configured speakers. Failures: <c>ha.unreachable</c> while Home Assistant is not
/// connected, <c>music.failed</c> (naming the speaker) when Home Assistant or Music Assistant rejects a call.
/// </summary>
public interface IMusicPlayer
{
    /// <summary>Searches all providers; up to <paramref name="limit"/> items per media type.</summary>
    Task<Result<IReadOnlyList<MusicItem>>> SearchAsync(string query, MusicMediaType? type, int limit, CancellationToken cancellationToken);

    Task<Result> PlayAsync(MusicSpeaker speaker, MusicPlayRequest request, CancellationToken cancellationToken);

    Task<Result<MusicQueueState>> GetQueueAsync(MusicSpeaker speaker, CancellationToken cancellationToken);

    /// <summary>Runs a control; <paramref name="volume"/> (0–100) is used for <see cref="MusicAction.Volume"/> only.</summary>
    Task<Result> ControlAsync(MusicSpeaker speaker, MusicAction action, int? volume, CancellationToken cancellationToken);

    /// <summary>Moves the queue and playback from one speaker to another.</summary>
    Task<Result> TransferAsync(MusicSpeaker from, MusicSpeaker to, CancellationToken cancellationToken);

    /// <summary>Joins the members into the leader's synced group.</summary>
    Task<Result> GroupAsync(MusicSpeaker leader, IReadOnlyList<MusicSpeaker> members, CancellationToken cancellationToken);

    /// <summary>Removes the speaker from its group.</summary>
    Task<Result> UngroupAsync(MusicSpeaker speaker, CancellationToken cancellationToken);
}
