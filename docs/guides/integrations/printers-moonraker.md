# Connect a Moonraker printer

Connect a Klipper printer through Moonraker so you can ask for its status, look at the webcam, control prints with confirmation, and let the print babysitter pause a failing print.

## Before you start

- A printer running Klipper with Moonraker, reachable from Marshal's container.
- Moonraker's API key, if it requires one (set `[authorization]` in `moonraker.conf`). Networks you trust in `trusted_clients` need none.
- For snapshots and the babysitter: a webcam configured in Moonraker (Marshal discovers its `snapshot_url` through `/server/webcams/list`).
- For `printer.inspect` and the babysitter: a model with `vision: true` in the `fast` or `standard` tier. See [models](../../installation/models-setup.md).
- Optional: the Mainsail or Fluidd URL.

## Steps

### 1. Store the API key

Provide it as `/run/secrets/moonraker_voron` or `MARSHAL_SECRET_MOONRAKER_VORON`. Skip this step if Moonraker needs no key.

### 2. Configure the printer

In `/config/marshal.yaml`:

```yaml
printers:
  - name: voron
    url: http://voron.local:7125
    webUrl: http://voron.local
    apiKeySecret: moonraker_voron
    macros: [PARK, LOAD_FILAMENT, UNLOAD_FILAMENT]
    babysitter: { enabled: true, interval: "00:01:00" }
printerSnapshots: { maxSize: 1024, quality: 85, retention: 20 }
```

