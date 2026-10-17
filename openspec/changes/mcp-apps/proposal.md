# Proposal

## Why

Chat replies are text, images and artifact cards. Some answers are better as small interactive views: the printer's live status with a pause button, the music queue with skip and volume, or an MCP server's own UI (a chart, a form, a player). MCP Apps (SEP-1865, stable since 2026-01-26, shipped in Claude, ChatGPT, VS Code and Goose) standardizes how an MCP server attaches an HTML UI (`ui://` resource) to a tool and how the host renders it in a sandboxed iframe. Marshal can render such UIs for servers the user allows. It can also render its own built-in widgets for its own tools, which gives value right away.

## What Changes

- **Built-in widgets:** built-in tools can declare a widget. When such a tool runs in a web turn, the stream carries a `widget` event, and the chat renders a Vue component inline. v1: printer status (live progress, temperatures, pause/resume/cancel) and music now playing (track, queue, play/pause, skip, volume). Widget buttons call Marshal's API as user actions; confirm-tier actions open a confirmation dialog, and confirming it counts as the user's confirmation.
- **MCP Apps:** servers opt in with `apps: true` in their `/config/mcp/<id>.yaml` (default off; not allowed with `untrusted: true`). When a web turn calls a tool of such a server whose definition names a `ui://` resource, Marshal reads the resource and renders it in a sandboxed iframe (opaque origin, no access to Marshal's cookies, storage or API, no network) next to the reply, and passes it the tool's input and result.
- The app can ask the host to call tools of **its own server only**. Such calls run through the guard as a separate run, with the result going back to the app only, never into the model's context. Confirm-tier calls from an app are denied (a click inside server-authored HTML is not a user confirmation). Requests to post chat messages are denied; requests to open links show the link for the user to click.
- The SPA's content security policy allows frames from Marshal's own origin only.

## Capabilities

### New Capabilities

- `chat-apps`: built-in widgets and MCP Apps in web chat: rendering, sandbox, messaging, tool calls from apps, persistence on reload.

### Modified Capabilities

- `conversation-api`: `widget` and `app` events in the stream.
- `mcp-client`: `apps` server option and reading `ui://` resources.
- `web-shell`: CSP allows same-origin frames.

## Non-goals

- Apps or widgets on voice, push or display surfaces.
- Network access for MCP apps (servers declaring external domains get no network).
- Apps posting messages into the conversation or updating the model's context.
- Marshal serving MCP Apps from its own MCP server.
- More built-in widgets than printer and music in v1.

## Impact

- `src/Marshal.Abstractions`: widget declaration on tool descriptors.
- `src/Marshal.Core/Mcp`: `apps` option, `ui://` resource reading, app tool-call proxy (same server, guard, separate run).
- `src/Marshal.Api`: `widget` / `app` stream events, app HTML endpoint with its own CSP, app tool-call endpoint, CSP `frame-src 'self'`.
- `src/Marshal.Integrations.Moonraker`, `.HomeAssistant` (music): widget declarations and widget data.
- `web/`: widget components, sandboxed app host with the MCP Apps postMessage protocol, persistence on reload.
- Docs: chat guide (widgets), MCP guide (`apps`, sandbox limits).
