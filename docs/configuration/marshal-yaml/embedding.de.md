---
icon: fa-solid fa-vector-square
description: "Das Embedding-Modell, von dem das Gedächtnis abhängt, und die Feinabstimmung von Gedächtnissuche, Zusammenfassung, Vorschlägen in Leerlaufzeiten und Projektaktivierung."
---

# `embedding` und `memory`

Das Embedding-Modell, von dem das Gedächtnis abhängt, und die Feinabstimmung von Gedächtnissuche, Zusammenfassung, Vorschlägen in Leerlaufzeiten und Projektaktivierung. Änderungen brauchen einen Neustart.

## Beispiel

```yaml
embedding:
  provider: ollama
  model: "bge-m3"
  endpoint: "http://ollama:11434"

memory:
  summarizer: { enabled: true, maxTurns: 50 }
  audit: { staleAfterDays: 180 }
  retrieval: { tokenBudget: 600 }
  duplicateThreshold: 0.92
```

## Schlüssel von `embedding`

`embedding` ist Pflicht. Nimm ein mehrsprachiges Modell (`bge-m3`, `multilingual-e5`, `nomic-embed-text-v2`), sonst verfehlt eine deutsche Frage einen englischen Gedächtniseintrag. Ein Wechsel des Modells berechnet beim nächsten Start alle Embeddings des Gedächtnisses neu.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `embedding.provider` | Zeichenkette | keiner (Pflicht) | `ollama` oder `openai-compatible`. Anthropic bietet keine Embeddings. |
| `embedding.model` | Zeichenkette | keiner (Pflicht) | Modellname beim Anbieter. |
| `embedding.endpoint` | URL | nicht gesetzt | Server-URL. Pflicht bei `openai-compatible`. |
| `embedding.apiKeySecret` | Secret-Name | nicht gesetzt | Secret mit dem API-Schlüssel, falls der Server einen braucht. |

## Schlüssel von `memory`

Alle optional. Die Standardwerte sind Ausgangspunkte. Nur `memory.duplicateThreshold` wird validiert: Der Start scheitert mit `memory.duplicateThreshold must be greater than 0 and at most 1.`

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `memory.retrieval.seedCount` | Ganzzahl | `20` | Startpunkte aus der Vektor- und aus der Schlüsselwortsuche, je. |
| `memory.retrieval.tokenBudget` | Ganzzahl | `600` | Token-Budget des Gedächtnisses, das jedem Turn hinzugefügt wird (angeheftete Einträge kommen dazu). |
| `memory.retrieval.recallTokenBudget` | Ganzzahl | `2000` | Token-Budget von `memory.recall`. |
| `memory.retrieval.minScore` | Zahl | `0.05` | Kandidaten unter diesem Wert werden verworfen. |
| `memory.retrieval.activeProjectBoost` | Zahl | `1.5` | Faktor für Einträge aktiver Projekte. |
| `memory.retrieval.inactiveProjectPenalty` | Zahl | `0.5` | Faktor für Einträge inaktiver Projekte. |
| `memory.retrieval.neighbourFactor` | Zahl | `0.7` | Ähnlichkeit von Nachbarn, die über Kanten erreicht werden, als Faktor ihres Startpunkts. |
| `memory.retrieval.halfLifeDays.<type>` | Zahl | event 30, fact 365, preference 365, person 730, procedure 730, device 730 | Halbwertszeit der Frische in Tagen pro Eintragstyp. |
| `memory.retrieval.quotas.<type>` | Ganzzahl | event 5, fact 6, preference 6, person 4, procedure 3, device 4 | Höchstzahl der Einträge pro Typ in einer Suche. |
| `memory.summarizer.enabled` | Boolean | `true` | Sitzungen beim Schließen ins Gedächtnis zusammenfassen (Stufe fast). |
| `memory.summarizer.maxTurns` | Ganzzahl | `50` | Die neuesten Turns, die die Zusammenfassung liest. |
| `memory.summarizer.memoryTokenBudget` | Ganzzahl | `1500` | Token-Budget des bestehenden Gedächtnisses, das die Zusammenfassung zu sehen bekommt. |
| `memory.audit.staleAfterDays` | Ganzzahl | `180` | Ein Eintrag, der so lange weder aktualisiert noch abgerufen wurde, ist veraltet. |
| `memory.duplicateThreshold` | Zahl | `0.92` | Embedding-Ähnlichkeit (größer als 0, höchstens 1), ab der der nächtliche Job zwei Einträge desselben Typs und Geltungsbereichs als Duplikate vorschlägt. |
| `memory.projects.highConfidence` | Zahl | `0.75` | Wert, der ein Projekt dauerhaft aktiviert. |
| `memory.projects.mediumConfidence` | Zahl | `0.6` | Wert, der ein Projekt für einen Turn aktiviert. |
| `memory.projects.negativeWeight` | Zahl | `0.5` | Gewicht der Ähnlichkeit zu einer abgelehnten Formulierung, das vom Wert abgezogen wird. |
| `memory.projects.recencyBoost` | Zahl | `0.05` | Wert, der für ein gerade aktives Projekt hinzukommt. |
| `memory.projects.recencyHalfLifeDays` | Zahl | `7` | Halbwertszeit des Aktualitätsbonus. |
| `memory.projects.stickyDecay` | Zahl | `0.7` | Faktor, mit dem das Gewicht einer dauerhaften Aktivierung jeden Turn multipliziert wird. |
| `memory.projects.stickyMinimum` | Zahl | `0.3` | Dauerhafte Aktivierungen unter diesem Gewicht fallen weg. |
| `memory.projects.topicShiftThreshold` | Zahl | `0.3` | Aufeinanderfolgende Turns, die weniger ähnlich sind, gelten als Themenwechsel. |
| `memory.projects.aliasPromotion` | Ganzzahl | `3` | Bestätigungen, die einen Alias-Kandidaten befördern. |
| `memory.projects.centroidTopChunks` | Ganzzahl | `10` | Die wichtigsten Projekteinträge, die in den Schwerpunkt gemittelt werden. |

Eintragstypen für `halfLifeDays` und `quotas`: `event`, `fact`, `preference`, `person`, `procedure`, `device`.

## Validierung

- `embedding.provider must be ollama or openai-compatible.`
- `embedding.model is required.`
- `embedding.endpoint is required for openai-compatible models.`
- Ein konfiguriertes `embedding.apiKeySecret` muss beim Start auflösbar sein.

## Verwandte Seiten

::: cards
- [`models`](models.md)
- [Gedächtnis](../../guides/memory.md)
- [Gedächtnismodell](../../concepts/memory-model.md)
:::
