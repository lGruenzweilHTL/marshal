---
icon: fa-solid fa-user-secret
description: "Gib Marshal API-Schlüssel, Tokens und Passwörter, ohne sie je in /config zu schreiben."
---

# Secrets verwalten

Gib Marshal API-Schlüssel, Tokens und Passwörter, ohne sie je in `/config` zu schreiben.

## Bevor du anfängst

- Du kennst die Secret-Namen, auf die deine Konfiguration verweist. Ein Verweis ist ein Schlüssel, der auf `Secret` endet, etwa `apiKeySecret: moonraker_voron` oder `tokenSecret: ha_token`.
- Ein Secret-Name passt auf `^[a-z][a-z0-9_]{0,63}$`: Kleinbuchstaben, Ziffern und `_`, beginnend mit einem Buchstaben.

`/config` enthält nur Secret-Namen. Marshal startet nicht, wenn darin ein Secret im Klartext steht.

## Woher die Werte kommen

| Quelle | Wie | Verwaltet durch |
|---|---|---|
| Docker- oder Datei-Secret | Datei `/run/secrets/<name>`; ein Zeilenumbruch am Ende wird entfernt | Compose, nur lesbar |
| Umgebungsvariable | `MARSHAL_SECRET_<NAME>` (Name in Großbuchstaben) | Compose, nur lesbar |
| Datenbank | Verschlüsselt mit ASP.NET Core Data Protection, Schlüssel in `/data/keys` | Weboberfläche |

## Schritte

::: tabs
== Datei-Secret
```yaml [docker-compose.yml]
services:
  marshal:
    secrets: [moonraker_voron]
secrets:
  moonraker_voron:
    file: ./secrets/moonraker_voron
```
== Umgebungsvariable
```yaml
services:
  marshal:
    environment:
      MARSHAL_SECRET_MOONRAKER_VORON: "${MOONRAKER_VORON_KEY}"
```

Übergib den Wert aus einer `.env`-Datei, die nicht in git liegt.
== Die Weboberfläche
1. Öffne **Einstellungen > Sicherheit**, Reiter **Secrets**.
2. Wähle **Wert setzen**. Gib Name, Wert und Geltungsbereich ein.
3. Wähle **Speichern**.

Geltungsbereiche: **Nur ich** (ein Benutzer-Secret) oder **Instanz** (alle Benutzer und die Konfiguration; nur für Admins). Werte sind nur schreibbar: Marshal nutzt sie und zeigt sie nie wieder an. Um einen zu ändern, setzt du ihn neu. Das Löschen eines Secrets stoppt die Integrationen, die es nutzen, bis es wieder einen Wert hat.

> [!IMPORTANT]
> Sichere `/data/keys` zusammen mit der Datenbank. Ohne die Data-Protection-Schlüssel lassen sich verschlüsselte Datenbank-Secrets nicht lesen. Siehe [Backups und Wiederherstellung](backups-and-restore.md).
:::

## Reihenfolge der Suche

- Ein Name, auf den `/config` verweist, löst zuerst über Umgebung oder Datei auf, dann über ein Instanz-Secret in der Datenbank. Er liest nie die Secrets eines Benutzers.
- Ein Name, auf den eine Zeile im Besitz eines Benutzers verweist (zum Beispiel ein Mailkonto), löst über Umgebung oder Datei auf, dann über das Secret dieses Benutzers, dann über das Instanz-Secret.

Das eigene Secret eines Benutzers überschreibt also ein Instanz-Secret gleichen Namens, aber nur für die Daten dieses Benutzers.

## Prüfen, ob es funktioniert

Der Reiter Secrets listet jedes Secret mit Name, Geltungsbereich (nur ich oder Instanz), Herkunft (Umgebung oder verschlüsselt) und letzter Änderung. Secrets aus Umgebung und Datei sind nur lesbar und ohne Wert. Der Reiter markiert auch jeden verwendeten Namen, den keine Quelle liefert, und sagt, wo er verwendet wird, zum Beispiel `freshrss_api`, gebraucht von `marshal.yaml`.

[`/health`](../reference/health-endpoints.md) meldet ein nicht aufgelöstes optionales Secret als Warnung, die den Verweis nennt, nie den Wert.

## Was beim Start fehlschlägt

Secrets, die der Start braucht, lassen ihn fehlschlagen, wenn sie nicht aufgelöst werden, und der Fehler nennt den Verweis: API-Schlüssel der Modelle, das OIDC-Client-Secret und das Home-Assistant-Token. Andere nicht aufgelöste Verweise erzeugen nur eine Warnung.

## Prüfprotokoll

Jedes Anlegen, Ersetzen und Löschen wird ohne den Wert protokolliert.

## Verwandte Seiten

::: cards
- [`secrets-reference`](../configuration/secrets-reference.md)
- [Umgebungsvariablen](../configuration/environment-variables.md)
- [Sicherheitsmodell](../concepts/security-model.md)
:::
