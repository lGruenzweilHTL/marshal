# model-routing Specification

## Purpose
Provider-neutral access to chat and embedding models, with tiers, capability flags and cost from a price table.

## Requirements

### Requirement: Provider-neutral model access
The system SHALL access all language models through `IChatClient` and all embedding models through `IEmbeddingGenerator` from `Microsoft.Extensions.AI`. The system SHALL support three provider kinds: `ollama`, `openai-compatible` (llama.cpp `llama-server`, vLLM, LM Studio) and `anthropic`. Provider SDK packages SHALL be referenced only by `Marshal.Api`.

#### Scenario: Configured providers are available
- **WHEN** `/config/marshal.yaml` defines one model per provider kind
- **THEN** the router can return a working `IChatClient` for each of them

#### Scenario: Feature projects stay provider-neutral
- **WHEN** the solution is built
- **THEN** no project other than `Marshal.Api` references `OllamaSharp`, `Anthropic` or `Microsoft.Extensions.AI.OpenAI`

### Requirement: Tiers and capability flags
Each configured model SHALL belong to exactly one tier (`fast`, `standard`, `advisor`) and SHALL declare the capability flags `vision`, `tools` and `contextLength`. A model request SHALL name a tier and optional required capabilities; the router SHALL return a model of that tier that has all required capabilities.

#### Scenario: Tier plus capability selects the matching model
- **WHEN** the `fast` tier has one model without `vision` and one with `vision`, and a call requests `fast` + `vision`
- **THEN** the router returns the model with `vision`

#### Scenario: No matching model
- **WHEN** no model of the requested tier has the required capabilities
- **THEN** the router returns a failed `Result` with a stable error code and no model call is made

### Requirement: Cost from a price table
The system SHALL compute the cost of every model call from a per-model price table in configuration (price per million input, output and cached tokens). Models without a price entry SHALL cost 0.

#### Scenario: Cloud call cost
- **WHEN** a call to a model priced at 3 / 15 / 0.3 per million input / output / cached tokens uses 1,000,000 input and 100,000 output tokens
- **THEN** the recorded cost is 4.5

#### Scenario: Local model is free
- **WHEN** a call goes to a model without a price entry
- **THEN** the recorded cost is 0

### Requirement: Reasoning effort
Each configured model SHALL have a `reasoning` setting of `off`, `low`, `medium` or `high`, defaulting to `off`. A model call SHALL request reasoning at the configured effort for text turns and for background task runs. A model call for a voice turn SHALL request no reasoning, whatever the setting. A model whose provider cannot return reasoning SHALL be called as if the setting were `off`, and startup SHALL log a warning naming the model.

#### Scenario: Text turn with reasoning
- **WHEN** a text turn routes to a model configured with `reasoning: medium`
- **THEN** the model call requests medium reasoning effort

#### Scenario: Voice turn
- **WHEN** a voice turn routes to the same model
- **THEN** the model call requests no reasoning

#### Scenario: Default
- **WHEN** a model has no `reasoning` key
- **THEN** its calls request no reasoning
