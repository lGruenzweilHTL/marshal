---
icon: fa-solid fa-user-secret
description: "Jeder Secret-Name, auf den ein Konfigurationsschlüssel verweisen kann, der Schlüssel, der darauf verweist, und welche Speicher den Wert liefern können."
---

# Secrets-Referenz

Jeder Secret-Name, auf den ein Konfigurationsschlüssel verweisen kann, der Schlüssel, der darauf verweist, und welche Speicher den Wert liefern können. Dateien in `/config` enthalten nur Secret-Namen; die Werte kommen aus dem Secret-Speicher.

## Secret-Namen

Ein Secret-Name passt auf `^[a-z][a-z0-9_]{0,63}$`: Kleinbuchstaben, Ziffern und Unterstriche, beginnend mit einem Buchstaben, höchstens 64 Zeichen. Die Namen wählst du selbst; die Tabellen unten zeigen die Schlüssel, die einen Namen nehmen, mit den Namen aus der Beispielkonfiguration.

## Speicher

| Speicher | Form | Geltungsbereich | Hinweise |
|---|---|---|---|
| Umgebung | `MARSHAL_SECRET_<NAME>` (Name in Großbuchstaben) | Instanz | Nur lesend. Hat Vorrang vor den anderen Speichern. |
| Secret-Datei | `/run/secrets/<name>` (Verzeichnis über `paths.secrets`) | Instanz | Nur lesend. Zeilenumbrüche am Ende werden entfernt. |
| Datenbank, Instanz | Verschlüsselte Zeile ohne Eigentümer | Instanz | Wird in der Weboberfläche von einem `admin` verwaltet, über `/api/v1/secrets/instance`. Verschlüsselt mit den Data-Protection-Schlüsseln in `/data/keys`. |
| Datenbank, Benutzer | Verschlüsselte Zeile mit Eigentümer | Ein Benutzer | Wird über `/api/v1/me/secrets` verwaltet. |

Werte lassen sich nur schreiben: Kein Endpunkt liefert sie zurück.

### Suchreihenfolge

- Ein Name, auf den `/config` verweist, wird in Umgebung, dann Datei, dann Datenbankzeilen der Instanz aufgelöst. Er liest nie die Zeilen eines Benutzers.
- Ein Name, auf den eine Zeile eines Benutzers verweist (ein Mailkonto), wird in Umgebung und Datei aufgelöst, dann in den Zeilen dieses Eigentümers, dann in den Zeilen der Instanz.

Ein fehlender Name scheitert mit `secret.not_found`.

## Verweise aus `marshal.yaml`

Alle diese werden nur aus Umgebung, Datei oder Datenbank der Instanz aufgelöst.

| Schlüssel | Genutzt für | Aufgelöst | Wenn nicht aufgelöst |
|---|---|---|---|
| `models.<key>.apiKeySecret` | API-Schlüssel eines Modells | Beim Start | Der Start scheitert. Pflicht bei `anthropic`-Modellen; optional bei anderen Anbietern. |
| `embedding.apiKeySecret` | API-Schlüssel des Embedding-Servers | Beim Start | Der Start scheitert, wenn er gesetzt ist. |
| `ha.tokenSecret` | Langlebiges Zugriffstoken von Home Assistant | Beim Start | Der Start scheitert. Pflicht, wenn `ha.url` gesetzt ist. |
| `auth.providers[<i>].clientCredentialSecret` | OIDC-Client-Secret (`type: oidc`) | Beim Start | Der Start scheitert. |
| `printers[<i>].apiKeySecret` | API-Schlüssel von Moonraker | Beim Start | `/health` warnt mit dem Verweis; die Integration bleibt nicht verfügbar. Lass den Schlüssel weg, wenn Moonraker dem Netz vertraut. |
| `freshrss.apiPasswordSecret` | API-Passwort von FreshRSS | Beim Start | `/health` warnt mit dem Verweis; die Integration bleibt nicht verfügbar. |
| `notes.credentialSecret` | HTTPS-Token oder SSH-Deploy-Key (privater Schlüssel) für das Notiz-Remote | Beim Start | `/health` warnt mit dem Verweis; die Integration bleibt nicht verfügbar. |
| `oauth.google.clientCredentialSecret` | Google-OAuth-Client-Secret für Mailkonten | Beim Start | `/health` warnt mit dem Verweis; die Integration bleibt nicht verfügbar. |
| `oauth.microsoft.clientCredentialSecret` | Microsoft-OAuth-Client-Secret für Mailkonten | Beim Start | `/health` warnt mit dem Verweis; die Integration bleibt nicht verfügbar. |
| `proxmox.tokenSecret` | Secret des Proxmox-API-Tokens (die UUID) | Bei jeder Anfrage | Die Werkzeuge `infra.*` melden die Integration als nicht verfügbar; `/health` warnt. |
| `media.seerr.apiKeySecret` | API-Schlüssel von Seerr | Bei jeder Anfrage | Werkzeuge melden den Dienst als nicht verfügbar. |
| `media.sonarr.apiKeySecret` | API-Schlüssel von Sonarr | Bei jeder Anfrage | Ebenso. |
| `media.radarr.apiKeySecret` | API-Schlüssel von Radarr | Bei jeder Anfrage | Ebenso. |
| `github.tokenSecret` | Fine-grained Personal Access Token von GitHub | Bei jeder Anfrage | Werkzeuge melden die Integration als nicht verfügbar. Pflicht (als gültiger Name), wenn `github.repos` gesetzt ist. |
| `executors.claudeCode.tokenSecret` | Token für den Executor-Host | Bei jeder Anfrage | Das Starten einer Sitzung scheitert: `The executor host token '<name>' is not set.` |

