---
icon: fa-solid fa-film
description: "Verbinde Marshal mit Seerr, damit du per Stimme oder Chat einen Film oder eine Serie anfragen kannst, und optional mit Sonarr und Radarr, um zu fragen, was bald erscheint und was gerade lädt."
---

# Medien mit Seerr, Sonarr und Radarr anfragen und verfolgen

Verbinde Marshal mit Seerr, damit du per Stimme oder Chat einen Film oder eine Serie anfragen kannst, und optional mit Sonarr und Radarr, um zu fragen, was bald erscheint und was gerade lädt.

Aus Sonarr und Radarr kann Marshal nur lesen. In Seerr kann es nur lesen und Anfragen anlegen.

## Bevor du anfängst

- Eine laufende Seerr-Instanz, die der Container von Marshal über `http` oder `https` erreicht.
- Optional: Sonarr und Radarr und ein Jellyfin-Webclient, für kommende Veröffentlichungen, Download-Status und Links.
- Der API-Schlüssel jedes Dienstes: Seerr unter **Settings > General**, Sonarr und Radarr unter **Settings > General > Security**.
- Shell-Zugriff auf den Secret-Speicher, wie unter [Secrets](../secrets.md) beschrieben.

> [!WARNING]
> Der API-Schlüssel von Seerr gehört dessen Admin-Benutzer, und Sonarr und Radarr haben keine Schlüssel nur zum Lesen. Marshal schickt nur die erlaubten Anfragen (siehe [Grenzen](#grenzen)), aber die Schlüssel selbst sind mächtig. Halte diese Dienste in deinem LAN und die Schlüssel in Secrets.

## Schritte

::: steps
1. **Speichere die API-Schlüssel als Secrets**

   Leg ein Secret pro Dienst an, zum Beispiel mit Docker-Secrets oder Umgebungsvariablen `MARSHAL_SECRET_<NAME>`. Namen bestehen aus Kleinbuchstaben, Ziffern und Unterstrichen und beginnen mit einem Buchstaben.

   ```text
   seerr_api_key
   sonarr_api_key
   radarr_api_key
   ```

   Marshal liest jedes Secret bei jeder Anfrage, du kannst einen Schlüssel also ohne Neustart ersetzen.

2. **Füg den Abschnitt `media` hinzu**

   Füge das `/config/marshal.yaml` hinzu. Nur `seerr` ist Pflicht. Lass `sonarr`, `radarr` oder `jellyfinUrl` weg, wenn du sie nicht nutzt.

   ```yaml
   media:
     seerr:  { url: https://seerr.lan/,  apiKeySecret: seerr_api_key }
     sonarr: { url: https://sonarr.lan/, apiKeySecret: sonarr_api_key }
     radarr: { url: https://radarr.lan/, apiKeySecret: radarr_api_key }
     jellyfinUrl: https://jellyfin.lan/
   ```

   Jede `url` und `jellyfinUrl` muss eine absolute `http`- oder `https`-URL sein. Jedes `apiKeySecret` muss ein Secret-Name sein. Der Start schlägt mit einer dieser Meldungen fehl, wenn eine Regel verletzt ist:

   - `media.seerr.url is required when a media section is configured.`
   - `media.seerr needs an absolute http(s) url and an apiKeySecret naming a secret.` (derselbe Text gilt für `sonarr` und `radarr`)
   - `media.jellyfinUrl must be an absolute http(s) URL.`

   Ohne Abschnitt `media` registriert Marshal kein Werkzeug `media_*`.

3. **Starte Marshal neu**

   ```sh
   docker compose restart marshal
   ```

4. **Optional: bekomm einen Push, wenn eine Anfrage bereit ist**

   Seerr kann Marshal benachrichtigen, wenn ein angefragter Titel verfügbar wird. Marshal macht daraus Ereignisse, auf die eine Beobachtung reagieren kann.

   1. Erstelle ein Token, das Webhooks von Seerr senden darf:

      ```sh
      docker exec marshal marshal tokens create --name seerr --scope hooks:seerr
      ```

      Marshal gibt das Token einmal aus. Kopiere es jetzt.

   2. Öffne in Seerr **Settings > Notifications > Webhook**, schalte den Agenten ein und setz:

      | Feld | Wert |
      |---|---|
      | Webhook URL | `https://<marshal>/api/v1/hooks/seerr` |
      | Authorization Header | `Bearer <Token aus Schritt 1>` |
      | Notification Types | Request Approved, Request Automatically Approved, Request Available, Request Declined, Request Failed |
      | JSON Payload | siehe unten |

      ```json
      {"notification_type":"{{notification_type}}","{{media}}":{"media_type":"{{media_type}}","tmdbId":"{{media_tmdbid}}","status":"{{media_status}}","jellyfinMediaId":"{{media_jellyfinMediaId}}"},"{{request}}":{"request_id":"{{request_id}}"}}
      ```

   3. Leg eine Beobachtung für `source: seerr`, `type: media.available` an. Die Datei `deploy/config.example/tasks/media-available.yaml` ist ein fertiges Beispiel; bitte Marshal, eine Beobachtung mit diesen Einstellungen anzulegen. Siehe [Ereignis-Auslöser](../tasks/event-triggers.md).

   Zu den Ereignissen und dazu, wie Marshal Webhooks begrenzt und authentifiziert, siehe [Webhooks von Uptime Kuma und Proxmox](uptime-kuma.md#wie-der-webhook-empfang-funktioniert).
:::

## Werkzeuge

| Werkzeug | Risikostufe | Was es tut |
|---|---|---|
| `media_search` | safe | Bis zu 10 Filme und Serien aus Seerr mit TMDB-ID, Typ, Titel, Jahr, gekürzter Beschreibung, Zahl der Staffeln bei Serien und Verfügbarkeit. |
| `media_request` | confirm | Legt in Seerr eine Anfrage für einen Film oder eine Serie an. |
| `media_requests` | safe | Bis zu 20 neueste Anfragen, optional nach Status gefiltert. |
| `media_upcoming` | safe | Folgen und Filmveröffentlichungen aus den Kalendern von Sonarr und Radarr für die nächsten Tage. |
| `media_downloads` | safe | Die Download-Warteschlangen von Sonarr und Radarr. |

Details:

- `media_search(query, type?)`: `type` ist `movie` oder `show`. Die Verfügbarkeit ist eines von `unknown`, `pending`, `processing`, `partially_available`, `available`.
- `media_request(type, tmdbId, title, year, seasons?)`: Für eine Serie ohne `seasons` fragt Marshal alle Staffeln an.
- `media_requests(status?)`: `status` ist `pending`, `approved`, `declined`, `failed` oder `completed`.
- `media_upcoming(days?)`: `days` ist standardmäßig 7 und höchstens 30. Jeder Eintrag zeigt, ob die Datei schon heruntergeladen ist.
- `media_downloads()`: zeigt Titel, bei Serien die Folge, Prozent fertig, geschätzte Restzeit und Zustand.

Ohne `sonarr` oder `radarr` antworten `media_upcoming` und `media_downloads` ohne die Daten dieses Dienstes und sagen, welcher Dienst nicht konfiguriert ist. Alle fünf Werkzeuge scheitern mit `media.unavailable`, wenn ein Dienst nicht erreichbar ist oder seinen API-Schlüssel ablehnt.

### Wie eine Anfrage bestätigt wird

`media_request` ist in der Bestätigungsstufe, Marshal friert den Aufruf also ein und fragt dich zuerst. Die Frage nennt Titel, Jahr und Typ und bei einer Serie „alle Staffeln“ oder die Staffelnummern, zum Beispiel „Dune (2021) anfragen, Film“. Nach deiner Bestätigung prüft Marshal, dass Titel und Jahr in Seerr für diese TMDB-ID zu dem passen, was du gehört hast. Weichen sie ab, scheitert der Aufruf mit `media.title_mismatch`, und nichts wird angefragt. Marshal lehnt außerdem mit `media.already_available` oder `media.already_requested` ab, wenn Seerr sagt, dass der Titel verfügbar oder angefragt ist. Siehe [Freigaben](../tasks/approvals.md) und [Risikostufen](../../reference/risk-tiers.md).

### Jellyfin-Links

Ist `jellyfinUrl` gesetzt und meldet Seerr eine Jellyfin-Item-ID für einen Titel, enthalten Ergebnisse und `seerr`-Ereignisse einen Link zur Seite des Items im Jellyfin-Webclient. Mit `jellyfinUrl: https://jellyfin.lan/` und dem Item `abc123` lautet der Link `https://jellyfin.lan/web/#/details?id=abc123`. Marshal baut Links nur aus `jellyfinUrl` und der Item-ID und ruft Jellyfin nie auf.

## Grenzen

- Die Clients für Sonarr und Radarr senden nur `GET`-Anfragen.
- Der Client für Seerr sendet `GET`-Anfragen und das `POST`, das eine Anfrage anlegt. Marshal kann Anfragen nicht löschen, genehmigen oder ablehnen, keine Serien oder Filme ändern oder entfernen, keine Suchen starten und keine Einstellungen ändern.
- Titel und Beschreibungen stammen von Dritten. Die Ausgabe jedes Medienwerkzeugs ist nicht vertrauenswürdig: Ein Durchlauf, der eines aufruft, ist mit der Quelle `media` belastet. Siehe [nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md).
- `media_search` listet höchstens 10 Ergebnisse, `media_requests` höchstens 20, `media_upcoming` sieht höchstens 30 Tage voraus.
- Webhook-Ereignisse von Seerr tragen nur IDs und den Jellyfin-Link, nie freien Text.

## Prüfen, ob es funktioniert

1. Öffne die Weboberfläche und frag „Such nach Dune“. Die Antwort listet die Filme von 1984 und 2021 und die Serie von 2000 als getrennte Einträge mit Jahr und TMDB-ID.
2. Bitte „Frag Dune von 2021 an“. Marshal fragt nach einer Bestätigung und nennt „Dune (2021), Film“. Bestätige und prüfe dann Seerr auf die neue Anfrage.
3. Frag „Was lädt gerade?“ und „Was läuft diese Woche?“, wenn du Sonarr oder Radarr konfiguriert hast.
4. Hast du den Webhook eingerichtet, nutze die Schaltfläche **Test** in Seerr. Marshal antwortet mit 202 und veröffentlicht kein Ereignis. Echte Benachrichtigungen erzeugen die Ereignisse `media.available`, `media.approved`, `media.declined` und `media.failed`.

Scheitert ein Werkzeug mit `media.unavailable`, nennt die Meldung den Dienst oder das Secret. Prüfe die URL, den API-Schlüssel und dass sich das Secret auflöst. Siehe [Fehlerbehebung](../troubleshooting.md).

## Verwandte Seiten

::: cards
- [`media` in marshal.yaml](../../configuration/marshal-yaml/integrations.md)
- [Werkzeug-Referenz](../../reference/tools.md)
- [Referenz der Ereignisse](../../reference/events.md)
- [Secrets](../secrets.md)
- [Maschinen-Tokens](../authentication/machine-tokens.md)
- [Musik](music.md)
:::
