---
icon: fa-solid fa-terminal
order: 60
description: "Betreibe den separaten Executor-Host, damit Marshal Coding-Sitzungen mit Claude Code in Sandbox-Containern starten kann."
---

# Executor-Host

Betreibe den separaten Executor-Host, damit Marshal Coding-Sitzungen mit Claude Code in Sandbox-Containern starten kann.

## Bevor du anfängst

- Eine eigene Maschine, VM oder einen rootless Docker-Daemon (oder Sysbox). Marshal bekommt nie einen Docker-Socket und liest die Repositorys nie selbst.
- Git-Repositorys, die auf diesem Host ausgecheckt sind.
- Einen Anthropic-API-Schlüssel für die Sandbox.

## Wie es funktioniert

Marshal ruft den Host über HTTP mit einem Bearer-Token auf. Für jede Sitzung legt der Host einen Git-Worktree und einen Branch `marshal/<id>` an und führt `claude -p` in einem Wegwerf-Container in einem Docker-Netz aus. Der Host meldet sich mit einem Callback-Token pro Sitzung bei Marshal zurück. Eine fertige Sitzung wird zu einem Code-Vorschlag, den du in der Weboberfläche prüfst. Das Mergen passiert auf dem Host, ohne Push. Ablehnen löscht den Branch. Dateien, die im Worktree nach `.marshal/artifacts/` geschrieben werden, werden als Artefakte hochgeladen.

## Schritte

::: steps
1. **Baue die Images**

   ```sh
   docker build -f src/Marshal.ExecutorHost/Dockerfile -t marshal-executor-host .
   docker build -t marshal-claude-sandbox src/Marshal.ExecutorHost/sandbox
   ```

2. **Leg Dateien auf dem Host an**

   - `executor_host_token`: eine lange Zufallszeichenkette. Derselbe Wert wird in Marshal ein Secret.
   - `sandbox.env`: die Umgebung der Sandbox, zum Beispiel eine Zeile `ANTHROPIC_API_KEY=<key>`. Docker reicht sie an die Sandboxes weiter; der Host liest sie nie.
   - Ein Docker-Netz `marshal-sandbox`, dessen ausgehender Verkehr auf das beschränkt ist, was du erlaubst (zum Beispiel über einen Allowlist-Proxy). Das Repository liefert den Proxy nicht mit.

3. **Starte den Host**

   ```yaml
   services:
     executor-host:
       image: marshal-executor-host
       restart: unless-stopped
       ports:
         - "8080:8080"
       environment:
         ExecutorHost__MarshalUrl: http://marshal.lan:8080/
         DOCKER_HOST: <rootless docker socket>
       volumes:
         - /srv/repos:/srv/repos
         - /srv/worktrees:/srv/worktrees
         - ./secrets:/run/secrets:ro
   ```

   Die Pfade von `/repos` und `/worktrees` müssen auf dem Docker-Host und im Container gleich sein, weil Sandboxes sie über den Pfad einhängen. Setz die Optionen auf diese Pfade:

   | Option (`ExecutorHost__...`) | Standard | Beschreibung |
   |---|---|---|
   | `MarshalUrl` | keiner, Pflicht | Absolute URL von Marshal aus Sicht des Hosts |
   | `TokenFile` | `/run/secrets/executor_host_token` | Datei mit dem Token von Marshal; bei jeder Anfrage neu gelesen |
   | `RepositoriesRoot` | `/repos` | Unterverzeichnisse sind die Repositorys |
   | `WorktreesRoot` | `/worktrees` | Worktrees pro Sitzung |
   | `Image` | `marshal-claude-sandbox` | Sandbox-Image |
   | `Network` | `marshal-sandbox` | Sandbox-Netz |
   | `EnvFile` | `/run/secrets/sandbox.env` | Umgebungsdatei der Sandbox |
   | `MaxTurns` | `50` | Turn-Limit von Claude Code |
   | `MaxBudgetUsd` | `5` | Budget pro Sitzung |

   Der Host beantwortet `GET /health` ohne Authentifizierung.

4. **Konfiguriere Marshal**

   Speichere das Token als Secret `executor_host_token` und ergänze dann `marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   executors:
     callbackBaseUrl: http://marshal:8080/
     claudeCode:
       url: http://executor-host:8080/
       tokenSecret: executor_host_token
       repositories:
         - name: marshal
           description: this project
   ```

   `name` ist ein Verzeichnis unter `RepositoriesRoot`. `callbackBaseUrl` ist Marshal aus Sicht des Hosts. Das Werkzeug `tasks.run_executor` bleibt verborgen, bis `executors.claudeCode.url` gesetzt ist. Starte Marshal neu.
:::

## Prüfen, ob es funktioniert

```sh
curl http://executor-host:8080/health
```

liefert `{"status":"ok"}`. Bitte Marshal um eine kleine Programmieraufgabe und prüfe den Vorschlag, der dabei entsteht.

## Verwandte Seiten

::: cards
- [Konzepte: Sicherheitsmodell](../concepts/security-model.md)
- [Freigaben](../guides/tasks/approvals.md)
:::
