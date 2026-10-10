# Proposal

## Why

"Play some jazz in the kitchen", "move the music to the living room" or "add this album to the queue" need a music layer that plain Home Assistant media players lack. Music Assistant (planned as an HA add-on) provides one library across providers, search, queues, moving playback between rooms and synced groups, all through HA services. Marshal knows which satellite is speaking, so it can pick the right speaker. The speakers are mixed: some rooms play on the satellite itself, others on a separate speaker. Item 7 of `docs/feature-roadmap.md`.

## What Changes

- New `music` configuration: named speakers (`music.speakers: { kitchen: media_player.kitchen }`, the Music Assistant player entities).
- New optional satellite profile key `speaker`. A satellite with a `speaker` plays there when no speaker is named, which covers the mixed setup: the satellite's own player in one room, a separate speaker in another.
- New tools in the capability namespace `music`, all safe:
  - `music.search(query, type?)`;
  - `music.play(item, type?, speaker?, enqueue?, radio?)`;
  - `music.queue(speaker?)`;
  - `music.control(action, speaker?, volume?)`;
  - `music.transfer(to, from?)`;
  - `music.group(speakers)` and `music.ungroup(speaker)`.

  They use Home Assistant's `music_assistant.*` and `media_player.*` services over the existing websocket connection.
- The model can name any configured speaker, never a raw entity. `music.play` refuses web URLs, so the model cannot make Music Assistant fetch a URL of its choice.
- The satellite context names the room's speaker and the configured speaker names, so "play here" resolves without a tool call.

## Capabilities

### New Capabilities

- `music`: music configuration, speaker resolution and the `music.*` tools.

### Modified Capabilities

- `satellite-policy`: profiles gain `speaker`, and the satellite context names it.
- `tool-registry`: `music` joins the reserved namespaces. The delta also contains `media` from `media-requests`, so archive `media-requests` first.

## Non-goals

- Installing or configuring Music Assistant, and provider accounts.
- Announcements (`music_assistant.play_announcement`). Spoken notifications stay with the existing voice delivery.
- Playback of Jellyfin video or other non-music media.
- Library edits: favorites, playlists, ratings.
- Speakers that are not Music Assistant players.

## Impact

- `src/Marshal.Integrations.HomeAssistant/Music`: options, speaker directory, tool source. No new project and no new package.
- `src/Marshal.Core`: the satellite profile gains `speaker` with validation, and the satellite context gains the speaker line.
- `deploy/config.example/marshal.yaml` (`music` section) and `satellites.yaml` (`speaker` examples).
- CLAUDE.md repo map line for the HA integration.
- Verification against a running Music Assistant waits until it is installed. Fixtures are built from HA's documented service schema first.
