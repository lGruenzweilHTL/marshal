# Design

## Context

See proposal.md (Why). Current state:

- Web chat renders markdown and artifact events; the SPA CSP allows same-origin scripts and styles, images from `'self'` and `data:`.
- The printer page and the live-update stream already show live printer state; music tools control Music Assistant through HA.
- MCP client: tools only, no resources. The pinning change fingerprints tool definitions.
- MCP Apps (SEP-1865): tools declare `_meta.ui.resourceUri = "ui://…"`; the resource has MIME type `text/html;profile=mcp-app`; host and app talk JSON-RPC over `postMessage` (initialize handshake, tool input and result notifications, app requests such as tool calls, messages, opening links). Verify the exact method names against the stable 2026-01-26 spec when building.

## Goals / Non-Goals

**Goals:**
- Server-authored HTML can never act as the user or read Marshal's data.
- Built-in widgets reuse existing APIs and live topics.

**Non-Goals:**
- Supporting app CSP domain declarations (network for apps).

## Decisions

### 1. Built-in widgets

`ToolDescriptor.Widget` (kind string). After a successful call in a web turn, the runner emits `widget` with data from a widget data provider: `IWidgetDataProvider` (`Marshal.Abstractions`), `Kind`, `GetAsync(toolResult) → object`. v1: `PrinterWidgetData` (Moonraker cache), `MusicWidgetData` (HA). The SPA has one component per kind and subscribes to the existing live topics (printer, music state through the HA cache topic). Actions use existing REST endpoints where they exist (printer page). For music, new endpoints `POST /api/v1/music/{speaker}/control` call the same code as the tools. Each action is recorded as a run with trigger `widget:<kind>` and passes the guard with a user-confirmation flag set only after the dialog, so the guard's "only user actions confirm" rule holds: the dialog is Marshal's own UI.

### 2. App hosting

```
SPA (origin A, CSP frame-src 'self')
 └─ <iframe sandbox="allow-scripts" src="/mcp-apps/{appId}">   → opaque origin
       served with: CSP default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline';
                    img-src data:; frame-ancestors 'self'
```

`GET /mcp-apps/{appId}` (cookie principal, owner check) returns the stored resource HTML. Because the iframe has no `allow-same-origin`, its origin is opaque. It cannot read cookies, `localStorage` or the parent DOM, and `connect-src` defaults to none. The SPA's `AppHost` component is the only `postMessage` peer. It checks `event.source === iframe.contentWindow` and implements the host side of the protocol.

`app_instances (owner_id, id, session_id, run_id, server, tool, tool_call_id, resource_uri, html, created_at)` stores the HTML for reload. HTML is limited to 1 MB.

### 3. App requests

`POST /api/v1/apps/{appId}/tool-calls` (cookie principal, antiforgery) from the SPA on behalf of the app. Server-side checks: the tool belongs to the app's server; it is visible under the owner's web visibility rules; the guard tier is evaluated, and confirm resolves to a denial because the request originates in server-authored HTML (decision recorded on the step as `denied` with reason `app_confirm`). The call runs through `IToolCallPipeline` (from the MCP server change) as its own run. Messages and context updates from apps are answered with an error. Link requests are rendered by `AppHost` as a plain link below the iframe.

New interface: `IAppInstanceStore` (`Marshal.Abstractions`), v1 `EfAppInstanceStore`.

### 4. Pinning interaction

The tool's `_meta.ui` block is added to the pin fingerprint, so changing which UI a tool shows needs approval. The HTML itself is not pinned (servers may change it freely); it runs sandboxed without network, and its requests are limited as above.

## Risks / Trade-offs

- [Apps that need network do not work] → documented limitation; `apps: true` is opt-in per server.
- [A malicious app phishes inside the iframe ("enter your password")] → the iframe has no forms (`allow-forms` absent) and a visible frame label naming the server; documented.
- [Large tool results needed for reload] → reload only when the stored result is complete; otherwise the "no longer available" card.
- [Protocol details of MCP Apps change] → stable since 2026-01-26; the host code is isolated in `AppHost` and covered by component tests against a fake app.

## Migration Plan

EF migration adds `app_instances`. CSP change adds `frame-src 'self'`. Features are off until a server sets `apps: true`; built-in widgets appear immediately in web chat.

## Verified protocol details (task 3.1)

Checked against the stable MCP Apps specification `specification/2026-01-26/apps.mdx` (modelcontextprotocol/ext-apps, SEP-1865, extension id `io.modelcontextprotocol/ui`, protocol version `2026-01-26`):

- Tool metadata: `_meta.ui.resourceUri` (a `ui://` URI). Resource MIME type: `text/html;profile=mcp-app`. Resource metadata `_meta.ui.csp` (`connectDomains`, `resourceDomains`, `frameDomains`, `baseUriDomains`), `_meta.ui.permissions` and `_meta.ui.prefersBorder`; Marshal ignores `csp` and `permissions` (no network, no permissions). Tools may set `_meta.ui.visibility` (`["model", "app"]` by default).
- View → host requests: `ui/initialize` (params `protocolVersion`, `appCapabilities`, `clientInfo`; result `protocolVersion`, `hostCapabilities`, `hostInfo`, `hostContext`), `tools/call`, `resources/read`, `ui/open-link` (`url`), `ui/message` (`role`, `content`), `ui/update-model-context`, `ui/request-display-mode`. View → host notifications: `ui/notifications/initialized`, `notifications/message`, `ui/notifications/size-changed` (`width`, `height`).
- Host → view notifications: `ui/notifications/tool-input` (`arguments`), `ui/notifications/tool-input-partial`, `ui/notifications/tool-result` (a `CallToolResult`), `ui/notifications/tool-cancelled` (`reason`), `ui/notifications/host-context-changed`; host → view request `ui/resource-teardown` (`reason`).
- Marshal's host answers `tools/call` through `POST /api/v1/apps/{appId}/tool-calls`; `ui/message`, `ui/update-model-context` and `resources/read` with a JSON-RPC error (`-32601`); `ui/open-link` by showing the link below the frame; `ui/request-display-mode` with `inline`. It sends `tool-input` and `tool-result` after `ui/notifications/initialized`.

## Decisions made while building

- The app HTML's CSP also carries `sandbox allow-scripts`. Without it, a user who opens `/mcp-apps/{appId}` directly (or follows a link an app shows) would run server-authored script in Marshal's origin with the session cookie. With it, the document has an opaque origin wherever it is loaded.
- `app_instances` also stores the call's arguments (`args`) and its `CallToolResult` (`result`, null above 256 KB), so `GET /api/v1/apps/{appId}` can hand the app its input and result after a reload. `available` is true when the result is complete, the server still has `apps: true` and the resource can still be read.
- `ToolOutput.CallResult` carries the raw `CallToolResult` JSON of every MCP call (up to 256 KB); app tool calls return it unchanged, so apps get `structuredContent`. A failed call reaches the app as a `CallToolResult` with `isError: true`.
- `ToolCallRequest.ConfirmationDenial` makes the pipeline deny a confirm-tier call instead of freezing it; app calls set it with code `app_confirm`. The step is recorded as `denied`.
- A skipped app resource is recorded as a context note `app_skipped` (label: the URI, detail: the reason) on the tool step.
