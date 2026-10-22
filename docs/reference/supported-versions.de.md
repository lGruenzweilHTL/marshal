---
icon: fa-solid fa-code-compare
description: "Die Versionen der Laufzeit, der Build-Werkzeuge und der externen Systeme, gegen die Marshals Code und Tests geschrieben sind."
---

# Unterstützte Versionen

Die Versionen der Laufzeit, der Build-Werkzeuge und der externen Systeme, gegen die Marshals Code und Tests geschrieben sind.

Marshal hat keine veröffentlichte Versionsmatrix. Wo eine Version steht, kommt sie aus dem Repository: aus dem SDK-Pin, den Paketdateien, dem Manifest der Home-Assistant-Komponente oder den Testcontainern. Wo keine Version steht, nutzt Marshal eine stabile öffentliche API des Systems und nennt kein Minimum. Marshal ist nicht gegen andere Versionen getestet; sie können trotzdem funktionieren.

## Marshal

| Element | Version | Quelle |
|---|---|---|
| Öffentliche API | `1.0` (URL-Segment `/api/v1`) | `ApiVersions` im Code; `GET /api/versions` listet sie. |
| Container-Laufzeit | `mcr.microsoft.com/dotnet/aspnet:10.0` mit `git` und `openssh-client` | `Dockerfile` |
| .NET SDK | `10.0.101`, mit Roll-forward auf das neueste Feature-Band | `global.json` |
| Ziel-Framework | `net10.0` | `Directory.Build.props` |
| Datenbank | SQLite über `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12, mit der Erweiterung `sqlite-vec` 0.1.7-alpha.2.1 für die Vektorsuche | `Directory.Packages.props` |
| Web-Build | Node 22 (`node:22-alpine` im Image-Build); Vue 3.5, Vite 8, TypeScript 5.9, Vue Router 5 | `Dockerfile`, `web/package.json` |

Der Image-Build braucht Docker mit Compose und baut das Image aus dem Quellcode; es gibt kein veröffentlichtes Image. Siehe [Voraussetzungen](../getting-started/requirements.md).

## Home Assistant

| Element | Version | Quelle |
|---|---|---|
| Home Assistant | 2026.9.0 oder neuer | `homeassistant` in `ha/hacs.json`. |
| Getestet gegen | 2026.9.4 | Gepinnt über `pytest-homeassistant-custom-component` 0.13.367 in `ha/pyproject.toml` und `ha/uv.lock`. |
| Python für die Tests der Komponente | 3.14.2 oder neuer | `requires-python` in `ha/pyproject.toml`. |
| Custom-Komponente von Marshal | 0.1.0 | `ha/custom_components/marshal/manifest.json` |

Die Komponente hat keine eigenen Python-Abhängigkeiten. Sie braucht die Integrationen `conversation` und `ai_task` und nutzt `assist_pipeline`, wenn vorhanden.

Marshal spricht mit der REST- und der Websocket-API von Home Assistant und mit seinem MCP-Server (Server-ID `ha`). Musik nutzt Player von Music Assistant, die Home Assistant als `media_player`-Entitäten bereitstellt.

## Sprachmodelle

| Anbieter | Bibliothek | Version |
|---|---|---|
| Ollama | OllamaSharp | 5.4.30 |
| Anthropic API | C#-SDK `Anthropic` | 12.50.0 |
| OpenAI-kompatible Server (llama.cpp `llama-server`, vLLM, LM Studio) | `Microsoft.Extensions.AI.OpenAI` | 10.10.1 |
| Abstraktionen | `Microsoft.Extensions.AI` | 10.10.0 |

Ein Embedding-Modell ist Pflicht. Anthropic bietet keines an; siehe [Modelle einrichten](../installation/models-setup.md).

## Integrationen

| System | Schnittstelle, die Marshal nutzt | Angaben zur Version |
|---|---|---|
| Moonraker (Klipper) | REST und der Websocket unter `/websocket` (JSON-RPC 2.0) | Keine angegeben. |
| FreshRSS | Google-Reader-kompatible API unter `/api/greader.php` | Tests laufen gegen `freshrss/freshrss:1.27.1`. |
| Mail | IMAP über MailKit 4.18.1, mit XOAUTH2 für OAuth-Anbieter | Tests laufen gegen Dovecot 2.4.1 und GreenMail 2.1.3. Marshal nutzt nie SMTP. |
| Notizen | Ein Git-Repository mit Markdown-Dateien, über https, ssh oder einen lokalen Pfad | Braucht `git`, das im Image enthalten ist. |
| Seerr | REST-API `/api/v1` mit einem API-Schlüssel | Keine angegeben. |
| Sonarr, Radarr | REST-API `/api/v3` (nur lesend) | Keine angegeben. |
| Jellyfin | Nur Links | Keine angegeben. |
| Proxmox VE | REST-API `/api2/json` mit einem API-Token (Rolle `PVEAuditor`) | Keine angegeben. |
| GitHub | REST-API mit dem Versions-Header `X-GitHub-Api-Version: 2026-03-10` | Im Code gepinnt. |
| Uptime Kuma | Webhook-Benachrichtigung (JSON oder Formularfeld `data`) | Keine angegeben. |
| MCP-Server | Model Context Protocol in den Revisionen 2026-07-28, 2025-11-25 und 2025-06-18, stdio und Streamable HTTP, über `ModelContextProtocol.Core` 2.2.0 | |
| OpenTelemetry | OTLP über gRPC | |

## Verwandte Seiten

::: cards
- [Voraussetzungen](../getting-started/requirements.md)
- [Aktualisieren](../installation/upgrading.md)
- [Die Home-Assistant-Komponente installieren](../home-assistant-component/installation.md)
- [API-Versionierung](../development/api-versioning.md)
:::
