---
icon: fa-solid fa-chart-line
description: "Die OpenTelemetry-Instrumente, die Marshal ausgibt."
---

# Metriken und Traces

Die OpenTelemetry-Instrumente, die Marshal ausgibt. Traces folgen den OpenTelemetry GenAI Semantic Conventions in Version 1.41. Marshals eigene Metriken kommen aus den Daten, die die Aufzeichnung der Durchläufe speichert, sie passen also immer zu den Datensätzen der Durchläufe.

## Export

Setz `telemetry.otlpEndpoint` in `marshal.yaml`, um über OTLP gRPC zu exportieren, zum Beispiel `http://otel-collector:4317`. Ohne den Schlüssel gibt Marshal die Instrumente aus, exportiert aber nichts. Siehe [`instance`, `paths`, `logging`, `telemetry`](../configuration/marshal-yaml/instance.md).

| Name | Art |
|---|---|
| `Marshal.Runs` | Activity Source: Spans `invoke_agent` und `execute_tool` |
| `Experimental.Microsoft.Extensions.AI` | Activity Source und Meter: Spans `chat` und `embeddings`, Metriken `gen_ai.client.*` |
| `Experimental.ModelContextProtocol` | Activity Source: Spans des MCP-Clients |
| `Marshal` | Meter: Marshals Metriken der Durchläufe |

Das Ressourcenattribut `marshal.genai_semconv_version` nennt die Version der Conventions (`1.41`). Die Conventions haben noch den Status Development, eine spätere Version kann also Attribute umbenennen; Marshal aktualisiert die Version mit dem Paket `Microsoft.Extensions.AI`.

