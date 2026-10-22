---
icon: fa-solid fa-right-to-bracket
description: "Die Anmeldeanbieter für die Weboberfläche. Mehrere Anbieter können gleichzeitig aktiv sein. Änderungen brauchen einen Neustart."
---

# `auth`

Die Anmeldeanbieter für die Weboberfläche. Mehrere Anbieter können gleichzeitig aktiv sein. Änderungen brauchen einen Neustart.

## Beispiel

```yaml
auth:
  providers:
    - name: local
      type: local
    - name: authentik
      type: oidc
      displayName: Authentik
      authority: "https://auth.example.com/application/o/marshal/"
      clientId: marshal
      clientCredentialSecret: authentik_client
      scopes: [openid, profile, email]
      claims: { subject: sub, name: name, groups: groups }
      roleMappings:
        marshal-admins: admin
        marshal-users: user
```

## Schlüssel

Jeder Eintrag von `auth.providers`:

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `auth.providers[].name` | Zeichenkette | keiner | Stabile ID in URLs (`/api/v1/auth/login/{name}`, `/signin-oidc/{name}`). Eindeutig. |
| `auth.providers[].type` | Zeichenkette | keiner | `oidc` oder `local`. Höchstens ein `local`-Anbieter. |
| `auth.providers[].displayName` | Zeichenkette | `Local account` bei `local`, sonst der Name | Beschriftung des Knopfs auf der Anmeldeseite. |
| `auth.providers[].authority` | URL | keiner | `oidc`: Issuer-URL (Discovery unter `.well-known/openid-configuration`). |
| `auth.providers[].clientId` | Zeichenkette | keiner | `oidc`: Client-ID. |
| `auth.providers[].clientCredentialSecret` | Secret-Name | keiner | `oidc`: Name des Secrets mit dem Client-Secret. |
| `auth.providers[].scopes` | Liste von Zeichenketten | `[openid, profile, email]` | `oidc`: angeforderte Scopes. |
| `auth.providers[].useUserInfo` | Boolean | `false` | `oidc`: Claims zusätzlich vom Userinfo-Endpunkt lesen, für IdPs, die Gruppen nicht ins ID-Token schreiben. |
| `auth.providers[].claims.subject` | Zeichenkette | `sub` | Claim mit dem Subject. |
| `auth.providers[].claims.name` | Zeichenkette | `name` | Claim mit dem Anzeigenamen. |
| `auth.providers[].claims.groups` | Zeichenkette | `groups` | Claim mit den Gruppen. |
| `auth.providers[].roleMappings` | Abbildung Gruppe auf Rolle | leer | `oidc`: IdP-Gruppe auf `admin` oder `user`. Eine Anmeldung ohne zugeordnete Gruppe wird abgelehnt. Rollen werden bei jeder Anmeldung neu abgeglichen. |
| `auth.providers[].serverDomain` | Zeichenkette | nicht gesetzt | `local`: ID der WebAuthn-Relying-Party (zum Beispiel `marshal.example.com`). Nicht gesetzt: der Host der Anfrage. Setz es, wenn der Proxy den Host-Header nicht prüft. |

Eine leere Anbieterliste ist gültig, lässt aber keine Möglichkeit zur Anmeldung.

## Validierung

- `auth.providers[i].name must be lowercase letters, digits or '-'.`
- `auth.providers[i].type must be 'oidc' or 'local'.`
- `auth.providers uses the name '<name>' more than once.`
- `auth.providers allows only one 'local' provider.`
- Für `oidc`: `...authority must be an absolute URL.`, `...clientId is required.`, `...clientCredentialSecret is required.`, `...roleMappings needs at least one group.`, `...roleMappings values must be 'admin' or 'user'.`
- Das Client-Secret muss beim Start auflösbar sein; sonst scheitert der Start mit einem Hinweis auf `auth.providers[i].clientCredentialSecret`.

## Verwandte Seiten

::: cards
- [Lokale Konten](../../guides/authentication/local-accounts.md)
- [OIDC](../../guides/authentication/oidc.md)
- [Machine-Tokens](../../guides/authentication/machine-tokens.md)
:::
