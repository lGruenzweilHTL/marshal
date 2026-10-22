---
icon: fa-solid fa-chart-line
description: "Sieh, was Marshal getan hat und was es gekostet hat: Durchläufe, Kosten, Zustand, das Prüfprotokoll und den Export nach OpenTelemetry."
---

# Beobachtbarkeit

Sieh, was Marshal getan hat und was es gekostet hat: Durchläufe, Kosten, Zustand, das Prüfprotokoll und den Export nach OpenTelemetry.

## Bevor du anfängst

- In der Weboberfläche angemeldet. Für OpenTelemetry einen OTLP-gRPC-Collector (zum Beispiel den OpenTelemetry Collector oder Grafana Alloy).
- Namen von Metriken und Traces: [Metriken und Traces](../reference/metrics-and-traces.md). Zustand: [Health-Endpunkte](../reference/health-endpoints.md).

## Einblick in Durchläufe in der Weboberfläche

Jeder Turn einer Unterhaltung und jeder Durchlauf einer Hintergrundaufgabe wird als Durchlauf aufgezeichnet.

| Seite | Zeigt |
|---|---|
| **Start** | Widgets für die letzten Durchläufe, die heutigen Kosten und den Systemzustand. Eine Warnung zum Zustand schiebt das Zustands-Widget nach oben. |
| **Läufe** | Alle Durchläufe (voice, web, task, api, automation) mit Filtern nach Quelle, Ergebnis und Kosten und der Zeit bis zum ersten Delta („erste Worte“), sortierbar mit den langsamsten zuerst. |
| **Detail eines Durchlaufs** | Den Kontextaufbau (mit aktiven Projekten und den geladenen Werkzeug-Namensräumen, jeweils mit Grund), eine Zeitleiste für Sprachdurchläufe (Kontextaufbau, Modellaufrufe, Werkzeugaufrufe, erste Worte, Ende), jeden Modellaufruf (Modell, Stufe, Tokens, Latenz, Zeit bis zum ersten Token, Kosten, Reasoning eingeklappt oder ein Hinweis, dass es nicht behalten wurde), jeden Werkzeugaufruf (Argumente, gekürztes oder geschwärztes Ergebnis, Risikostufe, Entscheidung des Guards, Gründe für die Bestätigung, Quellen der Belastung) und die Antwort. |
| **Kosten** | Kosten pro Turn, pro Tag und pro Stufe, mit dem Advisor in einer eigenen Zeile. Das sind die Zahlen, die die Budgets nutzen. |
| **System** | Details zum Zustand, App-Version und unterstützte API-Versionen, der Not-Aus und für Admins ein Link zur Konfigurationsansicht. |

Ergebnisse sind `ok`, `error`, `backgrounded` und `cancelled`. Ein belasteter Durchlauf ist markiert, mit dem ersten belastenden Schritt. Eine Entscheidung des Guards `escalated_by_taint` oder `denied` erklärt, warum eine Aktion eine Bestätigung brauchte oder nicht stattfand.

## Aufzeichnung und Schwärzung

Alle Aufzeichnungen laufen über einen Recorder, der die Datenbanktabellen schreibt und aus denselben Daten OpenTelemetry ausgibt. Nutzdaten der Schritte (Prompts, Werkzeugargumente und -ergebnisse, Antworten) bleiben `maintenance.payloadRetention` lang (30 Tage); Metadaten bleiben `maintenance.metadataRetention` lang (365 Tage). Zum Bereinigungsjob siehe [Backups und Wiederherstellung](backups-and-restore.md).

Ergebnisse von Werkzeugen, die als sensibel markiert sind (zum Beispiel Mail lesen), werden als Referenz gespeichert (Nachrichten-ID, Pfad), nicht als Inhalt. Anpassen kannst du das in [`recording`](../configuration/marshal-yaml/recording.md):

```yaml
recording:
  redactTools: ["notes.*"]      # immer nur als Referenz speichern
  keepContentTools: []          # vollständig speichern, auch wenn als sensibel markiert
```

Schritte von Modellaufrufen behalten das Reasoning des Modells. Sobald ein geschwärztes Werkzeugergebnis Teil eines Durchlaufs ist, wird das Reasoning späterer Modellaufrufe in diesem Durchlauf nicht gespeichert, weil es den geschwärzten Inhalt zitieren könnte; der Schritt hält nur fest, dass das Reasoning zurückgehalten wurde.

Längere Inhalte werden in den Nutzdaten der Schritte bei 8000 Zeichen gekürzt. Logs enthalten nie Secrets, Nachrichteninhalte, Notizinhalte, Reasoning oder vollständige Prompts.

## Prüfprotokoll

Aktionen außerhalb eines Durchlaufs landen im Prüfprotokoll, mit Art des Akteurs (`user`, `api_token`, `system`), Akteur-ID, Aktion, Ziel und Details: Aktionen in der Oberfläche, Freigaben, Änderungen an Tokens und Secrets, der Not-Aus, CLI-Befehle (zum Beispiel `token.create` mit der Akteursart `system`) und Änderungen am Gedächtnis wie `memory.chunk.pin`.

## Export nach OpenTelemetry

### Den Endpunkt setzen

```yaml
telemetry:
  otlpEndpoint: "http://otel-collector:4317"
```

