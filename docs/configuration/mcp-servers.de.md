---
icon: fa-solid fa-plug-circle-bolt
description: "Jede Datei in /config/mcp/ definiert einen Model-Context-Protocol-Server, dessen Werkzeuge Marshal dem Modell anbietet."
---

# MCP-Server (`mcp/*.yaml`)

Jede Datei in `/config/mcp/` definiert einen Model-Context-Protocol-Server, dessen Werkzeuge Marshal dem Modell anbietet. Marshal lädt das Verzeichnis ohne Neustart neu; Werkzeuglisten laufender Turns ändern sich nicht mitten im Turn.

## Beispiel

```yaml
id: ha
transport: http
url: http://homeassistant.local:8123/api/mcp
description: "Home control: lights, switches, covers, climate, locks, media players and the state of devices and areas"
authTokenSecret: ha_token
untrusted: false
untrustedTools: ["todo__get_items"]
pinTools: true
```

Ein lokaler Server über stdio:

```yaml
id: search
transport: stdio
command: /usr/local/bin/search-mcp
args: ["--safe"]
envSecrets:
  - variable: SEARCH_API_KEY
    secret: search_key
untrusted: true
```

## Schlüssel

Nur die Schlüssel unten werden akzeptiert. Der Dateiname ist frei; die `id` muss über alle Dateien eindeutig sein.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `id` | Zeichenkette | Pflicht | Server-ID. Muss auf `[a-z][a-z0-9]*` passen. |
| `transport` | `http` oder `stdio` | Pflicht | `http` ist Streamable HTTP. |
| `url` | absolute `http(s)`-URL | Pflicht bei `http` | Endpunkt des Servers. |
| `command` | Zeichenkette | Pflicht bei `stdio` | Zu startendes Programm. |
| `args` | Liste von Zeichenketten | `[]` | Argumente für `command`. |
| `toolPrefix` | Zeichenkette | `id` | Namensraum für die Werkzeuge des Servers statt der ID. Muss auf `[a-z][a-z0-9]*` passen. |
| `description` | Zeichenkette | gebildet aus `id` und den Werkzeugnamen | Einzeilige englische Beschreibung, worum es bei den Werkzeugen des Servers geht. Die Werkzeugsuche gleicht Turns damit ab; siehe [`tools`](marshal-yaml/tools.md). |
| `authTokenSecret` | Secret-Name | keiner | Secret mit einem Bearer-Token, gesendet als `Authorization: Bearer <token>`. |
| `envSecrets` | Liste von `{ variable, secret }` | `[]` | Umgebungsvariablen eines stdio-Servers, gefüllt aus Secrets. Beide Felder sind Pflicht. |
| `untrusted` | Boolean | `false` | Bei `true` ist die Ausgabe jedes Werkzeugs Daten von Dritten und belastet den Durchlauf (Quelle `mcp:<id>`). |
| `untrustedTools` | Liste von Globs | `[]` | Globs auf die Werkzeugnamen, die der Server meldet. Nur passende Werkzeuge belasten den Durchlauf. Einträge dürfen nicht leer sein. |
| `pinTools` | Boolean | `true` | Pinnt jede Werkzeugdefinition bei der ersten Erkennung. Ein später neues oder geändertes Werkzeug bleibt verborgen, bis ein Admin es freigibt. Siehe [Werkzeug-Pinning](#werkzeug-pinning). |
| `apps` | Boolean | `false` | Zeigt die MCP-Apps-Oberflächen des Servers im Web-Chat, in einer Sandbox. Siehe [MCP Apps](#mcp-apps). |

`untrusted: true` zusammen mit einem nicht leeren `untrustedTools` ist ein Fehler. `untrusted: true` zusammen mit `apps: true` ist ebenfalls ein Fehler.

## Werkzeugnamen

Der Namensraum ist `toolPrefix`, ohne ihn `id`. Ein Werkzeug, das der Server unter der ID `ha` als `intent__HassTurnOn` meldet, hat die interne ID `ha.intent__HassTurnOn` und den Namen `ha_intent__HassTurnOn` für das Modell. Es gibt kein Präfix `mcp.`. Risikoregeln in [`risk.yaml`](risk-yaml.md) treffen die interne ID. MCP-Werkzeuge ohne passende Regel stehen standardmäßig auf `confirm`.

Der Namensraum darf keiner sein, der für eingebaute Werkzeuge reserviert ist: `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music`, `system`.

## Verhalten

- Marshal spricht die MCP-Protokollrevision 2026-07-28 und weicht auf die neueste Revision aus, die ein Server unter 2025-11-25 und 2025-06-18 unterstützt, damit Server wie der von Home Assistant weiter funktionieren. Es bietet einem Server nie Sampling, Roots oder Logging an.
- Trägt die Werkzeugliste eines Servers ein `ttlMs`, listet Marshal die Werkzeuge vor Ablauf dieser Zeit nicht erneut, außer der Server schickt eine Änderungsbenachrichtigung. Ohne `ttlMs` listet Marshal nur bei Änderungsbenachrichtigungen erneut.
- Ein stdio-Kindprozess erbt die Umgebung von Marshal nicht. Er bekommt vom Marshal-Prozess nur `PATH`, `HOME`, `LANG`, `LC_ALL`, `TZ` und `TMPDIR`, plus die `envSecrets` seiner Datei.
- Ein ausgefallener Server wird mit exponentiellem Backoff neu verbunden (1 Sekunde, verdoppelt bis 1 Minute). Seine Werkzeuge sind verborgen, solange er ausgefallen ist, und `/health` zeigt eine Warnung.
- Ein Glob in `untrustedTools`, der nach der Erkennung kein Werkzeug trifft, erscheint als Warnung für diesen Server in der Konfigurationsansicht und in `/health`; die Werkzeuge des Servers laden trotzdem.
- Ein Secret, das sich nicht auflösen lässt, macht den Server nicht verfügbar: `<file>: authTokenSecret (<name>) does not resolve.` oder `<file>: envSecrets.<variable> (<name>) does not resolve.`

## Werkzeug-Pinning

Marshal speichert einen Fingerabdruck aus Name, Titel, Beschreibung, Eingabeschema, Ausgabeschema und Annotationen jedes Werkzeugs. Reihenfolge der Schlüssel und Leerzeichen ändern den Fingerabdruck nicht.

- Die erste Erkennung eines Servers pinnt jedes Werkzeug als freigegeben, ohne zu fragen.
- Ein Werkzeug, das nach der ersten Erkennung neu ist oder dessen Definition sich geändert hat, wartet auf Freigabe. Es ist vor dem Modell verborgen, und der Guard verweigert Aufrufe daran. Die anderen Werkzeuge des Servers bleiben verfügbar.
- Ein Werkzeug, das zu seiner freigegebenen Definition zurückkehrt, ist ohne Entscheidung wieder freigegeben.
- Der Eigentümer bekommt pro wartendem Werkzeug eine Benachrichtigung. Die Warnung zum Zustand `mcp.tool_pending` erscheint, bis über das Werkzeug entschieden ist.
- Admins geben in der Weboberfläche unter System > Konfiguration frei oder lehnen ab, mit vorheriger und aktueller Definition nebeneinander. Jede Entscheidung wird protokolliert und gilt ab dem nächsten Turn.

Mit `pinTools: false` werden die Werkzeuge des Servers nie gepinnt. Nutze das für Server, deren Werkzeugliste sich absichtlich oft ändert, etwa Home Assistant, wenn du Entitäten freigibst. Pinning schützt nur gegen spätere Änderungen: Ein Server, der schon bei der ersten Erkennung bösartig ist, wird so gepinnt, wie er ist.

## MCP Apps

Mit `apps: true` zeigt ein Werkzeug, dessen Definition eine `ui://`-Ressource nennt (MCP Apps, `_meta.ui.resourceUri`), diese Oberfläche unter der Antwort, wenn ein Turn im Web-Chat es aufruft. Sprache, API und Aufgabenläufe zeigen nie Apps.

- Marshal liest die Ressource mit `resources/read` und cacht sie für das `ttlMs` des Ergebnisses, standardmäßig 5 Minuten.
- Die Ressource muss den MIME-Typ `text/html;profile=mcp-app` haben und darf höchstens 1 MB groß sein. Sonst erscheint die Antwort ohne die App, und der Durchlauf zeichnet beim Werkzeugschritt die Notiz `app_skipped` auf.
- Die App läuft in einem iframe mit `sandbox="allow-scripts"` und der CSP `sandbox allow-scripts; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'self'`. Sie hat keinen Zugriff auf Cookies, Speicher oder API von Marshal und kann sich mit keinem Host verbinden, keine Formulare absenden, keine Pop-ups öffnen und die Seite nicht verlassen. Die Metadaten `csp` und `permissions` der Ressource werden ignoriert.
- Die App kann nur Werkzeuge ihres eigenen Servers aufrufen, und nur Werkzeuge, die du in einem Turn im Web-Chat nutzen könntest. Jeder Aufruf läuft als eigener Durchlauf mit dem Auslöser `app:<server>/<tool>` durch den Guard. Das Ergebnis geht nur an die App, nie an das Modell. Ein Aufruf der Bestätigungsstufe wird mit `app_confirm` verweigert.
- Anfragen, Chat-Nachrichten zu posten oder den Kontext des Modells zu ändern, werden abgelehnt. Eine Anfrage, einen Link zu öffnen, zeigt den Link unter der App; öffnen tust du ihn selbst.
- Der Chat behält die App bei der Antwort. Eine wieder geöffnete Unterhaltung zeigt sie erneut, solange der Server noch `apps: true` hat, die Ressource lesbar ist und das Werkzeugergebnis höchstens 256 KB groß war. Sonst zeigt der Chat „Diese interaktive Ansicht ist nicht mehr verfügbar.“

Der Block `_meta.ui` des Werkzeugs gehört zur gepinnten Definition, ein geänderter Verweis auf die Oberfläche wartet also auf Freigabe. Das HTML selbst wird nicht gepinnt.

## Eingabeanfragen

Ein Server kann den Benutzer während eines Werkzeugaufrufs um eine Eingabe bitten (MCP-Elicitation, Formular- oder URL-Modus). Marshal gibt die Anfrage nie an das Modell weiter:

- Die Anfrage wird zu einer ausstehenden Eingabeanfrage mit eingefrorenem Aufruf (Server, Werkzeug, Argumente). Das Modell bekommt ein festes Ergebnis, das sagt, dass der Benutzer in der Weboberfläche gefragt wurde.
- Der Eigentümer bekommt einen Push mit Link und ohne Aktionen. Nur die Weboberfläche antwortet: Eingang > Eingabeanfrage oder der Chat.
- Eine Formularantwort muss zum angeforderten Schema passen. Eine URL-Anfrage wird mit „Erledigt“ beantwortet, nachdem du auf der Seite fertig bist.
- Eine Antwort wiederholt den eingefrorenen Aufruf einmal mit der Antwort. Fragt der Server erneut, folgt eine neue Eingabeanfrage.
- Ablehnung oder Ablauf (die TTL für Freigaben) schickt nichts an den Server; die wartende Aufgabe macht mit der Ablehnung weiter.

## Server-Tasks

Ein Server kann einen Werkzeugaufruf mit einem Task-Handle statt eines Ergebnisses beantworten (die MCP-Erweiterung für Tasks). Marshal dann:

- legt eine wartende Aufgabe an, die dorthin zustellt, woher der Aufruf kam (der Satellit eines Sprach-Turns, sonst die Web-Unterhaltung und der Eingang), und sagt dem Modell, dass die Arbeit im Hintergrund weiterläuft;
- fragt den Status der Server-Task ab, nie früher als im Abfrageintervall des Servers, ab 5 Sekunden mit Backoff bis 5 Minuten;
- veröffentlicht das [Ereignis `mcp.task_finished`](../reference/events.md#mcp-server-tasks-mcp) und setzt die wartende Aufgabe mit Ergebnis oder Fehler fort, wenn die Server-Task endet. Das Ergebnis sind nicht vertrauenswürdige Daten, wenn das Werkzeug nicht vertrauenswürdig ist.
- macht aus jeder Eingabeanfrage einer laufenden Server-Task eine ausstehende Eingabeanfrage. Antwort, Ablehnung oder Ablauf schicken die Entscheidung an die Server-Task; die Aufgabe wartet weiter auf das Ergebnis.

Die wartende Aufgabe abzubrechen bricht die Server-Task ab. Ein Eigentümer kann höchstens 50 Server-Tasks gleichzeitig verfolgen; darüber hinaus bricht Marshal die neue Server-Task ab, und das Werkzeugergebnis sagt warum. Handles überstehen Neustarts.

## Validierung

Eine abgelehnte Datei beeinträchtigt nie die anderen. Fehler erscheinen in `/health`, in der Konfigurationsansicht und im Log.

- `<file>: id '<id>' must match [a-z][a-z0-9]*.`
- `<file>: toolPrefix '<prefix>' must match [a-z][a-z0-9]*.`
- `<file>: namespace '<ns>' is reserved for built-in tools; set another id or toolPrefix.`
- `<file>: untrusted: true already marks every tool; remove untrustedTools or set untrusted: false.`
- `<file>: untrustedTools entries must not be empty.`
- `<file>: authTokenSecret must be a secret name (^[a-z][a-z0-9_]{0,63}$).`
- `<file>: every envSecrets entry needs a variable and a secret name.`
- `<file>: url must be an absolute http(s) URL for transport http.`
- `<file>: command is required for transport stdio.`
- `<file>: transport '<value>' must be 'stdio' or 'http'.`
- `Server id '<id>' is already configured in another file.`
- `Namespace '<ns>' is already used by another server.`
- Ein wörtliches Secret in der Datei, ein unbekannter Schlüssel oder ungültiges YAML (`invalid value at line <n>`).
- Ein Server, dessen Start oder Erkennung scheitert oder dessen Werkzeugnamen kollidieren, wird mit einem Fehler abgewiesen.

## Verwandte Seiten

::: cards
- [Eigene MCP-Server](../guides/integrations/custom-mcp-servers.md)
- [Zustandsendpunkte](../reference/health-endpoints.md)
- [Home-Assistant-MCP](../guides/integrations/home-assistant-mcp.md)
- [Nicht vertrauenswürdige Inhalte](../concepts/untrusted-content.md)
- [Secrets-Referenz](secrets-reference.md)
:::
