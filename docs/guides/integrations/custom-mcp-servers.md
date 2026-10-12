# Add a custom MCP server

Connect any Model Context Protocol (MCP) server to Marshal so its tools appear next to the built-in ones.

## Before you start

- An MCP server that speaks streamable HTTP, or a command Marshal can start.
- Access to `/config/mcp/`.
- For authentication: a secret holding the token, see [secrets](../secrets.md).

> [!IMPORTANT]
> The Marshal container has no Node.js or Python runtime. A `stdio` server needs its command inside the container, so build a derived image that includes it. Prefer `http` for anything that runs elsewhere.

## Steps

### 1. Create a server file

Add one YAML file per server in `/config/mcp/`. The file name is free.

```yaml
id: search                      # lowercase letters and digits, starts with a letter
transport: http                 # http (streamable HTTP) or stdio
url: http://search.lan:3000/mcp
authTokenSecret: search_token   # secret NAME, sent as "Authorization: Bearer <value>"
toolPrefix: web                 # optional; replaces the id as tool namespace
untrusted: true                 # tool output is third-party data
```

For a local process, use `stdio`:

```yaml
id: files
transport: stdio
command: /usr/local/bin/files-mcp
args: ["--root", "/data/shared"]
envSecrets:
  - variable: FILES_API_KEY
    secret: files_api_key
```

Home Assistant's own MCP server uses the same format, see [Home Assistant MCP](home-assistant-mcp.md) and `deploy/config.example/mcp/ha.yaml`.

### 2. Keys

| Key | Type | Description |
|---|---|---|
| `id` | string | Required. Matches `[a-z][a-z0-9]*`. Unique across files. |
| `transport` | `http` or `stdio` | Required. |
| `url` | URL | Required for `http`. Absolute `http` or `https`. |
| `command`, `args` | string, list | `command` is required for `stdio`. |
| `toolPrefix` | string | Optional namespace instead of `id`. Same pattern. |
| `authTokenSecret` | secret name | Optional. Sent as a bearer header. |
| `envSecrets` | list of `variable` and `secret` | Environment variables of a `stdio` server filled from secrets. |
| `untrusted` | boolean | Default `false`. Marks every tool's output as third-party data. |
| `untrustedTools` | list of globs | Marks only matching tools (by the name the server reports). Not allowed together with `untrusted: true`. |

Unknown keys are errors. Config never holds secret values, and a literal secret in the file fails loading. See the full reference in [`mcp-servers`](../../configuration/mcp-servers.md).

### 3. Set risk rules

A tool from an MCP server that no rule in `risk.yaml` matches is in the confirm tier, so every call needs your approval. Add rules to allow read-only tools:

```yaml
risk:
  - match: { tool: "search.*" }
    tier: safe
```

The match is a glob on the internal tool id `<namespace>.<name>`. See [risk rules](../risk-rules.md).

### 4. Wait for the reload

Adding, changing or removing a file takes effect without a restart. Running turns keep their tool list. The next turn sees the change.

## How tools appear

The namespace is the `id` or `toolPrefix`, without any `mcp` prefix. Server `ha` reporting `intent__HassTurnOn` gives the internal id `ha.intent__HassTurnOn` and the model-facing name `ha_intent__HassTurnOn`.

Marshal reserves the namespaces of its built-in tools: `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music`. A server that would use one is rejected.

## Untrusted servers

If a server returns text that other people control, such as web search or shared lists, mark it:

- `untrusted: true` for every tool of the server.
- `untrustedTools: ["todo__get_items"]` for single tools, so home control stays trusted.

A run that calls such a tool is tainted with source `mcp:<id>`, its results are wrapped as data, and memory and task writes in that run need confirmation. A glob that matches no reported tool shows a warning for that server on the config view and `/health`. See [untrusted content](../../concepts/untrusted-content.md).

## Failure handling

A server that fails to start, fails discovery, uses a reserved or duplicate namespace, or has colliding tool names is rejected with an error. Other servers and built-in tools keep working. A server that disconnects is reconnected with backoff, and its tools are hidden while it is down.

## Check that it works

1. Open `/health`. The MCP check reports the connected servers and lists warnings for any server that is down or rejected.
2. Open the config view in the web UI and find your server and its tools.
3. Start a new conversation and ask Marshal to use a tool of the server. A tool without a matching risk rule asks for confirmation first.

## Related

- [`mcp-servers` reference](../../configuration/mcp-servers.md)
- [Risk rules](../risk-rules.md)
- [Tool visibility](../../concepts/tool-visibility.md)
- [Secrets](../secrets.md)
- [Home Assistant MCP](home-assistant-mcp.md)
