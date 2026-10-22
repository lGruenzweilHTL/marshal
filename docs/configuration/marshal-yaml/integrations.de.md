---
icon: fa-solid fa-plug
description: "Schlüssel der Integrationsabschnitte von marshal.yaml: Home Assistant, CalDAV-Kalender, Drucker, FreshRSS, Notizen, Websuche, OAuth-Clients für Mail, Proxmox, Medien, Musik, GitHub und Executors."
---

# Abschnitte der Integrationen

Schlüssel der Integrationsabschnitte von `marshal.yaml`: Home Assistant, CalDAV-Kalender, Drucker, FreshRSS, Notizen, Websuche, OAuth-Clients für Mail, Proxmox, Medien, Musik, GitHub und Executors. Jeder Abschnitt ist optional. Die Werkzeuge einer Integration bleiben verborgen, bis ihr Abschnitt gesetzt ist. Änderungen brauchen einen Neustart.

Secret-Schlüssel (`...Secret`) enthalten Secret-Namen, nie Werte. Siehe [Secrets-Referenz](../secrets-reference.md).

## Beispiel

```yaml
ha:
  url: http://homeassistant.local:8123
  tokenSecret: ha_token

printers:
  - name: voron
    url: http://voron.local:7125
    apiKeySecret: moonraker_voron
    macros: [PARK, LOAD_FILAMENT]

github:
  tokenSecret: github_pat
  repos:
    marshal: lukas/marshal
```

## `ha` (Home Assistant)

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `ha.url` | URL | nicht gesetzt | Basis-URL von Home Assistant, hinter einem Proxy einschließlich Basispfad. Nicht gesetzt: Home Assistant ist aus. |
| `ha.tokenSecret` | Secret-Name | nicht gesetzt | Langlebiges Zugriffstoken eines Admin-Benutzers (die Liste der freigegebenen Entitäten braucht Admin). Dasselbe Secret kann den HA-MCP-Server in `mcp/ha.yaml` authentifizieren. |

Validierung: `ha.url must be an absolute http(s) URL.`, `ha.tokenSecret is required when ha.url is set.` Das Token muss beim Start auflösbar sein.

## `calendar.caldav` (CalDAV-Kalender)

CalDAV-Konten des Eigentümers der Instanz, neben den Kalendern von Home Assistant. Kalender werden über die URL erkannt (Principal, dann Calendar Home Set) und als `caldav:<account>/<calendar>` angesprochen. Ein Konto, dessen Erkennung oder Anmeldung scheitert, erzeugt eine Warnung zum Zustand und beeinträchtigt andere Kalender nicht. Einrichtung pro Server: [Deine Kalender nutzen](../../guides/integrations/calendar.md).

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `calendar.caldav.accounts` | Abbildung | leer | Konten nach ID. Die ID passt auf `[a-z][a-z0-9_]*`. Leer: keine CalDAV-Kalender. |
| `calendar.caldav.accounts.<id>.url` | URL | Pflicht | DAV-URL. `https`, oder `http` für einen Host im lokalen Netz (private Adresse, `localhost`, ein einteiliger Name oder `.lan`, `.local`, `.home.arpa`, `.internal`, `.localdomain`). |
| `calendar.caldav.accounts.<id>.username` | Zeichenkette | Pflicht | Anmeldename. |
| `calendar.caldav.accounts.<id>.passwordSecret` | Secret-Name | Pflicht | Secret mit dem Passwort oder App-Passwort. Wird pro Anfrage gelesen, ein geänderter Wert braucht also keinen Neustart. |
| `calendar.caldav.accounts.<id>.calendars` | Liste von Zeichenketten | alle | Zu nutzende Kalendernamen (letztes Pfadsegment der Kalender-URL). |
| `calendar.caldav.accounts.<id>.pollMinutes` | Ganzzahl | `5` | Minuten zwischen Abfragen nach Änderungen. 1 bis 60. |

Validierung: `calendar.caldav.accounts.<id>: the account id must match [a-z][a-z0-9_]*.`, `calendar.caldav.accounts.<id>.url must be an absolute https URL (http only for a host on the local network).`, `calendar.caldav.accounts.<id>.username is required.`, `calendar.caldav.accounts.<id>.passwordSecret must be a secret name (^[a-z][a-z0-9_]{0,63}$).`, `calendar.caldav.accounts.<id>.calendars must not contain empty names.`, `calendar.caldav.accounts.<id>.pollMinutes must be 1-60.`

## `printers` und `printerSnapshots` (Moonraker)

