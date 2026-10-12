# MCP servers (`mcp/*.yaml`)

Each file in `/config/mcp/` defines one Model Context Protocol server whose tools Marshal offers to the model. Marshal reloads the directory without a restart; tool lists of running turns do not change mid-turn.

## Example

```yaml
id: ha
transport: http
url: http://homeassistant.local:8123/api/mcp
authTokenSecret: ha_token
untrusted: false
untrustedTools: ["todo__get_items"]
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
| `authTokenSecret` | secret name | none | Secret holding a bearer token, sent as `Authorization: Bearer <token>`. |
| `envSecrets` | list of `{ variable, secret }` | `[]` | Environment variables of a stdio server filled from secrets. Both fields are required. |
| `untrusted` | boolean | `false` | When `true`, every tool's output is third-party data and taints the run (source `mcp:<id>`). |
| `untrustedTools` | list of globs | `[]` | Globs on the tool names the server reports. Only matching tools taint the run. Entries must not be empty. |

`untrusted: true` together with a non-empty `untrustedTools` is an error.

## Tool names

The namespace is `toolPrefix`, or `id` without it. A tool the server reports as `intent__HassTurnOn` under id `ha` has the internal id `ha.intent__HassTurnOn` and the model-facing name `ha_intent__HassTurnOn`. There is no `mcp.` prefix. Risk rules in [`risk.yaml`](risk-yaml.md) match the internal id. Unmatched MCP tools are `confirm` by default.

The namespace must not be one reserved for built-in tools: `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music`.

## Behavior

- A stdio child does not inherit Marshal's environment. It gets only `PATH`, `HOME`, `LANG`, `LC_ALL`, `TZ` and `TMPDIR` from the Marshal process, plus the `envSecrets` of its file.
- A server that is down is reconnected with exponential backoff (1 second doubling up to 1 minute). Its tools are hidden while it is down, and `/health` shows a warning.
- A glob in `untrustedTools` that matches no tool after discovery shows as a warning for that server on the config view and `/health`; the server's tools still load.
- A secret that does not resolve makes the server unavailable: `<file>: authTokenSecret (<name>) does not resolve.` or `<file>: envSecrets.<variable> (<name>) does not resolve.`

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
- [Home Assistant MCP](../guides/integrations/home-assistant-mcp.md)
- [Untrusted content](../concepts/untrusted-content.md)
- [Secrets reference](secrets-reference.md)
