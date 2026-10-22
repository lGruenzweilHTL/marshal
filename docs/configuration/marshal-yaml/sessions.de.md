---
icon: fa-solid fa-comments
description: "Wann eine Unterhaltungssitzung im Leerlauf schließt oder ruht."
---

# `sessions`

Wann eine Unterhaltungssitzung im Leerlauf schließt oder ruht.

## Beispiel

```yaml
sessions:
  idleTtl: "00:05:00"
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `sessions.idleTtl` | Dauer | `00:05:00` | Eine Sprach- oder API-Sitzung, die so lange keinen Turn hatte, wird geschlossen. Ein späterer Turn mit derselben Client-ID startet eine neue Sitzung mit neuer Bestimmung des Eigentümers. Eine Web-Chat-Sitzung ruht stattdessen: Ihre ausstehende Aktion wird verworfen, und ihre neuen Turns werden ins Gedächtnis zusammengefasst, aber du kannst sie jederzeit fortsetzen. Bereich `00:00:30` bis `1.00:00:00`. |

## Validierung

- `sessions.idleTtl must be between 00:00:30 and 1.00:00:00.`

## Verwandte Seiten

::: cards
- [Datenbesitz](../../concepts/data-ownership.md)
- [Gesprochene Rückfragen](../../guides/voice/follow-ups.md)
:::
