# Configure satellite profiles

Give individual Assist satellites a reply length, notes, a display, a music speaker and tool limits through `/config/satellites.yaml`.

## Before you start

- Marshal is connected to Home Assistant and a satellite uses Marshal as its conversation agent ([Assist pipeline](assist-pipeline.md)).
- You know the satellite's entity id, for example `assist_satellite.kitchen`.
- For `display` or `speaker`, the display or speaker is already configured. See [displays](../displays.md) and [music](../integrations/music.md).

Profiles are optional. A satellite without a profile uses defaults: normal verbosity, no denied tools, no display, no speaker.

## Steps

::: steps
1. **Add a profile**

   Edit `/config/satellites.yaml`. Keys are satellite entity ids.

   ```yaml
   satellites:
     assist_satellite.kitchen:
       verbosity: short
       notes: "Cooking area. Timer and recipe questions are common."
       display: kitchen_tv          # a name from displays.surfaces in marshal.yaml
       speaker: kitchen             # a name from music.speakers in marshal.yaml
     assist_satellite.kids_room:
       verbosity: short
       denyConfirm: [printer.*, calendar.*]
       denyTools: [mail.*]
   ```

   | Key | Effect |
   |---|---|
   | `verbosity` | `short`, `normal` (default) or `detailed`. Marshal passes it to the model as the preferred reply length. |
   | `notes` | Free text added to the satellite context in the system prompt. |
   | `denyTools` | Tool id globs. Matching tools are hidden from the model and denied. |
   | `denyConfirm` | Tool id globs. Confirm-tier tools that match are hidden. |
   | `display` | Display in the satellite's room. `artifacts_show` plays there. |
   | `speaker` | Music speaker in the satellite's room. The music tools default to it. |

2. **Save the file**

   Marshal hot-reloads the file. No restart is needed.
:::

## What the policy does

- **Denied tools** are filtered out of the tool list, so the model does not see them.
- **Denied confirmation** hides confirm-tier tools. For tools whose tier depends on the arguments (for example a Home Assistant call that resolves to a lock), the tool stays visible and the guard denies a call that would need confirmation with the error "not allowed from this device". No pending action is created.
- A turn that Marshal moves to the background keeps the satellite's `denyTools` and `denyConfirm`.
- Marshal tells the model the area, floor, local time and language of each turn. When music speakers exist, it also names this room's speaker.

> [!NOTE]
> Profiles limit what a fixed device can do. They are not an authorization boundary: anyone who speaks to the satellite is the owner as far as Marshal knows.

## Check that it works

1. Open **Settings > Configuration** in the web UI (admin only). The satellite appears with its verbosity, notes and deny lists, and the last reload error per file.
2. Ask the satellite for a denied tool. Marshal says it cannot do that.

## Troubleshooting

- A `display` or `speaker` that names nothing configured is a configuration error for the file, reported as `satellites.<id>.display names no configured display '<name>' (displays.surfaces)`. The previous profiles stay active until you fix the file; the Configuration view shows the error.
- `verbosity` must be one of `short`, `normal`, `detailed`.

## Related

- [`satellites.yaml` reference](../../configuration/satellites-yaml.md)
- [Tool visibility](../../concepts/tool-visibility.md)
- [Risk tiers](../../reference/risk-tiers.md)