The name allows letters, digits, `-` and `_` and must be unique. Every key is in the [integration sections reference](../../configuration/marshal-yaml/integrations.md#printers-and-printersnapshots-moonraker). Changes need a restart.

### 3. Restart and open the printer page

The web UI shows the printer page with live status, a temperature chart, progress, ETA, layer, webcam, job queue and history.

## Tools

Printer tools take an optional `printer` argument. With one printer it defaults to that printer. With several and no argument the tool returns an error listing the names. Tools are hidden while no printer is configured.

| Tool | Tier | What it does |
|---|---|---|
| `printer.status` | safe | State, job, progress, layer, time left, temperatures, and the babysitter state. |
| `printer.list_files` | safe | Printable files, newest first (50 at most). |
| `printer.job_queue` | safe | The job queue. |
| `printer.history` | safe | Recent jobs, 1 to 50 (default 10). |
| `printer.snapshot` | safe | Webcam snapshot shown as an image. Hidden without a webcam. |
| `printer.inspect(question)` | safe | Sends a snapshot and your question to a vision model. Hidden without a webcam or vision model. |
| `printer.pause` | safe | Pauses the print at once. |
| `printer.emergency_stop` | safe | Halts the printer. Klipper needs a restart afterwards. |
| `printer.start_print(file)` | confirm | Starts a file from the printer's list. |
| `printer.resume` | confirm | Resumes a paused print. |
| `printer.cancel` | confirm | Cancels the running print. |
| `printer.set_temperature(heater, target)` | confirm | `extruder` up to 350 C, `bed` up to 150 C; 0 turns it off. Klipper's own `max_temp` still applies. |
| `printer.home(axes)` | confirm | Homes `x`, `y`, `z`, or all axes. |
| `printer.move(axis, distance)` | confirm | Relative move, non-zero, at most 300 mm either way. |
| `printer.edit_queue(action, ...)` | confirm | `add`, `remove`, `clear`, `pause`, `start`, `jump`. |
| `printer.run_macro(name)` | confirm | Runs an allowlisted macro. Hidden while no macro is configured. |

`pause` and `emergency_stop` are safe because stopping a print early costs little, and waiting for a confirmation costs more. Everything that heats, moves or starts something needs confirmation. See [risk tiers](../../reference/risk-tiers.md).

### Allowlisted macros, never raw G-code

Marshal has no tool that sends G-code, restarts firmware or the host, or deletes files. The only scripts that reach Moonraker's `/printer/gcode/script` are the ones Marshal builds from validated arguments (temperature, homing, a relative move) and the macro names you list in `macros`.

- `printer.run_macro` accepts only names in the configured `macros` list of that printer, compared in upper case. The tool's name argument is limited to those names.
- A macro never receives arguments.
- A name outside the list fails validation before any confirmation is asked.

List only macros that are safe to start unattended after a confirmation, such as `PARK` or `LOAD_FILAMENT`. A macro runs whatever your Klipper config says, so Marshal's protection is only as good as the macro.

## Print babysitter

The babysitter watches running prints for failures. It is a system component, not an agent task. The agent cannot start, stop or reconfigure it. Only `babysitter.enabled` in the config turns it on.

How it works:

1. When a print starts (or resumes, or is already running when Marshal starts), the babysitter takes a webcam snapshot every `interval`. The first check comes one interval after the start.
2. A vision model (`fast` tier, else `standard`) judges each snapshot. It looks for spaghetti, a detached or knocked-over part, a large blob around the nozzle, or printing into the air. A dark, blurry or unclear image is not a failure.
3. One positive verdict does nothing. **Two positive verdicts in a row** trigger action. A negative or missing verdict resets the count.
4. On the second strike Marshal pauses the print directly, with no agent loop and no confirmation, and sends a `critical` notification with the snapshot. It is delivered even during quiet hours.
5. The watch ends when the print pauses, completes, is cancelled, errors, or Klipper shuts down. Resuming the print starts a new watch.

`printer.status` and the printer page show whether the babysitter is active and its last verdicts.

Requirements: `interval` of at least 10 seconds, a webcam, and a vision model. Each check is one model call, so a short interval costs tokens or GPU time.

The babysitter only pauses. It never cancels. You decide what happens next.

## Snapshots

Marshal fetches the snapshot, resizes it with SkiaSharp to `printerSnapshots.maxSize` pixels, stores it under `/data/snapshots` and serves it from Marshal's origin. It keeps the newest `retention` snapshots per printer. The printer page offers **Keep**, which copies the shown snapshot to the artifact store as an `image` artifact with source `printer`. Kept artifacts are not pruned. See [artifacts](../artifacts.md).

## The printer page

Controls on the page call the API directly, not the model, with the same tiers. Safe actions act at once. Confirm-tier actions show a confirmation dialog. Forbidden actions are not offered, and the page links to `webUrl` (Mainsail or Fluidd) instead. Every control action is written to the audit log with the actor. The browser loads the webcam from Marshal, not from the printer.

## Limits

- Status comes from a websocket state cache. While the websocket is down, status tools fail with code `printer.offline`. Marshal reconnects with backoff and rereads the full state.
- The state cache covers `print_stats`, `virtual_sdcard`, `extruder`, `heater_bed`, `webhooks` and `display_status`.
- One webcam snapshot per request; no video analysis.

## Check that it works

1. The printer page shows live temperatures.
2. Ask "how is the print going?" The model calls `printer_status`.
3. Ask "how does the print look?" The model calls `printer_inspect` and answers from the snapshot.
4. Ask "heat the nozzle to 200". Marshal asks for confirmation first.
5. With the babysitter on, start a print. `printer_status` reports the babysitter as active.

## Troubleshooting

- **`printer.offline`.** Check `url`, the API key and that Moonraker's `trusted_clients` or `cors_domains` allow Marshal.
- **No `printer_inspect`.** No webcam is found, or no vision model is configured.
- **`printer_run_macro` is missing.** `macros` is empty.

See [troubleshooting](../troubleshooting.md).

## Related

- [Integration sections reference](../../configuration/marshal-yaml/integrations.md#printers-and-printersnapshots-moonraker)
- [Notifications and `critical` priority](../notifications.md)
- [Risk rules](../risk-rules.md)
- [Risk tiers](../../reference/risk-tiers.md)
- [Secrets](../secrets.md)