Marshal exportiert über OTLP gRPC. Lass den Schlüssel ungesetzt, um nichts zu exportieren. Denselben Wert kannst du als `MARSHAL__Telemetry__OtlpEndpoint` setzen. Ein Neustart übernimmt die Änderung.

### Was du bekommst

Traces folgen den OpenTelemetry GenAI Semantic Conventions, Version 1.41 (die Version, die `Microsoft.Extensions.AI` umsetzt). Das Ressourcenattribut `marshal.genai_semconv_version` nennt sie. Spans existieren, während der Durchlauf läuft:

- `invoke_agent <template>`: einer pro Durchlauf, aus der Quelle `Marshal.Runs`.
- `chat` und `embeddings`: einer pro Modell- oder Embedding-Aufruf, aus `Experimental.Microsoft.Extensions.AI`, mit Anbieter, Modell und Token-Verbrauch.
- `execute_tool <tool>`: einer pro Werkzeugaufruf, mit Risikostufe und Entscheidung des Guards.
- MCP-Client-Spans aus `Experimental.ModelContextProtocol`. MCP-Anfragen tragen den Trace-Kontext, ein MCP-Server, der selbst traced, schließt sich also dem Trace des Durchlaufs an.

Backends mit GenAI-Ansichten (Langfuse, Arize Phoenix, Grafana) zeigen die Durchläufe ohne eigenes Mapping. Die eigenen Metriken von Marshal kommen vom Meter `Marshal`; die Standardmetriken `gen_ai.client.token.usage` und `gen_ai.client.operation.duration` kommen vom Meter von M.E.AI:

| Metrik | Typ | Attribute |
|---|---|---|
| `marshal.runs` | Counter | `outcome` |
| `marshal.model.tokens` | Counter | `direction` (`input`, `output`) |
| `marshal.tool.calls` | Counter | `decision` (Entscheidung des Guards) |
| `marshal.run.cost` | Histogramm | keine |

Alle Namen und Attribute: [Metriken und Traces](../reference/metrics-and-traces.md).

### Traces an Langfuse oder Phoenix schicken

Marshal exportiert nur OTLP gRPC. Langfuse nimmt nur OTLP über HTTP an, stell also einen OpenTelemetry Collector dazwischen. Diese Collector-Konfiguration schickt Traces an Langfuse Cloud (EU) und an ein selbst gehostetes Phoenix:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317

exporters:
  otlphttp/langfuse:
    endpoint: https://cloud.langfuse.com/api/public/otel
    headers:
      # base64 von "<public key>:<secret key>"
      Authorization: "Basic ${env:LANGFUSE_AUTH}"
  otlp/phoenix:
    endpoint: phoenix:4317
    tls:
      insecure: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [otlphttp/langfuse, otlp/phoenix]
```

Setz dann `telemetry.otlpEndpoint: "http://otel-collector:4317"`. Nimm einen Exporter, wenn du nur ein Backend betreibst. Phoenix bietet OTLP gRPC auf Port 4317 und seine Oberfläche auf Port 6006.

### Inhalte mitschneiden (standardmäßig aus)

Ohne Mitschnitt der Inhalte tragen Spans nur IDs und Metadaten. Um Ein- und Ausgaben im Backend zu sehen, schalte ihn ein:

```yaml
telemetry:
  captureContent: true
```

> [!WARNING]
> Mit eingeschaltetem Mitschnitt speichert dein Telemetrie-Backend personenbezogene Daten: was du gesagt hast, Werkzeugargumente, Werkzeugergebnisse und Antworten. Marshal protokolliert beim Start eine Warnung, solange er an ist.

Spans tragen dann genau das, was die Aufzeichnung der Durchläufe speichert: die Eingabe des Benutzers und die Antwort an `invoke_agent` und die Argumente und das Ergebnis an `execute_tool`. Inhalte werden gekürzt, Ergebnisse sensibler Werkzeuge erscheinen nur als ihre Referenz, und zurückgehaltenes Reasoning bleibt zurückgehalten. System-Prompts und die vollständigen Nachrichten an das Modell erscheinen in keinem Span.

### Bei etwas Nützlichem alarmieren

Beispiele: ein Anstieg von `marshal.runs{outcome="error"}`, viele `marshal.tool.calls{decision="denied"}` nach einer Konfigurationsänderung oder `marshal.run.cost` über deiner Tagesgrenze.

## Logs und Zustand

Setz die Log-Stufe mit `logging.logLevel.default` (eine Stufe Debug oder Trace gibt beim Start auch Stacktraces für Konfigurationsfehler aus). `GET /health` braucht keine Authentifizierung, liefert keine Werte und meldet die Prüfungen `database`, `config`, `secrets`, `mcp`, `memory` (sqlite-vec) und `homeassistant`. `unhealthy` liefert HTTP 503, `degraded` (zum Beispiel ein optionales Secret, das sich nicht auflöst) liefert 200 mit Warnungen.

```sh
curl -s http://<marshal>:8080/health
```

## Prüfen, ob es funktioniert

Schick eine Chat-Nachricht, öffne **Läufe** und den neuen Durchlauf. Mit konfiguriertem OTLP findest du den Trace `invoke_agent default` in deinem Backend.

## Verwandte Seiten

::: cards
- [Fehlerbehebung](troubleshooting.md)
- [Metriken und Traces](../reference/metrics-and-traces.md)
- [`recording`](../configuration/marshal-yaml/recording.md)
- [Datenschutz](../concepts/privacy.md)
:::
