---
icon: fa-brands fa-docker
order: 10
description: "Baue das Marshal-Image und betreibe es mit Docker Compose."
---

# Mit Docker Compose installieren

Baue das Marshal-Image und betreibe es mit Docker Compose. Das Repository liefert ein Beispiel in `deploy/docker-compose.yml`.

## Bevor du anfängst

- Die [Voraussetzungen](../getting-started/requirements.md).
- Ein Klon des Repositorys.

## Schritte

::: steps
1. **Bereite die Konfiguration vor**

   ```sh
   cd deploy
   cp -r config.example config
   ```

   Bearbeite `config/marshal.yaml`. Die [Konfigurationsreferenz](../configuration/index.md) listet jeden Schlüssel.

2. **Stell die Secrets bereit**

   `marshal.yaml` verweist auf ein Secret über seinen Namen (zum Beispiel `tokenSecret: ha_token`). Marshal liest den Wert in dieser Reihenfolge aus:

   1. der Umgebungsvariable `MARSHAL_SECRET_<NAME>` (Name in Großbuchstaben) oder der Datei `/run/secrets/<name>`;
   2. verschlüsselten Zeilen in der Datenbank, gesetzt in der Weboberfläche.

   Für Docker-Secrets kommentierst du die `secrets`-Einträge in der Compose-Datei ein:

   ```yaml
   services:
     marshal:
       secrets:
         - anthropic_key
   secrets:
     anthropic_key:
       file: ./secrets/anthropic_key
   ```

   Siehe [Secrets](../guides/secrets.md).

3. **Bauen und starten**

   ```sh
   docker compose up -d --build
   ```

   Die Beispieldatei startet zwei Dienste:

   | Dienst | Image | Zweck |
   |---|---|---|
   | `marshal` | gebaut aus dem `Dockerfile` des Repositorys | Port 8080, `./config` schreibgeschützt unter `/config` eingehängt, benanntes Volume `marshal-data` unter `/data` |
   | `ollama` | `ollama/ollama:latest` | Modelle im Volume `ollama-models`, mit einer NVIDIA-GPU-Reservierung |

   Entferne den `deploy`-Block von `ollama`, wenn der Host keine NVIDIA-GPU hat, oder entferne den Dienst, wenn du andere Modellserver nutzt.

4. **Lade die Modelle**

   ```sh
   docker compose exec ollama ollama pull <model>
   ```

5. **Stell HTTPS davor**

   Browser-Sitzungen brauchen HTTPS. Siehe [Reverse Proxy](reverse-proxy.md).
:::

## Prüfen, ob es funktioniert

```sh
docker compose logs marshal
```

Beim Start prüft Marshal die Konfiguration, öffnet und migriert dann die Datenbank, löst dann die Secrets auf und startet dann alles andere. Ein Fehler gibt eine Zeile pro Problem mit Datei und Schlüssel aus und beendet sich mit Code `1`:

```text
marshal: startup failed (2 errors)
  marshal.yaml  embedding.provider  must be ollama or openai-compatible.
  marshal.yaml  embedding.model  is required.
```

Setz `logging.logLevel.default: Debug`, um Stacktraces zu ergänzen. `GET /health` meldet Datenbank, Konfiguration, Secrets, MCP-Server, Gedächtnis und Home Assistant.

## Der Container

| Punkt | Wert |
|---|---|
| Basis | `mcr.microsoft.com/dotnet/aspnet:10.0` plus `git` und `openssh-client` (Notizen-Remotes) |
| Benutzer | `app` (nicht root) |
| Port | 8080 |
| Standardwerte | `MARSHAL__Paths__Config=/config`, `MARSHAL__Paths__Data=/data`, `MARSHAL__Paths__Secrets=/run/secrets` |
| CLI | `docker compose exec marshal marshal tokens ...` |

> [!WARNING]
> Marshal braucht nie einen Docker-Socket. Häng keinen ein.

Wenn du `/data` per Bind-Mount statt als benanntes Volume einhängst, mach es für den Container-Benutzer `app` beschreibbar.

## Verwandte Seiten

::: cards
- [Volumes und Aufbau](volumes-and-layout.md)
- [Umgebungsvariablen](../configuration/environment-variables.md)
- [Aktualisieren](upgrading.md)
:::
