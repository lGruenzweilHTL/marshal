# MCP servers (`mcp/*.yaml`)

Each file in `/config/mcp/` defines one Model Context Protocol server whose tools Marshal offers to the model. Marshal reloads the directory without a restart; tool lists of running turns do not change mid-turn.

## Example

```yaml
id: ha
transport: http
url: http://homeassistant.local:8123/api/mcp
description: "Home control: lights, switches, covers, climate, locks, media players and the state of devices and areas"
authTokenSecret: ha_token
untrusted: false
untrustedTools: ["todo__get_items"]
pinTools: true
```

A local server over stdio:

```yaml
id: search
transport: stdio
command: /usr/local/bin/search-mcp
args: ["--safe"]
envSecrets:
  - variable: SEARCH_API_KEY
    secret: search_key
untrusted: true
```

## Keys

Only the keys below are accepted. The file name is free; the `id` must be unique across files.

| Key | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Server id. Must match `[a-z][a-z0-9]*`. |
| `transport` | `http` or `stdio` | required | `http` is streamable HTTP. |
| `url` | absolute `http(s)` URL | required for `http` | Server endpoint. |
| `command` | string | required for `stdio` | Executable to start. |
| `args` | list of strings | `[]` | Arguments for `command`. |
| `toolPrefix` | string | `id` | Namespace for the server's tools instead of the id. Must match `[a-z][a-z0-9]*`. |
| `description` | string | built from `id` and the tool names | One-line English description of what the server's tools are about. Tool retrieval matches turns against it; see [`tools`](marshal-yaml/tools.md). |
| `authTokenSecret` | secret name | none | Secret holding a bearer token, sent as `Authorization: Bearer <token>`. |
| `envSecrets` | list of `{ variable, secret }` | `[]` | Environment variables of a stdio server filled from secrets. Both fields are required. |
| `untrusted` | boolean | `false` | When `true`, every tool's output is third-party data and taints the run (source `mcp:<id>`). |
| `untrustedTools` | list of globs | `[]` | Globs on the tool names the server reports. Only matching tools taint the run. Entries must not be empty. |
| `pinTools` | boolean | `true` | Pins every tool definition at first discovery. A new or changed tool later stays hidden until an admin approves it. See [tool pinning](#tool-pinning). |
| `apps` | boolean | `false` | Shows the server's MCP Apps UIs in the web chat, in a sandbox. See [MCP apps](#mcp-apps). |

`untrusted: true` together with a non-empty `untrustedTools` is an error. `untrusted: true` together with `apps: true` is an error too.

## Tool names

The namespace is `toolPrefix`, or `id` without it. A tool the server reports as `intent__HassTurnOn` under id `ha` has the internal id `ha.intent__HassTurnOn` and the model-facing name `ha_intent__HassTurnOn`. There is no `mcp.` prefix. Risk rules in [`risk.yaml`](risk-yaml.md) match the internal id. Unmatched MCP tools are `confirm` by default.

The namespace must not be one reserved for built-in tools: `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music`, `system`.

## Behavior

- Marshal speaks MCP protocol revision 2026-07-28 and falls back to the newest revision a server supports among 2025-11-25 and 2025-06-18, so servers such as Home Assistant's keep working. It never offers sampling, roots or logging to a server.
- When a server's tool list carries a `ttlMs`, Marshal does not list the tools again before that time passes, unless the server sends a change notification. Without `ttlMs`, Marshal lists again on change notifications only.
- A stdio child does not inherit Marshal's environment. It gets only `PATH`, `HOME`, `LANG`, `LC_ALL`, `TZ` and `TMPDIR` from the Marshal process, plus the `envSecrets` of its file.
- A server that is down is reconnected with exponential backoff (1 second doubling up to 1 minute). Its tools are hidden while it is down, and `/health` shows a warning.
- A glob in `untrustedTools` that matches no tool after discovery shows as a warning for that server on the config view and `/health`; the server's tools still load.
- A secret that does not resolve makes the server unavailable: `<file>: authTokenSecret (<name>) does not resolve.` or `<file>: envSecrets.<variable> (<name>) does not resolve.`

## Tool pinning

Marshal stores a fingerprint of each tool's name, title, description, input schema, output schema and annotations. Key order and whitespace do not change the fingerprint.

- The first discovery of a server pins every tool as approved, without asking.
- A tool that is new after the first discovery, or whose definition changed, waits for approval. It is hidden from the model and the guard denies calls to it. The server's other tools stay available.
- A tool that changes back to its approved definition is approved again without a decision.
- The owner gets one notification per waiting tool. The health warning `mcp.tool_pending` shows until the tool is decided.
- Admins approve or reject in the web UI under System > Configuration, with the previous and the current definition side by side. Each decision is audited and applies from the next turn.

With `pinTools: false` the server's tools are never pinned. Use it for servers whose tool list changes often by design, such as Home Assistant when you expose entities. Pinning protects against later changes only: a server that is malicious at first discovery is pinned as it is.

## MCP apps

With `apps: true`, a tool whose definition names a `ui://` resource (MCP Apps, `_meta.ui.resourceUri`) shows that UI below the reply when a web chat turn calls it. Voice, API and task runs never show apps.

- Marshal reads the resource with `resources/read` and caches it for the result's `ttlMs`, 5 minutes by default.
- The resource must have the MIME type `text/html;profile=mcp-app` and be at most 1 MB. Otherwise the reply shows without the app, and the run records a note `app_skipped` on the tool step.
- The app runs in an iframe with `sandbox="allow-scripts"` and the CSP `sandbox allow-scripts; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'self'`. It has no access to Marshal's cookies, storage or API, and it cannot connect to any host, submit forms, open pop-ups or navigate the page. The resource's `csp` and `permissions` metadata are ignored.
- The app can call tools of its own server only, and only tools you could use in a web chat turn. Each call runs through the guard as its own run with trigger `app:<server>/<tool>`. The result goes to the app only, never to the model. A confirm-tier call is denied with `app_confirm`.
- Requests to post chat messages or change the model's context are refused. A request to open a link shows the link below the app; you open it yourself.
- The chat keeps the app with the reply. A reopened conversation shows it again while the server still has `apps: true`, the resource can be read and the tool result was at most 256 KB. Otherwise the chat shows "This interactive view is no longer available."

The tool's `_meta.ui` block is part of the pinned definition, so a changed UI reference waits for approval. The HTML itself is not pinned.

## Input requests

A server can ask the user for input during a tool call (MCP elicitation, form or URL mode). Marshal never passes the request to the model:

- The request becomes a pending input request with the call frozen (server, tool, arguments). The model gets a fixed result saying the user was asked in the web UI.
- The owner gets a push with a link and no actions. Only the web UI answers: Inbox > input request, or the chat.
- A form answer must fit the requested schema. A URL request is answered with "Done" after you finish on the page.
- Answering repeats the frozen call once with the answer. If the server asks again, a new input request follows.
- Declining or expiry (the approval TTL) sends nothing to the server; the waiting task continues with the refusal.

## Server tasks

A server can answer a tool call with a task handle instead of a result (the MCP tasks extension). Marshal then:

- creates a waiting task that delivers where the call came from (the satellite of a voice turn, otherwise the web conversation and the Inbox), and tells the model the work continues in the background;
- polls the server's task status, never sooner than the server's poll interval, from 5 seconds with backoff up to 5 minutes;
- publishes the [`mcp.task_finished` event](../reference/events.md#mcp-server-tasks-mcp) and resumes the waiting task with the result or error when the server task ends. The result is untrusted data if the tool is untrusted.
- turns each input request of a running server task into a pending input request. Answering, declining or expiry sends the decision to the server task; the task keeps waiting for the outcome.

Cancelling the waiting task cancels the server task. One owner can follow at most 50 server tasks at a time; beyond that, Marshal cancels the new server task and the tool result says why. Handles survive restarts.

## Validation

A rejected file never affects the others. Errors show on `/health` and the config view and in the log.

- `<file>: id '<id>' must match [a-z][a-z0-9]*.`
- `<file>: toolPrefix '<prefix>' must match [a-z][a-z0-9]*.`
- `<file>: namespace '<ns>' is reserved for built-in tools; set another id or toolPrefix.`
- `<file>: untrusted: true already marks every tool; remove untrustedTools or set untrusted: false.`
- `<file>: untrustedTools entries must not be empty.`
- `<file>: authTokenSecret must be a secret name (^[a-z][a-z0-9_]{0,63}$).`
- `<file>: every envSecrets entry needs a variable and a secret name.`
- `<file>: url must be an absolute http(s) URL for transport http.`
- `<file>: command is required for transport stdio.`
- `<file>: transport '<value>' must be 'stdio' or 'http'.`
- `Server id '<id>' is already configured in another file.`
- `Namespace '<ns>' is already used by another server.`
- A literal secret in the file, an unknown key, or invalid YAML (`invalid value at line <n>`).
- A server that fails to start or discovery, or whose tool names collide, is rejected with an error.

## Related

- [Custom MCP servers](../guides/integrations/custom-mcp-servers.md)
- [Health endpoints](../reference/health-endpoints.md)
- [Home Assistant MCP](../guides/integrations/home-assistant-mcp.md)
- [Untrusted content](../concepts/untrusted-content.md)
- [Secrets reference](secrets-reference.md)
