# Home Assistant MCP server

Let the model control your home through Home Assistant's MCP server, with Marshal's risk rules deciding which calls need confirmation.

Marshal uses HA's REST and websocket APIs for its own system calls and HA's MCP server for model-driven home control. The MCP server is configured as server id `ha`.

## Before you start

- [Home Assistant is connected](home-assistant.md) with `ha.url` and `ha.tokenSecret`.
- In HA, the **Model Context Protocol Server** integration is installed and set to the **Assist** API. It serves tools at `/api/mcp`. Tool names and the endpoint can vary with the HA version, so check what your HA reports.
- The entities Marshal may control are exposed to Assist. HA's exposure settings are the first filter.

## Steps

### 1. Add the server file

Create `/config/mcp/ha.yaml`, or copy `deploy/config.example/mcp/ha.yaml`:

```yaml
id: ha
transport: http
url: http://homeassistant.local:8123/api/mcp
authTokenSecret: ha_token
untrusted: false
untrustedTools: ["todo__get_items"]
```

`authTokenSecret` is a secret name. It may be the same secret as `ha.tokenSecret`. Marshal sends it as a bearer token. See [MCP servers](../../configuration/mcp-servers.md) for every key.

MCP files are hot-reloaded. No restart is needed. The new tools appear in the next turn.

### 2. Keep the shipped risk rules

`deploy/config.example/risk.yaml` contains:

```yaml
risk:
  - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
    tier: confirm
  - match: { tool: "ha.*", domain: cover, deviceClass: [garage, door, gate] }
    tier: confirm
  - match: { tool: "ha.*" }
    tier: safe
```

Rules are evaluated in order and the first match wins. Marshal resolves the call's targets first, so `ha.intent__HassTurnOff` with `area = hall` is checked against every light and lock in the hall. A lock in the hall makes the whole call `confirm`.

> [!IMPORTANT]
> External tools that no rule matches default to `confirm`. Without the catch-all `ha.*` rule every HA call would ask for confirmation. Keep a safe rule for the calls you trust.

See [risk rules](../risk-rules.md).

### 3. Mark third-party text as untrusted

To-do lists synced from other apps hold text other people typed. Mark only the list read tool:

```yaml
untrustedTools: ["todo__get_items"]
```

Reading a list then taints the run (source `mcp:ha`), while home control stays trusted. `untrustedTools` takes globs on the tool name HA reports. A glob that matches no tool shows a warning on `/health` and the config view. You cannot combine `untrusted: true` with `untrustedTools`: Marshal rejects the file. See [untrusted content](../../concepts/untrusted-content.md).

## Tool names

Tools use the server id as namespace, without an `mcp.` prefix.

| HA reports | Internal id | Model-facing name |
|---|---|---|
| `intent__HassTurnOn` | `ha.intent__HassTurnOn` | `ha_intent__HassTurnOn` |
| `homeassistant__GetLiveContext` | `ha.homeassistant__GetLiveContext` | `ha_homeassistant__GetLiveContext` |

Characters outside `[a-zA-Z0-9_-]` become `_`. Names over 64 characters are shortened with a hash suffix. Risk rules match the internal id.

## Limits

- The server id and `toolPrefix` must match `[a-z][a-z0-9]*`. `home_assistant` is rejected.
- Reserved built-in namespaces (`mail`, `music`, `timers` and others) cannot be used as id or `toolPrefix`.
- A server that is down loses its tools until it reconnects (with backoff). Other tools keep working and `/health` shows a warning.
- A bad file is rejected on its own. Other servers load.
- A tool list never changes in the middle of a turn.
- Tools the voice satellite denies (`denyTools`, `denyConfirm`) are hidden, not refused. See [satellites](../voice/satellites.md).

## Check that it works

1. Open `/health`. Server `ha` shows no warning.
2. Ask "turn on the kitchen light". The light switches on without a confirmation, as the catch-all rule says.
3. Ask "unlock the front door". Marshal asks for confirmation first.

## Troubleshooting

- **No `ha_` tools.** Check the URL ends in `/api/mcp`, the MCP Server integration is set to the Assist API, and the token is valid.
- **Every call asks for confirmation.** The catch-all `ha.*` rule is missing from `risk.yaml`.
- **Warning for a glob.** The tool name HA reports differs. Read the exact name on the config view.

## Related

- [Connect Home Assistant](home-assistant.md)
- [Custom MCP servers](custom-mcp-servers.md)
- [MCP servers reference](../../configuration/mcp-servers.md)
- [Risk rules](../risk-rules.md)
- [Tool visibility](../../concepts/tool-visibility.md)