`printers` ist eine Liste auf der obersten Ebene der Datei.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `printers[].name` | Zeichenkette | keiner | Name in Werkzeugen und der Oberfläche. Buchstaben, Ziffern, `-` oder `_`. Eindeutig, ohne Beachtung der Groß-/Kleinschreibung. |
| `printers[].url` | URL | keiner | Basis-URL von Moonraker, zum Beispiel `http://voron.lan:7125`. |
| `printers[].webUrl` | URL | nicht gesetzt | Mainsail oder Fluidd, verlinkt für Aktionen, die Marshal nicht anbietet. |
| `printers[].apiKeySecret` | Secret-Name | nicht gesetzt | Nur nötig, wenn Moonraker einen API-Schlüssel verlangt. |
| `printers[].powerEntity` | Zeichenkette | nicht gesetzt | `switch.*`-Entität in Home Assistant, die den Drucker mit Strom versorgt. Der [Vorschlag](suggestions.md) `print_done_away` bietet an, sie auszuschalten. |
| `printers[].macros` | Liste von Zeichenketten | `[]` | Allowlist für `printer.run_macro`. In Großbuchstaben umgewandelt. Es werden nie Argumente übergeben. Roher G-Code existiert nicht als Werkzeug. |
| `printers[].babysitter.enabled` | Boolean | `false` | Prüfungen per Snapshot während Drucken. Pausiert nach zwei fehlgeschlagenen Prüfungen. Braucht eine Webcam und ein Vision-Modell. |
| `printers[].babysitter.interval` | Dauer | `00:01:00` | Zeit zwischen Prüfungen. |
| `printerSnapshots.maxSize` | Ganzzahl (px) | `1024` | Längste Seite nach dem Verkleinern. |
| `printerSnapshots.quality` | Ganzzahl | `85` | JPEG-Qualität. |
| `printerSnapshots.retention` | Ganzzahl | `20` | Behaltene Snapshots pro Drucker. |

Validierung: `printers[].name must be letters, digits, '-' or '_'.`, `printers[].name must be unique.`, `printers[].url must be an absolute http(s) URL.`, `printers[].webUrl must be an absolute http(s) URL.`, `printers[].powerEntity must be a switch.* entity, not '<entity>'.`, `printers[].babysitter.interval must be at least 10 seconds.` (wenn eingeschaltet), `printerSnapshots.maxSize must be at least 64.`, `printerSnapshots.quality must be between 1 and 100.`, `printerSnapshots.retention must be at least 1.`

## `freshrss`

Das API-Passwort ist das in FreshRSS unter Profil > API-Verwaltung gesetzte, nicht das Anmeldepasswort.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `freshrss.url` | URL | nicht gesetzt | Basis-URL von FreshRSS (API unter `api/greader.php` darunter). Nicht gesetzt: RSS-Werkzeuge sind verborgen. |
| `freshrss.userName` | Zeichenkette | leer | FreshRSS-Benutzer. Pflicht, wenn `url` gesetzt ist. |
| `freshrss.apiPasswordSecret` | Secret-Name | nicht gesetzt | Secret mit dem API-Passwort. |
| `freshrss.searchWindow` | Ganzzahl | `500` | Neueste Artikel (gelesen und ungelesen), die `rss.search` durchsucht. 1 bis 5000. |
| `freshrss.pageSize` | Ganzzahl | `100` | Artikel pro API-Anfrage. 1 bis 1000. |
| `freshrss.fullText.maxBytes` | Ganzzahl | `2097152` | Größte gelesene Seite in Bytes. Mindestens 1024. |
| `freshrss.fullText.timeout` | Dauer | `00:00:15` | Längstes Laden einer Seite. Positiv. |
| `freshrss.fullText.maxCharacters` | Ganzzahl | `20000` | Längster Text, der ans Modell geht. Mindestens 100. |

Validierung: `freshrss.url must be an absolute http(s) URL.`, `freshrss.userName is required.`, `freshrss.searchWindow must be 1-5000 and pageSize 1-1000.`, `freshrss.fullText needs maxBytes >= 1024, maxCharacters >= 100 and a positive timeout.`

## `notes`

Markdown-Dateien in einem git-Repository. Ohne `remote` legst du ein Repository nach `/data/notes`. Mit `remote` klont Marshal es dorthin und spiegelt es rein lesend.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `notes.remote` | Zeichenkette | nicht gesetzt | Git-URL (https, ssh oder file). |
| `notes.branch` | Zeichenkette | Standard-Branch des Remotes | Branch, dem gefolgt wird. |
| `notes.credentialSecret` | Secret-Name | nicht gesetzt | Secret mit einem https-Token oder einem SSH-Deploy-Key (privater Schlüssel). |
| `notes.pullInterval` | Dauer | `00:15:00` | Wie oft das Remote gezogen und der Index aktualisiert wird. Mindestens eine Minute. |
| `notes.maxReadCharacters` | Ganzzahl | `50000` | Längster Notiztext, den `notes.read` liefert. Mindestens 1000. |

