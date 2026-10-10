# display-surfaces Specification

## Purpose
Screens in the house (Home Assistant media players) on which Marshal shows artifacts as media renditions, mapped to the satellites in the same room.

## Requirements

### Requirement: Display configuration
Displays SHALL be configured in `marshal.yaml` as `displays.surfaces`, a map from display name (`[a-z][a-z0-9_]*`) to `{ entity }`, where `entity` is a Home Assistant `media_player` entity id. `displays.mediaBaseUrl` SHALL be the absolute `http` or `https` URL at which displays reach Marshal; when it is not set, `notifications.publicUrl` SHALL be used. Configuring a display without either URL, a display name that does not match the pattern, or an entity outside the `media_player` domain SHALL fail startup with a configuration error naming the key.

#### Scenario: LAN base URL
- **WHEN** `displays.mediaBaseUrl` is `http://192.168.1.10:8080/` and a display `living_tv` is configured
- **THEN** display links for `living_tv` start with `http://192.168.1.10:8080/`

#### Scenario: Missing base URL
- **WHEN** a display is configured and neither `displays.mediaBaseUrl` nor `notifications.publicUrl` is set
- **THEN** startup fails with an error naming `displays.mediaBaseUrl`

#### Scenario: Wrong entity domain
- **WHEN** a display's entity is `light.living_room`
- **THEN** startup fails with an error naming `displays.surfaces.<name>.entity`

### Requirement: Media renditions
Each artifact kind SHALL have at most one media rendition that a media renderer can play:
- `image`: JPEG and PNG as stored; GIF and WebP converted to PNG (first frame);
- `video`: MP4 as stored;
- `code`, `diff`, `markdown`: the text rendered as PNG pages of at most 40 lines of monospace text on a dark background, 1920×1080 pixels, with the artifact name and page number in a header; highlighted as defined in `code-highlighting` with the dark theme's token colors; `code` with indent guides; in `diff`, added rows on a green and removed rows on a red background with a colored marker; lines longer than the page wrap, and a wrapped row keeps its colors but has no indent guides;
- `model3d`: two PNG pages of 1920×1080 pixels on the same dark background, with the artifact name, page number and the model's bounding box size in millimetres in a header. Page 1 is one shaded view from the front-right above. Page 2 is a sheet of four views: front, right, top and that perspective view. The model SHALL be centered and scaled to fill its view. Surfaces SHALL use the material or object colors from a 3MF file when present, else one default color, shaded by their angle to a fixed light. An STL or 3MF file that cannot be read, has no triangles, or has more than 5 million triangles SHALL have no rendition;
- every other kind and WebM video: no rendition.
A rendition SHALL be produced from the stored file only; renditions SHALL NOT be stored as artifacts.

#### Scenario: Code pages
- **WHEN** a 130-line `.cs` artifact is rendered
- **THEN** it has 4 PNG pages, the second one showing lines 41 to 80

#### Scenario: Highlighted code on the TV
- **WHEN** a `.cs` artifact containing `public sealed class Program` is rendered
- **THEN** `public` is drawn in the keyword color and `Program` in the type color

#### Scenario: Diff on the TV
- **WHEN** a diff of `src/App.cs` with the added line `+var x = 1;` is rendered
- **THEN** the row has the added background and `var` is drawn in the keyword color

#### Scenario: 3D model pages
- **WHEN** a binary STL of a 40 × 20 × 10 mm bracket is rendered
- **THEN** it has 2 PNG pages, the header shows `40.0 × 20.0 × 10.0 mm`, and page 2 shows four views

#### Scenario: 3MF colors
- **WHEN** a 3MF file with a red and a blue object is rendered
- **THEN** both objects are drawn in shades of their colors

#### Scenario: No rendition for a 3D model
- **WHEN** the rendition of a file named `part.stl` with no triangles is requested
- **THEN** the result is a failure with code `artifacts.no_rendition`

### Requirement: Display links
Display links SHALL be absolute URLs under the media base URL, require no sign-in, be bound to one owner, artifact and page, and end in the rendition's file extension (`.png`, `.jpg`, `.mp4`). They SHALL expire after `displays.linkMinutes` (default 240). An expired, altered or foreign link, or one for a deleted artifact, SHALL get 404. Responses SHALL support HTTP range requests and `HEAD`, carry the rendition's media type and `X-Content-Type-Options: nosniff`.

#### Scenario: Paused video resumes
- **WHEN** a TV requests a byte range of an MP4 display link issued 90 minutes ago
- **THEN** the response is 206 with that range

#### Scenario: Expired link
- **WHEN** a display link issued 241 minutes ago is requested with the default lifetime
- **THEN** the response is 404

#### Scenario: Altered page
- **WHEN** the page number in a display link is changed
- **THEN** the response is 404

### Requirement: Playing on a display
Showing an artifact on a display SHALL call Home Assistant's `media_player.play_media` for the display's entity with a display link as content id and the content type `image` or `video`. A failure from Home Assistant SHALL be returned to the model as `display.failed` naming the display; an unreachable Home Assistant SHALL fail with `ha.unreachable`.

#### Scenario: Image on the TV
- **WHEN** a JPEG artifact is shown on display `living_tv` with entity `media_player.living_room_tv`
- **THEN** HA receives `media_player.play_media` for `media_player.living_room_tv` with content type `image` and a display link ending in `.jpg`

#### Scenario: TV switched off
- **WHEN** HA rejects the call because the TV is unavailable
- **THEN** the tool fails with `display.failed` naming `living_tv`
