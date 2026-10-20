# Privacy

This page explains which data leaves your Marshal instance depending on the models and integrations you choose, what Marshal stores, and how long it keeps it.

## The problem

A home assistant sees a lot: what you say in each room, who is home, your mail, notes, calendar and print jobs. Self-hosting is meant to keep that at home, but an LLM agent sends text to a model on every turn. Whether that model runs on your hardware or in a cloud decides most of your privacy. You need to know exactly what goes where, so you can choose.

## What leaves the box depends on your model choice

Marshal talks to models through three provider kinds:

| Provider kind | Where it runs | What leaves your network |
|---|---|---|
| `ollama` | Your Ollama server | Nothing, if the server is on your network. |
| `openai-compatible` | Your llama.cpp `llama-server`, vLLM or LM Studio, or any server with that API | Nothing, if the server is on your network. Everything below, if you point it at a hosted service. |
| `anthropic` | Anthropic's Claude API | Everything below for each call routed to that model. |

Each model belongs to a tier, and Marshal picks models by tier. You can mix: for example a local `fast` model for voice and the judge, and a cloud `advisor`. Then only the calls routed to the cloud model leave your network. See [models setup](../installation/models-setup.md) and [`models`](../configuration/marshal-yaml/models.md).

### What a chat model call contains

| Call | Tier | Content sent |
|---|---|---|
| Conversation turn | template tier, else `fast` (voice) or `standard` (text) | The full system prompt (your display name, pinned and retrieved memory, local time and time zone, area and floor, satellite notes, states of entities in your area and of entities you mentioned, skills index), the last exchanges of the session, your message, the tool list, and every tool result of the run (for example a mail body, a note, an article, a calendar event, printer status). |
| Task run | template tier | The same, with the task's goal, notes and trigger payload instead of the transcript. |
| Confirmation judge | `fast` | Only the action summary and your reply. |
| Advisor | `advisor` | Only the question and an agent-written summary. No tools. |
| Session-close summarizer and nightly consolidation | cheap model | Your turns and Marshal's replies of the session, plus a slice of existing memory. Never raw tool results. |
| Subagents | the template's tier | The task text and the subagent's own tool results. |
| `printer.inspect`, the print babysitter | a `vision` model of the run's tier, else `standard` + `vision` | A resized webcam snapshot and the question. |
| `artifacts.inspect` | a `vision` model | One rendered page or image of the artifact and the question. |

If a cloud model is in the `fast` tier, every voice turn reaches that provider. If you want voice and home state to stay local, keep `fast` local.

### Embeddings

The embedding provider is `ollama` or `openai-compatible` (Anthropic offers no embeddings). It receives the text of every turn (with the area name), memory chunk titles and content when they are written, project descriptions, correction phrases, and the text of your notes for the notes index. Run it locally to keep these at home. See [`embedding`](../configuration/marshal-yaml/embedding.md).

## Other data that leaves the box