Validierung: `notes.pullInterval must be at least one minute.`, `notes.maxReadCharacters must be at least 1000.`

## `web` (SearXNG-Websuche)

SearXNG muss JSON liefern (`search.formats: [html, json]` in seiner `settings.yml`). Halte es im LAN oder hinter einem VPN.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `web.searxng.url` | URL | nicht gesetzt | Basis-URL von SearXNG. Nicht gesetzt: Webwerkzeuge sind verborgen. |
| `web.searxng.authHeaderSecret` | Secret-Name | nicht gesetzt | Secret mit einem Wert für den Header `Authorization`, für ein SearXNG hinter einem Reverse Proxy. |
| `web.searxng.engines` | Liste von Zeichenketten | `[google, bing, duckduckgo, wikipedia]` | Namen der SearXNG-Suchmaschinen, die mit jeder Anfrage geschickt werden. Der Suchtext erreicht jede davon. |
| `web.searxng.maxResults` | Ganzzahl | `8` | Höchstzahl der Ergebnisse einer Suche und das Standard-`limit` von `web.search`. 1 bis 20. |

Validierung: `web.searxng.url must be an absolute http(s) URL.`, `web.searxng.authHeaderSecret must be a secret name (^[a-z][a-z0-9_]{0,63}$).`, `web.searxng.maxResults must be 1-20.`, `web.searxng.engines must not contain empty names.`

## `oauth` (Verbindungen von Mailkonten)

OAuth-Clients, mit denen Benutzer Gmail- oder Microsoft-Mailkonten in der Weboberfläche verbinden. Ohne Client ist der Knopf zum Verbinden deaktiviert. Registriere die Redirect-URI `<public URL>/oauth/callback/google` (oder `/microsoft`). Google braucht den Scope `https://mail.google.com/`; Microsoft braucht `IMAP.AccessAsUser.All` und `offline_access`. Marshal sendet nie Mail.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `oauth.google.clientId` | Zeichenkette | leer | Client-ID. |
| `oauth.google.clientCredentialSecret` | Secret-Name | nicht gesetzt | Secret mit dem Client-Secret. |
| `oauth.microsoft.clientId` | Zeichenkette | leer | Client-ID. |
| `oauth.microsoft.clientCredentialSecret` | Secret-Name | nicht gesetzt | Secret mit dem Client-Secret. |

Kein Validator. Ein Client gilt nur als konfiguriert, wenn ID und aufgelöstes Secret vorhanden sind.

## `proxmox`

Rein lesender Zugriff auf Proxmox VE für die Werkzeuge `infra.*`. Erstelle ein API-Token mit Privilegientrennung und der Rolle `PVEAuditor` auf `/`. Marshal schickt nur GET-Anfragen.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `proxmox.url` | URL | nicht gesetzt | API-URL eines Cluster-Knotens, zum Beispiel `https://pve.lan:8006/`. Nicht gesetzt: Werkzeuge sind verborgen. Muss https sein. |
| `proxmox.tokenId` | Zeichenkette | leer | `user@realm!name`. |
| `proxmox.tokenSecret` | Secret-Name | nicht gesetzt | Secret mit dem Tokenwert (eine UUID). Wird pro Anfrage gelesen. |
| `proxmox.certificateFingerprint` | Zeichenkette | nicht gesetzt | SHA-256-Fingerabdruck des selbstsignierten Zertifikats des Knotens: 64 Hex-Ziffern, Doppelpunkte erlaubt. Pinnt das Zertifikat. |

Validierung (wenn `url` gesetzt ist): `proxmox.url must be an absolute https URL.`, `proxmox.tokenId must look like user@realm!name.`, `proxmox.tokenSecret must name a secret.`; und `proxmox.certificateFingerprint must be a SHA-256 fingerprint (64 hex digits, colons allowed).`

## `media`

