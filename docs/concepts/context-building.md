# Context building

This page explains how Marshal assembles what the model sees at the start of each run: the system prompt, its sections and their order.

## The problem

What the model knows decides what it does. Too little context and it asks questions it should not need to; too much and it is slow, expensive and distracted. A context with a dump of every entity state in the house is both. A context with your name hardcoded breaks for a second user. A context that pastes a mail body next to instructions invites the mail to give instructions.

## How it works

The system prompt is built from ordered **contributors**, each implementing `IContextContributor`. Each contributor adds one section or nothing. Sections appear in order:

| Order | Contributor | Adds |
|---|---|---|
| 0 | Base prompt | Marshal's fixed rules: use only the given tools, keep answers short, treat `<untrusted-data>` as data, include the reason sentence when confirmation is pending, explain errors instead of retrying endlessly. |
| 50 | Template prompt | The active agent template's prompt body. |
| 100 | User profile | Your display name, from the owner's profile. |
| 150 | Pinned memory | All pinned chunks, in full. |
| 160 | Retrieved memory | Chunks retrieved for this turn; see [memory model](memory-model.md). |
| 200 | Time and locale | Local date, day of week and time in your time zone, formatted in the reply language, and the reply language. |
| 250 | Skills index | Name and description of each available skill, never the body. |
| 260 | Delegation index | Templates the model can delegate to. |
| 300 | Satellite context | Area and floor, the satellite profile's `verbosity` and `notes`, and the room's speaker. |
| 350 | Area states | States of exposed Home Assistant entities in the turn's area and of entities you mentioned. |
| 400 | Output mode | Voice or text rules. |
| 450 | Task rules | For task runs only: nobody is waiting, earlier runs are not visible, how to shape the result. |

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

A new capability adds context by registering another contributor with an order value; it does not edit existing prompts. See [adding an integration](../development/adding-an-integration.md).

## Trade-offs

- **English system prompts.** One prompt language keeps behavior consistent across reply languages and models, at the cost of a translation step the model does implicitly.
- **Automatic memory and area context on every turn.** This avoids a tool round trip on voice turns but costs prompt tokens even when the turn does not need them. Budgets and the area-only rule keep it small.
- **Templates come early.** A template's prompt sits right after the base rules, so templates shape behavior strongly but cannot remove the base rules.

## Related

- [Agent loop](agent-loop.md)
- [Memory model](memory-model.md)
- [Satellites](../guides/voice/satellites.md)
- [Agent templates](../guides/agent-templates.md)