| Path | Destination | What |
|---|---|---|
| Integrations you configure | Home Assistant, Moonraker, your IMAP server, FreshRSS, Seerr / Sonarr / Radarr / Jellyfin, Proxmox, GitHub | The requests each tool needs. GitHub is the only one of these that is a cloud service by nature; Marshal reads only allowlisted repositories with read-only calls. |
| RSS full text | The article's website | `rss.fetch_full_text` loads the article link stored in FreshRSS. The website sees a request from your instance. |
| Web search | Your SearXNG instance, then the engines in `web.searxng.engines` | The query text of `web.search` and the question of `web.answer`, in every run, including runs that read untrusted content. Marshal never opens a result page. See [web search](../guides/integrations/web-search.md#privacy). |
| Push notifications | Home Assistant `notify` services (the companion app) | The `speech` text and a deep link. Delivery after Home Assistant follows your companion app setup. |
| MCP servers | Wherever the server runs | Tool arguments the model chooses for that server's tools. |
| OpenTelemetry export | Your OTLP collector, if configured | Run, model call and tool call spans with ids, source, input mode, trigger, outcome, model, tool, guard decision and token counts, plus metrics. No prompts, arguments, results or replies, unless you turn on `telemetry.captureContent`: then user input, tool arguments, results and replies, redacted like run recording. Never system prompts. See [observability](../guides/observability.md). |
| Executor host | Your executor host, then the coding model | Coding sessions run Claude Code headless in a sandbox, which sends repository content and the task to Anthropic. Only the repositories mounted on the host are reachable, and sandbox egress is limited by the network you allow. See [executor host](../installation/executor-host.md). |

The Home Assistant component sends Marshal only the turn fields (text, language, conversation id, device, satellite, area, Home Assistant user, input mode). It sends no entity states.

## What Marshal stores

Everything lives in `/data` on your host (see [data directory](../reference/data-directory.md)):

| Data | Where | Notes |
|---|---|---|
| Runs and steps | SQLite | Per step: the full system prompt of the context build, model and token metadata, the model's reasoning, tool arguments, results (truncated at 8000 characters by default) and the reply. |
| Sensitive tool results | SQLite | Stored as references (message id, note path) instead of content. Mail and notes tools are sensitive by default. Reasoning of model calls after such a result in the same run is not stored. Change per tool with `recording.redactTools` and `recording.keepContentTools`. See [`recording`](../configuration/marshal-yaml/recording.md). |
| Sessions | SQLite | Source, creating token, identity evidence and how the owner was resolved. |
| Memory | SQLite | Chunks, versions, edges, embeddings, project feedback. |
| Tasks, notifications, approvals, follow-ups | SQLite | Including notification history with the reason for each channel decision. |
| Audit log | SQLite | Actions outside runs: UI actions, approvals, token and secret changes, kill switch, CLI commands. Secret values are never written. |
| Secrets managed in the UI | SQLite | Encrypted with ASP.NET Core Data Protection; keys in `/data/keys`. |
| Artifacts | `/data/artifacts/<owner>/` | Uploaded files, kept snapshots, saved attachments, executor output, GitHub diffs and logs. |
| Printer snapshots | `/data` | Pruned by the snapshot store's retention unless you keep them as artifacts. |
| Notes repository and index | `/data` | A clone of your notes repository and its derived search index. |
| Backups | `/data/backups` | SQLite backup, a copy of `/data/keys`, and an NDJSON memory export per owner. |

Logs never contain secrets, message bodies, note contents or full prompts.

## Retention

| Data | Kept | Setting |
|---|---|---|
| Full run step payloads (prompts, tool arguments and results, replies) | 30 days | `maintenance.payloadRetention` |
| Run and step metadata (tokens, cost, tools, durations) | 1 year | `maintenance.metadataRetention` |
| Backups | 7 copies | `maintenance.backupCopies` |
| Eval cases | Until you delete them | Not affected by the retention purge. Created only by rating a run with "help Marshal learn" on, or by an admin saving a run. See [evals](../guides/evals.md#privacy). |
| Memory chunks | Until you delete them | Not affected by the retention purge. Deleting soft-deletes; the history view still shows them. |
| Pending confirmations | 60 seconds | `confirmation.ttl` |
| Background approvals | 10 minutes | `tasks.approvalTtl` |

See [`maintenance`](../configuration/marshal-yaml/maintenance.md).

> [!NOTE]
> Backups contain the database and the Data Protection keys. Anyone with a backup can decrypt your stored secrets. Store backups as carefully as the instance itself. See [backups and restore](../guides/backups-and-restore.md).

## Trade-offs

- **Recording is on by design.** Full run payloads make the run detail useful for understanding and debugging what Marshal did. They are also a record of your conversations for 30 days. Shorten `maintenance.payloadRetention` if that is too long for you.
- **Context is rich.** Memory and area states go into every turn to avoid tool round trips on voice. With a cloud model, that context goes with every call.
- **Eval cases outlive retention.** A case keeps the text of one turn until you delete it. "Help Marshal learn" is off by default, so no case exists unless you turn it on or an admin saves a run.
- **Redaction is per tool.** Results of sensitive tools are stored as references, but the model still saw the content during the run, and so did the model provider.

## Related

- [Security model](security-model.md)
- [Data ownership](data-ownership.md)
- [Context building](context-building.md)
- [Backups and restore](../guides/backups-and-restore.md)
