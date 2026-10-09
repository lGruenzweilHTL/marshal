# Proposal

## Why

"Marshal, show me the model you made" only works in the web chat today: `artifacts.show` is hidden in voice turns, because Marshal has no way to put an artifact on a screen. The TVs in the house are Home Assistant media players (DLNA renderers), so a voice turn could show images, video and code on the TV in the room it was asked from.

## What Changes

- New display surfaces in `marshal.yaml` (`displays.surfaces`): a name and a Home Assistant `media_player` entity each. Satellite profiles gain `display: <name>`, which maps a satellite to the screen in its room.
- `artifacts.show(id, page?)` becomes client-aware: a web turn keeps showing the artifact in the chat; a voice turn from a satellite with a display plays a media rendition of the artifact on that display through Home Assistant `media_player.play_media`. The display is fixed by the satellite profile; the model cannot choose a screen.
- Media renditions per kind: images as is (JPEG, PNG) or converted to PNG (GIF, WebP); MP4 video as is; code, diff and Markdown as monospace PNG pages (diff lines colored). Other kinds (3D models, SVG, PDF, HTML, WebM, other) have no rendition yet and fail with a clear error.
- Display links: signed, unauthenticated rendition URLs under a configurable base URL (`displays.mediaBaseUrl`, LAN HTTP now, HTTPS later), with their own lifetime (`displays.linkMinutes`, default 240) so a paused video keeps working, and a file extension in the path because renderers are picky.

## Capabilities

### New Capabilities

- `display-surfaces`: display configuration, media renditions, display links and playing an artifact on a display.

### Modified Capabilities

- `artifacts`: `artifacts.show` is visible in voice turns from satellites with a display and plays there.
- `satellite-policy`: satellite profiles gain `display`.

## Non-goals

- Browser displays (`kind: web`, Fully Kiosk, HA cast of the viewer). Media renderers only.
- 3D renditions (turntable video, still image): change `artifact-3d-render`.
- Choosing a display by name in the tool ("show it on the office TV"), and showing from background tasks.
- Stopping or controlling playback through Marshal; the TV remote does that.
- Syntax highlighting in code pages.

## Impact

- `src/Marshal.Artifacts`: rendition rendering (SkiaSharp, bundled monospace font), `artifacts.show` changes.
- `src/Marshal.Integrations.HomeAssistant`: playing a URL on a media player through `media_player.play_media`.
- `src/Marshal.Core` satellites: `display` in profiles, validated against the configured surfaces.
- `src/Marshal.Api`: signed rendition endpoint (range requests, no sign-in).
- `deploy/config.example`: `displays` section in `marshal.yaml`, `display` in `satellites.yaml`.
- Package diagram: SkiaSharp also referenced by `Marshal.Artifacts`.
