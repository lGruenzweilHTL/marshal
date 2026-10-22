---
icon: fa-solid fa-id-card
description: "Lass Personen sich über deinen Identity Provider anmelden (Authentik, Keycloak, Pocket ID oder ein anderer OIDC-Provider) und ordne Gruppen des Providers Rollen in Marshal zu."
---

# Mit OpenID Connect anmelden

Lass Personen sich über deinen Identity Provider anmelden (Authentik, Keycloak, Pocket ID oder ein anderer OIDC-Provider) und ordne Gruppen des Providers Rollen in Marshal zu.

## Bevor du anfängst

- Marshal ist über HTTPS unter einer festen Adresse erreichbar.
- Ein OIDC-Provider, der den Authorization Code Flow mit PKCE unterstützt.
- Ein Gruppen-Claim im ID-Token (oder in der Userinfo-Antwort), der die Gruppen des Benutzers listet.
- Zwei Gruppen beim Provider, zum Beispiel `marshal-admins` und `marshal-users`.

Marshal arbeitet als Confidential Client und behält die Tokens des Providers auf dem Server. Der Browser bekommt nur ein Sitzungs-Cookie mit HttpOnly, Secure und `SameSite=Lax`.

## Schritte

::: steps
1. **Registriere Marshal beim Provider**

   Leg einen OIDC-Client an mit:

   - Redirect-URI: `https://<marshal-host>/signin-oidc/<provider-name>`. Der Name ist der `name`, den du unten wählst. Läuft Marshal unter einem Basispfad (`http.pathBase`), nimm ihn mit auf.
   - Flow: Authorization Code mit PKCE; Client-Typ confidential.
   - Scopes: `openid`, `profile`, `email` plus das, was der Provider braucht, um Gruppen ins Token zu schreiben.

   Notiere Client-ID und Client-Secret.

2. **Speichere das Client-Secret**

   Speichere das Client-Secret unter einem Namen, zum Beispiel `authentik_client`, als Datei `/run/secrets/authentik_client`, als `MARSHAL_SECRET_AUTHENTIK_CLIENT` oder in der Weboberfläche unter **Einstellungen > Sicherheit > Secrets**. Schreib den Wert nie in `marshal.yaml`. Siehe [Secrets](../secrets.md).

3. **Füg den Provider hinzu**

   ```yaml
   auth:
     providers:
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

   Starte Marshal neu. Der Start schlägt fehl, wenn sich das Secret nicht auflöst.

   | Schlüssel | Standard | Bedeutung |
   |---|---|---|
   | `name` | Pflicht | Kleinbuchstaben, Ziffern, `-`. Wird in URLs verwendet. |
   | `displayName` | der Name | Beschriftung der Schaltfläche auf der Anmeldeseite. |
   | `authority` | Pflicht | Issuer-URL. Marshal liest von dort `.well-known/openid-configuration`. |
   | `clientId` | Pflicht | Client-ID. |
   | `clientCredentialSecret` | Pflicht | Secret-Name des Client-Secrets. |
   | `scopes` | `[openid, profile, email]` | Angeforderte Scopes. |
   | `useUserInfo` | `false` | Claims auch vom Userinfo-Endpunkt lesen, für Provider, die Gruppen nicht ins ID-Token schreiben. |
   | `claims.subject` / `name` / `groups` | `sub` / `name` / `groups` | Namen der Claims. |
   | `roleMappings` | Pflicht, mindestens eine | Gruppe des Providers auf `admin` oder `user`. |

   Vollständige Liste: [Referenz von `auth`](../../configuration/marshal-yaml/auth.md).
:::

## Beispiele für Provider

Prüfe die aktuelle Dokumentation jedes Providers; Menüs ändern sich zwischen Versionen.

### Authentik

1. Leg einen Provider vom Typ OAuth2/OpenID mit der Redirect-URI `https://<marshal-host>/signin-oidc/authentik` und eine Anwendung mit dem Slug `marshal` an.
2. Authority: `https://<authentik-host>/application/o/marshal/`.
3. Stell sicher, dass der Claim `groups` enthalten ist (das Standard-Mapping des Scopes profile liefert ihn in aktuellen Versionen). Fehlen Gruppen, setz `useUserInfo: true`.
4. Leg die Gruppen `marshal-admins` und `marshal-users` an und binde sie an die Anwendung.

