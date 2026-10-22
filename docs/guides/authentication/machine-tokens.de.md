---
icon: fa-solid fa-key
description: "Erstelle widerrufbare Tokens mit Scopes für Maschinen-Clients wie die Home-Assistant-Integration, Skripte und Webhook-Absender."
---

# API-Tokens anlegen und verwalten

Erstelle widerrufbare Tokens mit Scopes für Maschinen-Clients wie die Home-Assistant-Integration, Skripte und Webhook-Absender.

## Bevor du anfängst

- Für den Weg über die Weboberfläche: Melde dich mit einer Browser-Sitzung an. Token-Clients können keine Tokens verwalten.
- Für den Weg über die CLI: Shell-Zugriff auf den Marshal-Container.

## Schritte

### Ein Token in der Weboberfläche erstellen

1. Öffne **Einstellungen > Sicherheit**.
2. Wähle unter **API-Tokens**, ein Token zu erstellen. Gib einen Namen (zum Beispiel „Home Assistant“), die Berechtigungen und optional ein Ablaufdatum ein.
3. Kopiere das Token. Marshal zeigt es einmal an und speichert nur seinen Hash.

Du kannst nur Scopes vergeben, die du selbst hast. Ein `user` kann kein `admin`-Token erstellen.

### Ein Token mit der CLI erstellen

Die CLI funktioniert ohne Web-Anmeldung. Tokens gehören dem Eigentümer der Instanz.

```sh
docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write --scope aitasks:write
docker compose exec marshal marshal tokens create --name ci --scope tasks:read --scope tasks:write --expires-in-days 90
docker compose exec marshal marshal tokens list
docker compose exec marshal marshal tokens revoke <token-id>
```

`create` gibt das Token einmal aus, gefolgt von „Store it now; it is not shown again.“ Der Dienstname in deiner Compose-Datei kann abweichen. Siehe die [CLI-Referenz](../../reference/cli.md).

### Das Token nutzen

```http
POST /api/v1/conversation/turns HTTP/1.1
Authorization: Bearer <token>
```

Siehe [API-Authentifizierung](../../api/authentication.md).

### Ein Token widerrufen

Wähle in **Einstellungen > Sicherheit** beim Token **Widerrufen**, oder führe `marshal tokens revoke <token-id>` aus. Die nächste Anfrage mit diesem Token bekommt 401. Widerrufe und Erstellungen werden protokolliert.

## Scopes

| Scope | Erlaubt |
|---|---|
| `conversation:write` | `POST /api/v1/conversation/turns`. Nötig für die Home-Assistant-Integration. |
| `aitasks:write` | `POST /api/v1/ai-tasks`. Nötig für die AI-Task-Entität der Home-Assistant-Integration. |
| `mcp:read` | Den MCP-Server von Marshal unter `/mcp`: Werkzeuge zum Lesen von Gedächtnis, Notizen und Aufgaben. Siehe [MCP-Clients](../integrations/mcp-clients.md). |
| `mcp:write` | Den MCP-Server von Marshal: außerdem `tasks_create` und `ask_marshal`. |
| `tasks:read` | Aufgaben lesen. |
| `tasks:write` | Aufgaben anlegen und ändern. |
| `admin` | Admin-Endpunkte. |
| `hooks:uptime-kuma`, `hooks:proxmox`, `hooks:seerr` | Webhooks dieser Quelle senden. Nur `admin`-Benutzer können sie vergeben. Siehe [Webhooks](../../api/webhooks.md). |

Ein Token mit zu wenigen Scopes bekommt 403. Widerrufene oder abgelaufene Tokens bekommen 401.

## Callback-Tokens des Executors

Tokens der Art `executor_callback` gehören zu Coding-Sitzungen. Sie erscheinen schreibgeschützt in der Liste, lassen sich weder über die API noch über die Oberfläche anlegen und dort nicht widerrufen.

## Prüfen, ob es funktioniert

```sh
curl -sS -H "Authorization: Bearer <token>" https://<marshal-host>/api/v1/me
```

> [!NOTE]
> `/api/v1/me` zeigt die Scopes des Principals. Token-Clients erreichen keine Endpunkte, die Browser-Sitzungen vorbehalten sind, etwa die Verwaltung von Tokens und Secrets.

## Verwandte Seiten

::: cards
- [Assist-Pipeline](../voice/assist-pipeline.md)
- [Webhooks](../../api/webhooks.md)
- [Secrets](../secrets.md)
:::
