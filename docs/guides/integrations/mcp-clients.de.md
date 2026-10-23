---
icon: fa-solid fa-laptop-code
description: "Gib einem externen KI-Client über den eigenen MCP-Server von Marshal unter /mcp Zugriff auf das, was Marshal sich merkt, auf deine Notizen und die Aufgaben von Marshal, und lass ihn Marshal um Aktionen bitten."
---

# Claude Code oder VS Code mit Marshal verbinden

Gib einem externen KI-Client über den eigenen MCP-Server von Marshal unter `/mcp` Zugriff auf das, was Marshal sich merkt, auf deine Notizen und die Aufgaben von Marshal, und lass ihn Marshal um Aktionen bitten.

## Bevor du anfängst

- Marshal ist von der Maschine erreichbar, auf der der Client läuft, zum Beispiel `https://marshal.example.org`.
- Du kannst dich in der Weboberfläche anmelden oder hast Shell-Zugriff auf den Marshal-Host für die CLI.
- Der Client unterstützt MCP über Streamable HTTP mit einem Bearer-Token: Claude Code oder VS Code mit dem Agentenmodus von GitHub Copilot.
- Clients, die OAuth verlangen (zum Beispiel Connectors von claude.ai oder ChatGPT), können sich nicht verbinden. Der MCP-Server von Marshal nimmt nur Maschinen-Tokens an.

## Was der Client sieht

Welche Werkzeuge der Client bekommt, hängt von den Scopes des Tokens ab:

| Scope | MCP-Werkzeuge | Was sie tun |
|---|---|---|
| `mcp:read` | `memory_recall`, `notes_search`, `notes_read`, `tasks_list`, `tasks_status` | Das Gedächtnis von Marshal abrufen, deine Notizen durchsuchen und lesen, Aufgaben listen und ihren Status lesen. |
| `mcp:write` | die lesenden Werkzeuge plus `tasks_create` und `ask_marshal` | Außerdem Hintergrundaufgaben anlegen und Marshal bitten, mit seinen eigenen Werkzeugen zu antworten oder zu handeln. |

Ein Werkzeug einer Integration, die nicht konfiguriert ist, wird nicht gelistet. Die Notizwerkzeuge erscheinen zum Beispiel nur, wenn [Notizen](notes.md) konfiguriert sind. Hausteuerung, Mail und Drucker werden nie als Werkzeuge angeboten. Der Client erreicht sie nur über `ask_marshal`, der Guard und die Risikoregeln von Marshal entscheiden also über jeden Aufruf.

## Schritte

::: steps
1. **Erstelle ein Token**

   Öffne in der Weboberfläche **Sicherheit** und erstelle ein Token, das nach dem Client benannt ist, zum Beispiel `claude-code`. Wähle `mcp:read` und zusätzlich `mcp:write`, wenn der Client Aufgaben anlegen und Marshal um Aktionen bitten darf. Kopiere das Token; es wird nur einmal angezeigt.

   Oder auf dem Marshal-Host:

   ```sh
   docker compose exec marshal marshal tokens create --name claude-code --scope mcp:read --scope mcp:write
   ```

2. **Füg Marshal dem Client hinzu**

   ::: tabs
   == Claude Code

   Füg den Server für deinen Benutzer hinzu:

   ```sh
   claude mcp add --transport http --scope user marshal https://marshal.example.org/mcp \
     --header "Authorization: Bearer mrs_..."
   ```

   == VS Code

   Füg den Server in `.vscode/mcp.json` deines Workspaces hinzu oder in deine Benutzer-MCP-Konfiguration. VS Code fragt einmal nach dem Token und speichert es sicher:

   ```json
   {
     "inputs": [
       { "type": "promptString", "id": "marshal-token", "description": "Marshal API token", "password": true }
     ],
     "servers": {
       "marshal": {
         "type": "http",
         "url": "https://marshal.example.org/mcp",
         "headers": { "Authorization": "Bearer ${input:marshal-token}" }
       }
     }
   }
   ```
   :::

