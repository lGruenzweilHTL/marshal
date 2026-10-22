---
icon: fa-solid fa-tv
description: "Bildschirme (Mediaplayer in Home Assistant), auf denen artifacts_show Bilder, Videos, 3D-Modelle und Codeseiten abspielt."
---

# `displays`

Bildschirme (Mediaplayer in Home Assistant), auf denen `artifacts_show` Bilder, Videos, 3D-Modelle und Codeseiten abspielt.

## Beispiel

```yaml
displays:
  mediaBaseUrl: http://192.168.1.10:8080/
  linkMinutes: 240
  surfaces:
    living_tv: { entity: media_player.living_room_tv }
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `displays.mediaBaseUrl` | URL | nicht gesetzt | Absolute http(s)-URL, unter der Displays Marshal erreichen. Der Fernseher lädt die Medien selbst, die Adresse muss also vom Fernseher aus erreichbar sein. DLNA-Renderer bevorzugen schlichtes HTTP und eine IP-Adresse. Nicht gesetzt: `notifications.publicUrl`. |
| `displays.linkMinutes` | Ganzzahl (Minuten) | `240` | Lebensdauer der Display-Links. Lang genug, um ein Video zu pausieren. |
| `displays.surfaces.<name>.entity` | Zeichenkette | keiner (Pflicht) | `media_player`-Entitäts-ID des Displays in Home Assistant. |

`<name>` ist der Display-Name, den `satellites.yaml` nutzt (`display: living_tv`).

## Validierung

- `displays.linkMinutes must be between 5 and 1440.`
- `displays.mediaBaseUrl must be an absolute http or https URL.`
- `displays.surfaces.<name> must be named with lowercase letters, digits and underscores, starting with a letter.`
- `displays.surfaces.<name>.entity must be a media_player entity id.`
- `displays.mediaBaseUrl is required when displays are configured (or set notifications.publicUrl).`

## Verwandte Seiten

::: cards
- [Anleitung zu Displays](../../guides/displays.md)
- [satellites.yaml](../satellites-yaml.md)
- [`notifications`](notifications.md)
:::
