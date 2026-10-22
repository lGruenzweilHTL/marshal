---
icon: fa-brands fa-docker
order: 10
description: "Build the Marshal image and run it with Docker Compose."
---

# Install with Docker Compose

Build the Marshal image and run it with Docker Compose. The repository ships an example in `deploy/docker-compose.yml`.

## Before you start

- The [requirements](../getting-started/requirements.md).
- A clone of the repository.

## Steps

::: steps
1. **Prepare the configuration**

   ```sh
   cd deploy
   cp -r config.example config
   ```

   Edit `config/marshal.yaml`. The [configuration reference](../configuration/index.md) lists every key.

2. **Provide secrets**

   A secret is referenced by name in `marshal.yaml` (for example `tokenSecret: ha_token`). Marshal reads the value from, in order:

   1. the environment variable `MARSHAL_SECRET_<NAME>` (name upper-cased), or the file `/run/secrets/<name>`;
   2. encrypted rows in the database, set in the web interface.

   For Docker secrets, uncomment the `secrets` entries in the compose file:

   ```yaml
   services:
     marshal:
       secrets:
         - anthropic_key
   secrets:
     anthropic_key:
       file: ./secrets/anthropic_key
   ```

   See [secrets](../guides/secrets.md).

3. **Build and start**

   ```sh
   docker compose up -d --build
   ```

   The example file starts two services:

   | Service | Image | Purpose |
   |---|---|---|
   | `marshal` | built from the repository `Dockerfile` | Port 8080, `./config` mounted read-only at `/config`, named volume `marshal-data` at `/data` |
   | `ollama` | `ollama/ollama:latest` | Models in the volume `ollama-models`, with an NVIDIA GPU reservation |

   Remove the `deploy` block of `ollama` if the host has no NVIDIA GPU, or remove the service if you use other model servers.

4. **Pull models**

   ```sh
   docker compose exec ollama ollama pull <model>
   ```

5. **Put HTTPS in front**

   Browser sessions need HTTPS. See [reverse proxy](reverse-proxy.md).
:::

## Check that it works

```sh
docker compose logs marshal
```

At startup Marshal checks the configuration, then opens and migrates the database, then resolves secrets, then starts everything else. A failure prints one line per problem with file and key, and exits with code `1`:

```text
marshal: startup failed (2 errors)
  marshal.yaml  embedding.provider  must be ollama or openai-compatible.
  marshal.yaml  embedding.model  is required.
```

Set `logging.logLevel.default: Debug` to add stack traces. `GET /health` reports database, config, secrets, MCP servers, memory and Home Assistant.

## The container

| Item | Value |
|---|---|
| Base | `mcr.microsoft.com/dotnet/aspnet:10.0` plus `git` and `openssh-client` (notes remotes) |
| User | `app` (not root) |
| Port | 8080 |
| Defaults | `MARSHAL__Paths__Config=/config`, `MARSHAL__Paths__Data=/data`, `MARSHAL__Paths__Secrets=/run/secrets` |
| CLI | `docker compose exec marshal marshal tokens ...` |

> [!WARNING]
> Marshal never needs a Docker socket. Do not mount one.

If you bind-mount `/data` instead of using a named volume, make it writable by the container user `app`.

## Related

::: cards
- [Volumes and layout](volumes-and-layout.md)
- [Environment variables](../configuration/environment-variables.md)
- [Upgrading](upgrading.md)
:::
