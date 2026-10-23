---
icon: fa-solid fa-microchip
order: 30
description: "Konfiguriere Chat-Modelle für die Stufen fast und standard, einen optionalen Advisor und das Embedding-Modell."
---

# Modelle einrichten

Konfiguriere Chat-Modelle für die Stufen fast und standard, einen optionalen Advisor und das Embedding-Modell.

## Bevor du anfängst

- Ein Modellserver: Ollama, ein OpenAI-kompatibler Server oder ein Anthropic-API-Schlüssel.
- Entscheide, welche Stufen du brauchst. Modelle stehen unter `models` mit einem Namen deiner Wahl.

## Wofür die Stufen dienen

| Stufe | Dient für |
|---|---|
| `fast` | Sprach-Turns, den Bestätigungsprüfer, Gedächtniszusammenfassungen |
| `standard` | Text-Turns (Web-Chat, Hintergrundaufgaben) |
| `advisor` | Das Werkzeug `agent.ask_advisor`; verborgen in Sprach-Turns und wenn kein Advisor-Modell konfiguriert ist |

Ein Agent-Template kann eine Stufe wählen. Aufrufe mit Bildern (Druckerkameras, Prüfung von Artefakten) brauchen ein Modell mit `vision: true` in der Stufe und fallen auf `standard` zurück.

## Schlüssel pro Modell

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `tier` | `fast`, `standard`, `advisor` | keiner | Pflicht |
| `provider` | string | keiner | `ollama`, `openai-compatible` oder `anthropic` |
| `model` | string | keiner | Modellname beim Anbieter |
| `endpoint` | URL | Ollama: `http://localhost:11434` | Server-URL. Pflicht für `openai-compatible` |
| `apiKeySecret` | Secret-Name | keiner | Pflicht für `anthropic` |
| `vision` | boolean | `false` | Modell nimmt Bilder an |
| `tools` | boolean | `true` | Modell unterstützt Werkzeugaufrufe |
| `contextLength` | integer | `8192` | Muss positiv sein |
| `reasoning` | `off`, `low`, `medium`, `high` | `off` | Reasoning-Aufwand für Text-Turns und Hintergrundaufgaben; Sprach-Turns nutzen nie Reasoning |
| `price` | object | keiner | `inputPerMTok`, `outputPerMTok`, `cachedPerMTok`; ohne ihn sind die Kosten 0 |

## Anbieter

::: tabs
== Ollama

```yaml
models:
  fast:
    tier: fast
    provider: ollama
    model: "qwen3:8b"
    endpoint: "http://ollama:11434"
    contextLength: 32768
  standard:
    tier: standard
    provider: ollama
    model: "qwen3:30b"
    endpoint: "http://ollama:11434"
    contextLength: 32768
```

Lade jedes Modell: `docker compose exec ollama ollama pull qwen3:30b`. Setz `contextLength` auf den Wert, den der Modellserver tatsächlich nutzt.

== OpenAI-kompatible Server

Für llama.cpp `llama-server`, vLLM oder LM Studio:

```yaml
models:
  local:
    tier: standard
    provider: openai-compatible
    model: "qwen3"
    endpoint: "http://llama:8080/v1"
    # apiKeySecret: llama_key
```

== Anthropic

```yaml
models:
  advisor:
    tier: advisor
    provider: anthropic
    model: claude-sonnet-4-5
    apiKeySecret: anthropic_key
    contextLength: 200000
    price: { inputPerMTok: 3, outputPerMTok: 15, cachedPerMTok: 0.3 }
```

Stell den Schlüssel als Docker-Secret-Datei `/run/secrets/anthropic_key` oder als Variable `MARSHAL_SECRET_ANTHROPIC_KEY` bereit. Der Start schlägt fehl, wenn sich das Secret nicht auflösen lässt. Begrenze die Nutzung des Advisors mit `agent.advisor.maxCallsPerRun` und `agent.advisor.dailyCostCap`.
:::

## Reasoning

Denkende Modelle können ihr Reasoning im Web-Chat zeigen. Setz `reasoning` am Modell:

```yaml
models:
  standard:
    tier: standard
    provider: ollama
    model: "qwen3:30b"
    reasoning: medium
```

Text-Turns und Durchläufe von Hintergrundaufgaben fragen dann mit diesem Aufwand nach Reasoning. Sprach-Turns tun das nie, wegen der Latenzgrenze für Sprache. Reasoning-Tokens zählen als Ausgabe-Tokens, ein höherer Aufwand kostet also mehr und macht Antworten langsamer. Welche Anbieter und Modelle es unterstützen: [Unterstützung für Reasoning](../configuration/marshal-yaml/models.md#unterstützung-für-reasoning).

## Embeddings

```yaml
embedding:
  provider: ollama        # ollama oder openai-compatible
  model: "bge-m3"
  endpoint: "http://ollama:11434"
```

Nimm ein mehrsprachiges Modell (`bge-m3`, `multilingual-e5`, `nomic-embed-text-v2`). `endpoint` ist Pflicht für `openai-compatible`; `apiKeySecret` ist optional. Anthropic bietet keine Embeddings an. Ein Wechsel des Modells bettet beim nächsten Start das ganze Gedächtnis neu ein.

## Prüfen, ob es funktioniert

Starte Marshal nach einer Änderung der Modelle neu. Validierungsfehler nennen den Schlüssel, zum Beispiel `models.fast.provider must be one of ollama, openai-compatible, anthropic.` Schick eine Chat-Nachricht; die Ansicht des Durchlaufs zeigt Modell und Kosten.

## Verwandte Seiten

::: cards
- [`models`](../configuration/marshal-yaml/models.md)
- [`embedding`](../configuration/marshal-yaml/embedding.md)
- [Agent-Templates](../guides/agent-templates.md)
:::
