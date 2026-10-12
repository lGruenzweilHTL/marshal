# Environment variables

Every environment variable that Marshal and the executor host read. Marshal reads no other variables of its own.

## Marshal

| Variable | Default | Description |
|---|---|---|
| `MARSHAL__<Section>__<Key>` | none | Overrides a `marshal.yaml` key. `__` separates path segments and replaces `.` in the key path. Names are case-insensitive. Layering, low to high: built-in defaults, `/config/marshal.yaml`, environment. |
| `MARSHAL__Paths__Config` | `/config` | Directory with `marshal.yaml`, `risk.yaml`, `satellites.yaml`, `mcp/`, `skills/`, `templates/`. Read before configuration exists, so set it as an environment variable. |
| `MARSHAL__Paths__Data` | `/data` | Database, Data Protection keys (`/data/keys`), backups, artifacts, notes repository, snapshots. |
| `MARSHAL__Paths__Secrets` | `/run/secrets` | Directory of secret files (Docker secrets). |
| `MARSHAL_SECRET_<NAME>` | none | Provides the secret `<name>`; see below. Never enters configuration. |

The container image sets the three `MARSHAL__Paths__*` variables to the defaults above.

### Examples

```sh
MARSHAL__Http__PathBase=/marshal
MARSHAL__Models__Fast__Model=qwen3:8b
MARSHAL__Auth__Providers__0__Name=local
MARSHAL__Embedding__Provider=ollama
```

List items use their index (`__0__`).

An invalid value that came from a variable is reported with the variable name instead of `marshal.yaml`, for example `MARSHAL__Embedding__Provider`.

### Secret variables

`MARSHAL_SECRET_<NAME>` has one underscore after `MARSHAL`, so it never matches the `MARSHAL__` configuration prefix. `<NAME>` is the secret name in upper case: the secret `moonraker_voron` is `MARSHAL_SECRET_MOONRAKER_VORON`. The value is used as is, including any trailing newline. See [secrets reference](secrets-reference.md).

```sh
MARSHAL_SECRET_ANTHROPIC_KEY=<api-key>
```

### ASP.NET variables

Marshal clears the default ASP.NET configuration sources and builds its own, so `ASPNETCORE_*` and `DOTNET_*` variables do not set Marshal's options.

| Variable | Effect |
|---|---|
| `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS` | No effect. Kestrel listens on `http.urls` (default `http://0.0.0.0:8080`). Use `MARSHAL__Http__Urls`. |
| `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | Read by the .NET host. Marshal does not change behavior by environment name. |
| `TZ` | Process time zone. Passed to stdio MCP servers. Owner time zones come from `instance.timeZone` and user settings, not from `TZ`. |

### Variables passed to child processes

| Variable | Description |
|---|---|
| `PATH`, `HOME`, `LANG`, `LC_ALL`, `TZ`, `TMPDIR` | The only Marshal variables a stdio [MCP server](mcp-servers.md) inherits. Its `envSecrets` are added. |
| `GIT_TERMINAL_PROMPT=0`, `LC_ALL=C`, `GIT_SSH_COMMAND`, `GIT_ASKPASS`, `MARSHAL_GIT_TOKEN` | Set by Marshal for the `git` processes that mirror the notes repository. `MARSHAL_GIT_TOKEN` carries the `notes.credentialSecret` token for the duration of a call. Do not set them yourself. |

## Executor host

`Marshal.ExecutorHost` is a separate service. It uses the default ASP.NET Core configuration, so unprefixed environment variables set its `executorHost` section.

| Variable | Default | Description |
|---|---|---|
| `executorHost__MarshalUrl` | required | Marshal's absolute base URL as seen from the host. Callbacks go only below it. |
| `executorHost__TokenFile` | `/run/secrets/executor_host_token` | File with the token Marshal authenticates with. Read on every request, so you can rotate it without a restart. |
| `executorHost__RepositoriesRoot` | `/repos` | Directory whose subdirectories are the repositories sessions may use. |
| `executorHost__WorktreesRoot` | `/worktrees` | Directory for per-session git worktrees. |
| `executorHost__Image` | `marshal-claude-sandbox` | Sandbox image with Claude Code. |
| `executorHost__Network` | `marshal-sandbox` | Docker network of the sandbox. |
| `executorHost__EnvFile` | `/run/secrets/sandbox.env` | Environment file passed to the sandbox (for example `ANTHROPIC_API_KEY`). Passed to Docker, never read by the host. |
| `executorHost__MaxTurns` | `50` | Turn limit of a session. |
| `executorHost__MaxBudgetUsd` | `5` | Cost limit of a session in USD. |
| `DOCKER_HOST` | Docker default | Address of the rootless or Sysbox Docker daemon the host's `docker` CLI uses. Marshal itself never touches a Docker socket. |
| `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` | image default `8080` | Listen address of the host. |

`/repos` and `/worktrees` must be bind mounts with identical paths on the Docker host and in the host container.

## Related

- [Secrets reference](secrets-reference.md)
- [Configuration overview](index.md)
- [Executor host installation](../installation/executor-host.md)
- [Docker Compose installation](../installation/docker-compose.md)
