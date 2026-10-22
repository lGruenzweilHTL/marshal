---
icon: fa-solid fa-spell-check
---

# Glossary

The terms Marshal's documentation and specifications use, each with one meaning. Other pages use these terms and no synonyms.

## A

**Advisor.** The strongest model tier. The agent asks it for advice through `agent.ask_advisor`. It sees only the question and a context summary, and cannot call tools. See [tools](tools.md#agent-agent).

**Agent.** The part of Marshal that talks to a model, calls tools and produces a reply. One pass of work is a run.

**Agent loop.** The cycle in which the agent builds context, calls a model, runs the tool calls the model asks for, and repeats until it has a reply or reaches `agent.maxSteps`. See [agent loop](../concepts/agent-loop.md).

**Agent template.** A named configuration of a run: prompt, allowed tools and skills, model tier and limits. A subagent runs a template. See [agent templates](../guides/agent-templates.md).

**AI task.** A one-off request from a Home Assistant automation (`ai_task.generate_data`) that Marshal answers as JSON in one headless run with the `ai_task` template. Its session has the source `automation`. See [AI tasks](../home-assistant-component/ai-tasks.md).

**API token.** A scoped, revocable credential for a machine client, sent as `Authorization: Bearer`. Marshal stores only its hash. Also called a machine token. See [CLI](cli.md#marshal-tokens).

**Approval.** A user's decision on a pending action that a background task or another non-interactive run, such as an AI task, raised. Approvals sit in the approval queue and expire after `tasks.approvalTtl`. See [approvals](../guides/tasks/approvals.md).

**Artifact.** A file Marshal stores for you: an upload, executor output, a kept snapshot, a diff or a saved mail attachment. Each has a kind, a source and an owner. See [artifacts](../guides/artifacts.md).

**Assist.** Home Assistant's voice assistant. Marshal answers Assist requests through the Marshal conversation agent. See [Assist pipeline](../guides/voice/assist-pipeline.md).

## B

**Babysitter.** The print babysitter. It watches a running print through the webcam and pauses a failing print. It is a system source and can send `critical` notifications.

**Background task.** Work the agent continues without you waiting, as a task. `agent.move_to_background` and the latency cap both turn a run into one.

## C

**Chunk.** One entry of long-term memory, with a type, title, content and importance. A chunk has a deterministic id and a version history. See [memory model](../concepts/memory-model.md).

**Confirm tier.** The risk tier whose calls run only after a user action confirms them. See [risk tiers](risk-tiers.md).

**Consolidation.** How memory is written: explicit remembering, summaries when a session closes, and an audit of every change.

**Context builder.** The part of Marshal that assembles a turn's context: the system prompt, memory, the states of nearby entities and the tool list. See [context building](../concepts/context-building.md).

**Critical.** The notification priority that always uses voice and push. Only system sources send it. See [notification priorities](notifications-priorities.md).

## D

**Demo mode.** Simulated integrations in place of the unconfigured ones, plus seeded sample history that `marshal demo reset` removes. See [demo mode](../guides/demo-mode.md).

**Delivery.** Where and how the result of a task goes: a channel (`voice`, `push`, `inbox`, `silent`), a target and conditions.

**Display.** A screen Marshal can show an artifact on, in the room of a satellite.

## E

**Event.** A normalized record `{ source, type, entity, payload, ownerId?, time }` on the event bus. See [events](events.md).

**Event bus.** The in-process channel that carries events from event sources to trigger matching.

**Event source.** Something that publishes events: Home Assistant, printers, mail, executors, or a webhook source.

**Executor.** An external service that does long work for a task, for example a coding session. It runs on the separate [executor host](../installation/executor-host.md) and reports back through a callback token.

## F

**Filler.** A short line a slow built-in tool declares per language. In a voice turn Marshal speaks it before the tool runs when nothing was spoken yet. See [tool visibility](../concepts/tool-visibility.md#spoken-filler).

**Forbidden tier.** The risk tier whose calls never run. The tool is hidden from the model.

**Frozen action.** The stored tool, arguments, nonce and expiry of a pending action. Confirmation runs exactly this, and the model cannot change it.

## G

**Guard.** The server-side check that decides, for every tool call, whether it runs, needs confirmation or is denied. It repeats the rules that hide tools.

## H

**Health warning.** A problem Marshal reports about itself, with a component, a stable code, a severity and a message Marshal wrote. The web UI shows a banner and `system.health` returns the list. See [health endpoints](health-endpoints.md#health-warnings).

## I

**Inbox.** Your list of unread notifications. `notifications.inbox_list` and `notifications.inbox_consume` read and clear it.

**Input request.** A question an MCP server asks the user during a tool call (MCP elicitation, a form or a URL). Marshal freezes the call and only the web UI answers; the model never sees the request. See [MCP servers](../configuration/mcp-servers.md#input-requests).

**Instance owner.** The user the instance belongs to. Webhook and CLI actions run as this owner.

## J

**Judge.** A `fast`-tier model call that classifies your reply to a pending action as `positive`, `neutral` or `negative`.

## K

**Keyword.** A configured phrase such as "confirm". An exact match of your whole reply confirms a pending action without a model call.

**Kill switch.** `tasks.stop_all`. It stops all background tasks and pauses new ones until you resume them in the web UI.

## M

**MCP server.** A Model Context Protocol server whose tools Marshal offers to the model. Home Assistant's MCP server is one. See [custom MCP servers](../guides/integrations/custom-mcp-servers.md).

**Memory.** Long-term knowledge as chunks. Marshal retrieves it by hybrid search (keywords and vectors) and adds it to the context.

**Model tier.** A named class of models: `fast`, `standard` or `advisor`. Marshal routes each model call by tier.

## N

**Namespace.** The first part of a tool id, such as `printer` in `printer.status`. Tool retrieval loads tools by namespace; core namespaces are always loaded.

**Nonce.** A one-time value stored with a pending action. A push action counts only with the valid, unexpired nonce.

**Notification.** A proactive message to you, with a priority, a channel and a recorded outcome.

## O

**Owner.** The user who owns a row. Every user-owned row has an `owner_id`, and all data access is owner-scoped.

## P

**Pending action.** A confirm-tier call stored until you confirm it, it expires or it is superseded. A session has at most one.

**Persists.** A tool flag: the tool stores state the agent acts on later. In a tainted run its calls need confirmation.

**Project.** A named area of work. Marshal activates projects explicitly or by inference and uses them to boost matching chunks.

**Proposal.** A change an executor produced, such as a branch with a diff. You review and merge it in the web UI.

**Push.** A companion app notification through Home Assistant.

## Q

**Quiet hours.** The weekly schedule, in your time zone, when `normal` notifications are held or sent silently and `low` ones go to the inbox.

## R

**Rendition.** The displayable form of an artifact: a converted image, text pages, or rendered views of a 3D model.

**Risk tier.** `safe`, `confirm` or `forbidden`. See [risk tiers](risk-tiers.md).

**Run.** One pass of the agent loop, from a trigger to a reply or other outcome, recorded with its steps, cost and guard decisions.

## S

**Safe tier.** The risk tier whose calls run immediately.

**Satellite.** A voice endpoint in a room, such as an Assist satellite. Its profile in `satellites.yaml` sets its language, tool policy and display.

**Scope.** A permission of an API token, such as `conversation:write`.

**Secret.** A credential. `/config` holds only secret names. Values come from the secret store.

**Session.** A conversation with its history, for example one web chat or one voice dialogue.

**Skill.** A set of instructions in `SKILL.md` that the agent loads on demand with `agent.load_skill`.

**Step.** One recorded part of a run: context, a model call, a tool call or the reply.

**Subagent.** A nested run started by `agent.delegate`. It never runs confirm-tier actions.

## T

**Task.** A durable unit of background work with a goal, a trigger, deliveries and a status.

**Taint.** The state of a run that has read untrusted content. It is never cleared within the run, and it raises `persists` tools to the confirm tier.

**Time to first delta.** The time from a turn's request to the first streamed reply text. The runs view calls it "first words".

**Tool.** A function the model can call. It has an id `<namespace>.<name>` and a model name `<namespace>_<name>`. See [tools](tools.md).

**Tool pin.** The stored fingerprint of an MCP tool's definition. A new or changed tool stays hidden until an admin approves it. See [tool pinning](../configuration/mcp-servers.md#tool-pinning).

**Tool retrieval.** Offering the model only the tools of the namespaces a session has loaded, by embedding match, hints or `agent.find_tools`. See [tool visibility](../concepts/tool-visibility.md#tool-retrieval).

**Tool source.** A provider of tools that registers and unregisters as a unit: the built-in tools, an MCP server or a skill.

**Trigger.** What starts a task: now, a time, a cron schedule, another task, or an event.

**Turn.** One user message and the reply to it.

## U

**Untrusted output.** A tool flag: the result is third-party content (mail, RSS, notes, web pages). Marshal wraps it as data, never as instructions, and taints the run.

## V

**Voice follow-up.** A spoken question from Marshal that listens for your reply, started by a task delivery with `followUp`.

## W

**Watch.** A task triggered by an event that matches a filter. It fires once per arming. A `once` watch ends after the first match.

**Webhook source.** An event source that receives HTTP posts at `/api/v1/hooks/<source>`, such as `uptime-kuma`, `proxmox` and `seerr`.

## Related

- [Tools](tools.md)
- [Risk tiers](risk-tiers.md)
- [Events](events.md)
- [Documentation style](../development/docs-style.md)
