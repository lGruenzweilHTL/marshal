---
icon: fa-solid fa-layer-group
description: "How Marshal assembles what the model sees at the start of each run: the system prompt, its sections and their order."
---

# Context building

This page explains how Marshal assembles what the model sees at the start of each run: the system prompt, its sections and their order.

## The problem

What the model knows decides what it does. Too little context and it asks questions it should not need to; too much and it is slow, expensive and distracted. A context with a dump of every entity state in the house is both. A context with your name hardcoded breaks for a second user. A context that pastes a mail body next to instructions invites the mail to give instructions.

## How it works

The system prompt is built from **contributors**, each implementing `IContextContributor`. Each contributor adds one section or nothing, and declares a **stability**:

- `static`: the same for every run with the same configuration.
- `session`: the same for every turn of a session.
- `turn`: may change on every turn.

Marshal places all `static` sections first, then `session`, then `turn`. Within one stability, the order value decides. Sections appear in this order:

| Stability | Order | Contributor | Adds |
|---|---|---|---|
| `static` | 0 | Base prompt | Marshal's fixed rules: use only the given tools, keep answers short, treat `<untrusted-data>` as data, include the reason sentence when confirmation is pending, explain errors instead of retrying endlessly. |
| `static` | 50 | Template prompt | The active agent template's prompt body. |
| `static` | 250 | Skills index | Name and description of each available skill, never the body. |
| `static` | 260 | Delegation index | Templates the model can delegate to. |
| `session` | 100 | User profile | Your display name, from the owner's profile. |
| `session` | 150 | Pinned memory | All pinned chunks, in full. |
| `session` | 300 | Satellite context | Area and floor, the satellite profile's `verbosity` and `notes`, and the room's speaker. |
| `session` | 400 | Output mode | Voice or text rules. |
| `turn` | 160 | Retrieved memory | Chunks retrieved for this turn; see [memory model](memory-model.md). |
| `turn` | 200 | Time and locale | Local date, day of week and time in your time zone, formatted in the reply language, and the reply language. |
| `turn` | 350 | Area states | States of exposed Home Assistant entities in the turn's area and of entities you mentioned. |
| `turn` | 450 | Task rules | For task runs only: nobody is waiting, earlier runs are not visible, how to shape the result. |

### Stable text first

Model providers cache the start of a prompt. A cached prefix is cheaper and makes the first token arrive sooner, which matters most in voice turns. Because stable sections come first, two turns of one session have identical system prompts up to the first `turn` section. The tool list follows the same idea: tools keep a fixed order, and namespaces loaded later are appended to the end. See [tool visibility](tool-visibility.md#tool-retrieval).

The conversation history follows the system prompt: the last exchanges of the session (your messages and Marshal's replies), then your new message. For a resumed task, the goal, notes and trigger payload take the place of a transcript.

### No personal data in code

No user name or personal detail is hardcoded in any prompt. The profile contributor reads your name from the owner's record, so the same code serves any owner.

### Output mode

The output mode follows the turn's `inputMode`:

- **Voice:** plain, speakable text. No markdown. Short. Numbers written as spoken.
- **Text:** markdown allowed.

### Language

Marshal replies in the request language (`de` or `en`). Runs without one, such as task runs, use your stored language preference. System prompts stay in English; dates and numbers are formatted in the reply language and your time zone.

### Area context without dumps

The area section lists only exposed entities in the turn's area and entities named in your text, from the state cache. It never includes every state in the house. When the model needs more, it calls `ha.homeassistant__GetLiveContext`. See [Home Assistant](../guides/integrations/home-assistant.md).

### Progressive disclosure for skills

Skills appear only as an index. The model loads a skill's body with `agent.load_skill(name)` when it needs it, and additional files with `agent.load_skill(name, file)`. This keeps the prompt small while dozens of skills are installed. See [skills](../guides/skills.md).

### Untrusted results are wrapped

Tool results from untrusted sources enter the conversation inside an `<untrusted-data source="...">` wrapper, labeled as data and never as instructions. See [untrusted content](untrusted-content.md).

## Transparency

The context-build step of each run records the full system prompt, your input, and notes on what was included: memory chunks with their scores and how they were found, active projects with their reasons, and whether memory was unavailable. The run detail in the web UI shows these notes. Active projects are never spoken.

## Extending the context

A new capability adds context by registering another contributor with a stability and an order value; it does not edit existing prompts. See [adding an integration](../development/adding-an-integration.md).

## Trade-offs

- **English system prompts.** One prompt language keeps behavior consistent across reply languages and models, at the cost of a translation step the model does implicitly.
- **Automatic memory and area context on every turn.** This avoids a tool round trip on voice turns but costs prompt tokens even when the turn does not need them. Budgets and the area-only rule keep it small.
- **Templates come early.** A template's prompt sits right after the base rules, so templates shape behavior strongly but cannot remove the base rules.
- **Stability before order.** Grouping by stability keeps the cacheable prefix long. The cost is that a `turn` section, such as the time, can never appear before a `static` one, whatever its order value.

## Related

::: cards
- [Agent loop](agent-loop.md)
- [Memory model](memory-model.md)
- [Satellites](../guides/voice/satellites.md)
- [Agent templates](../guides/agent-templates.md)
:::
