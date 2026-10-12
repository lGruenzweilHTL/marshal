# Configuration

Marshal reads human-edited files from `/config` and environment variables. This page lists every file, the load order, what startup checks and which changes need a restart.

## Files in `/config`

| Path | Purpose | Reference |
|---|---|---|
| `marshal.yaml` | Instance settings, models, integrations. Read at startup only. | [marshal-yaml pages](#marshalyaml-sections) |
| `risk.yaml` | Risk rules: tier per tool, argument and target conditions. | [risk-yaml.md](risk-yaml.md) |
| `satellites.yaml` | Per-satellite profiles (verbosity, denied tools, display, speaker). | [satellites-yaml.md](satellites-yaml.md) |
| `mcp/*.yaml` | One file per MCP server. | [mcp-servers.md](mcp-servers.md) |
| `skills/<name>/SKILL.md` | Skills with optional extra files. | [skills-format.md](skills-format.md) |
| `templates/*.md` | Agent templates. | [templates-format.md](templates-format.md) |
| `tasks/*.yaml` | Example task definitions. Marshal does not read this folder; the files are the arguments of `tasks.create`. | [tasks-format.md](tasks-format.md) |

Every file is optional. A missing `marshal.yaml` means built-in defaults, but Marshal then refuses to start because `embedding` is required. The config directory is `/config`; set `MARSHAL__Paths__Config` to change it. See [environment variables](environment-variables.md).

Key names are camelCase and match the .NET options property names. Unknown keys in `risk.yaml` and `satellites.yaml` are errors. `marshal.yaml` ignores unknown keys, so a misspelled key silently keeps its default. Check the [config view](#config-view-in-the-web-ui).

## Load order

Configuration is layered. A later layer overrides an earlier one:

1. Built-in defaults (the defaults in the reference pages).
2. `/config/marshal.yaml`.
3. Environment variables with the exact prefix `MARSHAL__`.

`MARSHAL_SECRET_<NAME>` variables are secrets, not configuration. They never enter the configuration. See [secrets reference](secrets-reference.md).

## Environment overrides

Replace each `.` in the key path with `__`. Key names are case-insensitive.

| YAML | Environment variable |
|---|---|
| `http.pathBase: /b` | `MARSHAL__Http__PathBase=/b` |
| `models.fast.provider` | `MARSHAL__Models__Fast__Provider` |
| `auth.providers[0].name` | `MARSHAL__Auth__Providers__0__Name` |
| `agent.advisor.dailyCostCap` | `MARSHAL__Agent__Advisor__DailyCostCap` |

A validation error for a value set this way names the variable instead of `marshal.yaml`.

## Validation at startup

Startup checks in stages. A stage runs only if all earlier stages found no errors:

1. **Configuration.** Parse `marshal.yaml`, validate the options of every section, scan every `/config` file for literal secrets, validate `risk.yaml` and `satellites.yaml`, and reject secret values set directly in configuration.
2. **Database.** Initialize and migrate.
3. **Secrets.** Resolve every secret that must exist at startup.
4. **Services.** MCP servers, tool registry, HTTP listener.

The configuration and secrets stages collect all errors before failing. The exception is a `marshal.yaml` that is not valid YAML: Marshal reports it alone with line and column. Each error is one line naming the file (or environment variable) and the key. The line never contains the value. A stack trace is printed only when `logging.logLevel.default` is `Debug` or `Trace`. Every startup failure exits with code `1`.

### Literal-secret rejection

`/config` holds secret names only. Marshal scans the raw YAML of `marshal.yaml`, `risk.yaml`, `satellites.yaml`, and the MCP server, skill and template files, including unknown keys. It fails when:

| Rule | Example | Message |
|---|---|---|
| A key ending in `Secret` holds something other than a secret name (`^[a-z][a-z0-9_]{0,63}$`). | `apiKeySecret: My-Key` | `<file>: '<key>' must hold a secret name (^[a-z][a-z0-9_]{0,63}$), not a value.` |
| A key's last word is `password`, `passphrase`, `token`, `credential` or `credentials`, or its last two words are `api key`, `private key` or `access key`. Words are split at camelCase, snake_case and kebab-case boundaries. | `apiKey: abc` | `<file>: '<key>' names a credential. Store the value as a secret and reference it with a '...Secret' key.` |
| A value looks like a credential: PEM header, JWT (`eyJ...`), `sk-`, `sk-ant-`, `ghp_`, `xox?-`, a hex string of 32 or more characters, a base64 or base64url string of 32 or more characters that looks random, or another long high-entropy token. | any key | `<file>: the value of '<key>' looks like a credential. Store it as a secret and reference it by name.` |

`maxTokens: 4096` passes: the last word `tokens` is not `token`. A 32-character hex value fails even in a `...Secret` key.

Options properties that hold secret values (for example `models.<key>.apiKey`) cannot be set in configuration at all. Setting one fails with `<key> must not be set in configuration. Store the value as a secret and reference it by name.`

### Secrets that must resolve

After the database is migrated, Marshal resolves every secret reference that feeds an options value: `models.*.apiKeySecret`, `embedding.apiKeySecret`, `auth.providers[].clientCredentialSecret`, `ha.tokenSecret`, `printers[].apiKeySecret`, `freshrss.apiPasswordSecret`, `notes.credentialSecret` and `oauth.*.clientCredentialSecret`. An unresolved reference that startup needs (model and embedding API keys, the OIDC client secret, the Home Assistant token) fails startup, and the error lists all such references. Any other unresolved reference only produces a `/health` warning, and its integration stays unavailable. References read per request (`proxmox.tokenSecret`, `media.*.apiKeySecret`, `github.tokenSecret`, `executors.claudeCode.tokenSecret`) do not block startup. You can replace their values without a restart. See [secrets guide](../guides/secrets.md).

## Restart or reload

| Change | Takes effect |
|---|---|
| Anything in `marshal.yaml` | After restart. This includes models, auth, integrations and all limits. |
| Environment variables | After restart. |
| `risk.yaml`, `satellites.yaml` | Reload without restart. |
| `mcp/*.yaml`, `skills/**`, `templates/*.md` | Reload without restart. Tool lists of running turns do not change mid-turn. |
| Secret values from `/run/secrets` or the database | Restart for secrets bound at startup (list above); immediately for secrets read per request. |

`/config` is watched. A reload that fails validation keeps the previous configuration and logs the error. A broken file in `mcp/`, `skills/` or `templates/` is skipped; the other entries still load. A literal secret in any file fails startup.

`satellites.yaml` is validated against `displays.surfaces` and `music.speakers` from `marshal.yaml`, so removing a display needs a restart and then a matching `satellites.yaml`.

## Config view in the web UI

Admins see **System > Configuration** (`/system/config`, API `GET /api/v1/config`, scope `admin`). It is read-only and shows:

- loaded skills, templates, MCP servers (with status and tool count), risk rules, models and satellite profiles;
- secret names only, never values;
- the last load error per file, and whether the previous version stays active (`risk.yaml`, `satellites.yaml`) or the entry was skipped.

Users without the `admin` role get 403 and see no link. The view does not show the `marshal.yaml` sections other than models, so use it together with the pages below.

## marshal.yaml sections

| Section | Page |
|---|---|
| `instance`, `paths`, `logging`, `telemetry` | [instance.md](marshal-yaml/instance.md) |
| `owner` | [owner.md](marshal-yaml/owner.md) |
| `http` | [http.md](marshal-yaml/http.md) |
| `auth` | [auth.md](marshal-yaml/auth.md) |
| `models` | [models.md](marshal-yaml/models.md) |
| `embedding`, `memory` | [embedding.md](marshal-yaml/embedding.md) |
| `agent` | [agent.md](marshal-yaml/agent.md) |
| `confirmation` | [confirmation.md](marshal-yaml/confirmation.md) |
| `sessions` | [sessions.md](marshal-yaml/sessions.md) |
| `tasks` | [tasks.md](marshal-yaml/tasks.md) |
| `maintenance` | [maintenance.md](marshal-yaml/maintenance.md) |
| `notifications` | [notifications.md](marshal-yaml/notifications.md) |
| `artifacts` | [artifacts.md](marshal-yaml/artifacts.md) |
| `displays` | [displays.md](marshal-yaml/displays.md) |
| `webhooks` | [webhooks.md](marshal-yaml/webhooks.md) |
| `recording` | [recording.md](marshal-yaml/recording.md) |
| `ha`, `printers`, `printerSnapshots`, `freshrss`, `notes`, `oauth`, `proxmox`, `media`, `music`, `github`, `executors` | [integrations.md](marshal-yaml/integrations.md) |

## Related

- [Environment variables](environment-variables.md)
- [Secrets reference](secrets-reference.md)
- [Volumes and layout](../installation/volumes-and-layout.md)