### Keycloak

1. Leg in deinem Realm einen Client an: Client-Authentifizierung an, Standard Flow an, gültige Redirect-URI `https://<marshal-host>/signin-oidc/keycloak`.
2. Authority: `https://<keycloak-host>/realms/<realm>`.
3. Füge dem Client-Scope einen Mapper **Group Membership** mit dem Token-Claim-Namen `groups` hinzu, **Full group path** aus, ins ID-Token aufgenommen. Ohne den Mapper hat das Token keine Gruppen.
4. Leg die Gruppen `marshal-admins` und `marshal-users` an und füge Benutzer hinzu.

```yaml
auth:
  providers:
    - name: keycloak
      type: oidc
      displayName: Keycloak
      authority: "https://kc.example.com/realms/home"
      clientId: marshal
      clientCredentialSecret: keycloak_client
      roleMappings:
        marshal-admins: admin
        marshal-users: user
```

### Pocket ID

1. Leg einen OIDC-Client mit der Callback-URL `https://<marshal-host>/signin-oidc/pocketid` an und notiere ID und Secret.
2. Authority: die Basis-URL deines Pocket ID, zum Beispiel `https://id.example.com`.
3. Leg Benutzergruppen an und erlaube sie für den Client. Fordere den Scope `groups` an, damit der Gruppen-Claim ausgestellt wird; steht er nicht im ID-Token, setz `useUserInfo: true`.

```yaml
auth:
  providers:
    - name: pocketid
      type: oidc
      displayName: Pocket ID
      authority: "https://id.example.com"
      clientId: "<client id>"
      clientCredentialSecret: pocketid_client
      scopes: [openid, profile, email, groups]
      useUserInfo: true
      roleMappings:
        marshal-admins: admin
        marshal-users: user
```

## Rollen und Benutzer

- Rollen kommen über `roleMappings` aus Gruppen-Claims und werden bei jeder Anmeldung neu abgeglichen. Entfernst du einen Benutzer aus `marshal-admins`, verliert er `admin` bei der nächsten Anmeldung.
- Eine Anmeldung ohne zugeordnete Gruppe wird abgelehnt. Kein Benutzer wird angelegt oder verknüpft.
- `admin` bekommt alle Scopes; `user` bekommt alle Scopes außer `admin` und den Webhook-Scopes. Ansichten nur für Admins (etwa Konfiguration) sind für `user` verborgen.
- Marshal ordnet Benutzer über den Subject-Claim zu, nie über die E-Mail. Eine geänderte E-Mail führt zum selben Benutzer.
- Solange der Eigentümer der Instanz nicht übernommen ist, übernimmt ihn das erste OIDC-Subject mit einer zugeordneten Gruppe und behält seine ID, Sitzungen und Tokens. Spätere Subjects legen neue Benutzer an.
- Du kannst mehrere Provider gleichzeitig einschalten, zum Beispiel `local` plus einen OIDC-Provider.

## Prüfen, ob es funktioniert

Öffne die Anmeldeseite. Eine Schaltfläche mit dem Anzeigenamen des Providers erscheint (`GET /api/v1/auth/providers` listet ihn). Melde dich an. **Einstellungen** zeigt deinen Namen und deine Rolle im Fuß der Navigation.

## Fehlerbehebung

- Zurück auf der Anmeldeseite mit einem Fehler: Die Redirect-URI beim Provider passt nicht, das Client-Secret ist falsch, oder der Benutzer ist in keiner zugeordneten Gruppe.
- Gruppen fehlen: Prüfe den Claim-Namen in `claims.groups`; versuch `useUserInfo: true`.
- Startfehler mit `auth.providers[<n>]...`: Ein Pflichtschlüssel fehlt, oder eine Rolle ist weder `admin` noch `user`.

## Verwandte Seiten

::: cards
- [Lokale Konten](local-accounts.md)
  Als Notfallzugang
- [Secrets](../secrets.md)
- [Sicherheitsmodell](../../concepts/security-model.md)
:::
