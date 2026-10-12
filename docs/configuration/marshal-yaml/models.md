# `models`

The chat models Marshal can use, grouped into tiers. Changes need a restart.

## Example

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

## Keys

`models` is a map. The map key (`fast`, `advisor`, ...) is a name of your choice. Each entry `models.<key>`:

| Key | Type | Default | Description |
|---|---|---|---|
| `models.<key>.tier` | `fast`, `standard`, `advisor` | `fast` | Tier of the model. `fast`: voice turns, the confirmation judge, cheap classification. `standard`: normal work. `advisor`: consulted only through `agent.ask_advisor`. |
| `models.<key>.provider` | string | none (required) | `ollama`, `openai-compatible` (llama.cpp `llama-server`, vLLM, LM Studio) or `anthropic`. |
| `models.<key>.model` | string | none (required) | Provider model name, for example `qwen3:8b`. |
| `models.<key>.endpoint` | URL | unset | Server URL. Required for `openai-compatible`. |
| `models.<key>.apiKeySecret` | secret name | unset | Name of the secret holding the API key. Required for `anthropic`. |
| `models.<key>.vision` | boolean | `false` | The model accepts images. |
| `models.<key>.tools` | boolean | `true` | The model supports tool calling. |
| `models.<key>.contextLength` | integer | `8192` | Context window in tokens. |
| `models.<key>.price.inputPerMTok` | decimal | `0` | Cost per million input tokens. |
| `models.<key>.price.outputPerMTok` | decimal | `0` | Cost per million output tokens. |
| `models.<key>.price.cachedPerMTok` | decimal | input price | Cost per million cached input tokens. |

Without `price` a model costs 0. The `tier` default `fast` is the enum default; set the key explicitly.

## Routing

A request names a tier plus required capabilities (`vision`, `tools`, a minimum context length). The router picks the first model in file order whose tier matches and which has all capabilities. With no match, the call fails with `model.not_available` and no model call is made.

## Validation

Messages name the model key:

- `models.<key>.provider must be one of ollama, openai-compatible, anthropic.`
- `models.<key>.model is required.`
- `models.<key>.apiKeySecret is required for anthropic models.`
- `models.<key>.endpoint is required for openai-compatible models.`
- `models.<key>.contextLength must be positive.`
- A model with `apiKeySecret` whose secret does not resolve fails startup.

## Related

- [embedding](embedding.md)
- [agent](agent.md) (advisor limits)
- [Models setup](../../installation/models-setup.md)
