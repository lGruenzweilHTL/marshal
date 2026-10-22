---
title: Konfiguration
icon: fa-solid fa-sliders
order: 50
description: "Marshal liest von Menschen bearbeitete Dateien aus /config und Umgebungsvariablen."
---

# Konfiguration

Marshal liest von Menschen bearbeitete Dateien aus `/config` und Umgebungsvariablen. Diese Seite listet jede Datei, die Ladereihenfolge, was beim Start geprüft wird und welche Änderungen einen Neustart brauchen.

## Dateien in `/config`

| Pfad | Zweck | Referenz |
|---|---|---|
| `marshal.yaml` | Einstellungen der Instanz, Modelle, Integrationen. Wird nur beim Start gelesen. | [Seiten zu marshal.yaml](#abschnitte-von-marshalyaml) |
| `risk.yaml` | Risikoregeln: Stufe pro Werkzeug, Bedingungen an Argumente und Ziele. | [risk-yaml.md](risk-yaml.md) |
| `satellites.yaml` | Profile pro Satellit (Ausführlichkeit, verweigerte Werkzeuge, Display, Lautsprecher). | [satellites-yaml.md](satellites-yaml.md) |
| `mcp/*.yaml` | Eine Datei pro MCP-Server. | [mcp-servers.md](mcp-servers.md) |
| `skills/<name>/SKILL.md` | Skills mit optionalen zusätzlichen Dateien. | [skills-format.md](skills-format.md) |
| `templates/*.md` | Agent-Templates. | [templates-format.md](templates-format.md) |
| `tasks/*.yaml` | Beispiele für Aufgabendefinitionen. Marshal liest diesen Ordner nicht; die Dateien sind die Argumente von `tasks.create`. | [tasks-format.md](tasks-format.md) |

Die Beispielkonfiguration in [`deploy/config.example/`](https://github.com/lGruenzweilHTL/marshal/tree/master/deploy/config.example) hat diesen Aufbau:

```tree
/config
  marshal.yaml
  risk.yaml
  satellites.yaml
  mcp/
    ha.yaml  # MCP-Server von Home Assistant
  skills/
    briefing-format/
      SKILL.md
    house-status/
      SKILL.md
    rss-digest/
      SKILL.md
  templates/
    ai_task.md
    briefing.md
    house-inspector.md
    rss-scorer.md
  tasks/
    briefing.yaml  # das tägliche Briefing
    media-available.yaml
```

Jede Datei ist optional. Eine fehlende `marshal.yaml` bedeutet eingebaute Standardwerte, aber Marshal verweigert dann den Start, weil `embedding` Pflicht ist. Das Konfigurationsverzeichnis ist `/config`; setz `MARSHAL__Paths__Config`, um es zu ändern. Siehe [Umgebungsvariablen](environment-variables.md).

Schlüsselnamen sind camelCase und entsprechen den Eigenschaftsnamen der .NET-Optionen. Unbekannte Schlüssel in `risk.yaml` und `satellites.yaml` sind Fehler. `marshal.yaml` ignoriert unbekannte Schlüssel, ein falsch geschriebener Schlüssel behält also still seinen Standardwert. Prüf die [Konfigurationsansicht](#konfigurationsansicht-in-der-weboberfläche).

## Ladereihenfolge

Die Konfiguration ist geschichtet. Eine spätere Schicht überschreibt eine frühere:

1. Eingebaute Standardwerte (die Standardwerte in den Referenzseiten).
2. `/config/marshal.yaml`.
3. Umgebungsvariablen mit dem genauen Präfix `MARSHAL__`.

Variablen `MARSHAL_SECRET_<NAME>` sind Secrets, keine Konfiguration. Sie gelangen nie in die Konfiguration. Siehe [Secrets-Referenz](secrets-reference.md).

## Überschreiben per Umgebung

Ersetze jeden `.` im Schlüsselpfad durch `__`. Schlüsselnamen sind unabhängig von Groß-/Kleinschreibung.

| YAML | Umgebungsvariable |
|---|---|
| `http.pathBase: /b` | `MARSHAL__Http__PathBase=/b` |
| `models.fast.provider` | `MARSHAL__Models__Fast__Provider` |
| `auth.providers[0].name` | `MARSHAL__Auth__Providers__0__Name` |
| `agent.advisor.dailyCostCap` | `MARSHAL__Agent__Advisor__DailyCostCap` |

Ein Validierungsfehler für einen so gesetzten Wert nennt die Variable statt `marshal.yaml`.

## Validierung beim Start

Der Start prüft in Stufen. Eine Stufe läuft nur, wenn alle früheren Stufen keine Fehler gefunden haben:

1. **Konfiguration.** `marshal.yaml` parsen, die Optionen jedes Abschnitts validieren, jede Datei in `/config` nach wörtlichen Secrets durchsuchen, `risk.yaml` und `satellites.yaml` validieren und direkt in der Konfiguration gesetzte Werte von Secrets ablehnen.
2. **Datenbank.** Initialisieren und migrieren.
3. **Secrets.** Jedes Secret auflösen, das beim Start existieren muss.
4. **Dienste.** MCP-Server, Werkzeug-Registry, HTTP-Listener.

Die Stufen Konfiguration und Secrets sammeln alle Fehler, bevor sie scheitern. Die Ausnahme ist eine `marshal.yaml`, die kein gültiges YAML ist: Marshal meldet sie allein mit Zeile und Spalte. Jeder Fehler ist eine Zeile mit Datei (oder Umgebungsvariable) und Schlüssel. Die Zeile enthält nie den Wert. Ein Stacktrace wird nur ausgegeben, wenn `logging.logLevel.default` auf `Debug` oder `Trace` steht. Jedes Scheitern beim Start endet mit dem Code `1`.

### Ablehnung wörtlicher Secrets

`/config` enthält nur Secret-Namen. Marshal durchsucht das rohe YAML von `marshal.yaml`, `risk.yaml`, `satellites.yaml` und den Dateien der MCP-Server, Skills und Templates, einschließlich unbekannter Schlüssel. Es scheitert, wenn:

| Regel | Beispiel | Meldung |
|---|---|---|
| Ein Schlüssel auf `Secret` endet und etwas anderes als einen Secret-Namen enthält (`^[a-z][a-z0-9_]{0,63}$`). | `apiKeySecret: My-Key` | `<file>: '<key>' must hold a secret name (^[a-z][a-z0-9_]{0,63}$), not a value.` |
| Das letzte Wort eines Schlüssels `password`, `passphrase`, `token`, `credential` oder `credentials` ist oder seine letzten zwei Wörter `api key`, `private key` oder `access key` sind. Wörter werden an Grenzen von camelCase, snake_case und kebab-case getrennt. | `apiKey: abc` | `<file>: '<key>' names a credential. Store the value as a secret and reference it with a '...Secret' key.` |
| Ein Wert wie ein Zugangsdatum aussieht: PEM-Header, JWT (`eyJ...`), `sk-`, `sk-ant-`, `ghp_`, `xox?-`, eine Hex-Zeichenkette mit 32 oder mehr Zeichen, eine Base64- oder Base64url-Zeichenkette mit 32 oder mehr Zeichen, die zufällig aussieht, oder ein anderes langes Token mit hoher Entropie. | jeder Schlüssel | `<file>: the value of '<key>' looks like a credential. Store it as a secret and reference it by name.` |

`maxTokens: 4096` geht durch: Das letzte Wort `tokens` ist nicht `token`. Ein Hex-Wert mit 32 Zeichen scheitert auch in einem `...Secret`-Schlüssel.

Optionseigenschaften, die Werte von Secrets enthalten (zum Beispiel `models.<key>.apiKey`), lassen sich in der Konfiguration überhaupt nicht setzen. Wer eine setzt, scheitert mit `<key> must not be set in configuration. Store the value as a secret and reference it by name.`

### Secrets, die auflösbar sein müssen

Nach der Migration der Datenbank löst Marshal jeden Secret-Verweis auf, der in einen Optionswert einfließt: `models.*.apiKeySecret`, `embedding.apiKeySecret`, `auth.providers[].clientCredentialSecret`, `ha.tokenSecret`, `printers[].apiKeySecret`, `freshrss.apiPasswordSecret`, `notes.credentialSecret` und `oauth.*.clientCredentialSecret`. Ein nicht aufgelöster Verweis, den der Start braucht (API-Schlüssel von Modell und Embedding, das OIDC-Client-Secret, das Home-Assistant-Token), lässt den Start scheitern, und der Fehler listet alle solchen Verweise. Jeder andere nicht aufgelöste Verweis erzeugt nur eine Warnung in `/health`, und seine Integration bleibt nicht verfügbar. Pro Anfrage gelesene Verweise (`proxmox.tokenSecret`, `media.*.apiKeySecret`, `github.tokenSecret`, `executors.claudeCode.tokenSecret`) blockieren den Start nicht. Ihre Werte kannst du ohne Neustart ersetzen. Siehe [Anleitung zu Secrets](../guides/secrets.md).

## Neustart oder Neuladen

| Änderung | Wirkt |
|---|---|
| Alles in `marshal.yaml` | Nach einem Neustart. Das schließt Modelle, Authentifizierung, Integrationen und alle Grenzen ein. |
| Umgebungsvariablen | Nach einem Neustart. |
| `risk.yaml`, `satellites.yaml` | Neuladen ohne Neustart. |
| `mcp/*.yaml`, `skills/**`, `templates/*.md` | Neuladen ohne Neustart. Werkzeuglisten laufender Turns ändern sich nicht mitten im Turn. |
| Werte von Secrets aus `/run/secrets` oder der Datenbank | Neustart für beim Start gebundene Secrets (Liste oben); sofort für pro Anfrage gelesene Secrets. |

`/config` wird beobachtet. Ein Neuladen, das an der Validierung scheitert, behält die vorherige Konfiguration und schreibt den Fehler ins Log. Eine kaputte Datei in `mcp/`, `skills/` oder `templates/` wird übersprungen; die anderen Einträge laden trotzdem. Ein wörtliches Secret in irgendeiner Datei lässt den Start scheitern.

`satellites.yaml` wird gegen `displays.surfaces` und `music.speakers` aus `marshal.yaml` validiert, ein Display zu entfernen braucht also einen Neustart und dann eine passende `satellites.yaml`.

## Konfigurationsansicht in der Weboberfläche

Admins sehen **System > Konfiguration** (`/system/config`, API `GET /api/v1/config`, Scope `admin`). Sie ist schreibgeschützt und zeigt:

- geladene Skills, Templates, MCP-Server (mit Status und Anzahl der Werkzeuge), Risikoregeln, Modelle und Satellitenprofile;
- nur Secret-Namen, nie Werte;
- den letzten Ladefehler pro Datei und ob die vorherige Version aktiv bleibt (`risk.yaml`, `satellites.yaml`) oder der Eintrag übersprungen wurde.

Benutzer ohne die Rolle `admin` bekommen 403 und sehen keinen Link. Die Ansicht zeigt die Abschnitte von `marshal.yaml` außer den Modellen nicht, nutze sie also zusammen mit den Seiten unten.

## Abschnitte von marshal.yaml

| Abschnitt | Seite |
|---|---|
| `instance`, `paths`, `logging`, `telemetry` | [instance.md](marshal-yaml/instance.md) |
| `owner` | [owner.md](marshal-yaml/owner.md) |
| `http` | [http.md](marshal-yaml/http.md) |
| `auth` | [auth.md](marshal-yaml/auth.md) |
| `models` | [models.md](marshal-yaml/models.md) |
| `embedding`, `memory` | [embedding.md](marshal-yaml/embedding.md) |
| `agent` | [agent.md](marshal-yaml/agent.md) |
| `tools`, `voice` | [tools.md](marshal-yaml/tools.md) |
| `confirmation` | [confirmation.md](marshal-yaml/confirmation.md) |
| `sessions` | [sessions.md](marshal-yaml/sessions.md) |
| `tasks` | [tasks.md](marshal-yaml/tasks.md) |
| `aiTasks` | [ai-tasks.md](marshal-yaml/ai-tasks.md) |
| `maintenance` | [maintenance.md](marshal-yaml/maintenance.md) |
| `notifications` | [notifications.md](marshal-yaml/notifications.md) |
| `suggestions` | [suggestions.md](marshal-yaml/suggestions.md) |
| `artifacts` | [artifacts.md](marshal-yaml/artifacts.md) |
| `displays` | [displays.md](marshal-yaml/displays.md) |
| `webhooks` | [webhooks.md](marshal-yaml/webhooks.md) |
| `recording` | [recording.md](marshal-yaml/recording.md) |
| `demo` | [demo.md](marshal-yaml/demo.md) |
| `ha`, `printers`, `printerSnapshots`, `freshrss`, `notes`, `oauth`, `proxmox`, `media`, `music`, `github`, `executors` | [integrations.md](marshal-yaml/integrations.md) |

## Verwandte Seiten

::: cards
- [Umgebungsvariablen](environment-variables.md)
- [Secrets-Referenz](secrets-reference.md)
- [Volumes und Aufbau](../installation/volumes-and-layout.md)
:::
