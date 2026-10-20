# Tasks

## 1. Built-in widgets

- [x] 1.1 Write failing runner tests: a web turn's successful call of a tool with a widget emits a `widget` event with kind, call id and data from the provider; voice and API turns emit none; failed calls emit none. Add `ToolDescriptor.Widget`, `IWidgetDataProvider` and the emission. Verify the tests pass.
- [x] 1.2 Write failing tests for `PrinterWidgetData` and `MusicWidgetData` (data shape from the Moonraker cache and HA). Implement and declare the widgets on `printer.status`, `music.queue` and `music.play`. Verify the tests pass.
- [x] 1.3 Write failing API tests for `POST /api/v1/music/{speaker}/control` (cookie principal, guard, recorded run with trigger `widget:music`) and for printer widget actions through the existing endpoints (confirm-tier only with the dialog flag). Implement. Verify the tests pass.
- [x] 1.4 Write failing component tests for the printer and music widgets (live updates from topics, buttons, confirmation dialog for confirm-tier actions, German and English strings) and for storing widget events with the message. Implement. Verify `npm test` passes.

## 2. MCP app resources

- [x] 2.1 Write failing tests with an in-process MCP server: `apps` option (default false, rejected with `untrusted: true`); `ui://` resource read on a web turn's call with caching by `ttlMs`; non-profile MIME types and resources above 1 MB skipped with a run note; `_meta.ui` part of the pin fingerprint. Implement in `McpToolSource` and the config parser. Verify the tests pass.
- [x] 2.2 Write failing tests for `app_instances` (owner-scoped) and `GET /mcp-apps/{appId}` (owner check, the app CSP header exactly as specified, 404 for other owners). Add the table (migration, `erd.puml`) and implement `IAppInstanceStore` and the endpoint. Verify the tests pass.
- [x] 2.3 Write failing tests for the `app` stream event and the SPA CSP (`frame-src 'self'`). Implement. Verify the tests pass.

## 3. App requests

- [x] 3.1 Verify the MCP Apps 2026-01-26 message names (initialize, tool input and result notifications, tool call, message, open link) and record them in design.md. Verify the note is in design.md.
- [x] 3.2 Write failing API tests for `POST /api/v1/apps/{appId}/tool-calls`: same-server tools only; visibility rules apply; confirm tier denied with reason `app_confirm` and no pending action; result returned and recorded as a run with trigger `app:<server>/<tool>`; not added to the session's model history. Implement through `IToolCallPipeline`. Verify the tests pass.
- [x] 3.3 Write failing component tests for `AppHost` against a fake app page: sandbox attribute exactly `allow-scripts`; messages from other sources ignored; initialize handshake; tool input and result delivered; tool-call requests proxied; message requests answered with an error; link requests shown as a link without navigation; server label visible. Implement. Verify `npm test` passes.
- [x] 3.4 Write failing component tests for reload: widgets render with live data; apps re-render when the stored result is complete and the resource loads; otherwise the "no longer available" card. Implement. Verify `npm test`, `npm run build` and the screenshot sweep (preview scenario with both widgets and a fake app) pass.

## 4. Documentation

- [x] 4.1 Document widgets in the chat guide and `apps` with the sandbox limits (no network, no forms, same-server tool calls, no confirm-tier) in the MCP guide; add `apps` with a comment to the MCP example config. Verify the example config loads and the pages follow `docs/development/docs-style.md`.

## 5. Integration check

- [ ] 5.1 Run `dotnet test`, `npm test`, `npm run build` and `openspec validate mcp-apps --strict`. Verify all pass.
