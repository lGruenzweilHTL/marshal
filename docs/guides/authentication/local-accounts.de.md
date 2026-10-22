---
icon: fa-solid fa-fingerprint
description: "Übernimm beim ersten Start den Eigentümer der Instanz und melde dich dann mit einem Passkey oder einem Passwort an."
---

# Mit einem lokalen Konto anmelden

Übernimm beim ersten Start den Eigentümer der Instanz und melde dich dann mit einem Passkey oder einem Passwort an.

## Bevor du anfängst

- Marshal ist über HTTPS erreichbar. Browser-Sitzungen und das Antiforgery-Token brauchen HTTPS; über reines HTTP scheitert die Einrichtung mit dem Problem `auth.https_required`.
- `auth.providers` in `marshal.yaml` hat einen Provider `local`:

```yaml [/config/marshal.yaml]
auth:
  providers:
    - name: local
      type: local
```

Es ist nur ein Provider `local` erlaubt. Provider-Namen bestehen aus Kleinbuchstaben, Ziffern und `-`. Änderungen an `auth` brauchen einen Neustart.

## Schritte

::: steps
1. **Führe die Ersteinrichtung aus**

   Öffne Marshal im Browser. Solange der Eigentümer der Instanz nicht übernommen ist, erscheint die Einrichtungsseite.

   1. Gib einen Benutzernamen, optional einen Anzeigenamen und ein Passwort mit mindestens 12 Zeichen ein.
   2. Schick das Formular ab.

   Die Einrichtung setzt die Zugangsdaten des Eigentümers, gibt ihm die Rolle `admin`, behält seine ID und meldet dich an. Danach ist die Einrichtung nicht mehr erreichbar, und ihr Endpunkt antwortet mit 404.

2. **Füg einen Passkey hinzu**

   1. Öffne **Einstellungen > Sicherheit**.
   2. Gib dem Passkey unter **Passkeys** optional einen Namen (zum Beispiel „Handy“) und wähle **Passkey hinzufügen**.
   3. Bestätige mit Fingerabdruck, Gesicht oder Geräte-PIN.

   Ab dann kannst du dich mit dem Passkey statt mit dem Passwort anmelden. Das Passwort bleibt als Rückfall.
:::

## Prüfen, ob es funktioniert

Melde dich ab und wieder an, zuerst mit dem Passkey, dann mit dem Passwort.

## Sitzungen und Sperre

- Das Sitzungs-Cookie heißt `marshal_session`: HttpOnly, Secure, `SameSite=Lax`. Es gilt 14 Tage und verlängert sich bei Nutzung.
- Fünf fehlgeschlagene Passwortversuche sperren das Konto eine Zeit lang.
- Passkeys sind an den Hostnamen gebunden. Prüft dein Reverse Proxy den Host-Header nicht, setz `serverDomain` (zum Beispiel `marshal.example.com`) am Provider `local`.

## Weitere Benutzer

Der Provider `local` deckt den Eigentümer der Instanz ab. Marshal ist für mehrere Benutzer vorbereitet, aber weitere Personen melden sich über [OIDC](oidc.md) an. Ein Benutzer kann sowohl eine OIDC-Anmeldung als auch lokale Zugangsdaten haben; so hast du einen Notfallzugang, wenn der Identity Provider ausfällt.

## Fehlerbehebung

- „Marshal must be opened over HTTPS“: Nimm die HTTPS-Adresse ([Reverse Proxy](../../installation/reverse-proxy.md)).
- Die Einrichtungsseite fehlt: Der Eigentümer ist schon übernommen (Passwort, Passkey oder externe Anmeldung).
- Der Passkey scheitert auf einer neuen Domain: Passkeys sind an den Hostnamen gebunden; füge einen neuen hinzu.

## Verwandte Seiten

::: cards
- [Referenz von `auth`](../../configuration/marshal-yaml/auth.md)
- [Maschinen-Tokens](machine-tokens.md)
- [Sicherheitsmodell](../../concepts/security-model.md)
:::
