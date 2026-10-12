# Show artifacts on a TV

Let Marshal show images, video, code, diffs, Markdown and 3D models on a screen in your home, and tie each screen to the satellite in the same room.

## Before you start

- Home Assistant is connected.
- The screen is a Home Assistant `media_player` entity, for example a TV through the DLNA Digital Media Renderer integration.
- The screen can fetch media from Marshal: you know an address it can reach (see step 1).

## Steps

### 1. Choose the media base URL

The TV fetches media itself, so `displays.mediaBaseUrl` must be an address the TV reaches. DLNA renderers prefer plain HTTP and an IP address. Publish Marshal's HTTP port on the LAN, for example `ports: ["8080:8080"]` in `docker-compose.yml`. An https URL with a certificate the TV trusts also works. Without `mediaBaseUrl`, Marshal uses `notifications.publicUrl`.

### 2. Configure the display

```yaml
displays:
  mediaBaseUrl: http://192.168.1.10:8080/
  linkMinutes: 240
  surfaces:
    living_tv: { entity: media_player.living_room_tv }
```

Names use lowercase letters, digits and `_`, starting with a letter. `linkMinutes` is 5 to 1440. Restart Marshal. Startup fails, naming the key, when a display has no base URL to use, a bad name or an entity outside `media_player`. See [`displays`](../configuration/marshal-yaml/displays.md).

### 3. Tie the display to a satellite

In `/config/satellites.yaml`:

```yaml
satellites:
  assist_satellite.living_room:
    display: living_tv
```

See [satellite profiles](voice/satellites.md). The model cannot pick another display; a satellite shows on its own display only.

## Check that it works

Put an image into [artifacts](artifacts.md) (upload it in the web UI), then say to the living-room satellite: "Show the image <name> on the TV." The picture appears on the screen.

## What each kind looks like

| Artifact kind | On the display |
|---|---|
| `image` | JPEG and PNG as stored; GIF and WebP as PNG (first frame). |
| `video` | MP4 as stored. WebM has no rendition. |
| `code`, `diff`, `markdown` | PNG pages, 1920x1080, up to 40 lines of monospace text on a dark background, with name and page number in a header. Syntax highlighting as in the web viewer; indent guides for code; green and red rows for added and removed diff lines. Long lines wrap. |
| `model3d` (STL, 3MF) | Two PNG pages. Page 1 is a shaded view from front-right above. Page 2 is a sheet of front, right, top and perspective views. The header shows the bounding box in millimetres. 3MF colors are kept. |
| Other kinds | No rendition; Marshal says which kinds the display can show. |

Ask for a page: "Show page 2." A file that cannot be read, has no triangles or has more than 5 million triangles has no rendition.

## Display links

Marshal gives the TV an absolute link under the media base URL. It needs no sign-in, is bound to one owner, artifact and page, and expires after `linkMinutes` (default 240, long enough to pause a video). Expired, altered or foreign links get 404. Links support range requests, so a paused video resumes.

## Troubleshooting

- `display.failed` naming the display: Home Assistant refused the call, often because the TV is off or unavailable.
- `ha.unreachable`: Marshal cannot reach Home Assistant.
- The TV shows nothing: it cannot reach `mediaBaseUrl`. Test the address from another device on the LAN.
- `artifacts.no_rendition`: the artifact kind has no screen rendition.
- `artifacts_show` is missing in voice turns: the satellite's profile has no `display`.

## Related

- [Artifacts](artifacts.md)
- [Satellites](voice/satellites.md)
- [Tool visibility](../concepts/tool-visibility.md)
