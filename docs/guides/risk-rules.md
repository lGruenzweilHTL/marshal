---
icon: fa-solid fa-shield-halved
description: "Tune which actions need your confirmation by editing /config/risk.yaml, starting from the example file."
---

# Risk rules

Tune which actions need your confirmation by editing `/config/risk.yaml`, starting from the example file.

## Before you start

- Access to the `/config` volume. `risk.yaml` hot-reloads: the next tool call uses the new rules. A rule file that fails validation is ignored, the previous rules stay active and the error is logged.
- Key reference: [`risk.yaml`](../configuration/risk-yaml.md). Tier definitions: [risk tiers](../reference/risk-tiers.md).

## How the guard decides

Every tool call has one tier: `safe` (runs), `confirm` (runs only after a user action confirms it) or `forbidden` (never runs). Rules are evaluated in order, the first match wins, and a tool with no matching rule uses its default tier. External tools (MCP servers) default to `confirm`. Rules apply identically to built-in and external tools.

Tools the model cannot use are not shown to it: `forbidden` tools are filtered out of the tool list, and the guard enforces the same rules server-side as a second line. Capabilities that must never exist (sending mail, raw G-code) are not tools at all, so no rule can enable them.

The guard can only raise a tier, never lower it. Tainted runs (that read mail, RSS, notes, web pages or event data) raise `persists` tools such as `memory.remember` and `tasks.create` from `safe` to `confirm`, whatever your rules say.

## The example file

```yaml
risk:
  - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
    tier: confirm
  - match: { tool: "ha.*", domain: cover, deviceClass: [garage, door, gate] }
    tier: confirm
  - match: { tool: "ha.*" }
    tier: safe
```

Reading it from the top: any Home Assistant call that targets a lock or an alarm panel needs confirmation; covers whose device class is garage, door or gate need confirmation; every other Home Assistant call is safe. Without the last rule, Home Assistant MCP tools would default to `confirm`.

Target conditions (`domain`, `deviceClass`, `area`) are checked against each concrete entity a call resolves to, and the highest tier wins. A call that targets an area with two lights and one lock is `confirm` as a whole. A call with an unknown device name, no resolved targets, or a namespace without a resolver while a target rule matches is `confirm` with the reason `I couldn't tell exactly which device you mean.`

## Steps

::: steps
1. **Add a rule above the catch-all**

   Rules match a tool glob, optionally argument values and target attributes. They cannot match the time of day. Examples:

   ```yaml
   risk:
     # never let the model open the garage, even after a confirmation
     - match: { tool: "ha.*", domain: cover, deviceClass: garage }
       tier: forbidden
     # confirm everything in the workshop
     - match: { tool: "ha.*", area: workshop }
       tier: confirm
     - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
       tier: confirm
     - match: { tool: "ha.*" }
       tier: safe
   ```

   Put specific rules above general ones. Check the tool ids in the [tools reference](../reference/tools.md); `tool` is a glob such as `ha.*` or `mail.*`.

2. **Save and check the log**

   If the file is invalid the log shows, for example, `risk.yaml: risk[2].tier must be safe, confirm or forbidden.` See the [risk.yaml validation list](../configuration/risk-yaml.md#validation).

3. **Tune confirmation behavior**

   Confirmation itself is set in [`confirmation`](../configuration/marshal-yaml/confirmation.md):

   - A pending action in a conversation lasts `confirmation.ttl` (default 60 s); there is at most one per session and a new one supersedes the old one.
   - Your next utterance is normalized (trimmed, lowercased, punctuation removed) and compared with the keywords of your language (English defaults: confirm, confirmed, approved, yes do it). Only an exact whole-utterance match executes without a model call. "Confirmed, but not today" is not a match.
   - Otherwise a fast-tier judge reads only the action summary and your reply and answers `positive` (execute), `neutral` (drop and handle your message normally) or `negative` (drop and say it was cancelled). A judge timeout (`confirmation.judgeTimeout`) or unparsable answer drops the action.
   - Background tasks use the approval queue instead, see [approvals](tasks/approvals.md).

   Satellites can also deny confirm-tier actions from a device (`denyConfirm` in [satellites](../configuration/satellites-yaml.md)): `This action is not allowed from this device.`
:::

## Check that it works

Ask Marshal to do something your rule covers. In **Runs**, open the tool step: the detail shows the risk tier and the guard decision (`safe`, `pending`, `confirmed_by_keyword`, `judge_positive`, `judge_neutral`, `judge_negative`, `escalated_by_taint`, `denied`, `expired`).

## Related

::: cards
- [Risk and confirmation](../concepts/risk-and-confirmation.md)
- [Tool visibility](../concepts/tool-visibility.md)
- [Security model](../concepts/security-model.md)
- [`risk.yaml`](../configuration/risk-yaml.md)
- [Risk tiers](../reference/risk-tiers.md)
:::
