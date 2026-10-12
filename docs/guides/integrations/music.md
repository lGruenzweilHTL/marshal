# Play music on speakers with Music Assistant

Let Marshal search, play, queue, move and group music on named speakers, with the speaker of the asking satellite as the default.

Marshal does this through Home Assistant and the Music Assistant integration. It does not talk to Music Assistant directly.

## Before you start

- A working [Home Assistant connection](home-assistant.md) (`ha.url` set).
- The Music Assistant integration in Home Assistant, with at least one player that appears as a `media_player` entity.
- The entity ids of those players, for example `media_player.kitchen_satellite_ma`.

> [!IMPORTANT]
> Do not expose the Music Assistant players to Assist. Home Assistant's own media intents then offer a second way to pause or play the same speaker, and small models pick between the two at random.

## Steps

### 1. Name your speakers

Add a `music` section to `/config/marshal.yaml`. Each key is a speaker name, each value a Music Assistant `media_player` entity.

```yaml
music:
  speakers:
    kitchen:     media_player.kitchen_satellite_ma
    living_room: media_player.living_room_sonos
```

Speaker names match `[a-z][a-z0-9_]*`. Startup fails with an error naming `music.speakers.<name>` if a name does not match, or if the entity is not in the `media_player` domain. The model uses these names only and never an entity id.

Without a `music` section, or without a Home Assistant connection, Marshal registers no `music_*` tool.

### 2. Set the default speaker per satellite

In `/config/satellites.yaml`, give each satellite profile the speaker it plays on:

```yaml
# inside a satellite profile
speaker: kitchen
```

A music tool called without a speaker then uses the speaker of the satellite that asked. See [satellites](../voice/satellites.md) and [`satellites.yaml`](../../configuration/satellites-yaml.md).

### 3. Restart Marshal

```sh
docker compose restart marshal
```

## Tools

All music tools are in the safe tier. Each fails with `ha.unreachable` when Home Assistant cannot be reached, and with `music.failed` naming the speaker when Home Assistant or Music Assistant rejects the call.

| Tool | What it does |
|---|---|
| `music_search(query, type?)` | Up to 10 results per type (`track`, `album`, `artist`, `playlist`, `radio`) with name, artists, album and Music Assistant URI. |
| `music_play(item, type?, speaker?, enqueue?, radio?)` | Plays a search phrase such as `Queen - Innuendo`, or a URI from `music_search`. |
| `music_queue(speaker?)` | Playback state, current and next item, item count, shuffle and repeat mode. |
| `music_control(action, speaker?, volume?)` | `pause`, `resume`, `next`, `previous`, `stop`, or `volume` with a value from 0 to 100. |
| `music_transfer(to, from?)` | Moves queue and playback from `from` (default: the turn's speaker) to `to`. |
| `music_group(speakers)` | Joins the listed speakers into one synced group led by the first. |
| `music_ungroup(speaker)` | Removes a speaker from its group. |

`enqueue` in `music_play` is `play` (default), `replace`, `next`, `replace_next` or `add`. `radio: true` continues with similar music.

### Which speaker a call uses

- A call without `speaker` uses the speaker of the turn's satellite profile.
- A call without `speaker` in a turn that has none, such as a web chat turn, fails with `music.no_speaker` and lists the configured names.
- An unknown name fails with `music.unknown_speaker` and lists the configured names.

## Limits

- `music_play` refuses a URL as `item` (`http`, `https`, `ftp`, `file`, `rtsp` and any other scheme followed by a host) with `music.invalid_arguments`, before it calls Home Assistant. Music Assistant URIs such as `library://album/12` and plain search phrases work. Marshal never plays a web address the model picked.
- A volume outside 0 to 100 is denied with `music.invalid_arguments` and nothing is sent.
- Names from music providers are third-party text. The output of `music_search` and `music_queue` is untrusted: a run that calls either is tainted with source `music`. See [untrusted content](../../concepts/untrusted-content.md).

## Check that it works

1. In the web UI, ask "Search for Innuendo". The answer lists tracks and albums with URIs.
2. Ask "Play the album on kitchen". The kitchen speaker starts playing. A web turn without a speaker name gets a `music.no_speaker` answer that lists your speakers, which proves the lookup works.
3. Ask a satellite "Play some Queen". Playback starts on the satellite's own speaker.
4. Ask "Move the music to the living room". The queue continues on `living_room`.

## Related

- [Home Assistant](home-assistant.md)
- [`music` in marshal.yaml](../../configuration/marshal-yaml/integrations.md)
- [Voice satellites](../voice/satellites.md)
- [Tool reference](../../reference/tools.md)
- [Media requests](media.md)
