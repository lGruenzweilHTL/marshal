# Design

## Context

See proposal.md (Why). External facts (HA `music_assistant` integration docs and `services.yaml`; re-check against the installed version):

- **Response services** `music_assistant.search`, `get_library`, `get_queue` (called with `return_response`):
  - `search(config_entry_id, name, media_type?, artist?, album?, search_options{limit, library_only})` returns lists per type (`artists`, `albums`, `tracks`, `playlists`, `radio`, …). Items have `name`, `uri`, `media_type`, `artists`, `album`.
  - `get_queue` (target: player entity) returns per entity `current_item`, `next_item`, `items` (count), `shuffle_enabled`, `repeat_mode`, `elapsed_time`.
- **Action services:**
  - `music_assistant.play_media(media_id, media_type?, artist?, album?, enqueue?, radio_mode?)`. `media_id` is a name, `"Artist - Title"`, a URI or a URL. `enqueue` ∈ `play`, `replace`, `next`, `replace_next`, `add`. `media_type` ∈ `artist`, `album`, `audiobook`, `folder`, `playlist`, `podcast`, `track`, `radio`.
  - `music_assistant.transfer_queue(source_player?, auto_play?)`: the target entity is the destination.
- **Generic `media_player` services** work on MA players: `media_pause`, `media_play`, `media_next_track`, `media_previous_track`, `media_stop`, `volume_set(volume_level 0..1)`, `join(group_members)`, `unjoin`.
- **`config_entry_id`** for `search` comes from websocket `config_entries/get` with `domain: music_assistant`.
- **Websocket service calls.** `IHomeAssistantCommands.CommandAsync` sends any websocket command, so `call_service` with `return_response: true` is available. The registry cache knows entities and devices.
- **Satellite profiles** are parsed and validated in `Marshal.Core` (with the `display` key from display-surfaces). The satellite context goes through `ISatelliteContextProvider` / `SatelliteContextContributor`.
- **MA is not installed yet** (explore session). Fixtures start from the documented schema and are replaced with recorded responses once MA runs.

## Goals / Non-Goals

**Goals:**
- "Play X" from a satellite needs one tool call, without asking where.
- The model cannot reach an arbitrary entity or URL through music tools.

**Non-Goals:**
- Guessing a satellite's speaker from the device registry. MA creates its own player entities, so the satellite's ESPHome player and its MA player differ, and the guess is unreliable. The profile's `speaker` is explicit and handles the mixed setup.
- Browsing the library (`get_library`). Search covers voice use.

## Decisions

### 1. Location and interfaces

Code lives in `Marshal.Integrations.HomeAssistant/Music`, because every call goes through the HA connection. No new project.

| Interface | v1 implementation | Purpose |
|---|---|---|
| `ISpeakerDirectory` (Abstractions) | `ConfiguredSpeakerDirectory` (Core, from `music.speakers` + satellite profiles) | `ForSatellite(satelliteId)`, `ByName(name)`, `Names`; used by the tools and the satellite context |
| `IMusicPlayer` | `HomeAssistantMusicPlayer` | `SearchAsync`, `PlayAsync`, `GetQueueAsync`, `ControlAsync`, `TransferAsync`, `GroupAsync`, `UngroupAsync` over `IHomeAssistantCommands`; caches the MA `config_entry_id` until reconnect |

`MusicToolSource : IToolSource` (namespace `music`) depends on both interfaces, so tool tests use fakes. `HomeAssistantMusicPlayer` is tested against the existing in-process fake HA websocket server, checking service domain, service, target and data per call.

This mirrors display-surfaces (`IDisplayDirectory` in Abstractions, `ConfiguredDisplayDirectory` in Core), so the satellite profile validation for `speaker` sits next to the one for `display`.

### 2. Tools

| Tool | Tier | Untrusted output | Hidden when |
|---|---|---|---|
| `music.search(query, type?)` | safe | yes (taint source `music`) | no `music` section or no HA connection |
| `music.play(item, type?, speaker?, enqueue?, radio?)` | safe | no | same |
| `music.queue(speaker?)` | safe | yes | same |
| `music.control(action, speaker?, volume?)` | safe | no | same |
| `music.transfer(to, from?)` | safe | no | same |
| `music.group(speakers)` / `music.ungroup(speaker)` | safe | no | same |

- **Why safe.** Household audio, reversible, nothing outward. Satellite profiles can still hide them (`denyTools: [music.*]` in the kids' room).
- **Validation before any tier applies** (`ITool.Validate`):
  - URL rejection for `music.play`: a value with `://` is accepted only if it matches `^[a-z0-9_-]+://(track|album|artist|playlist|radio|podcast|audiobook|folder)/\S+$` and its scheme is not `http`, `https`, `ftp`, `file` or `rtsp`;
  - `volume` 0..100; `enqueue` and `action` enums; `group` needs at least two distinct speakers.
  - Speaker names are checked at invocation, because the default speaker depends on the turn.
- **Resolution.** `music.play` passes `item` as `media_id` and `type` as `media_type`. MA resolves free text itself: no own fuzzy matching. `music.control` maps `volume` to `volume_level / 100`.
- **Results** name the speaker and what was sent ("Playing album Innuendo on kitchen"). The play result does not include provider names, so it is trusted.

Alternative: rely on HA's own intents (`HassMediaSearchAndPlay`, pause/volume intents through HA MCP) instead of `music.*`. Rejected as the only path: those intents do not offer queue, transfer or grouping, and they do not know the satellite → speaker mapping.

### 3. Overlap with HA MCP media intents

If MA players are exposed to Assist, HA MCP also offers pause, volume and search-and-play intents for them. Two tools for one action make small models pick at random. The example config recommends not exposing MA players to Assist when `music` is configured. The `music.*` descriptions say they are the way to control music speakers. No code rule hides HA intents: they are generic and also serve TVs.

### 4. Satellite context line

`SatelliteContextContributor` adds one line when speakers exist, e.g. "Music speaker in this room: kitchen. Speakers: kitchen, living_room, office." It costs about 20 tokens and saves a tool call in "play it in the office".

### 5. Configuration

```yaml
# marshal.yaml
music:
  speakers:
    kitchen:     media_player.kitchen_satellite_ma   # the satellite's own MA player
    living_room: media_player.living_room_sonos
# satellites.yaml
assist_satellite.kitchen:     { speaker: kitchen }
assist_satellite.living_room: { speaker: living_room, display: living_tv }
```

## Risks / Trade-offs

- **MA not installed, so the schema is unverified.** Responses are parsed leniently (unknown fields ignored, missing fields null). The tasks include re-recording fixtures against the real MA before the change is archived.
- **`media_id` free text plays something unexpected.** The user hears it at once and says "stop"; the action is harmless and reversible.
- **Providers could return URLs as URIs** (e.g. radio streams), and the model passes one back. The URL rule rejects any `http(s)` value regardless of origin. Radio stations are then played by name or by their `<provider>://radio/<id>` URI.
- **Taint from `music.search` in voice runs.** Memory and task writes later in that run need confirmation. Acceptable: searches are rarely followed by memory writes, and taint is per run.
