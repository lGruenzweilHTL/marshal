---
icon: fa-solid fa-wand-magic-sparkles
description: "Grenzen für AI-Tasks, die Durchläufe, die Automationen in Home Assistant über die AI-Task-Entität von Marshal starten (POST /api/v1/ai-tasks)."
---

# `aiTasks`

Grenzen für AI-Tasks, die Durchläufe, die Automationen in Home Assistant über die AI-Task-Entität von Marshal starten (`POST /api/v1/ai-tasks`). Änderungen brauchen einen Neustart.

## Beispiel

```yaml
aiTasks:
  timeoutSeconds: 60
  maxAttachments: 4
  maxAttachmentBytes: 10485760
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `aiTasks.timeoutSeconds` | Ganzzahl | `60` | Eine Aufgabe, die nach so vielen Sekunden noch läuft, wird abgebrochen und mit `504` (`ai_task.timeout`) beantwortet. Bereich 1 bis 3600. Die Home-Assistant-Komponente wartet 75 Sekunden, bleib also darunter. |
| `aiTasks.maxAttachments` | Ganzzahl | `4` | Höchstzahl der Bildanhänge pro Aufgabe. Mehr werden mit `413` beantwortet. Bereich 1 bis 32. |
| `aiTasks.maxAttachmentBytes` | Ganzzahl | `10485760` (10 MB) | Größte Größe eines Anhangs in Bytes. Ein größerer wird mit `413` beantwortet. Höchstens `26214400` (25 MB). |

Die Grenze für den Request-Body ist `maxAttachments` × `maxAttachmentBytes` plus 64 KB für den Aufgabenteil.

AI-Tasks zählen auch gegen [`tasks.dailyCostCap`](tasks.md): Ist die Grenze ausgeschöpft, wird eine Aufgabe mit `429` (`ai_task.daily_cap`) beantwortet. Modellstufe und Werkzeuge kommen aus dem [Template `ai_task`](../templates-format.md#eingebaute-templates); eine Aufgabe mit Bildern braucht ein Modell mit `vision: true` in dieser Stufe ([Modelle](models.md)).

## Validierung

- `aiTasks.timeoutSeconds must be between 1 and 3600.`
- `aiTasks.maxAttachments must be between 1 and 32.`
- `aiTasks.maxAttachmentBytes must be between 1 and 26214400.`

## Verwandte Seiten

::: cards
- [AI-Tasks aus Home Assistant](../../home-assistant-component/ai-tasks.md)
- [API für AI-Tasks](../../api/ai-tasks.md)
- [Agent-Templates](../templates-format.md)
:::