Schlüssel, die bei jeder Anfrage gelesen werden, übernehmen einen geänderten Wert ohne Neustart.

Nur die Secrets, die der Start braucht, lassen ihn scheitern: API-Schlüssel von Modell und Embedding, das OIDC-Client-Secret und das Home-Assistant-Token. Scheitert eines davon, gibt der Start jeden nicht aufgelösten Verweis mit Schlüssel aus, zum Beispiel `ha.tokenSecret (ha_token)`, und endet mit dem Code `1`.

## Verweise aus `/config/mcp/*.yaml`

| Schlüssel | Genutzt für | Wenn nicht aufgelöst |
|---|---|---|
| `authTokenSecret` | Bearer-Token für einen HTTP-Server | Der Server ist nicht verfügbar: `<file>: authTokenSecret (<name>) does not resolve.` |
| `envSecrets[].secret` | Umgebungsvariable eines stdio-Servers | Der Server ist nicht verfügbar: `<file>: envSecrets.<variable> (<name>) does not resolve.` |

Aufgelöst aus Umgebung, Datei oder Datenbank der Instanz.

## Verweise aus Zeilen von Benutzern

| Verweis | Genutzt für | Speicher |
|---|---|---|
| Zugangsdaten eines Mailkontos (gesetzt beim Verbinden eines Mailkontos in der Weboberfläche) | IMAP-App-Passwort oder OAuth-Refresh-Daten | Umgebung, Datei, dann die Datenbankzeilen des Eigentümers, dann Zeilen der Instanz. |

## Nicht als Secrets konfigurierbar

- Die Token-Datei des Executor-Hosts und die Umgebungsdatei der Sandbox sind Pfade ([Umgebungsvariablen](environment-variables.md#executor-host)), keine Secret-Namen.
- Webhook- und API-Tokens verwaltet Marshal selbst (Machine-Tokens), sie werden nicht aus der Konfiguration referenziert.

## Prüfung auf wörtliche Secrets

Marshal lehnt wörtliche Secrets in jeder Datei in `/config` beim Start und bei jedem Neuladen ab (ein gescheitertes Neuladen behält die vorherige Konfiguration). Eine Datei wird abgelehnt, wenn:

- ein Schlüssel auf `Secret` endet und einen Wert hat, der kein gültiger Secret-Name ist;
- das letzte camelCase-Wort eines Schlüssels `password`, `passphrase`, `token`, `credential` oder `credentials` ist oder seine letzten zwei Wörter `api key`, `private key` oder `access key` sind;
- ein Wert wie ein Zugangsdatum aussieht: ein PEM-Header, ein JWT (`eyJ`), `sk-`, `sk-ant-`, `ghp_`, `xox?-`, eine Hex-Zeichenkette mit 32 oder mehr Zeichen, eine Base64- oder Base64url-Zeichenkette mit 32 oder mehr Zeichen oder eine andere lange Zeichenkette mit hoher Entropie.

`maxTokens: 4096` geht durch. `apiKeySecret: 0a1b2c3d4e5f60718293a4b5c6d7e8f9` scheitert, weil die Prüfung der Werte greift, obwohl das Namensmuster ihn erlaubt. Der Fehler nennt Datei und Schlüssel, nie den Wert. Direkt in `marshal.yaml` gesetzte Secret-Eigenschaften (zum Beispiel `ha.token`) werden ebenfalls abgelehnt.

## Verwandte Seiten

::: cards
- [Anleitung zu Secrets](../guides/secrets.md)
- [Umgebungsvariablen](environment-variables.md)
- [Sicherheitsmodell](../concepts/security-model.md)
:::
