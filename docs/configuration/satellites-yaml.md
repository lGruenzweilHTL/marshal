---
icon: fa-solid fa-tower-broadcast
description: "/config/satellites.yaml holds optional per-satellite profiles, keyed by the satellite entity id."
---

# `satellites.yaml`

`/config/satellites.yaml` holds optional per-satellite profiles, keyed by the satellite entity id. Marshal reloads the file without a restart.

A profile is a convenience limit for fixed devices, not an authorization boundary. Unlisted satellites, and turns without a `satelliteId`, use the defaults: normal verbosity, no denied tools, no display, no speaker.

## Example

```yaml
satellites:
  assist_satellite.kitchen:
    verbosity: short
    notes: "Cooking area. Timer and recipe questions are common."
    display: kitchen_tv
    speaker: kitchen
  assist_satellite.kids_room:
    verbosity: short
    denyConfirm: [printer.*, calendar.*]
    denyTools: [mail.*]
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `satellites` | map | `{}` | Profiles keyed by satellite entity id. |
| `satellites.<id>.verbosity` | `short`, `normal` or `detailed` | `normal` | How long answers from this satellite are. |
| `satellites.<id>.notes` | string | none | Free text added to the satellite context lines of the system prompt. |
| `satellites.<id>.denyTools` | list of globs | `[]` | Tool id globs hidden from the model and denied by the guard for turns from this satellite. |
| `satellites.<id>.denyConfirm` | list of globs | `[]` | Tool id globs whose confirm-tier calls are not allowed here. Confirm-tier tools are hidden. Argument-aware tools (for example `ha.*`) stay visible; a call that resolves to confirm is denied with "not allowed from this device" and creates no pending action. |
| `satellites.<id>.display` | string | none | Name of a surface in [`displays.surfaces`](marshal-yaml/displays.md) in the satellite's room. `artifacts_show` plays there. |
| `satellites.<id>.speaker` | string | none | Name of a speaker in `music.speakers` (see [`integrations`](marshal-yaml/integrations.md)) in the satellite's room. `music_*` tools default to it. |

The satellite's area and floor, local time, day of week and language come from Home Assistant per turn, not from this file.

Task runs started from a satellite keep that satellite's policy for the whole task tree.

## Validation

Errors name the file and key. A failing first load stops startup; a failing reload keeps the previous profiles.

- `satellites.yaml: satellites.<id>.verbosity must be one of short, normal, detailed.`
- `satellites.yaml: satellites.<id>.display names no configured display '<name>' (displays.surfaces).`
- `satellites.yaml: satellites.<id>.speaker names no configured speaker '<name>' (music.speakers).`
- Unknown keys, invalid YAML, or a credential-like value are rejected.

## Related

::: cards
- [Satellites guide](../guides/voice/satellites.md)
- [`risk.yaml`](risk-yaml.md)
- [Tool visibility](../concepts/tool-visibility.md)
:::
