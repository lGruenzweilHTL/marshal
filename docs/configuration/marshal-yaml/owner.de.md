---
icon: fa-solid fa-user
description: "Das Profil des Eigentümers der Instanz, das Marshal beim ersten Start anlegt."
---

# `owner`

Das Profil des Eigentümers der Instanz, das Marshal beim ersten Start anlegt.

## Beispiel

```yaml
owner:
  displayName: "Your name"
  language: en
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `owner.displayName` | Zeichenkette | `Owner` | Anzeigename des Eigentümers der Instanz. Pflicht, nicht leer. |
| `owner.language` | Zeichenkette | `en` | Sprache der Oberfläche und der Antworten: `de` oder `en`. |

Die Zeitzone des Eigentümers ist `instance.timeZone`. Spätere Änderungen werden pro Benutzer in der Weboberfläche gemacht. Die Einrichtung beim ersten Start oder die erste OIDC-Anmeldung übernimmt den Eigentümer (siehe [auth](auth.md)).

## Validierung

- `owner.displayName is required.`
- `owner.language is invalid.` (alles außer `de` oder `en`)

## Verwandte Seiten

::: cards
- [`instance`](instance.md)
- [Lokale Konten](../../guides/authentication/local-accounts.md)
:::
