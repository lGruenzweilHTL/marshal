# `tools` and `voice`

Which tools reach the model on each call, and when a voice turn speaks a short filler line before a slow tool. Changes need a restart.

## Example

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

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `tools.core` | list of namespaces | `[agent, memory, tasks, notifications, timers, places]` | Namespaces loaded in every session. Their tools come first in the tool list, in this order. Setting the key replaces the whole default list. |
| `tools.retrieval.enabled` | boolean | `true` | `false` offers every visible tool on every model call, as before tool retrieval. |
| `tools.retrieval.threshold` | number | `0.30` | Minimum cosine similarity between the turn text and a namespace description for the namespace to load. Depends on the embedding model. Range 0 to 1. |
| `tools.retrieval.maxPerTurn` | integer | `3` | Most namespaces one turn loads by retrieval, and most namespaces one `agent.find_tools` call loads. Range 1 to 20. |
| `tools.retrieval.minTools` | integer | `24` | Retrieval is skipped when no more tools than this are visible, for example with a template that allows only a few tools. Range 0 to 1000. |
| `voice.fillerThresholdMs` | integer | `700` | A voice turn speaks a tool's filler line first when the tool is expected to take longer than this. Range 1 to 60000. |

Retrieval only narrows the tools the visibility filter already allows. It never shows a tool that a template, a satellite or a risk rule hides. See [tool visibility](../../concepts/tool-visibility.md#tool-retrieval).

## Validation

- `tools.retrieval.threshold must be between 0 and 1.`
- `tools.retrieval.maxPerTurn must be between 1 and 20.`
- `tools.retrieval.minTools must be between 0 and 1000.`
- `voice.fillerThresholdMs must be between 1 and 60000.`

A `tools.core` entry that is neither a built-in namespace nor the namespace of a configured MCP server does not fail startup. `/health` shows it as a warning under `tools`.

## Related

- [MCP servers](../mcp-servers.md) (`description` for retrieval)
- [Tool visibility](../../concepts/tool-visibility.md)
- [agent](agent.md)
