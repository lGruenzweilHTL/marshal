# Risk tiers

The three risk tiers, how Marshal picks the tier of a tool call, and the reasons, statuses and decisions it records.

## Tiers

Every tool call has exactly one tier.

| Tier | Behavior |
|---|---|
| `safe` | The tool runs immediately. |
| `confirm` | The tool runs only after a user action confirms the frozen call. |
| `forbidden` | The tool never runs. It is removed from the tool list, and the guard denies a call to it. |

Capabilities that must never exist (sending mail, raw G-code) are not tools at all, so no tier applies to them. See [tools](tools.md) for the default tier of every tool.

## How the tier is resolved

1. **Rules.** Marshal evaluates the rules of [`risk.yaml`](../configuration/risk-yaml.md) in order. The first matching rule sets the tier. A rule matches on a glob of the tool id, optional argument conditions and optional target conditions (`domain`, `deviceClass`, `area`).
2. **Default.** If no rule matches, built-in tools use the default tier of their descriptor. External tools (MCP servers) default to `confirm`.
3. **Targets.** For rules with target conditions, Marshal expands the call to its concrete targets and evaluates the rules per target. The highest tier wins. A call whose targets cannot be resolved, resolve to nothing, or belong to a namespace without a resolver while a target rule matches is `confirm` with reason `unresolved_target`.
4. **Confirming arguments.** Some arguments raise a call to at least `confirm` when they are set. `overrideQuietHoursUntil` on `tasks.create`, `tasks.watch` and `tasks.update` is one: the reason is `quiet_hours_override`.
5. **Taint.** In a tainted run, calls to tools flagged `persists` are raised from `safe` to `confirm` with reason `taint`. The guard only raises tiers, never lowers them.
6. **Visibility.** Before every model call Marshal hides tools that are `forbidden`, not in the template's allowlist, denied for the satellite (`denyTools`), confirm-tier on a satellite that denies confirmation for them (`denyConfirm`), confirm-tier inside a subagent, or hidden by the tool's own condition. Tools whose tier depends on arguments stay visible. The guard repeats these checks on every call.

> [!NOTE]
> A rule can lower a built-in tool's tier, for example from `confirm` to `safe`. Marshal never lowers a tier by itself, and taint escalation applies on top of the result.

## Confirmation flow

A `confirm` call in an interactive turn becomes a pending action.

1. Marshal stores the tool, arguments, session, satellite, owner, a nonce, the reasons and an expiry. It ends the turn with `continueConversation = true` and asks the user.
2. A session has at most one pending action. A new one supersedes the old one.
3. On the next user turn, Marshal normalizes the whole utterance (trim, lowercase, strip punctuation) and compares it with the confirmation keywords of the request language. An exact match runs the frozen action without a model call.
4. Otherwise a `fast`-tier judge reads only the action summary and the reply and answers `positive`, `neutral` or `negative`.
   - `positive` runs the frozen action.
   - `neutral` drops the action and handles the message as a new turn.
   - `negative` drops the action and replies that it was cancelled.
   - A judge timeout or unparsable answer drops the action.
5. The model cannot change a frozen action. After the expiry (`confirmation.ttl`) nothing runs and the action is `expired`.

Only a user action confirms: a user turn in the same session, a web UI approval or a nonce-verified push action. Tool output, model output and other content never confirm. Subagents never run confirm-tier actions. In background tasks, a confirm-tier call goes to the approval queue (see [approvals](../guides/tasks/approvals.md)).

Keywords, TTL and judge timeout are set in [`confirmation`](../configuration/marshal-yaml/confirmation.md).

## Confirmation reasons

Each pending action carries one or more reasons, each with a code and a short sentence in the request language. The tool result tells the model the sentence, and the system prompt requires the question to include it. When several reasons apply, `taint` is the spoken one.

| Code | Meaning |
|---|---|
| `rule` | A rule or the tool's default tier requires confirmation. |
| `unresolved_target` | The call's targets could not be resolved, or resolved to nothing, while a target rule applies. |
| `taint` | The run read untrusted content and the tool persists state. The sentence names the source, for example mail. |
| `quiet_hours_override` | The call sets `overrideQuietHoursUntil`. |

## Guard decisions

The guard records one decision on every tool-call step of a run.

| Decision | Meaning |
|---|---|
| `safe` | The call was safe and ran. |
| `pending` | The call became a pending action. |
| `confirmed_by_keyword` | A confirmation keyword matched. |
| `judge_positive` | The judge accepted the confirmation. |
| `judge_neutral` | The judge saw a topic change and dropped the action. |
| `judge_negative` | The judge saw a refusal and dropped the action. |
| `escalated_by_taint` | A `persists` tool was raised to `confirm` in a tainted run. |
| `denied` | The guard refused the call, with the reason. The model receives an error result. |
| `expired` | The user confirmed after the expiry. |
| `confirmed_in_ui` | The user confirmed the action in Marshal's own dialog, for example the cancel button of the printer widget in the chat. |

A tool call that an MCP app requests is never confirmed: a confirm-tier call from an app is `denied` with the error code `app_confirm`, and no pending action is created. A click inside server-authored HTML does not count as your confirmation.

A pending action itself ends as confirmed, `expired`, `superseded` or dropped. These decisions appear as the `decision` tag of the `marshal.tool.calls` metric, see [metrics and traces](metrics-and-traces.md).

## Taint

A run becomes tainted when the result of a tool flagged untrusted enters its context. It records the taint sources (kind and first tainting step) and never clears them within the run. Starting a run from an event also taints it, with source `event`, unless the event source is trusted in code.

| Taint source | Set by |
|---|---|
| `mail` | `mail.search`, `mail.read_message`, `mail.read_thread` |
| `rss` | `rss.get_unread`, `rss.get_article`, `rss.search`, `rss.fetch_full_text` |
| `notes` | `notes.*` |
| `web` | `web.search`, and `web.answer` with answer type `text` |
| `calendar` | `calendar.list_events` |
| `media` | `media.*` |
| `music` | `music.search`, `music.queue` |
| `github` | `repos.*` |
| `artifact` | `artifacts.list`, `artifacts.inspect` |
| `notifications` | `notifications.inbox_list` |
| `mcp:<server id>` | Tools of an untrusted MCP server, or matching `untrustedTools` |
| `event` | A run started by an untrusted event |

Tools that persist state and are escalated in a tainted run: `memory.remember`, the `tasks.*` tools that store work (`create`, `schedule_wakeup`, `watch`, `update`, `run_executor`) and `mail.save_attachment`.

## Related

- [Tools](tools.md)
- [`risk.yaml`](../configuration/risk-yaml.md)
- [Risk rules guide](../guides/risk-rules.md)
- [Risk and confirmation](../concepts/risk-and-confirmation.md)
- [Untrusted content](../concepts/untrusted-content.md)
