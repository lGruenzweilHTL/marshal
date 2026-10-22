---
icon: fa-solid fa-tv
description: "Screens (Home Assistant media players) on which artifacts_show plays images, video, 3D models and code pages."
---

# `displays`

Screens (Home Assistant media players) on which `artifacts_show` plays images, video, 3D models and code pages.

## Example

```yaml
displays:
  mediaBaseUrl: http://192.168.1.10:8080/
  linkMinutes: 240
  surfaces:
    living_tv: { entity: media_player.living_room_tv }
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `displays.mediaBaseUrl` | URL | unset | Absolute http(s) URL at which displays reach Marshal. The TV fetches media itself, so the address must be reachable from the TV. DLNA renderers prefer plain HTTP and an IP address. Unset: `notifications.publicUrl`. |
| `displays.linkMinutes` | integer (minutes) | `240` | Lifetime of display links. Long enough to pause a video. |
| `displays.surfaces.<name>.entity` | string | none (required) | Home Assistant `media_player` entity id of the display. |

`<name>` is the display name used in `satellites.yaml` (`display: living_tv`).

## Validation

- `displays.linkMinutes must be between 5 and 1440.`
- `displays.mediaBaseUrl must be an absolute http or https URL.`
- `displays.surfaces.<name> must be named with lowercase letters, digits and underscores, starting with a letter.`
- `displays.surfaces.<name>.entity must be a media_player entity id.`
- `displays.mediaBaseUrl is required when displays are configured (or set notifications.publicUrl).`

## Related

::: cards
- [Displays guide](../../guides/displays.md)
- [Satellites.yaml](../satellites-yaml.md)
- [`notifications`](notifications.md)
:::
