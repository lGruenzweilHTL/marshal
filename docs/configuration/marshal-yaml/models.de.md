---
icon: fa-solid fa-microchip
description: "Die Chat-Modelle, die Marshal nutzen kann, gruppiert in Stufen."
---

# `models`

Die Chat-Modelle, die Marshal nutzen kann, gruppiert in Stufen. Änderungen brauchen einen Neustart.

## Beispiel

```yaml
models:
  fast:
    tier: fast
    provider: ollama
    model: "qwen3:8b"
    endpoint: "http://ollama:11434"
    contextLength: 32768
  advisor:
    tier: advisor
    provider: anthropic
    model: claude-sonnet-4-5
    apiKeySecret: anthropic_key
    contextLength: 200000
    price: { inputPerMTok: 3, outputPerMTok: 15, cachedPerMTok: 0.3 }
```

## Schlüssel

`models` ist eine Abbildung. Der Schlüssel (`fast`, `advisor`, ...) ist ein frei gewählter Name. Jeder Eintrag `models.<key>`:

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `models.<key>.tier` | `fast`, `standard`, `advisor` | `fast` | Stufe des Modells. `fast`: Sprach-Turns, der Bestätigungsprüfer, günstige Klassifikation. `standard`: normale Arbeit. `advisor`: nur über `agent.ask_advisor` befragt. |
| `models.<key>.provider` | Zeichenkette | keiner (Pflicht) | `ollama`, `openai-compatible` (llama.cpp-`llama-server`, vLLM, LM Studio) oder `anthropic`. |
| `models.<key>.model` | Zeichenkette | keiner (Pflicht) | Modellname beim Anbieter, zum Beispiel `qwen3:8b`. |
| `models.<key>.endpoint` | URL | nicht gesetzt | Server-URL. Pflicht bei `openai-compatible`. |
| `models.<key>.apiKeySecret` | Secret-Name | nicht gesetzt | Name des Secrets mit dem API-Schlüssel. Pflicht bei `anthropic`. |
| `models.<key>.vision` | Boolean | `false` | Das Modell nimmt Bilder an. |
| `models.<key>.tools` | Boolean | `true` | Das Modell unterstützt Werkzeugaufrufe. |
| `models.<key>.contextLength` | Ganzzahl | `8192` | Kontextfenster in Tokens. |
| `models.<key>.reasoning` | `off`, `low`, `medium`, `high` | `off` | Aufwand für Reasoning in Text-Turns und Aufgabenläufen im Hintergrund. Sprach-Turns fragen wegen der Latenzgrenze für Sprache nie nach Reasoning. Der Web-Chat zeigt das Reasoning, während es streamt, und die Details eines Laufs behalten es. Reasoning-Tokens zählen als Ausgabe-Tokens. |
| `models.<key>.price.inputPerMTok` | Dezimalzahl | `0` | Kosten pro Million Eingabe-Tokens. |
| `models.<key>.price.outputPerMTok` | Dezimalzahl | `0` | Kosten pro Million Ausgabe-Tokens. |
| `models.<key>.price.cachedPerMTok` | Dezimalzahl | Eingabepreis | Kosten pro Million gecachter Eingabe-Tokens. |

Ohne `price` kostet ein Modell 0. Der Standard `fast` für `tier` ist der Standard der Aufzählung; setz den Schlüssel ausdrücklich.

## Routing

Eine Anfrage nennt eine Stufe plus benötigte Fähigkeiten (`vision`, `tools`, eine Mindestlänge des Kontexts). Der Router wählt das erste Modell in Dateireihenfolge, dessen Stufe passt und das alle Fähigkeiten hat. Ohne Treffer scheitert der Aufruf mit `model.not_available`, und kein Modellaufruf wird gemacht.

## Unterstützung für Reasoning

| Anbieter | Was Marshal schickt | Hinweise |
|---|---|---|
| `ollama` | das Flag `think` | Denkende Modelle (Qwen 3, DeepSeek-R1, gpt-oss und andere). |
| `openai-compatible` | `reasoning_effort` | Der Server muss `reasoning_content` liefern (llama.cpp mit `--reasoning-format`, vLLM mit einem Reasoning-Parser). |
| `anthropic` | adaptives Thinking mit dem Aufwand, zusammengefasste Anzeige | Claude 4.6 und neuer. Ältere Claude-Modelle (zum Beispiel Haiku 4.5) laufen ohne Reasoning, und der Start schreibt eine Warnung ins Log. |

## Validierung

Die Meldungen nennen den Modellschlüssel:

- `models.<key>.provider must be one of ollama, openai-compatible, anthropic.`
- `models.<key>.model is required.`
- `models.<key>.apiKeySecret is required for anthropic models.`
- `models.<key>.endpoint is required for openai-compatible models.`
- `models.<key>.reasoning must be one of off, low, medium, high.`
- `models.<key>.contextLength must be positive.`
- Ein Modell mit `apiKeySecret`, dessen Secret sich nicht auflösen lässt, lässt den Start scheitern.

## Verwandte Seiten

::: cards
- [`embedding`](embedding.md)
- [`agent`](agent.md)
  Grenzen des Advisors
- [Modelle einrichten](../../installation/models-setup.md)
:::
