---
icon: fa-solid fa-tower-broadcast
description: "/config/satellites.yaml enthält optionale Profile pro Satellit, mit der Entitäts-ID des Satelliten als Schlüssel."
---

# `satellites.yaml`

`/config/satellites.yaml` enthält optionale Profile pro Satellit, mit der Entitäts-ID des Satelliten als Schlüssel. Marshal lädt die Datei ohne Neustart neu.

Ein Profil ist eine bequeme Einschränkung für fest installierte Geräte, keine Grenze der Autorisierung. Nicht gelistete Satelliten und Turns ohne `satelliteId` nutzen die Standardwerte: normale Ausführlichkeit, keine verweigerten Werkzeuge, kein Display, kein Lautsprecher.

## Beispiel

```yaml
satellites:
  assist_satellite.kitchen:
    verbosity: short
    notes: "Cooking area. Timer and recipe questions are common."
    display: kitchen_tv
    speaker: kitchen
  assist_satellite.kids_room:
    verbosity: short
    denyConfirm: [printer.*, calendar.*]
    denyTools: [mail.*]
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `satellites` | Abbildung | `{}` | Profile mit der Entitäts-ID des Satelliten als Schlüssel. |
| `satellites.<id>.verbosity` | `short`, `normal` oder `detailed` | `normal` | Wie lang Antworten von diesem Satelliten sind. |
| `satellites.<id>.notes` | Zeichenkette | keiner | Freitext, der den Zeilen zum Satellitenkontext im System-Prompt hinzugefügt wird. |
| `satellites.<id>.denyTools` | Liste von Globs | `[]` | Globs auf Werkzeug-IDs, die bei Turns von diesem Satelliten vor dem Modell verborgen und vom Guard verweigert werden. |
| `satellites.<id>.denyConfirm` | Liste von Globs | `[]` | Globs auf Werkzeug-IDs, deren Aufrufe der Bestätigungsstufe hier nicht erlaubt sind. Werkzeuge der Bestätigungsstufe werden verborgen. Argumentabhängige Werkzeuge (zum Beispiel `ha.*`) bleiben sichtbar; ein Aufruf, der zu `confirm` wird, wird mit „not allowed from this device“ verweigert und erzeugt keine ausstehende Aktion. |
| `satellites.<id>.display` | Zeichenkette | keiner | Name einer Fläche aus [`displays.surfaces`](marshal-yaml/displays.md) im Raum des Satelliten. `artifacts_show` spielt dort ab. |
| `satellites.<id>.speaker` | Zeichenkette | keiner | Name eines Lautsprechers aus `music.speakers` (siehe [`integrations`](marshal-yaml/integrations.md)) im Raum des Satelliten. Die Werkzeuge `music_*` nutzen ihn standardmäßig. |

Bereich und Stockwerk des Satelliten, Ortszeit, Wochentag und Sprache kommen pro Turn von Home Assistant, nicht aus dieser Datei.

Von einem Satelliten gestartete Aufgabenläufe behalten die Richtlinie dieses Satelliten für den ganzen Aufgabenbaum.

## Validierung

Fehler nennen Datei und Schlüssel. Ein scheiterndes erstes Laden stoppt den Start; ein scheiterndes Neuladen behält die vorherigen Profile.

- `satellites.yaml: satellites.<id>.verbosity must be one of short, normal, detailed.`
- `satellites.yaml: satellites.<id>.display names no configured display '<name>' (displays.surfaces).`
- `satellites.yaml: satellites.<id>.speaker names no configured speaker '<name>' (music.speakers).`
- Unbekannte Schlüssel, ungültiges YAML oder ein Wert, der wie ein Zugangsdatum aussieht, werden abgelehnt.

## Verwandte Seiten

::: cards
- [Anleitung zu Satelliten](../guides/voice/satellites.md)
- [`risk.yaml`](risk-yaml.md)
- [Sichtbarkeit von Werkzeugen](../concepts/tool-visibility.md)
:::
