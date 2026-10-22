---
icon: fa-solid fa-dollar-sign
description: "Jede Umgebungsvariable, die Marshal und der Executor-Host lesen."
---

# Umgebungsvariablen

Jede Umgebungsvariable, die Marshal und der Executor-Host lesen. Marshal liest keine anderen eigenen Variablen.

## Marshal

| Variable | Standard | Beschreibung |
|---|---|---|
| `MARSHAL__<Section>__<Key>` | keiner | Überschreibt einen Schlüssel aus `marshal.yaml`. `__` trennt Pfadsegmente und ersetzt `.` im Schlüsselpfad. Namen sind unabhängig von Groß-/Kleinschreibung. Schichten, von niedrig nach hoch: eingebaute Standardwerte, `/config/marshal.yaml`, Umgebung. |
| `MARSHAL__Paths__Config` | `/config` | Verzeichnis mit `marshal.yaml`, `risk.yaml`, `satellites.yaml`, `mcp/`, `skills/`, `templates/`. Wird gelesen, bevor die Konfiguration existiert, setz es also als Umgebungsvariable. |
| `MARSHAL__Paths__Data` | `/data` | Datenbank, Data-Protection-Schlüssel (`/data/keys`), Sicherungen, Artefakte, Notiz-Repository, Snapshots. |
| `MARSHAL__Paths__Secrets` | `/run/secrets` | Verzeichnis der Secret-Dateien (Docker-Secrets). |
| `MARSHAL_SECRET_<NAME>` | keiner | Liefert das Secret `<name>`; siehe unten. Gelangt nie in die Konfiguration. |

Das Container-Image setzt die drei Variablen `MARSHAL__Paths__*` auf die Standardwerte oben.

### Beispiele

```sh
MARSHAL__Http__PathBase=/marshal
MARSHAL__Models__Fast__Model=qwen3:8b
MARSHAL__Auth__Providers__0__Name=local
MARSHAL__Embedding__Provider=ollama
```

Listeneinträge nutzen ihren Index (`__0__`).

Ein ungültiger Wert, der aus einer Variable kam, wird mit dem Variablennamen statt `marshal.yaml` gemeldet, zum Beispiel `MARSHAL__Embedding__Provider`.

### Secret-Variablen

`MARSHAL_SECRET_<NAME>` hat nach `MARSHAL` einen Unterstrich, passt also nie auf das Konfigurationspräfix `MARSHAL__`. `<NAME>` ist der Secret-Name in Großbuchstaben: Das Secret `moonraker_voron` ist `MARSHAL_SECRET_MOONRAKER_VORON`. Der Wert wird unverändert genutzt, auch mit einem Zeilenumbruch am Ende. Siehe [Secrets-Referenz](secrets-reference.md).

```sh
MARSHAL_SECRET_ANTHROPIC_KEY=<api-key>
```

### ASP.NET-Variablen

Marshal leert die Standard-Konfigurationsquellen von ASP.NET und baut eigene, Variablen `ASPNETCORE_*` und `DOTNET_*` setzen also keine Optionen von Marshal.

| Variable | Wirkung |
|---|---|
| `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS` | Keine Wirkung. Kestrel lauscht auf `http.urls` (standardmäßig `http://0.0.0.0:8080`). Nutze `MARSHAL__Http__Urls`. |
| `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | Werden vom .NET-Host gelesen. Marshal ändert sein Verhalten nicht nach dem Namen der Umgebung. |
| `TZ` | Zeitzone des Prozesses. Wird an stdio-MCP-Server weitergegeben. Die Zeitzonen der Eigentümer kommen aus `instance.timeZone` und den Benutzereinstellungen, nicht aus `TZ`. |

### An Kindprozesse übergebene Variablen

| Variable | Beschreibung |
|---|---|
| `PATH`, `HOME`, `LANG`, `LC_ALL`, `TZ`, `TMPDIR` | Die einzigen Variablen von Marshal, die ein stdio-[MCP-Server](mcp-servers.md) erbt. Seine `envSecrets` kommen dazu. |
| `GIT_TERMINAL_PROMPT=0`, `LC_ALL=C`, `GIT_SSH_COMMAND`, `GIT_ASKPASS`, `MARSHAL_GIT_TOKEN` | Von Marshal für die `git`-Prozesse gesetzt, die das Notiz-Repository spiegeln. `MARSHAL_GIT_TOKEN` trägt das Token aus `notes.credentialSecret` für die Dauer eines Aufrufs. Setz sie nicht selbst. |

## Executor-Host

`Marshal.ExecutorHost` ist ein getrennter Dienst. Er nutzt die Standard-Konfiguration von ASP.NET Core, Umgebungsvariablen ohne Präfix setzen also seinen Abschnitt `executorHost`.

| Variable | Standard | Beschreibung |
|---|---|---|
| `executorHost__MarshalUrl` | Pflicht | Die absolute Basis-URL von Marshal, wie der Host sie sieht. Callbacks gehen nur darunter. |
| `executorHost__TokenFile` | `/run/secrets/executor_host_token` | Datei mit dem Token, mit dem Marshal sich authentifiziert. Wird bei jeder Anfrage gelesen, du kannst es also ohne Neustart rotieren. |
| `executorHost__RepositoriesRoot` | `/repos` | Verzeichnis, dessen Unterverzeichnisse die Repositories sind, die Sitzungen nutzen dürfen. |
| `executorHost__WorktreesRoot` | `/worktrees` | Verzeichnis für die git-Worktrees pro Sitzung. |
| `executorHost__Image` | `marshal-claude-sandbox` | Sandbox-Image mit Claude Code. |
| `executorHost__Network` | `marshal-sandbox` | Docker-Netz der Sandbox. |
| `executorHost__EnvFile` | `/run/secrets/sandbox.env` | Umgebungsdatei, die an die Sandbox geht (zum Beispiel `ANTHROPIC_API_KEY`). Wird an Docker übergeben, nie vom Host gelesen. |
| `executorHost__MaxTurns` | `50` | Turn-Grenze einer Sitzung. |
| `executorHost__MaxBudgetUsd` | `5` | Kostengrenze einer Sitzung in USD. |
| `DOCKER_HOST` | Docker-Standard | Adresse des rootless oder Sysbox-Docker-Daemons, den die `docker`-CLI des Hosts nutzt. Marshal selbst fasst nie einen Docker-Socket an. |
| `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` | Image-Standard `8080` | Adresse, auf der der Host lauscht. |

`/repos` und `/worktrees` müssen Bind Mounts mit identischen Pfaden auf dem Docker-Host und im Container des Hosts sein.

## Verwandte Seiten

::: cards
- [Secrets-Referenz](secrets-reference.md)
- [Überblick zur Konfiguration](index.md)
- [Installation des Executor-Hosts](../installation/executor-host.md)
- [Installation mit Docker Compose](../installation/docker-compose.md)
:::
