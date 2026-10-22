---
icon: fa-solid fa-bell
description: "Routing, Lärmkontrolle und Einstellungen für Rückfragen bei proaktiven Benachrichtigungen."
---

# `notifications`

Routing, Lärmkontrolle und Einstellungen für Rückfragen bei proaktiven Benachrichtigungen.

## Beispiel

```yaml
notifications:
  quietHoursMode: hold
  coalesceWindow: "00:02:00"
  sourceVoiceInterval: "00:30:00"
  voicePerHour: 6
  followUpReplyWindow: "00:00:20"
  publicUrl: https://marshal.example.com/
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `notifications.quietHoursMode` | `hold` oder `silentPush` | `hold` | Was mit `normal`-Benachrichtigungen während der Ruhezeiten passiert. `hold`: zustellen, wenn die Ruhezeiten enden. `silentPush`: sofort als stiller Push zustellen. |
| `notifications.coalesceWindow` | Dauer | `00:02:00` | `normal`-Benachrichtigungen innerhalb dieses Fensters werden zu einer Nachricht. `00:00:00` schaltet das aus. Bereich 0 bis 30 Minuten. |
| `notifications.sourceVoiceInterval` | Dauer | `00:30:00` | Höchstens eine Sprachbenachrichtigung pro Quelle in diesem Intervall. Spätere werden gepusht. |
| `notifications.voicePerHour` | Ganzzahl | `6` | Unaufgeforderte Sprachbenachrichtigungen pro Stunde. Darüber hinaus werden sie gepusht. Nicht negativ. |
| `notifications.followUpTtl` | Dauer | `00:02:00` | Wie lange eine gesprochene Frage auf ihre Antwort wartet. Muss positiv sein. |
| `notifications.followUpReplyWindow` | Dauer | `00:00:20` | Ein Sprach-Turn so kurz nach einer gesprochenen Frage vom selben Satelliten zählt als Antwort, wenn der Satellit keinen Verweis auf die Rückfrage schickt. `00:00:00` schaltet das aus. Zwischen 0 und `followUpTtl`. |
| `notifications.publicUrl` | URL | nicht gesetzt | Die öffentliche URL von Marshal. Push-Benachrichtigungen verlinken damit in die Weboberfläche; ohne sie tragen sie keinen Link. Auch der Ersatz für `displays.mediaBaseUrl`. |

`critical`-Benachrichtigungen ignorieren Ruhezeiten und Grenzen.

## Validierung

- `notifications.coalesceWindow must be between 0 and 30 minutes.`
- `notifications.followUpTtl must be positive.`
- `notifications.followUpReplyWindow must be between 0 and followUpTtl.`
- `notifications.voicePerHour cannot be negative.`
- `notifications.publicUrl must be an absolute http(s) URL.`

## Verwandte Seiten

::: cards
- [Anleitung zu Benachrichtigungen](../../guides/notifications.md)
- [Prioritäten von Benachrichtigungen](../../reference/notifications-priorities.md)
- [`displays`](displays.md)
- [Gesprochene Rückfragen](../../guides/voice/follow-ups.md)
:::