3. **Frag Marshal etwas**

   Frag den Client zum Beispiel: „Frag Marshal, ob der Drucker beschäftigt ist.“ Der Client ruft `ask_marshal` mit deinem Text auf. Marshal führt einen Turn mit seinem Standard-Template aus und liefert die Antwort und eine `conversationId`. Übergibt der Client die `conversationId` erneut, setzt Marshal dieselbe Unterhaltung fort.
:::

## Wie Marshal MCP-Aufrufe behandelt

- **Jeder Aufruf ist ein Durchlauf.** Jeder Aufruf erscheint unter **Läufe** mit der Quelle `api` und dem Auslöser `mcp:<tool>`, zum Beispiel `mcp:ask_marshal`.
- **Über MCP wird nichts bestätigt.** Braucht `ask_marshal` eine Aktion der Bestätigungsstufe, zum Beispiel einen Druckstart, legt Marshal sie in die Freigabe-Warteschlange, und die Antwort sagt, dass sie auf eine Freigabe wartet. Ein späteres „ja“ vom Client bestätigt sie nicht. Gib sie in der Weboberfläche frei oder lehne sie ab.
- **Kosten zählen gegen die Tagesgrenze.** MCP-Durchläufe zählen gegen `tasks.dailyCostCap`. Ist die Grenze erreicht, antworten die Werkzeuge mit einem Fehler, der das sagt, und Marshal ruft kein Modell auf.
- **Aufrufe beginnen unbelastet.** Die Eingabe von `ask_marshal` zählt als deine eigene Anfrage, wie ein API-Turn. Ein Durchlauf wird belastet, sobald er nicht vertrauenswürdige Inhalte liest, und spätere Schreibvorgänge in Gedächtnis und Aufgaben brauchen in diesem Durchlauf eine Freigabe.

## Prüfen, ob es funktioniert

Liste im Client die MCP-Werkzeuge von `marshal`. Ein Token mit beiden Scopes listet sieben Werkzeuge, ohne Notizen fünf. Ruf `tasks_list` auf, öffne dann **Läufe** in der Weboberfläche und finde den Durchlauf mit dem Auslöser `mcp:tasks_list`.

## Fehlerbehebung

| Symptom | Ursache |
|---|---|
| HTTP 401 | Das Token fehlt, ist widerrufen oder abgelaufen. |
| HTTP 403 | Das Token hat weder `mcp:read` noch `mcp:write`, oder die Anfrage kam mit einer Browser-Sitzung statt mit einem Token. |
| `Unknown tool` | Die Scopes des Tokens erlauben das Werkzeug nicht. Erstelle ein Token mit `mcp:write`. |
| Keine Notizwerkzeuge | Notizen sind nicht konfiguriert. |

## Datenschutz und Injection

- Gedächtnis, Notizen und Aufgabenergebnisse gehen an den Client und von dort an das Modell, das der Client nutzt, oft ein Cloud-Modell. Vergib `mcp:read` nur an Clients, denen du deine Notizen zeigen würdest.
- Der externe Agent hat womöglich selbst nicht vertrauenswürdige Inhalte gelesen, etwa eine Webseite oder ein Repository. Ein manipulierter Agent könnte Aufgaben anlegen oder Marshal um Aktionen bitten. Aktionen der Bestätigungsstufe brauchen weiterhin deine Freigabe in der Freigabe-Warteschlange. Vergib `mcp:write` nur an Clients, denen du vertraust, und widerrufe das Token unter **Sicherheit**, wenn sich ein Client falsch verhält.

## Verwandte Seiten

::: cards
- [Maschinen-Tokens](../authentication/machine-tokens.md)
- [API-Authentifizierung](../../api/authentication.md)
- [Risikoregeln](../risk-rules.md)
- [Beobachtbarkeit](../observability.md)
- [Eigene MCP-Server](custom-mcp-servers.md)
  Die andere Richtung, Marshal als MCP-Client
:::
