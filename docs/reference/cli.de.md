---
icon: fa-solid fa-terminal
description: "Jeder Befehl und jedes Flag des Programms marshal."
---

# Kommandozeile

Jeder Befehl und jedes Flag des Programms `marshal`. Der Container liefert `/app/marshal` mit und verlinkt es als `marshal` im `PATH`, du kannst es also im laufenden Container ausführen.

```sh
docker compose exec marshal marshal tokens list
```

Ohne Argumente startet das Programm den Webserver. Das erste Argument wählt einen Modus.

| Aufruf | Modus |
|---|---|
| `marshal` | Startet den Webserver. |
| `marshal tokens <command>` | Verwaltet API-Tokens ohne Web-Anmeldung. |
| `marshal demo <command>` | Entfernt die Demo-Daten oder legt sie an. |
| `marshal --export-openapi <file>` | Schreibt das OpenAPI-Dokument und beendet sich. |

Alle anderen Argumente gehen als Konfigurationsargumente an den ASP.NET-Core-Host. Marshal definiert keine weiteren Flags.

## `marshal tokens`

Verwaltet API-Tokens für Maschinen-Clients. Die Befehle lesen `/config/marshal.yaml` und die Umgebung wie der Server, öffnen die Datenbank in `/data` und legen das Datenbankschema an, falls es noch nicht existiert. Tokens gehören dem Eigentümer der Instanz. Jedes Erstellen und Widerrufen wird mit der Akteursart `system` und dem Akteur `cli` ins Audit-Log geschrieben.

> [!NOTE]
> Führe diese Befehle gegen dasselbe `/config` und `/data` aus wie den Server. Im Container tun sie das standardmäßig.

### `marshal tokens create`

Erstellt ein Token und gibt seinen Klartext einmal aus.

```sh
marshal tokens create --name <name> --scope <scope> [--scope <scope>...] [--expires-in-days <days>]
```

| Flag | Pflicht | Beschreibung |
|---|---|---|
| `--name <name>` | ja | Eine Bezeichnung für das Token. |
| `--scope <scope>` | ja, wiederholbar | Ein zu vergebender Scope. Mindestens einer ist Pflicht. |
| `--expires-in-days <days>` | nein | Tage, bis das Token abläuft. Eine positive ganze Zahl. Ohne läuft das Token nicht ab. |

Bekannte Scopes:

| Scope | Erlaubt |
|---|---|
| `conversation:write` | Anfragen an die Unterhaltung schicken (die Home-Assistant-Komponente). |
| `tasks:read` | Aufgaben lesen. |
| `tasks:write` | Aufgaben anlegen und ändern. |
| `admin` | Administrative Endpunkte. |
| `hooks:uptime-kuma` | Webhooks von Uptime Kuma posten. |
| `hooks:proxmox` | Webhooks von Proxmox VE posten. |
| `hooks:seerr` | Webhooks von Seerr posten. |

Ein unbekannter Scope scheitert mit `token.invalid_scope` und der Liste der bekannten Scopes.

Ausgabe bei Erfolg:

```text
Created token '<name>' (<token-id>) with scopes: <scopes>
<plaintext token>
Store it now; it is not shown again.
```

Marshal speichert nur einen Hash des Tokens. Nutze den Klartext in einem Header `Authorization: Bearer <token>`. Siehe [API-Authentifizierung](../api/authentication.md).

### `marshal tokens list`

```sh
marshal tokens list
```

Gibt eine Zeile pro Token aus: ID, Name, Scopes, Erstellungszeit und einen Zustand, der `active`, `expires <time>` oder `revoked` ist.

### `marshal tokens revoke`

```sh
marshal tokens revoke <token-id>
```

Widerruft ein Token über seine ID (aus `marshal tokens list`). Gibt `Revoked token <token-id>.` aus.

### Exit-Codes

| Code | Bedeutung |
|---|---|
| `0` | Erfolg. |
| `1` | Die Operation ist gescheitert. Die Fehlermeldung geht auf die Standardfehlerausgabe. |
| `2` | Die Argumente waren falsch. Der Hilfetext geht auf die Standardfehlerausgabe. |

## `marshal demo`

Entfernt die Beispieldaten des [Demo-Modus](../guides/demo-mode.md) für den Eigentümer der Instanz oder legt sie an. Die Befehle lesen `/config` und die Umgebung wie der Server, bauen die Dienste des Servers, ohne ihn zu starten, und migrieren die Datenbank, falls nötig. Jeder Befehl wird mit der Akteursart `system` und dem Akteur `demo` ins Audit-Log geschrieben.

### `marshal demo reset`

```sh
marshal demo reset
```

Löscht genau die Zeilen, die der Seeder aufgezeichnet hat, mit ihren Schritten und Gedächtniskanten. Zeilen, die du oder der Agent während der Demo angelegt haben, bleiben. Eine angelegte Unterhaltung, die du fortgesetzt hast, bleibt ebenfalls, weil deine Turns auf sie verweisen. Funktioniert mit ein- oder ausgeschaltetem Demo-Modus. Gibt `Removed <count> seeded demo rows.` aus.

Ein Neustart legt nach einem Reset nicht erneut an. Nutze dafür `marshal demo seed`.

### `marshal demo seed`

```sh
marshal demo seed
```

Legt die Beispieldaten erneut an, mit Zeiten relativ zu jetzt. Braucht `demo.enabled: true` und keine angelegten Daten (führe zuerst `marshal demo reset` aus). Das Anlegen des Gedächtnisses braucht das Embedding-Modell; ist es nicht erreichbar, wird das Gedächtnis übersprungen und der Rest angelegt. Gibt `Seeded <count> demo rows.` aus.

Die Exit-Codes sind dieselben wie bei `marshal tokens`.

## `marshal --export-openapi`

```sh
marshal --export-openapi <file>
```

Schreibt das OpenAPI-Dokument v1 nach `<file>`, ohne den Server zu starten, und legt fehlende Elternverzeichnisse an. Die Liste `servers` fehlt, weil die Weboberfläche die API auf ihrem eigenen Ursprung aufruft. Der Web-Build nutzt das, um seine API-Typen zu erzeugen (`npm run gen:api`). Ohne Dateiargument gibt es `Usage: marshal --export-openapi <file>` aus und endet mit `2`. Siehe [OpenAPI](../api/openapi.md).

## Umgebung

Die CLI beachtet dieselben Umgebungsvariablen wie der Server, zum Beispiel `MARSHAL__Paths__Config`, `MARSHAL__Paths__Data` und `MARSHAL__Paths__Secrets`. Siehe [Umgebungsvariablen](../configuration/environment-variables.md).

## Verwandte Seiten

::: cards
- [Machine-Tokens](../guides/authentication/machine-tokens.md)
- [Demo-Modus](../guides/demo-mode.md)
- [API-Authentifizierung](../api/authentication.md)
- [Datenverzeichnis](data-directory.md)
:::
