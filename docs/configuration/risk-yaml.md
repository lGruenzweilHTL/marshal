---
icon: fa-solid fa-shield-halved
description: "/config/risk.yaml holds the ordered rules that set the risk tier of tool calls."
---

# `risk.yaml`

`/config/risk.yaml` holds the ordered rules that set the [risk tier](../reference/risk-tiers.md) of tool calls. The first matching rule wins. Marshal reloads the file without a restart.

## Example

```yaml
risk:
  - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
    tier: confirm
  - match: { tool: "ha.*", domain: cover, deviceClass: [garage, door, gate] }
    tier: confirm
  - match: { tool: "tasks.create", args: { template: briefing } }
    tier: confirm
  - match: { tool: "ha.*" }
    tier: safe
```

## Keys

The file has one top-level key. A missing file or an empty file means no rules.

| Key | Type | Default | Description |
|---|---|---|---|
| `risk` | list of rules | `[]` | Rules, evaluated in order. |
| `risk[].match` | map | required | Conditions. Must contain `tool`. |
| `risk[].match.tool` | string (glob) | required | Glob on the internal tool id (`namespace.name`, for example `ha.intent__HassTurnOn`). `*` matches any run of characters, `?` one character. Case-sensitive. |
| `risk[].match.args` | map | none | Argument conditions. Each key is a top-level argument name; the value is one value or a list of values. The call matches when its argument equals one of them (case-insensitive). Strings, numbers and booleans compare as text. A missing argument never matches. |
| `risk[].match.<attribute>` | string or list of strings | none | Any other key is a target condition, for example `domain`, `deviceClass`, `area`. It is compared with each concrete target the call resolves to (case-insensitive, any listed value matches). |
| `risk[].tier` | `safe`, `confirm` or `forbidden` | required | Tier when the rule matches. Case-insensitive. Numbers are rejected. |

All conditions of a rule must hold. A rule with only `tool` matches every call to those tools.

## Target conditions

Home Assistant tools (`ha.*`) resolve their targets through the registry. Every filter in the call (name, area, floor, domain, device class) expands to the concrete entities. Each target carries these attributes:

| Attribute | Value |
|---|---|
| `domain` | Entity domain, for example `lock`, `cover`, `light`. |
| `deviceClass` | The entity's device class, when it has one. |
| `area` | The entity's area name, when it has one. |

Rules are evaluated per target and the highest tier wins. A call whose targets cannot be resolved, resolve to nothing, or belong to a namespace without a resolver while a target rule matches its tool id is `confirm` with reason `unresolved_target`.

## Tools no rule matches

A call that no rule matches uses the tool's default tier. Built-in tools use the default their descriptor declares. External tools (MCP servers) default to `confirm`, so add an explicit rule such as `{ tool: "ha.*" } -> safe` to let them run without confirmation. The same rules apply to built-in and external tools.

## Validation

Startup fails, or a reload keeps the previous rules and logs the error, when:

- the YAML is invalid or has an unknown top-level key: `risk.yaml: invalid value at line <n>: ...`
- `risk.yaml: risk[<i>].match.tool is required and must be a glob string.`
- `risk.yaml: risk[<i>].tier must be safe, confirm or forbidden.`
- `risk.yaml: risk[<i>].match.args must map argument names to values.`
- `risk.yaml: risk[<i>].match.args.<name> must be a value or a list of values.`
- `risk.yaml: risk[<i>].match.<key> must be a value or a list of values.` (a list must be non-empty and contain only strings)
- a value looks like a credential (see [secrets reference](secrets-reference.md#literal-secret-check)).

## Related

::: cards
- [Risk tiers](../reference/risk-tiers.md)
- [Risk rules guide](../guides/risk-rules.md)
- [Risk and confirmation](../concepts/risk-and-confirmation.md)
- [`satellites.yaml`](satellites-yaml.md)
:::
