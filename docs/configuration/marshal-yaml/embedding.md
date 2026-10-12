# `embedding` and `memory`

The embedding model that memory depends on, and the tuning of memory retrieval, summarization and project activation. Changes need a restart.

## Example

```yaml
embedding:
  provider: ollama
  model: "bge-m3"
  endpoint: "http://ollama:11434"

memory:
  summarizer: { enabled: true, maxTurns: 50 }
  audit: { staleAfterDays: 180 }
  retrieval: { tokenBudget: 600 }
```

## `embedding` keys

`embedding` is required. Use a multilingual model (`bge-m3`, `multilingual-e5`, `nomic-embed-text-v2`), otherwise a German question misses an English memory. Changing the model re-embeds all memory at the next start.

| Key | Type | Default | Description |
|---|---|---|---|
| `embedding.provider` | string | none (required) | `ollama` or `openai-compatible`. Anthropic offers no embeddings. |
| `embedding.model` | string | none (required) | Provider model name. |
| `embedding.endpoint` | URL | unset | Server URL. Required for `openai-compatible`. |
| `embedding.apiKeySecret` | secret name | unset | Secret holding the API key, if the server needs one. |

## `memory` keys

All optional. Defaults are starting points. There is no validator for this section.

| Key | Type | Default | Description |
|---|---|---|---|
| `memory.retrieval.seedCount` | integer | `20` | Seeds taken from each of vector and keyword search. |
| `memory.retrieval.tokenBudget` | integer | `600` | Token budget of memory added to each turn (pinned chunks are extra). |
| `memory.retrieval.recallTokenBudget` | integer | `2000` | Token budget of `memory.recall`. |
| `memory.retrieval.minScore` | number | `0.05` | Candidates below this score are dropped. |
| `memory.retrieval.activeProjectBoost` | number | `1.5` | Score factor for chunks of active projects. |
| `memory.retrieval.inactiveProjectPenalty` | number | `0.5` | Score factor for chunks of inactive projects. |
| `memory.retrieval.neighbourFactor` | number | `0.7` | Similarity of edge-expanded neighbours as a factor of their seed. |
| `memory.retrieval.halfLifeDays.<type>` | number | event 30, fact 365, preference 365, person 730, procedure 730, device 730 | Freshness half-life in days per chunk type. |
| `memory.retrieval.quotas.<type>` | integer | event 5, fact 6, preference 6, person 4, procedure 3, device 4 | Maximum chunks per type in one retrieval. |
| `memory.summarizer.enabled` | boolean | `true` | Summarize sessions into memory when they close (fast tier). |
| `memory.summarizer.maxTurns` | integer | `50` | Most recent turns the summarizer reads. |
| `memory.summarizer.memoryTokenBudget` | integer | `1500` | Token budget of existing memory shown to the summarizer. |
| `memory.audit.staleAfterDays` | integer | `180` | A chunk neither updated nor retrieved for this long is stale. |
| `memory.projects.highConfidence` | number | `0.75` | Score that activates a project stickily. |
| `memory.projects.mediumConfidence` | number | `0.6` | Score that activates a project for one turn. |
| `memory.projects.negativeWeight` | number | `0.5` | Weight of similarity to a rejected phrase subtracted from the score. |
| `memory.projects.recencyBoost` | number | `0.05` | Score added for a project active just now. |
| `memory.projects.recencyHalfLifeDays` | number | `7` | Half-life of the recency boost. |
| `memory.projects.stickyDecay` | number | `0.7` | Factor applied to a sticky activation's weight every turn. |
| `memory.projects.stickyMinimum` | number | `0.3` | Sticky activations below this weight are dropped. |
| `memory.projects.topicShiftThreshold` | number | `0.3` | Consecutive turns less similar than this are a topic shift. |
| `memory.projects.aliasPromotion` | integer | `3` | Confirmations that promote an alias candidate. |
| `memory.projects.centroidTopChunks` | integer | `10` | Most important project chunks averaged into the centroid. |

Chunk types for `halfLifeDays` and `quotas`: `event`, `fact`, `preference`, `person`, `procedure`, `device`.

## Validation

- `embedding.provider must be ollama or openai-compatible.`
- `embedding.model is required.`
- `embedding.endpoint is required for openai-compatible models.`
- A configured `embedding.apiKeySecret` must resolve at startup.

## Related

- [models](models.md)
- [Memory](../../guides/memory.md)
- [Memory model](../../concepts/memory-model.md)
