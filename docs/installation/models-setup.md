# Models setup

Configure chat models for the fast and standard tiers, an optional advisor, and the embedding model.

## Before you start

- A model server: Ollama, an OpenAI-compatible server, or an Anthropic API key.
- Decide which tiers you need. Models are keyed by a name of your choice under `models`.

## How tiers are used

| Tier | Used for |
|---|---|
| `fast` | Voice turns, the confirmation judge, memory summaries |
| `standard` | Text turns (web chat, background tasks) |
| `advisor` | The `agent.ask_advisor` tool; hidden in voice turns and when no advisor model is configured |

An agent template can pick a tier. Vision calls (printer cameras, artifact inspection) need a model with `vision: true` in the tier and fall back to `standard`.

## Keys per model

| Key | Type | Default | Description |
|---|---|---|---|
| `tier` | `fast`, `standard`, `advisor` | none | Required |
| `provider` | string | none | `ollama`, `openai-compatible` or `anthropic` |
| `model` | string | none | Provider model name |
| `endpoint` | URL | Ollama: `http://localhost:11434` | Server URL. Required for `openai-compatible` |
| `apiKeySecret` | secret name | none | Required for `anthropic` |
| `vision` | boolean | `false` | Model accepts images |
| `tools` | boolean | `true` | Model supports tool calls |
| `contextLength` | integer | `8192` | Must be positive |
| `reasoning` | `off`, `low`, `medium`, `high` | `off` | Reasoning effort for text turns and background tasks; voice turns never reason |
| `price` | object | none | `inputPerMTok`, `outputPerMTok`, `cachedPerMTok`; without it the cost is 0 |

## Ollama

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

Pull each model: `docker compose exec ollama ollama pull qwen3:30b`. Set `contextLength` to what the model server actually uses.

## OpenAI-compatible servers

For llama.cpp `llama-server`, vLLM or LM Studio:

```yaml
models:
  local:
    tier: standard
    provider: openai-compatible
    model: "qwen3"
    endpoint: "http://llama:8080/v1"
    # apiKeySecret: llama_key
```

## Anthropic

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

Provide the key as the Docker secret file `/run/secrets/anthropic_key` or the variable `MARSHAL_SECRET_ANTHROPIC_KEY`. Startup fails when the secret does not resolve. Limit advisor use with `agent.advisor.maxCallsPerRun` and `agent.advisor.dailyCostCap`.

## Reasoning

Thinking models can show their reasoning in the web chat. Set `reasoning` on the model:

```yaml
models:
  standard:
    tier: standard
    provider: ollama
    model: "qwen3:30b"
    reasoning: medium
```

Text turns and background task runs then ask for reasoning at that effort. Voice turns never do, because of the voice latency cap. Reasoning tokens count as output tokens, so a higher effort costs more and makes replies slower. Which providers and models support it: [reasoning support](../configuration/marshal-yaml/models.md#reasoning-support).

## Embeddings

```yaml
embedding:
  provider: ollama        # ollama or openai-compatible
  model: "bge-m3"
  endpoint: "http://ollama:11434"
```

Use a multilingual model (`bge-m3`, `multilingual-e5`, `nomic-embed-text-v2`). `endpoint` is required for `openai-compatible`; `apiKeySecret` is optional. Anthropic offers no embeddings. Changing the model re-embeds all memory at the next start.

## Check that it works

Restart Marshal after changing models. Validation errors name the key, for example `models.fast.provider must be one of ollama, openai-compatible, anthropic.` Send a chat message; the run view shows the model and cost.

## Related
- [`models`](../configuration/marshal-yaml/models.md), [`embedding`](../configuration/marshal-yaml/embedding.md)
- [Agent templates](../guides/agent-templates.md)
