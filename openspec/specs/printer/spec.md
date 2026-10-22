# printer Specification

## Purpose
3D printers through Moonraker: configuration, the live state cache, tools with risk tiers, snapshots with inspection, and the printer page.

## Requirements

### Requirement: Printer configuration
Printers SHALL be configured as `printers: [{ name, url, apiKeySecret, powerEntity? }]`. `powerEntity`, when set, SHALL be a Home Assistant `switch.*` entity that powers the printer; another domain SHALL fail startup with a configuration error naming the key. Printer tools SHALL take an optional `printer` argument that defaults to the only configured printer; with several printers and no argument, the tool SHALL return an error listing the names.

#### Scenario: Single printer default
- **WHEN** one printer `voron` is configured and `printer_status` is called without `printer`
- **THEN** the status of `voron` is returned

#### Scenario: Power entity of the wrong domain
- **WHEN** a printer sets `powerEntity: light.voron`
- **THEN** startup fails with an error naming `printers[0].powerEntity`

### Requirement: State cache from the websocket
The system SHALL connect to Moonraker's websocket (`/websocket`, JSON-RPC 2.0) with the API key, identify itself, subscribe to `print_stats`, `virtual_sdcard`, `extruder`, `heater_bed`, `webhooks` and `display_status`, and maintain a state cache by merging `notify_status_update` diffs into the initial state. It SHALL resubscribe after `notify_klippy_ready` and reconnect with exponential backoff, re-reading the full state after every reconnect. Status tools and the printer page SHALL read the cache.

#### Scenario: Diff merge
- **WHEN** the initial state has extruder temperature 25 and a diff sets it to 200
- **THEN** the cache reports 200 and all other fields unchanged

#### Scenario: Printer offline
- **WHEN** the websocket is disconnected
- **THEN** status tools return a failed result with code `printer.offline`

### Requirement: Printer tools and tiers
The system SHALL provide these tools with these fixed tiers:
- safe: `printer.status`, `printer.list_files`, `printer.job_queue`, `printer.history`, `printer.snapshot`, `printer.inspect(question)`, `printer.pause`, `printer.emergency_stop`
- confirm: `printer.start_print(file)`, `printer.resume`, `printer.cancel`, `printer.set_temperature(heater, target)`, `printer.home(axes)`, `printer.move(axis, distance)`, `printer.edit_queue(action, ...)`, `printer.run_macro(name)`

Raw G-code, firmware / host restart and file deletion SHALL NOT exist as tools or code paths. `printer.run_macro` SHALL only accept macros from the configured allowlist.

#### Scenario: Pause without confirmation
- **WHEN** the model calls `printer_pause`
- **THEN** the print pauses immediately

#### Scenario: Heating needs confirmation
- **WHEN** the model calls `printer_set_temperature` for the extruder
- **THEN** a pending action with reason `rule` is created

#### Scenario: Macro not allowlisted
- **WHEN** `printer_run_macro` is called with a macro not in the allowlist
- **THEN** the call fails validation before any confirmation is asked

### Requirement: Snapshots and inspection
Snapshots SHALL be fetched from the `snapshot_url` discovered via `/server/webcams/list`, resized with SkiaSharp, stored under `/data`, and returned as a same-origin URL. `printer.inspect(question)` SHALL send the resized snapshot and the question to a model with the `vision` capability of the run's tier (falling back to `standard` + `vision`) and return its answer.

#### Scenario: How does the print look
- **WHEN** a voice turn calls `printer_inspect` and the `fast` tier has a vision model
- **THEN** the snapshot is resized and sent to that model and the answer is returned as text

### Requirement: Printer page
The web UI SHALL show live printer status from the state cache (via the live-update stream), temperatures with a chart, progress, ETA, current layer, a webcam snapshot / stream proxied through Marshal, the job queue and print history. Controls SHALL call the API directly (not the model) with the same tiers: safe actions act immediately, confirm-tier actions require a UI confirmation dialog, forbidden actions are not offered (link to Mainsail / Fluidd instead). Every control action SHALL be written to the audit log with the actor.

#### Scenario: Start from the UI
- **WHEN** the user clicks "start" on a file
- **THEN** a confirmation dialog appears, and only after confirming is the print started and an audit event written

#### Scenario: Webcam through Marshal
- **WHEN** the printer page shows the webcam
- **THEN** the browser loads it from Marshal's origin, not from the printer's address

### Requirement: Keeping snapshots
The printer page SHALL offer a "Keep" action on the shown snapshot that copies it into the artifact store as an `image` artifact with source `printer`, the printer name and snapshot time as source reference, not tainted. Kept artifacts SHALL NOT be affected by the snapshot store's retention.

#### Scenario: Keep a snapshot
- **WHEN** the user clicks "Keep" on a snapshot of printer `voron`
- **THEN** an image artifact with source `printer` exists and survives the pruning of the original snapshot
