---
icon: fa-solid fa-toolbox
description: "Welche Werkzeuge das Modell bei jedem Aufruf erreichen und wann ein Sprach-Turn vor einem langsamen Werkzeug einen kurzen Überbrückungssatz spricht."
---

# `tools` und `voice`

Welche Werkzeuge das Modell bei jedem Aufruf erreichen und wann ein Sprach-Turn vor einem langsamen Werkzeug einen kurzen Überbrückungssatz spricht. Änderungen brauchen einen Neustart.

## Beispiel

```yaml
tools:
  core: [agent, memory, tasks, notifications, timers, places]
  retrieval:
    enabled: true
    threshold: 0.30
    maxPerTurn: 3
    minTools: 24

voice:
  fillerThresholdMs: 700
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `tools.core` | Liste von Namensräumen | `[agent, memory, tasks, notifications, timers, places]` | Namensräume, die in jeder Sitzung geladen sind. Ihre Werkzeuge stehen zuerst in der Werkzeugliste, in dieser Reihenfolge. Den Schlüssel zu setzen ersetzt die ganze Standardliste. |
| `tools.retrieval.enabled` | Boolean | `true` | `false` bietet bei jedem Modellaufruf jedes sichtbare Werkzeug an, wie vor der Werkzeugsuche. |
| `tools.retrieval.threshold` | Zahl | `0.30` | Kleinste Kosinus-Ähnlichkeit zwischen Turn-Text und der Beschreibung eines Namensraums, damit der Namensraum geladen wird. Hängt vom Embedding-Modell ab. Bereich 0 bis 1. |
| `tools.retrieval.maxPerTurn` | Ganzzahl | `3` | Höchstzahl der Namensräume, die ein Turn durch die Suche lädt, und Höchstzahl der Namensräume, die ein Aufruf von `agent.find_tools` lädt. Bereich 1 bis 20. |
| `tools.retrieval.minTools` | Ganzzahl | `24` | Die Suche entfällt, wenn nicht mehr Werkzeuge als diese Zahl sichtbar sind, zum Beispiel bei einem Template, das nur wenige Werkzeuge erlaubt. Bereich 0 bis 1000. |
| `voice.fillerThresholdMs` | Ganzzahl | `700` | Ein Sprach-Turn spricht zuerst den Überbrückungssatz eines Werkzeugs, wenn das Werkzeug voraussichtlich länger als diese Zeit braucht. Bereich 1 bis 60000. |

Die Suche engt nur die Werkzeuge ein, die der Sichtbarkeitsfilter schon erlaubt. Sie zeigt nie ein Werkzeug, das ein Template, ein Satellit oder eine Risikoregel verbirgt. Siehe [Sichtbarkeit von Werkzeugen](../../concepts/tool-visibility.md#werkzeugsuche).

## Validierung

- `tools.retrieval.threshold must be between 0 and 1.`
- `tools.retrieval.maxPerTurn must be between 1 and 20.`
- `tools.retrieval.minTools must be between 0 and 1000.`
- `voice.fillerThresholdMs must be between 1 and 60000.`

Ein Eintrag in `tools.core`, der weder ein eingebauter Namensraum noch der Namensraum eines konfigurierten MCP-Servers ist, lässt den Start nicht scheitern. `/health` zeigt ihn als Warnung unter `tools`.

## Verwandte Seiten

::: cards
- [MCP-Server](../mcp-servers.md)
  `description` für die Suche
- [Sichtbarkeit von Werkzeugen](../../concepts/tool-visibility.md)
- [`agent`](agent.md)
:::
