# Spec Delta

## Purpose

Music through Music Assistant: search, play, queue, control, move and group playback on named speakers, with the speaker of the asking satellite as the default.

## ADDED Requirements

### Requirement: Music configuration
Music SHALL be configured in `marshal.yaml` as `music.speakers`, a map from speaker name (`[a-z][a-z0-9_]*`) to a Home Assistant `media_player` entity id of a Music Assistant player. A speaker name that does not match the pattern, or an entity outside the `media_player` domain, SHALL fail startup with a configuration error naming the key. Without a `music` section or without a Home Assistant connection, no `music_*` tool SHALL exist.

#### Scenario: Not configured
- **WHEN** `marshal.yaml` has no `music` section
- **THEN** no `music_*` tool is in any tool list

#### Scenario: Wrong entity domain
- **WHEN** a speaker's entity is `light.kitchen`
- **THEN** startup fails with an error naming `music.speakers.<name>`

### Requirement: Speaker resolution
Music tools SHALL take speakers by configured name only. A tool call without a speaker SHALL use the speaker of the turn's satellite profile. A call without a speaker in a turn that has no profile speaker (e.g. a web turn) SHALL fail with `music.no_speaker` listing the configured speaker names. An unknown speaker name SHALL fail with `music.unknown_speaker` listing the configured names. No tool SHALL accept an entity id.

#### Scenario: Default speaker
- **WHEN** a turn from `assist_satellite.kitchen` (profile `speaker: kitchen`) calls `music_play` without a speaker
- **THEN** playback starts on the `kitchen` speaker's entity

#### Scenario: Another room
- **WHEN** a kitchen turn calls `music_play` with `speaker: living_room`
- **THEN** playback starts on the `living_room` speaker

#### Scenario: Web turn without speaker
- **WHEN** a web turn calls `music_play` without a speaker
- **THEN** the tool fails with `music.no_speaker` listing the speaker names

### Requirement: Music tools
The system SHALL provide these safe tools, each failing with `ha.unreachable` when Home Assistant cannot be reached and with `music.failed` naming the speaker when Home Assistant or Music Assistant rejects the call:
- `music.search(query, type?)`: up to 10 results per type (`track`, `album`, `artist`, `playlist`, `radio`) with name, artists, album and the item's Music Assistant URI;
- `music.play(item, type?, speaker?, enqueue?, radio?)`: plays `item` (a search phrase such as `Queen - Innuendo`, or a URI from `music.search`) on the speaker. `enqueue` is `play` (default), `replace`, `next`, `replace_next` or `add`. `radio: true` continues with similar music;
- `music.queue(speaker?)`: the playback state, the current item, the next item, the number of items, and the shuffle and repeat mode of the speaker's queue;
- `music.control(action, speaker?, volume?)`: `pause`, `resume`, `next`, `previous`, `stop`, or `volume` with `volume` from 0 to 100;
- `music.transfer(to, from?)`: moves the queue and playback from speaker `from` (default: the turn's speaker) to speaker `to`;
- `music.group(speakers)`: joins the listed speakers into one synced group led by the first; `music.ungroup(speaker)` removes a speaker from its group.
The output of `music.search` and `music.queue` SHALL be untrusted, because names come from music providers.

#### Scenario: Search and play
- **WHEN** `music_search` is called with `Innuendo` and the model then calls `music_play` with the album URI from the result
- **THEN** that album plays on the turn's speaker

#### Scenario: Add to queue
- **WHEN** `music_play` is called with `enqueue: add`
- **THEN** the item is added to the end of the speaker's queue and the current item keeps playing

#### Scenario: Move to the living room
- **WHEN** a kitchen turn calls `music_transfer` with `to: living_room`
- **THEN** the kitchen speaker's queue continues on the `living_room` speaker

#### Scenario: Volume out of range
- **WHEN** `music_control` is called with `action: volume` and `volume: 150`
- **THEN** the call is denied with `music.invalid_arguments` and nothing is sent

#### Scenario: Queue taints
- **WHEN** a run calls `music_queue`
- **THEN** the run is tainted with source `music`

### Requirement: No web URLs in play
`music.play` SHALL reject an `item` that is a URL (`http`, `https`, `ftp`, `file`, `rtsp` and any other scheme followed by a host), with `music.invalid_arguments` and before any call to Home Assistant. Music Assistant URIs of the form `<provider>://<type>/<id>`, where `<type>` is a media type, and plain search phrases SHALL be accepted.

#### Scenario: Web URL refused
- **WHEN** `music_play` is called with `item: https://example.com/stream.mp3`
- **THEN** the call is denied with `music.invalid_arguments` and nothing is sent to Home Assistant

#### Scenario: Library URI accepted
- **WHEN** `music_play` is called with `item: library://album/12`
- **THEN** Home Assistant receives `music_assistant.play_media` with that media id