Ohne `telemetry.captureContent` tragen Traces und Metriken nur IDs und Metadaten, nie Nachrichteninhalte, Werkzeugargumente oder Ergebnisse. Siehe [Inhalte erfassen](#inhalte-erfassen).

## Metriken

Der Meter `Marshal` hat vier Instrumente.

| Instrument | Typ | Einheit | Tags | Beschreibung |
|---|---|---|---|---|
| `marshal.runs` | Counter (Ganzzahl) | keine | `outcome` | Abgeschlossene Durchläufe nach Ergebnis. |
| `marshal.model.tokens` | Counter (Ganzzahl) | `{token}` | `direction` | Modell-Tokens nach Richtung. Zählt jeden aufgezeichneten Schritt mit Token-Daten. |
| `marshal.tool.calls` | Counter (Ganzzahl) | keine | `decision` | Werkzeugaufrufe nach Entscheidung des Guards. |
| `marshal.run.cost` | Histogramm (double) | keine | keine | Kosten pro Durchlauf, berechnet aus den `price`-Einstellungen der genutzten Modelle. |

Werte der Tags:

| Tag | Werte |
|---|---|
| `outcome` | `ok`, `error`, `backgrounded`, `cancelled` |
| `direction` | `input`, `output` |
| `decision` | `safe`, `pending`, `confirmed_by_keyword`, `judge_positive`, `judge_neutral`, `judge_negative`, `escalated_by_taint`, `denied`, `expired`, `confirmed_in_ui` (siehe [Risikostufen](risk-tiers.md#entscheidungen-des-guards)) |

Marshal erfasst `marshal.runs` und `marshal.run.cost`, wenn ein Durchlauf endet. `marshal.model.tokens` und `marshal.tool.calls` erfasst es, sobald ein Schritt aufgezeichnet wird.

Der Meter `Experimental.Microsoft.Extensions.AI` ergänzt pro Modell- und Embedding-Aufruf die Metriken der Conventions `gen_ai.client.token.usage` und `gen_ai.client.operation.duration`. Der Token-Verbrauch erscheint daher doppelt: `marshal.model.tokens` passt zur Ansicht der Durchläufe, `gen_ai.client.token.usage` ist die Standardmetrik für GenAI-Dashboards.

## Traces

Jeder Durchlauf ist ein Trace. Die Spans existieren, während der Durchlauf läuft, Modellaufrufe und MCP-Anfragen hängen also an ihnen.

```
invoke_agent default                 Marshal.Runs
├── chat qwen3                       Experimental.Microsoft.Extensions.AI
├── execute_tool ha_HassTurnOn       Marshal.Runs
│   └── tools/call                   Experimental.ModelContextProtocol
└── chat qwen3
```

### Spans

| Span | Quelle | Beschreibung |
|---|---|---|
| `invoke_agent <template>` | `Marshal.Runs` | Der ganze Durchlauf. Ein Durchlauf eines Subagenten ist ein untergeordneter `invoke_agent`-Span. |
| `chat <model>` | `Experimental.Microsoft.Extensions.AI` | Ein Modellaufruf, einschließlich der Aufrufe für strukturierte Ausgabe und Zusammenfassung im Durchlauf. |
| `embeddings <model>` | `Experimental.Microsoft.Extensions.AI` | Ein Embedding-Aufruf. |
| `execute_tool <tool>` | `Marshal.Runs` | Ein Werkzeugaufruf, von der Entscheidung des Guards bis zum Ergebnis. Eine eingefrorene Aktion, die nach einer Bestätigung läuft, ist ein `execute_tool`-Span des bestätigenden Durchlaufs. |
| Span des MCP-Clients | `Experimental.ModelContextProtocol` | Eine Anfrage an einen MCP-Server. Ihr Trace-Kontext geht in `_meta` an den Server (`traceparent`, `tracestate`). |

### Attribute von `invoke_agent`

| Attribut | Beschreibung |
|---|---|
| `gen_ai.operation.name` | `invoke_agent`. |
| `gen_ai.agent.name` | Das Agent-Template, `default` bei Turns einer Unterhaltung. |
| `gen_ai.conversation.id` | Die Sitzungs-ID, wenn der Durchlauf zu einer Sitzung gehört. |
| `marshal.run_id` | Die ID des Durchlaufs. Passt zum Durchlauf in der Weboberfläche. |
| `marshal.source` | `voice`, `web`, `task`, `api` oder `automation`. |
| `marshal.input_mode` | `text` oder `voice`. |
| `marshal.trigger` | Was den Durchlauf gestartet hat, zum Beispiel `turn`, `task`, `continuation`, `confirmation` oder `delegate`. |
| `marshal.outcome` | `ok`, `error`, `backgrounded` oder `cancelled`. Das Ergebnis `error` setzt auch den Status des Spans auf Fehler. |
| `marshal.outcome_reason` | Der Grund für das Ergebnis, wenn es einen gibt. |
| `marshal.tainted` | `true`, wenn der Durchlauf nicht vertrauenswürdige Inhalte gelesen hat. |

### Attribute von `execute_tool`

| Attribut | Beschreibung |
|---|---|
| `gen_ai.operation.name` | `execute_tool`. |
| `gen_ai.tool.name` | Der Werkzeugname, den das Modell sieht, zum Beispiel `ha_HassTurnOn`. |
| `gen_ai.tool.call.id` | Die Aufruf-ID des Modells. |
| `gen_ai.tool.type` | `function`. |
| `marshal.tool` | Die interne Werkzeug-ID (siehe [Werkzeuge](tools.md)). |
| `marshal.risk_tier` | `safe`, `confirm` oder `forbidden`. |
| `marshal.guard_decision` | Die Entscheidung des Guards. |
| `error.type` | Der Fehlercode, wenn das Werkzeug gescheitert ist. Der Status des Spans ist dann Fehler. Eine Ablehnung durch den Guard ist kein Fehler. |

Die Spans `chat` und `embeddings` tragen die Attribute der Conventions aus `Microsoft.Extensions.AI`, darunter `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.usage.input_tokens` und `gen_ai.usage.output_tokens`. Marshal schaltet ihren Modus für sensible Daten ab, sie tragen also nie Nachrichten.

Attribute ohne Wert entfallen.

### Inhalte erfassen

Mit `telemetry.captureContent: true` ergänzt Marshal die Inhalte, die die Aufzeichnung der Durchläufe speichert. Inhalte werden wie die Nutzdaten von Schritten gekürzt, sensible Ergebnisse erscheinen als ihre Referenz, und zurückgehaltenes Reasoning fehlt.

| Attribut | Span | Inhalt |
|---|---|---|
| `marshal.input` | `invoke_agent` | Die Eingabe des Benutzers im Turn. |
| `marshal.reply` | `invoke_agent` | Die Antwort. |
| `gen_ai.tool.call.arguments` | `execute_tool` | Die Werkzeugargumente als JSON. |
| `gen_ai.tool.call.result` | `execute_tool` | Das Ergebnis oder bei einem sensiblen Werkzeug die Referenz. |

Kein Span trägt den Systemprompt oder die vollständigen Nachrichten an das Modell.

## Beispielabfragen

| Frage | Idee für die Abfrage |
|---|---|
| Scheitern Durchläufe? | Rate von `marshal.runs{outcome="error"}`. |
| Hat eine Konfigurationsänderung Werkzeuge blockiert? | Rate von `marshal.tool.calls{decision="denied"}`. |
| Wie viel gebe ich pro Durchlauf aus? | Perzentile von `marshal.run.cost`. |
| Welches Werkzeug ist langsam? | Dauer der `execute_tool`-Spans, gruppiert nach `marshal.tool`. |
| Welches Modell ist langsam? | `gen_ai.client.operation.duration`, gruppiert nach `gen_ai.request.model`. |

## Verwandte Seiten

::: cards
- [Beobachtbarkeit](../guides/observability.md)
- [Endpunkte für Zustand und Erkennung](health-endpoints.md)
- [Risikostufen](risk-tiers.md)
:::
