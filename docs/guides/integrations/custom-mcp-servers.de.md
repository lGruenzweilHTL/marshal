---
icon: fa-solid fa-plug-circle-plus
description: "Verbinde einen beliebigen Model-Context-Protocol-Server (MCP) mit Marshal, damit seine Werkzeuge neben den eingebauten erscheinen."
---

# Einen eigenen MCP-Server hinzufügen

Verbinde einen beliebigen Model-Context-Protocol-Server (MCP) mit Marshal, damit seine Werkzeuge neben den eingebauten erscheinen.

## Bevor du anfängst

- Ein MCP-Server, der Streamable HTTP spricht, oder ein Befehl, den Marshal starten kann.
- Zugriff auf `/config/mcp/`.
- Für die Authentifizierung: ein Secret, das das Token enthält, siehe [Secrets](../secrets.md).

> [!IMPORTANT]
> Der Marshal-Container hat keine Laufzeit für Node.js oder Python. Ein Server mit `stdio` braucht seinen Befehl im Container, bau also ein abgeleitetes Image, das ihn enthält. Nimm `http` für alles, was woanders läuft.

## Schritte

::: steps
1. **Leg eine Server-Datei an**

   Füg pro Server eine YAML-Datei in `/config/mcp/` hinzu. Der Dateiname ist frei.

   ```yaml
   id: search                      # Kleinbuchstaben und Ziffern, beginnt mit einem Buchstaben
   transport: http                 # http (Streamable HTTP) oder stdio
   url: http://search.lan:3000/mcp
   authTokenSecret: search_token   # Secret-NAME, gesendet als "Authorization: Bearer <value>"
   toolPrefix: web                 # optional; ersetzt die ID als Namensraum der Werkzeuge
   untrusted: true                 # Werkzeugausgaben sind Daten von Dritten
   ```

   Für einen lokalen Prozess nimm `stdio`:

   ```yaml
   id: files
   transport: stdio
   command: /usr/local/bin/files-mcp
   args: ["--root", "/data/shared"]
   envSecrets:
     - variable: FILES_API_KEY
       secret: files_api_key
   ```

   Der eigene MCP-Server von Home Assistant nutzt dasselbe Format, siehe [MCP von Home Assistant](home-assistant-mcp.md) und `deploy/config.example/mcp/ha.yaml`.

2. **Schlüssel**

   | Schlüssel | Typ | Beschreibung |
   |---|---|---|
   | `id` | string | Pflicht. Passt auf `[a-z][a-z0-9]*`. Eindeutig über alle Dateien. |
   | `transport` | `http` oder `stdio` | Pflicht. |
   | `url` | URL | Pflicht für `http`. Absolutes `http` oder `https`. |
   | `command`, `args` | string, Liste | `command` ist Pflicht für `stdio`. |
   | `toolPrefix` | string | Optionaler Namensraum statt `id`. Dasselbe Muster. |
   | `authTokenSecret` | Secret-Name | Optional. Wird als Bearer-Header gesendet. |
   | `envSecrets` | Liste aus `variable` und `secret` | Umgebungsvariablen eines `stdio`-Servers, gefüllt aus Secrets. |
   | `untrusted` | boolean | Standard `false`. Markiert die Ausgabe jedes Werkzeugs als Daten von Dritten. |
   | `untrustedTools` | Liste von Glob-Mustern | Markiert nur passende Werkzeuge (nach dem Namen, den der Server meldet). Nicht zusammen mit `untrusted: true` erlaubt. |
   | `pinTools` | boolean | Standard `true`. Ein Werkzeug, das nach der ersten Erkennung neu ist oder sich geändert hat, wartet auf deine Freigabe. |

   Unbekannte Schlüssel sind Fehler. Die Konfiguration enthält nie Werte von Secrets, und ein Secret im Klartext in der Datei lässt das Laden scheitern. Die vollständige Referenz steht unter [`mcp-servers`](../../configuration/mcp-servers.md).

3. **Setz Risikoregeln**

   Ein Werkzeug eines MCP-Servers, auf das keine Regel in `risk.yaml` passt, ist in der Bestätigungsstufe, jeder Aufruf braucht also deine Freigabe. Füg Regeln hinzu, um nur lesende Werkzeuge zu erlauben:

   ```yaml [/config/risk.yaml]
   risk:
     - match: { tool: "search.*" }
       tier: safe
   ```

   Der Treffer ist ein Glob-Muster auf die interne Werkzeug-ID `<namespace>.<name>`. Siehe [Risikoregeln](../risk-rules.md).

4. **Warte auf das Neuladen**

   Das Hinzufügen, Ändern oder Entfernen einer Datei wirkt ohne Neustart. Laufende Turns behalten ihre Werkzeugliste. Der nächste Turn sieht die Änderung.
:::

## Wie Werkzeuge erscheinen

Der Namensraum ist die `id` oder `toolPrefix`, ohne Präfix `mcp`. Der Server `ha`, der `intent__HassTurnOn` meldet, ergibt die interne ID `ha.intent__HassTurnOn` und den Namen `ha_intent__HassTurnOn` für das Modell.

Marshal reserviert die Namensräume seiner eingebauten Werkzeuge: `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music`, `system`. Ein Server, der einen davon nutzen würde, wird abgewiesen.

## Nicht vertrauenswürdige Server

Liefert ein Server Text, den andere Personen kontrollieren, etwa Websuche oder geteilte Listen, markiere ihn:

- `untrusted: true` für jedes Werkzeug des Servers.
- `untrustedTools: ["todo__get_items"]` für einzelne Werkzeuge, damit die Hausteuerung vertrauenswürdig bleibt.

Ein Durchlauf, der ein solches Werkzeug aufruft, ist mit der Quelle `mcp:<id>` belastet, seine Ergebnisse werden als Daten eingepackt, und Schreibvorgänge in Gedächtnis und Aufgaben brauchen in diesem Durchlauf eine Bestätigung. Ein Glob-Muster, das auf kein gemeldetes Werkzeug passt, zeigt für diesen Server eine Warnung in der Konfigurationsansicht und in `/health`. Siehe [nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md).

## Die Apps des Servers zeigen

Manche Server hängen an ein Werkzeug eine interaktive Oberfläche (MCP Apps): ein Diagramm, einen Player, eine formularartige Ansicht. Marshal zeigt diese Oberflächen im Web-Chat, wenn du in der Server-Datei `apps: true` setzt. Lass es bei Servern aus, denen du nicht vertraust; `apps: true` zusammen mit `untrusted: true` wird abgewiesen.

Eine App ist HTML, das der Server geschrieben hat. Marshal begrenzt, was sie tun kann:

- Sie läuft in einer Sandbox ohne Netzzugriff. Apps, die Daten aus dem Internet laden, funktionieren nicht.
- Sie hat keine Formulare, Pop-ups oder Navigation und kann die Cookies und Daten von Marshal nicht lesen.
- Sie kann nur Werkzeuge ihres eigenen Servers aufrufen. Jeder Aufruf läuft als eigener Durchlauf durch den Guard, und Aufrufe der Bestätigungsstufe werden verweigert.
- Sie kann nicht in die Unterhaltung schreiben. Links, die sie vorschlägt, erscheinen unter ihr zum Anklicken.
- Eine Beschriftung über der App nennt den Server. Tippe nie ein Passwort in eine App.

Details stehen unter [MCP Apps](../../configuration/mcp-servers.md#mcp-apps).

## Geänderte Werkzeuge freigeben

Marshal pinnt jede Werkzeugdefinition, wenn es den Server zum ersten Mal sieht. Fügt der Server später ein Werkzeug hinzu oder ändert eines, bleibt dieses Werkzeug verborgen, bis du entscheidest:

1. Eine Benachrichtigung und das Banner zum Zustand sagen, dass ein Werkzeug auf Freigabe wartet.
2. Öffne System > Konfiguration. Das wartende Werkzeug zeigt seine vorherige und seine aktuelle Definition nebeneinander.
3. Lies die Beschreibung genau. Eine geänderte Beschreibung ist der Weg, auf dem ein kompromittierter Server versuchen würde, das Modell anzuweisen.
4. Gib frei oder lehne ab. Die Entscheidung gilt ab dem nächsten Turn.

Setz `pinTools: false` für einen Server, dessen Werkzeugliste sich absichtlich oft ändert. Siehe [Werkzeug-Pinning](../../configuration/mcp-servers.md#werkzeug-pinning).

## Eingaben und Arbeit im Hintergrund

Manche Server bitten dich während eines Aufrufs um eine Eingabe oder setzen einen langen Aufruf im Hintergrund fort:

- **Eingabeanfragen** (MCP Elicitation) erreichen nie das Modell. Marshal fragt dich in der Weboberfläche und schickt einen Push mit einem Link. Antworte im Eingang; der Aufruf wird einmal mit deiner Antwort wiederholt.
- **Server-Tasks** (MCP-Tasks-Erweiterung) werden zu einer wartenden Aufgabe. Marshal sagt dir, dass die Arbeit weitergeht, und stellt das Ergebnis dort zu, wo du es wolltest, sobald der Server es als fertig meldet.

Details: [Eingabeanfragen](../../configuration/mcp-servers.md#eingabeanfragen) und [Server-Tasks](../../configuration/mcp-servers.md#server-tasks).

## Umgang mit Fehlern

Ein Server, der nicht startet, bei der Erkennung scheitert, einen reservierten oder doppelten Namensraum nutzt oder kollidierende Werkzeugnamen hat, wird mit einem Fehler abgewiesen. Andere Server und eingebaute Werkzeuge funktionieren weiter. Ein Server, der die Verbindung verliert, wird mit Backoff neu verbunden, und seine Werkzeuge sind verborgen, solange er ausgefallen ist.

## Prüfen, ob es funktioniert

1. Öffne `/health`. Die MCP-Prüfung meldet die verbundenen Server und listet Warnungen für jeden Server, der ausgefallen oder abgewiesen ist.
2. Öffne die Konfigurationsansicht in der Weboberfläche und finde deinen Server und seine Werkzeuge.
3. Beginne eine neue Unterhaltung und bitte Marshal, ein Werkzeug des Servers zu nutzen. Ein Werkzeug ohne passende Risikoregel fragt zuerst nach einer Bestätigung.

## Verwandte Seiten

::: cards
- [Referenz von `mcp-servers`](../../configuration/mcp-servers.md)
- [Risikoregeln](../risk-rules.md)
- [Sichtbarkeit von Werkzeugen](../../concepts/tool-visibility.md)
- [Secrets](../secrets.md)
- [MCP von Home Assistant](home-assistant-mcp.md)
:::
