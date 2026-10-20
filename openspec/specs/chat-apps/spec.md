# chat-apps Specification

## Purpose
Shows interactive views next to chat replies: Marshal's own widgets for its built-in tools, and MCP Apps UIs from servers the user allows, rendered in a sandbox.

## Requirements

### Requirement: Built-in widgets
A built-in tool MAY declare a widget kind. When a web turn calls such a tool successfully, the stream SHALL carry a `widget` event with the kind, the tool call id and the widget's data, and the chat SHALL render the widget below the reply. v1 widget kinds: `printer` (from `printer.status`: state, progress, temperatures, file, live updates on the printer topic, pause / resume / cancel) and `music` (from `music.queue` and `music.play`: current item, next items, play / pause, next, previous, volume, live updates). Widget buttons SHALL call Marshal's API as user actions with the cookie principal; an action whose tool is confirm tier SHALL first show a confirmation dialog naming the action, and confirming it SHALL count as the user's confirmation. Voice and API turns SHALL NOT produce widget events.

#### Scenario: Printer status widget
- **WHEN** a web turn calls `printer_status`
- **THEN** the reply shows the printer widget with live progress

#### Scenario: Pause from the widget
- **WHEN** the user clicks pause in the printer widget
- **THEN** the print pauses through the API with the user as actor and the run is recorded with trigger `widget:printer`

#### Scenario: Cancel needs confirmation
- **WHEN** the user clicks cancel in the printer widget and `printer.cancel` is confirm tier
- **THEN** a confirmation dialog appears and the print is cancelled only after the user confirms

### Requirement: MCP Apps rendering
When a web turn calls a tool of an MCP server with `apps: true` whose tool definition names a `ui://` resource, the system SHALL read the resource from the server and the stream SHALL carry an `app` event with the server, tool, tool call id and an app id. The chat SHALL render the app in an iframe loaded from Marshal's origin with `sandbox="allow-scripts"` only (no same-origin, no forms, no popups, no top navigation), served with a CSP that allows inline scripts and styles, images from `data:` only, and no network connections. The host SHALL deliver the tool's input and result to the app through the MCP Apps messaging protocol. A resource that is not `text/html` with the MCP App profile, exceeds 1 MB, or cannot be read SHALL be skipped with a note in the run, and the reply SHALL render without it.

#### Scenario: App shown
- **WHEN** a web turn calls `charts.sales_report` of a server with `apps: true` whose tool names `ui://charts/report`
- **THEN** the reply shows the app in a sandboxed iframe that receives the tool result

#### Scenario: No cookies
- **WHEN** an app's script tries to read `document.cookie` or call `/api/v1/...`
- **THEN** it gets no cookie and the request is blocked

#### Scenario: Server without apps
- **WHEN** the server has `apps: false`
- **THEN** no `app` event is sent, even if its tool names a `ui://` resource

### Requirement: Requests from apps
An app's request to call a tool SHALL be allowed only for tools of the app's own server that are visible to the owner's web turns, and SHALL run through the guard as a separate run with source `web` and trigger `app:<server>/<tool>`, with the result returned to the app only and never added to the model's context. A call that resolves to confirm tier SHALL be denied with an error to the app. Requests to post chat messages or change the model's context SHALL be denied. A request to open a link SHALL show the link below the app for the user to click; the host SHALL NOT navigate by itself.

#### Scenario: Refresh from the app
- **WHEN** the app asks to call `charts.sales_report` again with another month
- **THEN** the call runs through the guard as its own run and the app receives the new result

#### Scenario: Other server's tool
- **WHEN** the app asks to call `ha.HassTurnOn`
- **THEN** the request is denied

#### Scenario: Confirm-tier from an app
- **WHEN** the app asks to call a tool of its server that is confirm tier
- **THEN** the request is denied and no pending action is created

### Requirement: Widgets and apps after reload
Widget and app events SHALL be stored with the message. When a conversation is reopened, widgets SHALL render with current live data. An app SHALL render again when its stored tool result is complete and the resource can still be read; otherwise the chat SHALL show a card saying the interactive view is no longer available.

#### Scenario: Reopen with printer widget
- **WHEN** the user reopens a conversation with a printer widget an hour later
- **THEN** the widget shows the printer's current state

#### Scenario: App server gone
- **WHEN** the app's server was removed and the conversation is reopened
- **THEN** the chat shows the "no longer available" card in place of the app
