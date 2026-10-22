---
icon: fa-solid fa-key
order: 10
description: "Marshal nimmt zwei Arten von Zugangsdaten an: API-Tokens mit Scopes für Maschinen-Clients und eine Cookie-Sitzung für Personen, die die Weboberfläche nutzen."
---

# Authentifizierung an der API

Marshal nimmt zwei Arten von Zugangsdaten an: API-Tokens mit Scopes für Maschinen-Clients und eine Cookie-Sitzung für Personen, die die Weboberfläche nutzen. Diese Seite beschreibt beide und listet die Scopes der Tokens.

## Maschinen-Tokens

Schick das Token im Header `Authorization`:

```http
Authorization: Bearer <token>
```

Ein Token hat einen Eigentümer, einen Namen, eine Menge von Scopes, einen optionalen Ablaufzeitpunkt und einen Zeitpunkt des Widerrufs. Marshal speichert nur einen Hash des Tokens. Den Klartext zeigt es einmal, beim Anlegen. Ein widerrufenes, abgelaufenes oder unbekanntes Token bekommt `401`.

Ein Token wird nur über seine eigenen Scopes autorisiert. Ein Token mit `tasks:read`, das einen Endpunkt aufruft, der `conversation:write` braucht, bekommt `403`.

### Scopes

| Scope | Erlaubt |
|---|---|
| `conversation:write` | `POST /api/v1/conversation/turns` |
| `tasks:read` | Aufgaben lesen |
| `tasks:write` | Aufgaben ändern |
| `aitasks:write` | `POST /api/v1/ai-tasks` ([AI-Tasks](ai-tasks.md)) |
| `mcp:read` | Marshals MCP-Server unter `/mcp`, lesende Werkzeuge ([MCP-Clients](../guides/integrations/mcp-clients.md)) |
| `mcp:write` | Marshals MCP-Server, zusätzlich `tasks_create` und `ask_marshal` |
| `admin` | Administrative Endpunkte |
| `hooks:uptime-kuma` | [Webhooks](webhooks.md) von Uptime Kuma senden |
| `hooks:proxmox` | Webhooks von Proxmox VE senden |
| `hooks:seerr` | Webhooks von Seerr senden |

Jedes gültige Token kann `GET /api/v1/me` und `GET /api/v1/events` aufrufen. Die meisten anderen Endpunkte sind für die Weboberfläche da und brauchen eine Cookie-Sitzung; ein Token erreicht sie nie, egal welche Scopes es hat. Siehe [API-Überblick](index.md#interne-endpunkte-der-spa). Jede Operation in der [API-v1-Referenz](v1/index.md) nennt die Zugangsdaten, die sie annimmt.

Gib einem Token nur die Scopes, die sein Client braucht. Die Home-Assistant-Komponente braucht `conversation:write` für Assist und `aitasks:write` für ihre AI-Task-Entität, sonst nichts.

### Ein Token anlegen

Auf dem Marshal-Host, im Verzeichnis deiner `docker-compose.yml`:

```sh
docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write --scope aitasks:write
```

Der Befehl funktioniert ohne Anmeldung im Web, legt das Token für den Eigentümer der Instanz an und gibt den Klartext einmal aus. Wiederhole `--scope` für weitere Scopes und ergänze `--expires-in-days <days>` für ein Token mit Ablauf.

```sh
docker compose exec marshal marshal tokens list
docker compose exec marshal marshal tokens revoke <token-id>
```

Öffne in der Weboberfläche **Sicherheit**, um deine eigenen Tokens zu listen, anzulegen und zu widerrufen. Du kannst nur Scopes vergeben, die du selbst hast; ein Benutzer mit der Rolle `user` kann also kein `admin`-Token anlegen. Jedes Anlegen und jeder Widerruf steht im Audit-Log, mit dir als Akteur.

Tokens der Art `executor_callback` gehören zum Coding-Executor. Sie erscheinen schreibgeschützt in der Liste; du kannst sie weder anlegen noch widerrufen.

Die vollständige Anleitung steht unter [API-Tokens anlegen und verwalten](../guides/authentication/machine-tokens.md).

> [!IMPORTANT]
> Behandle ein Token wie ein Passwort. Bewahre es in einem Secret-Store oder einer Umgebungsvariable des Clients auf, nie in einem Repository, und widerrufe es, wenn der Client außer Dienst geht.

### Ein Token prüfen

```sh
curl -H "Authorization: Bearer <token>" https://marshal.example.com/api/v1/me
```

Oder führe es hier aus, mit **Authorize** und deinem Token (siehe [Die API in der Doku ausprobieren](openapi.md#die-api-in-der-doku-ausprobieren)):

::: request v1 GetMe
:::

```json
{ "id": "…", "displayName": "Lukas", "roles": [], "scopes": ["conversation:write"], "demo": { "enabled": false, "showBadge": true } }
```

`roles` ist bei Token-Principals leer, und `scopes` listet die eigenen Scopes des Tokens.

## Cookie-Sitzungen (Browser)

Die Weboberfläche meldet sich über einen Backend-for-Frontend-Ablauf (BFF) an. Beim Anbieter `oidc` ist Marshal der vertrauliche Client (Authorization Code mit PKCE) und behält die Tokens des Identitätsanbieters auf dem Server. Der Browser bekommt nur ein Sitzungs-Cookie:

- `marshal_session`, `HttpOnly`, `Secure`, `SameSite=Lax`.

Die Anmeldung beginnt bei `/api/v1/auth/login/{provider}` und endet bei `/api/v1/auth/logout`. `GET /api/v1/auth/providers` listet die aktiven Anbieter ohne Authentifizierung. Richte Anbieter unter [OpenID Connect](../guides/authentication/oidc.md) oder [lokale Konten](../guides/authentication/local-accounts.md) ein.

Browser-Sitzungen brauchen HTTPS. Eine Sitzungsanfrage über einfaches HTTP scheitert mit dem Problem-Code `auth.https_required`.

### Antiforgery

Jede Anfrage, die den Zustand ändert (alles außer `GET`, `HEAD`, `OPTIONS`, `TRACE`) und nicht per Bearer authentifiziert ist, braucht ein Antiforgery-Token im Header `X-XSRF-TOKEN`. Hol dir nach jeder Anmeldung und Abmeldung eines von `GET /api/v1/auth/antiforgery`. Ein fehlendes oder ungültiges Token gibt `400` mit dem Code `auth.antiforgery_invalid` zurück.

Bearer-Anfragen überspringen diese Prüfung, weil ein Browser einer Cross-Site-Anfrage kein Bearer-Token anhängen kann.

### Rollen und Scopes

Eine Cookie-Sitzung trägt die Scopes ihrer Rollen. Es gelten dieselben Scope-Richtlinien wie für Tokens.

| Rolle | Scopes |
|---|---|
| `user` | `conversation:write`, `tasks:read`, `tasks:write` |
| `admin` | alle Scopes, einschließlich `admin` und der `hooks:*`-Scopes |

Ein Benutzer mit der Rolle `user`, der einen Endpunkt aufruft, der `admin` braucht, bekommt `403`.

## Verwandte Seiten

::: cards
- [API-Tokens anlegen und verwalten](../guides/authentication/machine-tokens.md)
- [Webhooks](webhooks.md)
- [Sicherheitsmodell](../concepts/security-model.md)
:::
