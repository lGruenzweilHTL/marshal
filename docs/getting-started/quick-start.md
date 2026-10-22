---
icon: fa-solid fa-play
order: 30
description: "Build Marshal, start it with Ollama and HTTPS, and sign in."
---

# Quick start

Build Marshal, start it with Ollama and HTTPS, and sign in. You end with a working web interface.

## Before you start

- Docker with the Compose plugin, git, and the [requirements](requirements.md).
- A hostname for Marshal, called `<host>` below (for example `marshal.lan`), resolvable from your browser.

## Steps

::: steps
1. **Get the code and the configuration**

   ```sh
   git clone https://github.com/lGruenzweilHTL/marshal.git
   cd marshal/deploy
   cp -r config.example config
   ```

2. **Edit `config/marshal.yaml`**

   Set your time zone and name, and use one small model for both tiers to keep the download short:

   ```yaml
   instance:
     timeZone: Europe/Vienna
   owner:
     displayName: "Your name"
     language: en
   auth:
     providers:
       - name: local
         type: local
   models:
     fast:
       tier: fast
       provider: ollama
       model: "qwen3:8b"
       endpoint: "http://ollama:11434"
       contextLength: 32768
     standard:
       tier: standard
       provider: ollama
       model: "qwen3:8b"
       endpoint: "http://ollama:11434"
       contextLength: 32768
   embedding:
     provider: ollama
     model: "bge-m3"
     endpoint: "http://ollama:11434"
   ```

   Keep the other sections of the example file as they are. Marshal refuses to start when a file in `/config` contains a literal secret.

3. **Add HTTPS**

   Create `deploy/Caddyfile`:

   ```text
   <host> {
   	tls internal
   	reverse_proxy marshal:8080 {
   		flush_interval -1
   	}
   }
   ```

   Create `deploy/compose.caddy.yml`:

   ```yaml [deploy/compose.caddy.yml]
   services:
     caddy:
       image: caddy:2
       restart: unless-stopped
       ports:
         - "443:443"
       volumes:
         - ./Caddyfile:/etc/caddy/Caddyfile:ro
         - caddy-data:/data
       depends_on:
         - marshal
   volumes:
     caddy-data:
   ```

   `tls internal` uses Caddy's own certificate authority. Your browser warns about it until you trust that root, or you move to a real certificate ([TLS and domains](../installation/tls-and-domains.md)).

4. **Build and start**

   ```sh
   docker compose -f docker-compose.yml -f compose.caddy.yml up -d --build
   ```

5. **Pull the models**

   ```sh
   docker compose exec ollama ollama pull qwen3:8b
   docker compose exec ollama ollama pull bge-m3
   ```

6. **Create the first account**

   Open `https://<host>/setup`. Enter a display name, user name and a password of at least 12 characters. This account becomes the admin and claims the instance owner. The setup page is unavailable once the owner is claimed.
:::

## Check that it works

```sh
docker compose logs marshal
curl -k https://<host>/health
```

`/health` returns `{"status":"healthy",...}` or `degraded` (both HTTP 200). Open the **Chat** view in the web interface and send a message.

## Troubleshooting

- The container exits: `docker compose logs marshal` lists every configuration problem with file and key. See [troubleshooting](../guides/troubleshooting.md).
- Setup shows that Marshal must be opened over HTTPS: you opened `http://...:8080`. Use the HTTPS address.

## Related

::: cards
- [Connect Home Assistant](connect-home-assistant.md)
- [Docker Compose installation](../installation/docker-compose.md)
:::