Seerr für Suche und Anfragen (Anfragen brauchen eine Bestätigung), optional rein lesende Kalender und Warteschlangen von Sonarr und Radarr sowie Jellyfin-Links. Die Werkzeuge sind verborgen, bis `media.seerr.url` gesetzt ist. Die Schlüssel für Sonarr und Radarr haben vollen Zugriff; Marshal schickt nur GET-Anfragen.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `media.seerr.url` | URL | nicht gesetzt | Basis-URL von Seerr. |
| `media.seerr.apiKeySecret` | Secret-Name | nicht gesetzt | API-Schlüssel von Seerr (Admin). Wird pro Anfrage gelesen. |
| `media.sonarr.url`, `media.sonarr.apiKeySecret` | URL, Secret-Name | nicht gesetzt | Optional. |
| `media.radarr.url`, `media.radarr.apiKeySecret` | URL, Secret-Name | nicht gesetzt | Optional. |
| `media.jellyfinUrl` | URL | nicht gesetzt | Basis-URL des Jellyfin-Webclients, für Links. |

Validierung: `media.seerr.url is required when a media section is configured.`, `media.seerr needs an absolute http(s) url and an apiKeySecret naming a secret.` (genauso für `sonarr`, `radarr`), `media.jellyfinUrl must be an absolute http(s) URL.`

## `music` (Music Assistant)

Braucht `ha.url` und die Music-Assistant-Integration in Home Assistant.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `music.speakers.<name>` | Zeichenkette | keiner | Lautsprechername, abgebildet auf eine `media_player`-Entitäts-ID von Music Assistant. Die Werkzeuge `music_*` nehmen nur diese Namen. Ein Satellit spielt auf dem Lautsprecher, den sein Profil in `satellites.yaml` nennt. |

Gib die Player von Music Assistant nicht für Assist frei: Die eigenen Medien-Intents von HA bieten dann einen zweiten Weg, denselben Lautsprecher zu steuern.

Validierung: `music.speakers.<name> must be named with lowercase letters, digits and underscores, starting with a letter.`, `music.speakers.<name> must be a media_player entity id.`

## `github`

Rein lesender Zugriff für die Werkzeuge `repos.*` über ein Fine-grained Personal Access Token, das auf die gelisteten Repositories beschränkt ist (nur Lesen für Metadata, Contents, Pull requests, Issues, Actions). Das Modell sieht nur die IDs. Werkzeugausgabe ist Text von Dritten und belastet den Durchlauf.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `github.tokenSecret` | Secret-Name | nicht gesetzt | Secret mit dem Token. Wird pro Anfrage gelesen: Ersetze den Wert ohne Neustart. |
| `github.repos.<id>` | Zeichenkette | keiner | `owner/name`. Die ID ist das, was das Modell nutzt. |

Validierung: `github.repos ids must be lowercase letters, digits and dashes, starting with a letter.`, `github.repos values must be owner/name.`, `github.tokenSecret must name a secret when repos are configured.` Ohne Repositories gibt es keine Werkzeuge.

## `executors`

Coding-Sitzungen auf dem getrennten Executor-Host. `tasks.run_executor` ist verborgen, bis `executors.claudeCode.url` gesetzt ist.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `executors.callbackBaseUrl` | URL | nicht gesetzt | Marshal, wie der Executor-Host es sieht. Callbacks gehen an `api/v1/executor/callbacks/<session>` darunter. |
| `executors.claudeCode.url` | URL | nicht gesetzt | Basis-URL des Executor-Hosts. |
| `executors.claudeCode.tokenSecret` | Secret-Name | nicht gesetzt | Secret mit dem Token des Executor-Hosts. |
| `executors.claudeCode.repositories[].name` | Zeichenkette | leer | Verzeichnis unter `repositoriesRoot` des Hosts. |
| `executors.claudeCode.repositories[].description` | Zeichenkette | nicht gesetzt | Was das Repository ist, für das Modell. |

Validierung: `executors.callbackBaseUrl must be an absolute URL.`, und wenn `claudeCode.url` gesetzt ist: `executors.claudeCode needs an absolute url, a tokenSecret name, at least one repository and executors.callbackBaseUrl.`

## Verwandte Seiten

::: cards
- [Home Assistant](../../guides/integrations/home-assistant.md)
- [Drucker](../../guides/integrations/printers-moonraker.md)
- [RSS](../../guides/integrations/rss-freshrss.md)
- [Notizen](../../guides/integrations/notes.md)
- [Websuche](../../guides/integrations/web-search.md)
- [Mail](../../guides/integrations/mail.md)
- [Proxmox](../../guides/integrations/proxmox.md)
- [Medien](../../guides/integrations/media.md)
- [Musik](../../guides/integrations/music.md)
- [GitHub](../../guides/integrations/github.md)
- [Executor-Host](../../installation/executor-host.md)
:::
