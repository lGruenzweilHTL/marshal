# Health and discovery endpoints

The unversioned endpoints of Marshal and of the executor host that report health and supported versions.

## Marshal

### `GET /health`

Liveness and readiness. No authentication, and no secret values in the response. Use it for container health checks and uptime monitors.

```sh
curl -s http://<marshal>:8080/health
```

| HTTP status | Overall `status` | Meaning |
|---|---|---|
| `200` | `healthy` | All checks are healthy. |
| `200` | `degraded` | At least one check has warnings. Marshal still serves requests. |
| `503` | `unhealthy` | At least one check failed. |

Response body:

```json
{
  "status": "degraded",
  "checks": {
    "database": { "status": "healthy", "description": "reachable", "warnings": null },
    "config": { "status": "healthy", "description": "loaded", "warnings": null },
    "secrets": { "status": "degraded", "description": "optional secrets missing", "warnings": ["secret reference mail_work does not resolve"] },
    "mcp": { "status": "healthy", "description": "1 of 1 servers connected", "warnings": null },
    "memory": { "status": "healthy", "description": "sqlite-vec <version>", "warnings": null },
    "homeassistant": { "status": "healthy", "description": "connected", "warnings": null }
  }
}
```

Each check has a `status` (`healthy`, `degraded` or `unhealthy`), a `description` and `warnings`, a list of sentences or null.

### Checks

| Check | Healthy | Degraded or unhealthy |
|---|---|---|
| `database` | `reachable`. | `unhealthy`: `unreachable`. |
| `config` | `loaded`. | `degraded` with `reload failed`. Warnings list each file whose last reload failed (`risk.yaml`, `satellites.yaml`: `last reload failed, previous version active`), and skill and template errors (`skills/...`, `templates/...`). The previous configuration stays active. |
| `secrets` | `resolved`. | `degraded` with `optional secrets missing`. Warnings name the references: `secret reference <name> does not resolve`. Names only, never values. A secret that startup requires (model API keys, OIDC client secret, the Home Assistant token) fails startup instead. |
| `mcp` | `<n> of <m> servers connected`. | `degraded` with the same count. Warnings list servers that are down (`MCP server <id> is down: <reason>`) or connecting, `untrustedTools` globs that match no tool, and configuration errors in `/config/mcp` (`mcp/<file>: <message>`). A down server's tools are hidden while other tools keep working. |
| `memory` | `sqlite-vec <version>`. | `unhealthy` with the error when the `sqlite-vec` extension does not load. Vector search fails until it does. |
| `homeassistant` | `not configured` or `connected`. | `degraded` with `not connected` and the last error, when `ha.url` is set and the cache has no connection. |

> [!NOTE]
> The web UI shows the same information on the **System** page and moves the health widget up on **Home** when a check has warnings.

### `GET /api/versions`

Lists the API versions this build serves. No authentication. The Home Assistant component calls it during setup.

```json
{ "supported": ["1.0"], "deprecated": [] }
```

A request to an unknown `/api/...` route or an unsupported version returns `404` with the problem code `api.unsupported_version`, the header `api-supported-versions` and the list `supportedVersions`.

### `GET /api/v1/system`

Requires authentication. Returns the application `version`, `supportedApiVersions`, `deprecatedApiVersions`, `eventSources` and `simulatedIntegrations`. `eventSources` lists, for each event source, its `source` name, `connected` state, whether it is a `webhook` source and `lastReceivedAt`. For webhook sources, `connected` means a webhook arrived in the last 24 hours. See [events](events.md). `simulatedIntegrations` lists the integrations [demo mode](../guides/demo-mode.md) simulates (`home_assistant`, `printers`, `mail`, `rss`, `notes`); it is empty without demo mode.

### `GET /api/v1/system/health`

Requires a signed-in user or a token with the `admin` scope. Returns `warnings`: the current health warnings, errors first, then by component and code. The list is empty when everything is fine. Changes are published on the live topic `health` (event `health.changed`, see [live events](../api/live-events.md)). `/health` is unchanged.

#### Health warnings

Each warning has a `component` (for example `database`, `mcp:paperless`), a stable `code`, a `severity` (`warning` or `error`), a `message` in German and English, an optional `link` into the web UI and `since`, when it first appeared. Messages are written by Marshal and never contain secret values or text from external systems; error details stay in the log and the config view.

| Code | Severity | Meaning |
|---|---|---|
| `database.unreachable` | error | The database does not answer. |
| `memory.vector_search_unavailable` | error | Memory vector search is unavailable. |
| `secrets.missing` | warning | A secret that `/config` references does not resolve. |
| `config.reload_failed` | warning | A file in `/config` failed to reload; the previous version stays active. |
| `config.skill_rejected` | warning | A skill was rejected. |
| `config.template_rejected` | warning | A template was rejected. |
| `homeassistant.disconnected` | warning | The Home Assistant websocket is disconnected. |
| `mcp.unreachable` | warning | An MCP server is down; its tools are hidden. |
| `mcp.config_rejected` | warning | An MCP server file was rejected. |
| `mcp.untrusted_tools_unmatched` | warning | An `untrustedTools` glob matches no tool of the server. |
| `mcp.tool_pending` | warning | A tool of an MCP server waits for approval ([tool pinning](../configuration/mcp-servers.md#tool-pinning)). |
| `tools.unknown_core_namespace` | warning | `tools.core` names a namespace no tool source has. |

The web UI shows the warnings in a banner on every page, and the model can read them with `system.health` (see [tools](tools.md)).

### Other unversioned paths

`/openapi/{document}.json`, authentication callbacks and the web UI routes are unversioned too. See [OpenAPI](../api/openapi.md).

## Executor host

The separate [executor host](../installation/executor-host.md) answers `GET /health` without authentication.

```json
{ "status": "ok" }
```

## Related

- [Observability guide](../guides/observability.md)
- [Metrics and traces](metrics-and-traces.md)
- [API overview](../api/index.md)
- [Troubleshooting](../guides/troubleshooting.md)
