# Overview

Marshal is a self-hosted AI agent that Home Assistant talks to. This page explains what you run, what it connects to and what the tutorial in this section builds.

## What you run

Marshal is one container. It contains the ASP.NET host (REST API and server-sent events), the web interface and a SQLite database. Three things sit around it:

| Part | Role | Required |
|---|---|---|
| Marshal container | The agent, the API and the web interface | Yes |
| Language model server | Ollama, an OpenAI-compatible server, or the Anthropic API | Yes |
| Home Assistant with the Marshal integration | Forwards Assist voice and text turns to Marshal | For voice |
| Executor host | Separate service for coding sessions | No |

Voice through Home Assistant Assist is the main interface. The web interface and the public API are secondary interfaces.

## What you configure

Two volumes hold everything:

- `/config` holds human-edited files such as `marshal.yaml`. It contains secret names, never secret values.
- `/data` holds the database, encryption keys, backups, artifacts and the notes repository.

See [volumes and layout](../installation/volumes-and-layout.md).

## What the tutorial builds

The [quick start](quick-start.md) builds the image, starts Marshal with Ollama, puts HTTPS in front of it and creates the first account. [Connect Home Assistant](connect-home-assistant.md) then adds the integration and selects Marshal in an Assist pipeline. [Next steps](next-steps.md) lists what to set up afterwards.

## Related
- [Requirements](requirements.md)
- [Architecture](../concepts/architecture.md)
