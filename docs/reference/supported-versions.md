# Supported versions

The versions of Marshal's runtime, build tools and external systems that the code and tests are written against.

Marshal has no published release matrix. Where a version is listed, it comes from the repository: the SDK pin, the package files, the Home Assistant component manifest or the test containers. Where no version is listed, Marshal uses a stable public API of that system and does not state a minimum. Marshal is not tested against other versions, which may work.

## Marshal

| Item | Version | Source |
|---|---|---|
| Public API | `1.0` (URL segment `/api/v1`) | `ApiVersions` in code; `GET /api/versions` lists it. |
| Container runtime | `mcr.microsoft.com/dotnet/aspnet:10.0` with `git` and `openssh-client` | `Dockerfile` |
| .NET SDK | `10.0.101`, rolling forward to the latest feature band | `global.json` |
| Target framework | `net10.0` | `Directory.Build.props` |
| Database | SQLite through `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12, with the `sqlite-vec` extension 0.1.7-alpha.2.1 for vector search | `Directory.Packages.props` |
| Web build | Node 22 (`node:22-alpine` in the image build); Vue 3.5, Vite 8, TypeScript 5.9, Vue Router 5 | `Dockerfile`, `web/package.json` |

The image build needs Docker with Compose and builds the image from source; there is no published image. See [requirements](../getting-started/requirements.md).

## Home Assistant

| Item | Version | Source |
|---|---|---|
| Home Assistant | 2026.9.0 or newer | `homeassistant` in `ha/hacs.json`. |
| Tested against | 2026.9.4 | Pinned through `pytest-homeassistant-custom-component` 0.13.367 in `ha/pyproject.toml` and `ha/uv.lock`. |
| Python for the component tests | 3.14.2 or newer | `requires-python` in `ha/pyproject.toml`. |
| Marshal custom component | 0.1.0 | `ha/custom_components/marshal/manifest.json` |

The component has no Python requirements of its own. It needs the `conversation` and `ai_task` integrations and uses `assist_pipeline` when present.

Marshal talks to Home Assistant's REST and websocket APIs and to its MCP server (server id `ha`). Music uses Music Assistant players exposed as Home Assistant `media_player` entities.

## Language models

| Provider | Library | Version |
|---|---|---|
| Ollama | OllamaSharp | 5.4.30 |
| Anthropic API | `Anthropic` C# SDK | 12.50.0 |
| OpenAI-compatible servers (llama.cpp `llama-server`, vLLM, LM Studio) | `Microsoft.Extensions.AI.OpenAI` | 10.10.1 |
| Abstractions | `Microsoft.Extensions.AI` | 10.10.0 |

An embedding model is required. Anthropic offers none; see [models setup](../installation/models-setup.md).

## Integrations

| System | Interface Marshal uses | Version information |
|---|---|---|
| Moonraker (Klipper) | REST and the websocket at `/websocket` (JSON-RPC 2.0) | None stated. |
| FreshRSS | Google Reader-compatible API at `/api/greader.php` | Tests run against `freshrss/freshrss:1.27.1`. |
| Mail | IMAP through MailKit 4.18.1, with XOAUTH2 for OAuth providers | Tests run against Dovecot 2.4.1 and GreenMail 2.1.3. Marshal never uses SMTP. |
| Notes | A git repository of Markdown files, over https, ssh or a local path | Needs `git`, which the image includes. |
| Seerr | REST API `/api/v1` with an API key | None stated. |
| Sonarr, Radarr | REST API `/api/v3` (read-only) | None stated. |
| Jellyfin | Links only | None stated. |
| Proxmox VE | REST API `/api2/json` with an API token (role `PVEAuditor`) | None stated. |
| GitHub | REST API with the version header `X-GitHub-Api-Version: 2026-03-10` | Pinned in code. |
| Uptime Kuma | Webhook notification (JSON or form `data`) | None stated. |
| MCP servers | Model Context Protocol revisions 2026-07-28, 2025-11-25 and 2025-06-18, stdio and streamable HTTP, through `ModelContextProtocol.Core` 2.2.0 | |
| OpenTelemetry | OTLP over gRPC | |

## Related

- [Requirements](../getting-started/requirements.md)
- [Upgrading](../installation/upgrading.md)
- [Home Assistant component installation](../home-assistant-component/installation.md)
- [API versioning](../development/api-versioning.md)
